using System.Windows;
using System.Windows.Controls;
using FileTinder.Services;

namespace FileTinder.Views;

public partial class SpeedupView : UserControl
{
    private List<StartupItem> _items = [];

    public SpeedupView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            try { PopulateBootTab(); }
            catch { /* registry unavailable — safe to ignore */ }
        };
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag as string;

        // Reset all tabs to inactive
        TabEasyBtn.Style    = (Style)Resources["SpeedupTabBtn"];
        TabBootBtn.Style    = (Style)Resources["SpeedupTabBtn"];
        TabManualBtn.Style  = (Style)Resources["SpeedupTabBtn"];
        TabHistoryBtn.Style = (Style)Resources["SpeedupTabBtn"];
        btn.Style = (Style)Resources["SpeedupTabBtnActive"];

        EasyPanel.Visibility    = Visibility.Collapsed;
        BootPanel.Visibility    = Visibility.Collapsed;
        ManualPanel.Visibility  = Visibility.Collapsed;
        HistoryPanel.Visibility = Visibility.Collapsed;

        switch (tag)
        {
            case "Easy":
                EasyPanel.Visibility = Visibility.Visible;
                break;
            case "Boot":
                BootPanel.Visibility = Visibility.Visible;
                PopulateBootTab();
                break;
            case "Manual":
                ManualPanel.Visibility = Visibility.Visible;
                LoadManualList();
                break;
            case "History":
                HistoryPanel.Visibility = Visibility.Visible;
                LoadHistory();
                break;
        }
    }

    // ── Easy Speedup ──────────────────────────────────────────────────────────

    private async void ScanNow_Click(object sender, RoutedEventArgs e)
    {
        EasyIdle.Visibility     = Visibility.Collapsed;
        EasyResults.Visibility  = Visibility.Collapsed;
        EasyScanning.Visibility = Visibility.Visible;
        OptimizeBanner.Visibility = Visibility.Collapsed;

        _items = await Task.Run(() =>
        {
            try { return SpeedupService.GetStartupItems(); }
            catch { return new List<StartupItem>(); }
        });

        EasyScanning.Visibility = Visibility.Collapsed;

        var high = _items.Count(i => i.Impact == PerformanceImpact.High);
        var med  = _items.Count(i => i.Impact == PerformanceImpact.Medium);
        var low  = _items.Count(i => i.Impact == PerformanceImpact.Low || i.Impact == PerformanceImpact.Unknown);

        HighCountText.Text = high.ToString();
        MedCountText.Text  = med.ToString();
        LowCountText.Text  = low.ToString();

        var highItems = _items.Where(i => i.Impact == PerformanceImpact.High).ToList();
        var medItems  = _items.Where(i => i.Impact == PerformanceImpact.Medium && i.IsEnabled && i.CanToggle).ToList();

        if (highItems.Count > 0)
        {
            // High-impact items found — standard optimize flow
            OptimizeBtn.IsEnabled  = true;
            OptimizeBtnText.Text   = "⚡ Disable all high-impact items";
            HighImpactList.ItemsSource  = highItems;
            HighImpactSection.Visibility = Visibility.Visible;
            OptimizeGoodBanner.Visibility = Visibility.Collapsed;
        }
        else if (medItems.Count > 0)
        {
            // No high items but medium items exist — offer to manage those
            OptimizeBtn.IsEnabled  = true;
            OptimizeBtnText.Text   = $"⚡ Disable {medItems.Count} medium-impact item{(medItems.Count == 1 ? "" : "s")}";
            HighImpactSection.Visibility = Visibility.Collapsed;
            OptimizeGoodBanner.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Nothing to do — startup is clean
            OptimizeBtn.IsEnabled  = false;
            OptimizeBtnText.Text   = "⚡ Nothing to optimize";
            HighImpactSection.Visibility = Visibility.Collapsed;
            OptimizeGoodBanner.Visibility = Visibility.Visible;
        }

        EasyResults.Visibility = Visibility.Visible;
    }

    private void Optimize_Click(object sender, RoutedEventArgs e)
    {
        // Disable high-impact items first, then medium if none high
        var highEnabled = _items
            .Where(i => i.Impact == PerformanceImpact.High && i.IsEnabled && i.CanToggle)
            .ToList();
        var targetItems = highEnabled.Count > 0
            ? highEnabled
            : _items.Where(i => i.Impact == PerformanceImpact.Medium && i.IsEnabled && i.CanToggle).ToList();

        int disabled = 0;
        foreach (var item in targetItems)
        {
            if (SpeedupService.SetItemEnabled(item, false))
            {
                disabled++;
                SpeedupService.AddHistory(new SpeedupHistoryEntry
                {
                    Action   = "Disabled",
                    ItemName = item.Name,
                    Details  = $"{item.ImpactLabel}-impact item disabled via Easy Speedup. Location: {item.LocationLabel}"
                });
            }
        }

        OptimizeBanner.Visibility = Visibility.Visible;
        OptimizeBannerText.Text = disabled > 0
            ? $"✅ Disabled {disabled} item{(disabled == 1 ? "" : "s")}. Restart to see improvement."
            : "ℹ️ No eligible items to disable (some may require admin rights).";

        OptimizeBtn.IsEnabled = false;

        // Refresh lists
        HighImpactList.ItemsSource = null;
        HighImpactList.ItemsSource = _items.Where(i => i.Impact == PerformanceImpact.High).ToList();
    }

    // ── My Boot Time ──────────────────────────────────────────────────────────

    private void PopulateBootTab()
    {
        var uptime = SpeedupService.GetUptime();
        UptimeText.Text    = SpeedupService.FormatUptime(uptime);
        UptimeSubText.Text = $"Last restarted: {DateTime.Now - uptime:dd\\d\\ hh\\:mm} ago";

        if (_items.Count == 0)
            _items = SpeedupService.GetStartupItems();

        var enabledCount = _items.Count(i => i.IsEnabled);
        StartupCountText.Text = enabledCount.ToString();

        var highEnabled = _items.Count(i => i.Impact == PerformanceImpact.High && i.IsEnabled);
        if (highEnabled == 0)
        {
            BootHealthText.Text      = "Good ✅";
            BootHealthText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981"));
            BootWarning.Visibility   = Visibility.Collapsed;
        }
        else
        {
            BootHealthText.Text      = "Fair ⚠️";
            BootHealthText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B"));

            BootWarningTitle.Text  = $"{highEnabled} high-impact startup program{(highEnabled == 1 ? "" : "s")} enabled";
            BootWarningDetail.Text = "These programs slow down your boot time. Use Easy Speedup or Manual to disable them.";
            BootWarning.Visibility = Visibility.Visible;
        }
    }

    private void BootTabScan_Click(object sender, RoutedEventArgs e)
    {
        _items = SpeedupService.GetStartupItems();
        PopulateBootTab();
    }

    // ── Manual ────────────────────────────────────────────────────────────────

    private void LoadManualList()
    {
        if (_items.Count == 0)
            _items = SpeedupService.GetStartupItems();

        ManualList.ItemsSource = null;
        ManualList.ItemsSource = _items.OrderBy(i => i.Impact).ThenBy(i => i.Name).ToList();
    }

    private void ManualRefresh_Click(object sender, RoutedEventArgs e)
    {
        _items = SpeedupService.GetStartupItems();
        LoadManualList();
    }

    private void ManualToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StartupItem item) return;

        bool newState = !item.IsEnabled;
        if (SpeedupService.SetItemEnabled(item, newState))
        {
            SpeedupService.AddHistory(new SpeedupHistoryEntry
            {
                Action   = newState ? "Enabled" : "Disabled",
                ItemName = item.Name,
                Details  = $"{item.LocationLabel} — changed via Manual manager"
            });
        }

        // Reload list so button text updates
        ManualList.ItemsSource = null;
        ManualList.ItemsSource = _items.OrderBy(i => i.Impact).ThenBy(i => i.Name).ToList();
    }

    // ── History ───────────────────────────────────────────────────────────────

    private void LoadHistory()
    {
        var history = SpeedupService.GetHistory();
        HistoryList.ItemsSource     = history;
        HistoryEmptyText.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
