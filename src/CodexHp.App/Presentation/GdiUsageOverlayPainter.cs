using CodexHp.App.Infrastructure;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation;

internal static class GdiUsageOverlayPainter
{
    private static readonly ColorValue Background = ColorValue.Parse("#18181C");
    private static readonly ColorValue TrackBorderColor = ColorValue.Parse("#0B0B0D");
    private static readonly nint InvalidGraphicObject = new(-1);

    internal static void Paint(nint deviceContext, UsageOverlayLayout layout)
    {
        if (deviceContext == nint.Zero)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(layout);
        foreach (var command in layout.Commands)
        {
            var color = Blend(command.Color, Background, command.Opacity);
            if (command.Kind == OverlayDrawKind.Rectangle)
            {
                if (IsGaugeFillRole(command.Role))
                {
                    PaintGaugeFill(deviceContext, command.Bounds, color);
                }
                else if (IsGaugeTrackRole(command.Role))
                {
                    PaintGaugeTrack(deviceContext, command.Bounds, color);
                }
                else
                {
                    FillRectangle(deviceContext, command.Bounds, color);
                }
            }
            else
            {
                DrawText(deviceContext, command, color);
            }
        }
    }

    // Scoped to the provider columns (Codex/Claude/Ollama) - the row people
    // actually look at. The legacy single-row Mana/Hp gauge keeps its flat
    // fill: acceptance tests pixel-sample it against an exact solid color,
    // and it is only ever shown briefly before provider data arrives.
    private static bool IsGaugeFillRole(OverlayElementRole role) => role
        is OverlayElementRole.ProviderShortFill
        or OverlayElementRole.ProviderWeeklyFill;

    private static bool IsGaugeTrackRole(OverlayElementRole role) => role
        is OverlayElementRole.ProviderShortTrack
        or OverlayElementRole.ProviderWeeklyTrack;

    // A flat fill reads as a sticker; a top-lit gradient reads as a game HP bar.
    // GradientFill is one native call per bar - the same cost class as the
    // FillRect it replaces, so this adds no meaningful CPU cost.
    private static void PaintGaugeFill(nint deviceContext, LayoutRect bounds, ColorValue color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var highlight = Blend(new ColorValue(255, 255, 255), color, 0.35);
        var vertices = new[]
        {
            ToVertex(bounds.Left, bounds.Top, highlight),
            ToVertex(bounds.Right, bounds.Bottom, color),
        };
        var mesh = new[] { new NativeMethods.GradientRect { UpperLeft = 0, LowerRight = 1 } };
        if (!NativeMethods.GradientFill(deviceContext, vertices, 2, mesh, 1, NativeMethods.GradientFillRectV))
        {
            FillRectangle(deviceContext, bounds, color);
        }
    }

    // Tracks get a hairline border so the fill reads as sitting inside a slot
    // instead of floating on the background. Skipped under 4px - the provider
    // mini-bars are 2px tall and a 1px inset would leave nothing to fill.
    private static void PaintGaugeTrack(nint deviceContext, LayoutRect bounds, ColorValue color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (bounds.Height < 4)
        {
            FillRectangle(deviceContext, bounds, color);
            return;
        }

        FillRectangle(deviceContext, bounds, TrackBorderColor);
        var inset = bounds with
        {
            Left = bounds.Left + 1,
            Top = bounds.Top + 1,
            Width = Math.Max(1, bounds.Width - 2),
            Height = Math.Max(1, bounds.Height - 2),
        };
        FillRectangle(deviceContext, inset, color);
    }

    private static NativeMethods.TriVertex ToVertex(int x, int y, ColorValue color) => new()
    {
        X = x,
        Y = y,
        Red = (ushort)(color.Red << 8),
        Green = (ushort)(color.Green << 8),
        Blue = (ushort)(color.Blue << 8),
        Alpha = 0,
    };

    internal static uint ToColorRef(ColorValue color) =>
        color.Red | ((uint)color.Green << 8) | ((uint)color.Blue << 16);

    internal static ColorValue Blend(
        ColorValue foreground,
        ColorValue background,
        double opacity)
    {
        var alpha = Math.Clamp(opacity, 0, 1);
        return new ColorValue(
            BlendComponent(foreground.Red, background.Red, alpha),
            BlendComponent(foreground.Green, background.Green, alpha),
            BlendComponent(foreground.Blue, background.Blue, alpha));
    }

    private static byte BlendComponent(byte foreground, byte background, double opacity) =>
        checked((byte)Math.Round(
            (foreground * opacity) + (background * (1 - opacity)),
            MidpointRounding.AwayFromZero));

    private static void FillRectangle(
        nint deviceContext,
        LayoutRect bounds,
        ColorValue color)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var brush = NativeMethods.CreateSolidBrush(ToColorRef(color));
        if (brush == nint.Zero)
        {
            return;
        }

        try
        {
            var rectangle = ToNativeRect(bounds);
            _ = NativeMethods.FillRect(deviceContext, ref rectangle, brush);
        }
        finally
        {
            _ = NativeMethods.DeleteObject(brush);
        }
    }

    private static void DrawText(
        nint deviceContext,
        OverlayDrawCommand command,
        ColorValue color)
    {
        if (command.Bounds.Width <= 0 || command.Bounds.Height <= 0)
        {
            return;
        }

        _ = NativeMethods.SetBkMode(deviceContext, NativeMethods.TransparentBackgroundMode);
        _ = NativeMethods.SetTextColor(deviceContext, ToColorRef(color));
        var font = NativeMethods.CreateFontW(
            -Math.Max(1, command.FontSize),
            0,
            0,
            0,
            NativeMethods.FontWeightSemiBold,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            "Segoe UI Variable Text");
        var previousFont = font == nint.Zero
            ? nint.Zero
            : NativeMethods.SelectObject(deviceContext, font);

        try
        {
            var rectangle = ToNativeRect(command.Bounds);
            _ = NativeMethods.DrawTextW(
                deviceContext,
                command.Text ?? string.Empty,
                -1,
                ref rectangle,
                NativeMethods.DrawTextCenter
                    | NativeMethods.DrawTextVerticalCenter
                    | NativeMethods.DrawTextSingleLine);
        }
        finally
        {
            if (previousFont != nint.Zero && previousFont != InvalidGraphicObject)
            {
                _ = NativeMethods.SelectObject(deviceContext, previousFont);
            }

            if (font != nint.Zero)
            {
                _ = NativeMethods.DeleteObject(font);
            }
        }
    }

    private static NativeMethods.NativeRect ToNativeRect(LayoutRect bounds) => new()
    {
        Left = bounds.Left,
        Top = bounds.Top,
        Right = bounds.Right,
        Bottom = bounds.Bottom,
    };
}
