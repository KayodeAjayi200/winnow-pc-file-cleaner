using System.Windows;
using System.Windows.Controls;
using FileTinder.Services;

namespace FileTinder.Views;

public partial class CleanupView : UserControl
{
    // ── State ─────────────────────────────────────────────────────────────────

    private enum Phase { Ready, Scanning, Scanned, Cleaning, Done }

    private Phase _phase = Phase.Ready;
    private IReadOnlyList<CleanupScanResult>? _scanResults;
    private CancellationTokenSource? _cts;

    // ── Constructor ───────────────────────────────────────────────────────────

    public CleanupView()
    {
        InitializeComponent();
    }

    // ── Button handler ────────────────────────────────────────────────────────

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_phase)
        {
            case Phase.Ready:
            case Phase.Scanned:
                await RunScanAsync();
                break;

            case Phase.Done:
                await RunScanAsync(); // re-scan
                break;

            default: /* scanning/cleaning — ignore */ break;
        }
    }

    // ── Scan ──────────────────────────────────────────────────────────────────

    private async Task RunScanAsync()
    {
        _phase = Phase.Scanning;
        _cts   = new CancellationTokenSource();

        SetUiScanning();

        try
        {
            var targets = CleanupService.GetTargets()
                .Where(t => IsSelected(t.Category))
                .ToList();

            var progress = new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusText.Text = $"Scanning: {msg}…"));

            _scanResults = await CleanupService.ScanAsync(targets, progress, _cts.Token);
            _phase = Phase.Scanned;
            SetUiScanned();
        }
        catch (OperationCanceledException) { SetUiReady(); }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            StatusText.Visibility = Visibility.Visible;
            ScanProgress.Visibility = Visibility.Collapsed;
            ActionLabel.Text = "Retry Scan";
            ActionIcon.Text  = "🔍 ";
            _phase = Phase.Ready;
        }
    }

    private async void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanResults == null || _phase != Phase.Scanned) return;

        _phase = Phase.Cleaning;
        _cts   = new CancellationTokenSource();
        SetUiCleaning();

        try
        {
            var progress = new Progress<string>(msg =>
                Dispatcher.Invoke(() => StatusText.Text = msg));

            var (deleted, freed) = await CleanupService.CleanAsync(_scanResults, progress, _cts.Token);
            _phase = Phase.Done;
            SetUiDone(deleted, freed);
        }
        catch (OperationCanceledException) { SetUiReady(); }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            _phase = Phase.Scanned;
        }
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private bool IsSelected(CleanupCategory cat) => cat switch
    {
        CleanupCategory.Basic   => BasicCheck.IsChecked  == true,
        CleanupCategory.System  => SystemCheck.IsChecked == true,
        CleanupCategory.Privacy => PrivacyCheck.IsChecked == true,
        _                       => false
    };

    private void SetUiReady()
    {
        StatusText.Visibility   = Visibility.Collapsed;
        ScanProgress.Visibility = Visibility.Collapsed;
        ResultBanner.Visibility = Visibility.Collapsed;
        DetailSection.Visibility = Visibility.Collapsed;
        HideSizeBadges();
        ActionLabel.Text = "Scan";
        ActionIcon.Text  = "🔍 ";
        ActionButton.Click -= CleanButton_Click;
    }

    private void SetUiScanning()
    {
        StatusText.Text       = "Scanning…";
        StatusText.Visibility = Visibility.Visible;
        ScanProgress.Visibility = Visibility.Visible;
        ResultBanner.Visibility = Visibility.Collapsed;
        DetailSection.Visibility = Visibility.Collapsed;
        ActionButton.IsEnabled = false;
        HideSizeBadges();
    }

    private void SetUiScanned()
    {
        ScanProgress.Visibility = Visibility.Collapsed;
        StatusText.Visibility   = Visibility.Collapsed;
        ActionButton.IsEnabled  = true;

        if (_scanResults == null) return;

        // Per-category size badges
        SetCategoryBadge(CleanupCategory.Basic,   BasicSizeBadge,  BasicSizeText);
        SetCategoryBadge(CleanupCategory.System,  SystemSizeBadge, SystemSizeText);
        SetCategoryBadge(CleanupCategory.Privacy, PrivacySizeBadge, PrivacySizeText);

        // Detail list
        var rows = _scanResults
            .Where(r => r.FileCount > 0)
            .Select(r => new DetailRow(r.Target.Label,
                                       $"{r.FileCount} file{(r.FileCount == 1 ? "" : "s")}",
                                       CleanupService.FormatBytes(r.TotalBytes)))
            .ToList();

        if (rows.Count > 0)
        {
            DetailList.ItemsSource  = rows;
            DetailSection.Visibility = Visibility.Visible;
        }

        // Switch button to Clean
        ActionButton.Click -= CleanButton_Click;
        ActionButton.Click += CleanButton_Click;
        ActionLabel.Text = "Clean Now";
        ActionIcon.Text  = "✨ ";

        long total = _scanResults.Sum(r => r.TotalBytes);
        ResultBanner.Visibility = Visibility.Visible;
        ResultText.Text = $"Found {CleanupService.FormatBytes(total)} of junk — ready to clean";
    }

    private void SetUiCleaning()
    {
        StatusText.Visibility   = Visibility.Visible;
        ScanProgress.Visibility = Visibility.Visible;
        ActionButton.IsEnabled  = false;
    }

    private void SetUiDone(int deleted, long freed)
    {
        ScanProgress.Visibility = Visibility.Collapsed;
        StatusText.Visibility   = Visibility.Collapsed;
        ActionButton.IsEnabled  = true;
        ActionButton.Click -= CleanButton_Click;

        ResultBanner.Visibility = Visibility.Visible;
        ResultText.Text = $"Cleaned {deleted} files — freed {CleanupService.FormatBytes(freed)}";
        DetailSection.Visibility = Visibility.Collapsed;
        HideSizeBadges();

        ActionLabel.Text = "Scan Again";
        ActionIcon.Text  = "🔍 ";
    }

    private void SetCategoryBadge(CleanupCategory cat, Border badge, TextBlock label)
    {
        if (_scanResults == null) return;
        long bytes = _scanResults
            .Where(r => r.Target.Category == cat)
            .Sum(r => r.TotalBytes);

        if (bytes > 0)
        {
            label.Text = CleanupService.FormatBytes(bytes);
            badge.Visibility = Visibility.Visible;
        }
    }

    private void HideSizeBadges()
    {
        BasicSizeBadge.Visibility   = Visibility.Collapsed;
        SystemSizeBadge.Visibility  = Visibility.Collapsed;
        PrivacySizeBadge.Visibility = Visibility.Collapsed;
    }
}

/// <summary>Row model for the detail ItemsControl.</summary>
internal record DetailRow(string Label, string FileCountText, string SizeText);
