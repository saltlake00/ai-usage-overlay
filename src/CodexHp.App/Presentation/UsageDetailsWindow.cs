using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexHp.Core.Domain;

namespace CodexHp.App.Presentation;

internal sealed class UsageDetailsWindow : Window
{
    private readonly StackPanel rows;

    public UsageDetailsWindow()
    {
        this.Title = "AI Usage Overlay - Details";
        this.Width = 360;
        this.SizeToContent = SizeToContent.Height;
        this.ResizeMode = ResizeMode.NoResize;
        this.ShowInTaskbar = false;
        this.Topmost = true;
        this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Background = new SolidColorBrush(Color.FromRgb(24, 24, 28));
        this.rows = new StackPanel { Margin = new Thickness(16) };
        this.Content = this.rows;
    }

    public void Apply(IReadOnlyList<ProviderUsageRowState> providerRows, string? providerId)
    {
        this.rows.Children.Clear();
        foreach (var row in providerRows.Where(row =>
                     providerId is null || row.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            var name = row.Id switch
            {
                "codex" => "Codex",
                "claude" => "Claude",
                "ollama" => "Ollama Cloud",
                _ => row.Id,
            };
            var shortLabel = row.Id == "ollama" ? "단기" : "5시간";
            var freshness = row.IsStale ? " · 이전 값" : string.Empty;
            this.rows.Children.Add(new TextBlock
            {
                Text = $"{name}\n{shortLabel} 남음 {Format(row.ShortRemainingPercent)}  ·  주간 남음 {Format(row.WeeklyRemainingPercent)}{freshness}",
                Foreground = Brushes.White,
                FontSize = 14,
                Margin = new Thickness(0, 4, 0, 10),
            });
        }
    }

    private static string Format(int? percent) => percent is null ? "--" : $"{percent}%";
}
