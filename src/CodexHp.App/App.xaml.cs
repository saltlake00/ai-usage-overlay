using System.IO;
using System.Net.Http;
using CodexHp.App.Accounts;
using CodexHp.App.Application;
using CodexHp.App.Infrastructure;
using CodexHp.App.Infrastructure.Claude;
using CodexHp.App.Infrastructure.Ollama;
using CodexHp.App.Presentation;
using CodexHp.App.Presentation.Accounts;
using CodexHp.App.Presentation.Settings;
using CodexHp.Core.Domain;
using CodexHp.Core.Positioning;
using CodexHp.Core.Settings;

namespace CodexHp.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? singleInstance;
    private RollingFileLogger? logger;
    private HttpClient? httpClient;
    private HttpClient? ollamaHttpClient;
    private CancellationTokenSource? lifetimeCancellation;
    private Task? coordinatorTask;
    private ApplicationCoordinator? primaryCoordinator;
    private Task? secondaryProvidersTask;
    private TrayIconController? trayIcon;
    private UsageOverlayWindow? usageOverlayWindow;
    private SettingsWindow? settingsWindow;
    private UsageDetailsWindow? usageDetailsWindow;
    private AccountsWindow? accountsWindow;
    private SettingsWindowController? settingsWindowController;
    private OverlayPositionController? positionController;
    private DisplayEnvironmentWatcher? displayEnvironmentWatcher;
    private SettingsCommitService? settingsCommitService;
    private AppSettings activeSettings = AppSettings.Default;
    private OverlayPresentationSettings activePresentation =
        OverlayPresentationSettings.FromUnscaled(AppSettings.Default);
    private UsageOverlayState currentUsageOverlayState = UsageOverlayStateReducer.Reduce(
        UsageProviderState.Waiting,
        TokenActivityProviderState.Waiting,
        ServiceHealthState.Unknown,
        string.Empty,
        new VisibilityState(false, false),
        AppSettings.Default,
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private int shutdownStarted;
    private IReadOnlyList<ProviderUsageRowState> secondaryProviderRows =
    [
        new ProviderUsageRowState("claude", "CLAUDE", null, null, true),
        new ProviderUsageRowState("ollama", "OLLAMA", null, null, true, ShortWindowLabel: "SHORT"),
    ];
    private readonly ClaudeQuotaFallbackPolicy claudeQuotaFallback = new();
    private readonly SemaphoreSlim claudeRefreshSignal = new(0, 1);
    private readonly SemaphoreSlim ollamaRefreshSignal = new(0, 1);
    private readonly SemaphoreSlim usageCacheGate = new(1, 1);
    private AccountConnectionService? accountConnectionService;

    protected override void OnStartup(System.Windows.StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        this.singleInstance = SingleInstanceGuard.TryAcquire();
        if (this.singleInstance is null)
        {
            this.Shutdown();
            return;
        }

        try
        {
            this.StartApplication();
        }
        catch (Exception exception)
        {
            this.logger?.Log(DiagnosticLevel.Error, "Startup", "AI Usage Overlay could not start.", exception);
            System.Windows.MessageBox.Show(
                $"{UserInterfaceText.StartupFailure}\n\n{exception.Message}",
                "AI Usage Overlay",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            this.DisposeResources();
            this.Shutdown();
        }
    }

    protected override void OnSessionEnding(System.Windows.SessionEndingCancelEventArgs eventArgs)
    {
        this.BeginShutdown();
        base.OnSessionEnding(eventArgs);
    }

    protected override void OnExit(System.Windows.ExitEventArgs eventArgs)
    {
        this.DisposeResources();
        base.OnExit(eventArgs);
    }

    private void StartApplication()
    {
        this.logger = new RollingFileLogger();
        var monitorService = new WindowsMonitorService();
        var taskbarLocator = new TaskbarWindowLocator();
        Func<string, PhysicalRect?> taskbarBounds = monitorId =>
            taskbarLocator.FindForMonitor(monitorId)?.TaskbarBounds;
        var settingsStore = new JsonSettingsStore(
            monitors: monitorService.GetMonitors,
            taskbarBounds: taskbarBounds);
        var startupRegistration = new StartupRegistration(
            Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is not available."));
        this.activeSettings = settingsStore.Load() with
        {
            StartWithWindows = startupRegistration.IsEnabled(),
        };
        var settingsCommitService = new SettingsCommitService(settingsStore, startupRegistration);
        this.settingsCommitService = settingsCommitService;
        this.positionController = new OverlayPositionController(monitorService, taskbarBounds);
        var displayResolution = this.positionController.Resolve(this.activeSettings);
        this.activePresentation = new OverlayPresentationSettings(
            this.activeSettings.Colors,
            displayResolution.Appearance);

        this.usageOverlayWindow = new UsageOverlayWindow(
            new OverlayWindowHost(taskbarLocator, monitorService));
        this.usageOverlayWindow.Apply(this.currentUsageOverlayState, this.activePresentation);
        this.usageOverlayWindow.SetPlacement(displayResolution.Placement);
        this.usageOverlayWindow.OpenSettingsRequested += (_, _) => this.OpenSettings();
        this.usageOverlayWindow.ProviderDetailsRequested += this.OpenProviderDetails;
        this.usageOverlayWindow.OverlayPositionChanged += this.OnOverlayPositionChanged;
        this.usageOverlayWindow.DisplayEnvironmentChangeRequested +=
            (_, _) => this.displayEnvironmentWatcher?.RequestRefresh();
        this.usageOverlayWindow.Show();
        this.displayEnvironmentWatcher = new DisplayEnvironmentWatcher(
            this.Dispatcher,
            this.RefreshDisplayEnvironment);

        this.settingsWindowController = new SettingsWindowController(
            () => new SettingsWindowViewModel(
                this.activeSettings,
                this.ApplySettingsPreview,
                enabled => this.usageOverlayWindow.SetOverlayPositionChangeMode(enabled),
                desired => settingsCommitService.Commit(desired),
                canStartWithWindows: startupRegistration.CanEnable),
            this.ShowSettingsWindow,
            this.ActivateSettingsWindow);

        this.trayIcon = new TrayIconController(
            this.OpenSettings,
            this.BeginShutdown,
            this.RequestProviderRefresh,
            this.ToggleOverlayPositionLock,
            this.OpenAccounts);
        this.httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        this.lifetimeCancellation = new CancellationTokenSource();
        var clock = new SystemClock();
        var serviceStatusClient = new OpenAiServiceStatusClient(this.httpClient);
        var serviceStatusPoller = new OpenAiServiceStatusPoller(
            serviceStatusClient.FetchAsync,
            () => clock.UnixTimeMilliseconds);
        var visibilitySource = new WindowsVisibilitySource(
            new ChatGptProcessDetector(),
            new FullscreenDetector(),
            monitorService);
        var coordinator = new ApplicationCoordinator(
            new CodexCredentialSource(),
            new OpenAiUsageClient(this.httpClient),
            new CodexTokenUsageScanner(),
            serviceStatusPoller.ReadAsync,
            () => visibilitySource.Read(this.usageOverlayWindow.WindowHandle),
            () => Volatile.Read(ref this.activeSettings),
            clock,
            this.logger,
            readGraphAppearance: () => ToAppearanceSettings(
                Volatile.Read(ref this.activePresentation).Appearance));
        this.primaryCoordinator = coordinator;
        coordinator.UsageOverlayStateChanged += this.OnUsageOverlayStateChanged;
        this.coordinatorTask = this.RunCoordinatorAsync(coordinator, this.lifetimeCancellation.Token);
        this.ollamaHttpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        var claudeCredentials = new ClaudeCredentialSource();
        var claudeClient = new ClaudeUsageClient(this.httpClient);
        var claudeLocalUsage = new ClaudeLocalUsageSource();
        var ollamaCredentials = new OllamaCredentialSource();
        var ollamaClient = new OllamaUsageClient(this.ollamaHttpClient);

        // 계정 연동 서비스: 사용자가 앱 UI에서 등록한 비밀(DPAPI 암호화)을 읽어 조회한다.
        // Codex는 기존 auth.json을 그대로 읽고, Claude는 OAuth 토큰, Ollama는 API 키를
        // 앱 UI에서 등록한다. 등록하지 않은 공급자는 조회하지 않는다.
        var credentialsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIUsageOverlay",
            "credentials");
        var accountConnectionService = new AccountConnectionService(
            new DpapiAccountSecretStore(credentialsDirectory),
            new AccountConnectionStore(Path.Combine(credentialsDirectory, "state.json")),
            fetch: (providerId, secret, ct) => providerId switch
            {
                "claude" => this.FetchClaudeUsageAsync(
                    claudeCredentials,
                    claudeClient,
                    claudeLocalUsage,
                    secret,
                    ct),
                "ollama" => ollamaClient.FetchAsync(
                    new OllamaCredentials(null, secret),
                    ct),
                _ => throw new ArgumentOutOfRangeException(nameof(providerId)),
            },
            classify: ClassifyProviderError);
        this.accountConnectionService = accountConnectionService;
        var usageCache = new ProviderUsageCache();
        this.secondaryProvidersTask = Task.WhenAll(
            this.RunSecondaryProviderAsync(
                "claude",
                accountConnectionService,
                usageCache,
                this.claudeRefreshSignal,
                this.lifetimeCancellation.Token),
            this.RunSecondaryProviderAsync(
                "ollama",
                accountConnectionService,
                usageCache,
                this.ollamaRefreshSignal,
                this.lifetimeCancellation.Token));
        this.logger.Log(DiagnosticLevel.Information, "Lifecycle", "AI Usage Overlay started.");
    }

    private static ConnectionStatus ClassifyProviderError(Exception exception) => exception switch
    {
        UsageProviderException provider => provider.Kind switch
        {
            ProviderErrorKind.Authentication => ConnectionStatus.ReconnectRequired,
            ProviderErrorKind.AccessDenied => ConnectionStatus.ReconnectRequired,
            ProviderErrorKind.RateLimited => ConnectionStatus.TransientError,
            ProviderErrorKind.Network => ConnectionStatus.TransientError,
            ProviderErrorKind.UnsupportedFormat => ConnectionStatus.Unsupported,
            _ => ConnectionStatus.TransientError,
        },
        OllamaUsageException ollama => ollama.Kind switch
        {
            ProviderErrorKind.Authentication => ConnectionStatus.ReconnectRequired,
            ProviderErrorKind.AccessDenied => ConnectionStatus.ReconnectRequired,
            ProviderErrorKind.RateLimited => ConnectionStatus.TransientError,
            ProviderErrorKind.Network => ConnectionStatus.TransientError,
            ProviderErrorKind.UnsupportedFormat => ConnectionStatus.Unsupported,
            _ => ConnectionStatus.TransientError,
        },
        HttpRequestException => ConnectionStatus.TransientError,
        TaskCanceledException => ConnectionStatus.TransientError,
        _ => ConnectionStatus.TransientError,
    };

    private async Task RunSecondaryProviderAsync(
        string providerId,
        AccountConnectionService accountConnectionService,
        ProviderUsageCache cache,
        SemaphoreSlim refreshSignal,
        CancellationToken cancellationToken)
    {
        var schedule = new ProviderPollSchedule();
        try
        {
            if (providerId == "claude")
            {
                try
                {
                    var cached = await cache.LoadAsync(cancellationToken);
                    if (cached.Count > 0)
                    {
                        this.secondaryProviderRows = cached
                            .Where(snapshot => snapshot.Id is "claude" or "ollama")
                            .Select(snapshot => ToProviderRow(snapshot, isStale: true))
                            .ToArray();
                        _ = this.Dispatcher.BeginInvoke(this.ApplyCurrentProviderRows);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    this.logger?.Log(DiagnosticLevel.Warning, "UsageCache", "The display cache could not be loaded.", exception);
                }
            }

            while (true)
            {
                var (success, snapshot, generation) = await accountConnectionService.FetchAsync(providerId, cancellationToken);
                if (success && snapshot is not null)
                {
                    await this.usageCacheGate.WaitAsync(cancellationToken);
                    try
                    {
                        // 캐시 쓰기 직전에도 세대를 검사한다.
                        var current = accountConnectionService.GetState(providerId);
                        if (current.Generation == generation)
                        {
                            await cache.SaveAsync([snapshot], cancellationToken);
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        this.logger?.Log(DiagnosticLevel.Warning, "UsageCache", "The display cache could not be saved.", exception);
                    }
                    finally
                    {
                        this.usageCacheGate.Release();
                    }
                }

                var state = accountConnectionService.GetState(providerId);
                this.secondaryProviderRows = this.secondaryProviderRows
                    .Select(row => row.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)
                        ? ToProviderRow(providerId, state, snapshot)
                        : row)
                    .ToArray();
                _ = this.Dispatcher.BeginInvoke(this.ApplyCurrentProviderRows);
                var outcome = state.Status == ConnectionStatus.Connected
                    ? PollOutcome.Success
                    : PollOutcome.Failure;
                var hidden = !this.currentUsageOverlayState.IsVisible;
                _ = await refreshSignal.WaitAsync(
                    schedule.NextDelay(outcome, hidden),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            this.logger?.Log(DiagnosticLevel.Error, "Providers", "The secondary provider loop stopped unexpectedly.", exception);
        }
    }

    private static ProviderUsageRowState ToProviderRow(
        string providerId,
        AccountConnectionState state,
        ProviderUsageSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new ProviderUsageRowState(
                providerId,
                DisplayName(providerId),
                null,
                null,
                true,
                ShortWindowLabel: ShortWindowLabelFor(providerId));
        }

        return ToProviderRow(snapshot, state.Status != ConnectionStatus.Connected);
    }

    // The quota endpoint needs an unexpired Claude Code token, which is often not
    // what sits on disk. Claude Code's own transcripts are always there, so a
    // failed quota read degrades to counting them rather than to a blank row.
    // When the user registered a token in the account screen, it takes precedence
    // over the on-disk credential file.
    private async Task<ProviderUsageSnapshot> FetchClaudeUsageAsync(
        ClaudeCredentialSource credentials,
        ClaudeUsageClient client,
        ClaudeLocalUsageSource localUsage,
        string? registeredToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var claudeCredentials = string.IsNullOrWhiteSpace(registeredToken)
                ? credentials.Load()
                : new ClaudeCredentials(registeredToken.Trim());
            var quota = await client.FetchAsync(claudeCredentials, cancellationToken);
            this.claudeQuotaFallback.RecordQuotaSuccess(DateTimeOffset.UtcNow);
            return quota;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A transient failure must stay a failure: the coordinator then keeps the
            // last good percentage and the poll schedule backs off instead of hammering
            // a rate-limited endpoint every minute.
            if (!this.claudeQuotaFallback.ShouldCountLocalTranscripts(DateTimeOffset.UtcNow))
            {
                this.logger?.Log(
                    DiagnosticLevel.Warning,
                    "Providers",
                    $"claude: {exception.Message} Keeping the last quota reading.");
                throw;
            }

            this.logger?.Log(
                DiagnosticLevel.Information,
                "Providers",
                $"claude: quota unavailable ({exception.Message}) - counting local transcripts instead.");
            var local = localUsage.Read(cancellationToken);
            this.logger?.Log(
                DiagnosticLevel.Information,
                "Providers",
                $"claude: local tokens 5h={local.ShortTokens:N0} 7d={local.WeeklyTokens:N0} (messages 5h={local.ShortMessages:N0}).");
            return new ProviderUsageSnapshot(
                "claude",
                "Claude",
                UsageWindow.FromUsedPercent(0, null, TimeSpan.FromHours(5)),
                UsageWindow.FromUsedPercent(0, null, TimeSpan.FromDays(7)),
                local.ObservedAt,
                local.ShortTokens,
                local.WeeklyTokens);
        }
    }

    // A token-counted snapshot has no quota to draw a gauge from, so the percent
    // stays null and the row carries the counts instead.
    private static ProviderUsageRowState ToProviderRow(
        ProviderUsageSnapshot snapshot,
        bool isStale)
    {
        var countsOnly = snapshot.ShortTokens is not null || snapshot.WeeklyTokens is not null;
        return new ProviderUsageRowState(
            snapshot.Id,
            DisplayName(snapshot.Id),
            countsOnly ? null : (int)Math.Round(snapshot.ShortWindow.RemainingPercent),
            countsOnly ? null : (int)Math.Round(snapshot.WeeklyWindow.RemainingPercent),
            isStale,
            snapshot.ShortTokens,
            snapshot.WeeklyTokens,
            ShortWindowLabelFor(snapshot.Id));
    }

    private static string DisplayName(string providerId) => providerId.ToUpperInvariant();

    // Ollama Cloud's short window is not a five-hour quota, so the row carries its
    // own label instead of the renderer testing the provider id.
    private static string ShortWindowLabelFor(string providerId) =>
        providerId.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? "SHORT" : "5H";

    private void ApplyCurrentProviderRows()
    {
        if (this.usageOverlayWindow is null || Volatile.Read(ref this.shutdownStarted) != 0)
        {
            return;
        }

        this.currentUsageOverlayState = this.currentUsageOverlayState with
        {
            ProviderRows = this.ComposeProviderRows(this.currentUsageOverlayState),
        };
        this.usageOverlayWindow.Apply(this.currentUsageOverlayState, this.activePresentation);
    }

    private IReadOnlyList<ProviderUsageRowState> ComposeProviderRows(UsageOverlayState state) =>
    [
        new ProviderUsageRowState(
            "codex",
            "CODEX",
            state.ManaBar.RemainingPercent,
            state.HpBar.RemainingPercent,
            state.ManaBar.IsStale || state.HpBar.IsStale),
        .. this.secondaryProviderRows,
    ];

    private async Task RunCoordinatorAsync(
        ApplicationCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            this.logger?.Log(DiagnosticLevel.Error, "Coordinator", "The coordinator stopped unexpectedly.", exception);
            _ = this.Dispatcher.BeginInvoke(this.BeginShutdown);
        }
    }

    private void OnUsageOverlayStateChanged(UsageOverlayState state)
    {
        this.Dispatcher.BeginInvoke(() =>
        {
            if (Volatile.Read(ref this.shutdownStarted) != 0 || this.usageOverlayWindow is null)
            {
                return;
            }

            this.currentUsageOverlayState = state with { ProviderRows = this.ComposeProviderRows(state) };
            this.usageOverlayWindow.Apply(this.currentUsageOverlayState, this.activePresentation);
        });
    }

    private static AppearanceSettings ToAppearanceSettings(EffectiveAppearanceSettings appearance) =>
        new(
            appearance.OverlayWidth,
            appearance.OverlayHeight,
            appearance.GaugePaneWidth,
            appearance.GraphBarWidth,
            appearance.GraphBarGap,
            appearance.StatusStripeWidth);

    private void ApplySettingsPreview(AppSettings settings)
    {
        this.activeSettings = settings;
        if (this.usageOverlayWindow is null || this.positionController is null)
        {
            return;
        }

        var displayResolution = this.positionController.Resolve(settings);
        this.activePresentation = new OverlayPresentationSettings(
            settings.Colors,
            displayResolution.Appearance);
        this.usageOverlayWindow.Apply(this.currentUsageOverlayState, this.activePresentation);
        this.usageOverlayWindow.SetPlacement(displayResolution.Placement);
    }

    private void OnOverlayPositionChanged(CodexHp.Core.Positioning.PhysicalRect overlayBounds)
    {
        if (this.positionController is null)
        {
            return;
        }

        var location = this.positionController.Capture(overlayBounds);
        if (this.settingsWindowController?.Current is { } viewModel)
        {
            viewModel.PreviewLocation(location);
            return;
        }

        if (this.settingsCommitService is not null)
        {
            this.activeSettings = this.settingsCommitService.Commit(
                this.activeSettings with { Location = location });
        }
    }

    private void RefreshDisplayEnvironment()
    {
        if (Volatile.Read(ref this.shutdownStarted) != 0
            || this.usageOverlayWindow is null
            || this.positionController is null)
        {
            return;
        }

        try
        {
            var resolution = this.positionController.Resolve(this.activeSettings);
            this.activePresentation = new OverlayPresentationSettings(
                this.activeSettings.Colors,
                resolution.Appearance);
            this.usageOverlayWindow.Apply(this.currentUsageOverlayState, this.activePresentation);
            this.usageOverlayWindow.SetPlacement(resolution.Placement);
            this.ConstrainSettingsWindow(resolution.Placement.MonitorId, center: false);
        }
        catch (Exception exception)
        {
            this.logger?.Log(
                DiagnosticLevel.Warning,
                "Display",
                "The display environment could not be refreshed.",
                exception);
        }
    }

    private void OpenSettings()
    {
        if (!this.Dispatcher.CheckAccess())
        {
            this.Dispatcher.BeginInvoke(this.OpenSettings);
            return;
        }

        this.settingsWindowController?.Open();
    }

    private void OpenAccounts()
    {
        if (!this.Dispatcher.CheckAccess())
        {
            this.Dispatcher.BeginInvoke(this.OpenAccounts);
            return;
        }

        if (this.accountConnectionService is null)
        {
            return;
        }

        if (this.accountsWindow is null)
        {
            var viewModel = new AccountsViewModel(this.accountConnectionService);
            this.accountsWindow = new AccountsWindow(viewModel, this.accountConnectionService);
            this.accountsWindow.Closed += (_, _) => this.accountsWindow = null;
        }

        this.accountsWindow.Show();
        this.accountsWindow.Activate();
    }

    private void OpenProviderDetails(string? providerId)
    {
        if (!this.Dispatcher.CheckAccess())
        {
            this.Dispatcher.BeginInvoke(() => this.OpenProviderDetails(providerId));
            return;
        }

        if (this.usageDetailsWindow is null)
        {
            this.usageDetailsWindow = new UsageDetailsWindow();
            this.usageDetailsWindow.Closed += (_, _) => this.usageDetailsWindow = null;
        }

        this.usageDetailsWindow.Apply(this.currentUsageOverlayState.ProviderRows, providerId);
        this.usageDetailsWindow.Show();
        this.usageDetailsWindow.Activate();
    }

    private void RequestProviderRefresh()
    {
        if (this.claudeRefreshSignal.CurrentCount == 0)
        {
            this.claudeRefreshSignal.Release();
        }

        if (this.ollamaRefreshSignal.CurrentCount == 0)
        {
            this.ollamaRefreshSignal.Release();
        }

        if (this.primaryCoordinator is not null && this.lifetimeCancellation is not null)
        {
            _ = this.RefreshPrimaryProviderAsync(
                this.primaryCoordinator,
                this.lifetimeCancellation.Token);
        }
    }

    private async Task RefreshPrimaryProviderAsync(
        ApplicationCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.PollUsageOnceAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ToggleOverlayPositionLock()
    {
        if (this.usageOverlayWindow is null)
        {
            return;
        }

        this.usageOverlayWindow.SetOverlayPositionChangeMode(
            !this.usageOverlayWindow.IsOverlayPositionChangeMode);
    }

    private void ShowSettingsWindow(SettingsWindowViewModel viewModel)
    {
        var window = new SettingsWindow(viewModel);
        this.settingsWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(this.settingsWindow, window))
            {
                this.settingsWindow = null;
            }
        };
        window.Show();
        if (this.positionController is { } controller)
        {
            var resolution = controller.Resolve(this.activeSettings);
            this.ConstrainSettingsWindow(resolution.Placement.MonitorId, center: true);
        }
    }

    private void ConstrainSettingsWindow(string monitorId, bool center)
    {
        if (this.settingsWindow is null || this.positionController is null)
        {
            return;
        }

        var monitor = this.positionController.GetDisplays()
            .Select(display => display.Monitor)
            .FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                monitorId,
                StringComparison.OrdinalIgnoreCase));
        if (monitor is not null)
        {
            this.settingsWindow.ConstrainToWorkArea(monitor, center);
        }
    }

    private void ActivateSettingsWindow(SettingsWindowViewModel viewModel)
    {
        if (this.settingsWindow is null)
        {
            return;
        }

        if (this.settingsWindow.WindowState == System.Windows.WindowState.Minimized)
        {
            this.settingsWindow.WindowState = System.Windows.WindowState.Normal;
        }

        this.settingsWindow.Activate();
    }

    private void BeginShutdown()
    {
        if (!this.Dispatcher.CheckAccess())
        {
            this.Dispatcher.BeginInvoke(this.BeginShutdown);
            return;
        }

        if (Interlocked.Exchange(ref this.shutdownStarted, 1) != 0)
        {
            return;
        }

        _ = this.ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        this.trayIcon?.Dispose();
        this.trayIcon = null;
        this.lifetimeCancellation?.Cancel();
        if (this.coordinatorTask is not null)
        {
            await this.coordinatorTask;
        }
        if (this.secondaryProvidersTask is not null)
        {
            await this.secondaryProvidersTask;
        }

        this.settingsWindowController?.Current?.Cancel(SettingsCancelTrigger.WindowClose);
        this.usageOverlayWindow?.CloseForShutdown();
        this.logger?.Log(DiagnosticLevel.Information, "Lifecycle", "AI Usage Overlay stopped.");
        this.DisposeResources();
        this.Shutdown();
    }

    private void DisposeResources()
    {
        this.lifetimeCancellation?.Cancel();
        this.lifetimeCancellation?.Dispose();
        this.lifetimeCancellation = null;
        this.trayIcon?.Dispose();
        this.trayIcon = null;
        this.displayEnvironmentWatcher?.Dispose();
        this.displayEnvironmentWatcher = null;
        this.usageOverlayWindow?.CloseForShutdown();
        this.usageOverlayWindow = null;
        this.httpClient?.Dispose();
        this.httpClient = null;
        this.ollamaHttpClient?.Dispose();
        this.ollamaHttpClient = null;
        this.singleInstance?.Dispose();
        this.singleInstance = null;
    }
}
