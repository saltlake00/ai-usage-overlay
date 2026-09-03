namespace CodexHp.App.Presentation;

public enum TrayMouseButton
{
    Other,
    Left,
    Right,
}

public enum TrayMenuCommand
{
    Refresh,
    TogglePositionLock,
    Options,
    Accounts,
    Exit,
}

public enum TrayIconAsset
{
    CodexHpGauge,
}

public sealed record TrayMenuItem(TrayMenuCommand Command, string Text);

internal static class TrayIconMessageRouter
{
    private const uint LeftButtonUp = 0x0202;
    private const uint RightButtonUp = 0x0205;
    private const uint RefreshCommandId = 1;
    private const uint TogglePositionLockCommandId = 2;
    private const uint OptionsCommandId = 3;
    private const uint AccountsCommandId = 4;
    private const uint ExitCommandId = 5;

    public static TrayMouseButton RouteMouseButton(uint nativeMessage) => nativeMessage switch
    {
        LeftButtonUp => TrayMouseButton.Left,
        RightButtonUp => TrayMouseButton.Right,
        _ => TrayMouseButton.Other,
    };

    public static TrayMenuCommand? RouteMenuCommand(uint nativeCommand) => nativeCommand switch
    {
        RefreshCommandId => TrayMenuCommand.Refresh,
        TogglePositionLockCommandId => TrayMenuCommand.TogglePositionLock,
        OptionsCommandId => TrayMenuCommand.Options,
        AccountsCommandId => TrayMenuCommand.Accounts,
        ExitCommandId => TrayMenuCommand.Exit,
        _ => null,
    };
}

public interface ITrayIconView : IDisposable
{
    event Action<TrayMouseButton>? MouseClicked;

    event Action<TrayMenuCommand>? MenuCommandInvoked;

    bool Visible { get; set; }

    TrayIconAsset IconAsset { get; }

    string ToolTipText { get; }

    IReadOnlyList<TrayMenuItem> MenuItems { get; }
}

public sealed class TrayIconController : IDisposable
{
    public static IReadOnlyList<TrayMenuItem> DefaultMenuItems { get; } =
    [
        new TrayMenuItem(TrayMenuCommand.Refresh, "Refresh now"),
        new TrayMenuItem(TrayMenuCommand.TogglePositionLock, "Unlock position"),
        new TrayMenuItem(TrayMenuCommand.Options, "Options"),
        new TrayMenuItem(TrayMenuCommand.Accounts, "계정 연동"),
        new TrayMenuItem(TrayMenuCommand.Exit, "Exit"),
    ];

    private readonly ITrayIconView view;
    private readonly Action openOptions;
    private readonly Action exit;
    private readonly Action refresh;
    private readonly Action togglePositionLock;
    private readonly Action openAccounts;
    private bool disposed;

    public TrayIconController(Action openOptions, Action exit)
        : this(new WindowsTrayIconView(), openOptions, exit, () => { }, () => { }, () => { })
    {
    }

    public TrayIconController(
        Action openOptions,
        Action exit,
        Action refresh,
        Action togglePositionLock)
        : this(new WindowsTrayIconView(), openOptions, exit, refresh, togglePositionLock, () => { })
    {
    }

    public TrayIconController(
        Action openOptions,
        Action exit,
        Action refresh,
        Action togglePositionLock,
        Action openAccounts)
        : this(new WindowsTrayIconView(), openOptions, exit, refresh, togglePositionLock, openAccounts)
    {
    }

    public TrayIconController(ITrayIconView view, Action openOptions, Action exit)
        : this(view, openOptions, exit, () => { }, () => { }, () => { })
    {
    }

    public TrayIconController(
        ITrayIconView view,
        Action openOptions,
        Action exit,
        Action refresh,
        Action togglePositionLock)
        : this(view, openOptions, exit, refresh, togglePositionLock, () => { })
    {
    }

    public TrayIconController(
        ITrayIconView view,
        Action openOptions,
        Action exit,
        Action refresh,
        Action togglePositionLock,
        Action openAccounts)
    {
        this.view = view ?? throw new ArgumentNullException(nameof(view));
        this.openOptions = openOptions ?? throw new ArgumentNullException(nameof(openOptions));
        this.exit = exit ?? throw new ArgumentNullException(nameof(exit));
        this.refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        this.togglePositionLock = togglePositionLock ?? throw new ArgumentNullException(nameof(togglePositionLock));
        this.openAccounts = openAccounts ?? throw new ArgumentNullException(nameof(openAccounts));
        this.view.MouseClicked += this.OnMouseClicked;
        this.view.MenuCommandInvoked += this.OnMenuCommandInvoked;
        this.view.Visible = true;
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.view.MouseClicked -= this.OnMouseClicked;
        this.view.MenuCommandInvoked -= this.OnMenuCommandInvoked;
        this.view.Visible = false;
        this.view.Dispose();
    }

    private void OnMouseClicked(TrayMouseButton button)
    {
        if (button == TrayMouseButton.Left)
        {
            this.openOptions();
        }
    }

    private void OnMenuCommandInvoked(TrayMenuCommand command)
    {
        switch (command)
        {
            case TrayMenuCommand.Refresh:
                this.refresh();
                break;
            case TrayMenuCommand.TogglePositionLock:
                this.togglePositionLock();
                break;
            case TrayMenuCommand.Options:
                this.openOptions();
                break;
            case TrayMenuCommand.Accounts:
                this.openAccounts();
                break;
            case TrayMenuCommand.Exit:
                this.exit();
                break;
        }
    }
}
