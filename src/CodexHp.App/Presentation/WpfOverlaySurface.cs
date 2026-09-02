using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexHp.App.Presentation;

internal sealed class WpfOverlaySurface : IDisposable
{
    private readonly Window window;
    private readonly System.Windows.Controls.Image image;
    private readonly HwndSource source;
    private readonly HwndSourceHook messageHook;
    private readonly NativeOverlayTooltip statusStripeTooltip;
    private string? statusStripeTooltipText;
    private bool isVisible;
    private bool isStatusStripeTooltipSuppressed;
    private bool isDisposed;
    private int providerRowCount;

    internal WpfOverlaySurface(
        int initialWidth,
        int initialHeight,
        HwndSourceHook messageHook)
    {
        this.messageHook = messageHook ?? throw new ArgumentNullException(nameof(messageHook));
        this.image = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(this.image, BitmapScalingMode.NearestNeighbor);

        this.window = new Window
        {
            Title = "AI Usage Overlay",
            Width = initialWidth,
            Height = initialHeight,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Content = this.image,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };
        this.window.PreviewMouseLeftButtonDown += this.OnPreviewMouseLeftButtonDown;

        this.WindowHandle = new WindowInteropHelper(this.window).EnsureHandle();
        AltTabWindowStyle.Apply(this.WindowHandle);
        this.statusStripeTooltip = new NativeOverlayTooltip(this.WindowHandle);
        this.source = HwndSource.FromHwnd(this.WindowHandle)
            ?? throw new InvalidOperationException("The WPF overlay surface source is unavailable.");
        this.source.AddHook(this.messageHook);
    }

    internal nint WindowHandle { get; }

    internal event EventHandler? OpenSettingsRequested;

    internal event Action<int?>? ProviderDetailsRequested;

    internal bool ShowInTaskbar => this.window.ShowInTaskbar;

    internal bool IsStatusStripeTooltipEnabled => this.statusStripeTooltip.IsEnabled;

    internal nint StatusStripeTooltipWindowHandle => this.statusStripeTooltip.WindowHandle;

    internal void ProcessLeftButtonDown(int clickCount)
    {
        if (clickCount == 2)
        {
            this.OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static int ResolveProviderRow(double y, double height, int rowCount)
    {
        if (rowCount <= 0 || height <= 0)
        {
            return -1;
        }

        return Math.Clamp((int)(y / height * rowCount), 0, rowCount - 1);
    }

    internal bool Present(UsageOverlayLayout layout)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);
        this.providerRowCount = layout.Commands.Count(command =>
            command.Role == OverlayElementRole.ProviderLabel);
        this.image.Source = GdiBitmapSourceRenderer.Render(layout);
        return true;
    }

    internal void UpdateStatusStripeTooltip(string? text)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);
        this.statusStripeTooltipText = text;
        this.ApplyStatusStripeTooltip();
    }

    internal void SetStatusStripeTooltipSuppressed(bool isSuppressed)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);
        this.isStatusStripeTooltipSuppressed = isSuppressed;
        this.ApplyStatusStripeTooltip();
    }

    internal void SetVisibility(bool isVisible)
    {
        ObjectDisposedException.ThrowIf(this.isDisposed, this);
        if (isVisible)
        {
            this.isVisible = true;
            this.window.Show();
            this.ApplyStatusStripeTooltip();
            return;
        }

        this.isVisible = false;
        this.ApplyStatusStripeTooltip();
        this.window.Hide();
    }

    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;
        this.window.PreviewMouseLeftButtonDown -= this.OnPreviewMouseLeftButtonDown;
        this.source.RemoveHook(this.messageHook);
        this.statusStripeTooltip.Dispose();
        this.window.Close();
        this.OpenSettingsRequested = null;
        this.ProviderDetailsRequested = null;
    }

    private void OnPreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs eventArgs)
    {
        if (this.providerRowCount > 0)
        {
            if (eventArgs.ClickCount == 2)
            {
                this.ProviderDetailsRequested?.Invoke(null);
            }
            else if (eventArgs.ClickCount == 1)
            {
                var point = eventArgs.GetPosition(this.window);
                this.ProviderDetailsRequested?.Invoke(ResolveProviderRow(
                    point.Y,
                    this.window.ActualHeight,
                    this.providerRowCount));
            }

            return;
        }

        this.ProcessLeftButtonDown(eventArgs.ClickCount);
    }

    private void ApplyStatusStripeTooltip() =>
        this.statusStripeTooltip.Update(
            this.isVisible && !this.isStatusStripeTooltipSuppressed
                ? this.statusStripeTooltipText
                : null);
}
