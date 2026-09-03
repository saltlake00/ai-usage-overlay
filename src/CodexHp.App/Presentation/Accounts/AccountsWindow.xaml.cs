using System.Windows;
using System.Windows.Controls;
using CodexHp.App.Accounts;

namespace CodexHp.App.Presentation.Accounts;

public partial class AccountsWindow : Window
{
    private readonly AccountsViewModel viewModel;
    private readonly AccountConnectionService service;
    private readonly Func<string, string?> readEnvironment;
    private readonly Action<string, string> setEnvironment;

    internal AccountsWindow(
        AccountsViewModel viewModel,
        AccountConnectionService service,
        Func<string, string?>? readEnvironment = null,
        Action<string, string>? setEnvironment = null)
    {
        this.InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
        this.setEnvironment = setEnvironment ?? ((name, value) =>
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User));
        this.DataContext = this.viewModel;
        this.viewModel.CloseRequested += this.OnViewModelCloseRequested;
        this.RefreshAllStatus();
    }

    private void OnViewModelCloseRequested()
    {
        this.Close();
    }

    private void RefreshAllStatus()
    {
        this.RefreshStatus("codex", this.CodexStatusText, this.CodexConnectButton, this.CodexDisconnectButton);
        this.RefreshStatus("claude", this.ClaudeStatusText, this.ClaudeConnectButton, this.ClaudeDisconnectButton);
        this.RefreshStatus("ollama", this.OllamaStatusText, this.OllamaConnectButton, this.OllamaDisconnectButton);
    }

    private void RefreshStatus(string providerId, TextBlock statusText, Button connectButton, Button disconnectButton)
    {
        var status = this.viewModel.GetStatus(providerId);
        statusText.Text = StatusLabel(status);
        var connecting = this.viewModel.IsConnecting(providerId);
        connectButton.IsEnabled = !connecting;
        disconnectButton.IsEnabled = !connecting && status != ConnectionStatus.Disconnected;
    }

    private static string StatusLabel(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Disconnected => "미연결",
        ConnectionStatus.Connecting => "확인 중…",
        ConnectionStatus.Connected => "연결됨",
        ConnectionStatus.ReconnectRequired => "재연결 필요",
        ConnectionStatus.TransientError => "일시 오류",
        ConnectionStatus.Unsupported => "지원 안 됨",
        _ => "미연결",
    };

    private async void OnCodexConnect(object sender, RoutedEventArgs e)
    {
        await this.ConnectAsync("codex", null);
    }

    private async void OnClaudeConnect(object sender, RoutedEventArgs e)
    {
        var secret = this.ClaudePasswordBox.Password;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        await this.ConnectAsync("claude", secret);
        this.ClaudePasswordBox.Clear();
    }

    private async void OnOllamaConnect(object sender, RoutedEventArgs e)
    {
        var secret = this.OllamaPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        await this.ConnectAsync("ollama", secret);
        this.OllamaPasswordBox.Clear();
    }

    private async Task ConnectAsync(string providerId, string? secret)
    {
        try
        {
            await this.viewModel.ConnectAsync(providerId, secret, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            this.RefreshAllStatus();
        }
    }

    private async void OnCodexDisconnect(object sender, RoutedEventArgs e) =>
        await this.DisconnectAsync("codex");

    private async void OnClaudeDisconnect(object sender, RoutedEventArgs e) =>
        await this.DisconnectAsync("claude");

    private async void OnOllamaDisconnect(object sender, RoutedEventArgs e) =>
        await this.DisconnectAsync("ollama");

    private async Task DisconnectAsync(string providerId)
    {
        try
        {
            await this.viewModel.DisconnectAsync(providerId, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            this.RefreshAllStatus();
        }
    }

    private void OnClaudePasswordChanged(object sender, RoutedEventArgs e) =>
        this.ClaudeConnectButton.IsEnabled = !string.IsNullOrWhiteSpace(this.ClaudePasswordBox.Password);

    private void OnOllamaPasswordChanged(object sender, RoutedEventArgs e) =>
        this.OllamaConnectButton.IsEnabled = !string.IsNullOrWhiteSpace(this.OllamaPasswordBox.Password);

    private void OnImportEnvironment(object sender, RoutedEventArgs e)
    {
        var imported = new System.Text.StringBuilder();
        var claude = this.readEnvironment("CLAUDE_AI_SESSION_KEY");
        if (!string.IsNullOrWhiteSpace(claude))
        {
            this.service.ConnectAsync("claude", claude, CancellationToken.None).GetAwaiter().GetResult();
            imported.Append("Claude ");
        }

        var ollama = this.readEnvironment("OLLAMA_SESSION_COOKIE");
        if (!string.IsNullOrWhiteSpace(ollama))
        {
            this.service.ConnectAsync("ollama", ollama, CancellationToken.None).GetAwaiter().GetResult();
            imported.Append("Ollama ");
        }

        this.ImportResultText.Text = imported.Length == 0
            ? "가져올 환경변수가 없습니다."
            : $"{imported.ToString().Trim()} 설정을 가져왔습니다.";
        this.RefreshAllStatus();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        this.viewModel.Close();
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        this.viewModel.Close();
    }
}
