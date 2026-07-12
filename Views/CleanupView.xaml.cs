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

    private bool _scheduleInitializing = true;

    // ── Constructor ───────────────────────────────────────────────────────────

    public CleanupView()
    {
        InitializeComponent();
        InitScheduleUI();
    }

    // ── Schedule UI init ──────────────────────────────────────────────────────

    private void InitScheduleUI()
    {
        // Hours 0-23
        HourCombo.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToList();
        // Minutes: 00, 15, 30, 45
        MinuteCombo.ItemsSource = new[] { "00", "15", "30", "45" };
        // Days of week
        WeekDayCombo.ItemsSource = Enum.GetValues<DayOfWeek>().Cast<DayOfWeek>().ToList();
        WeekDayCombo.DisplayMemberPath = null;

        var sched = ScheduledCleanupService.Load();

        ScheduleEnabledCheck.IsChecked = sched.IsEnabled;
        FreqDaily.IsChecked  = sched.Frequency != ScheduleFrequency.Weekly;
        FreqWeekly.IsChecked = sched.Frequency == ScheduleFrequency.Weekly;
        HourCombo.SelectedIndex   = sched.Hour;
        MinuteCombo.SelectedIndex = sched.Minute switch { 15 => 1, 30 => 2, 45 => 3, _ => 0 };
        WeekDayCombo.SelectedItem = sched.WeekDay;

        ScheduleOptionsPanel.Visibility = sched.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        WeekDayPanel.Visibility = sched.Frequency == ScheduleFrequency.Weekly
            ? Visibility.Visible : Visibility.Collapsed;

        if (sched.IsEnabled)
            ShowActiveSchedule(sched);

        _scheduleInitializing = false;
    }

    private void ShowActiveSchedule(CleanupSchedule sched)
    {
        var time = $"{sched.Hour:D2}:{sched.Minute:D2}";
        var when = sched.Frequency == ScheduleFrequency.Daily
            ? $"daily at {time}"
            : $"weekly on {sched.WeekDay} at {time}";
        ActiveScheduleText.Text    = $"Scheduled to run {when}";
        ActiveScheduleBanner.Visibility = Visibility.Visible;
        ScheduleStatusLabel.Text   = "Enabled";
        ScheduleStatusLabel.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81));
    }

    // ── Schedule changed handlers ─────────────────────────────────────────────

    private void Schedule_Changed(object sender, RoutedEventArgs e)
    {
        if (_scheduleInitializing) return;

        bool enabled = ScheduleEnabledCheck.IsChecked == true;
        ScheduleOptionsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (!enabled)
        {
            ActiveScheduleBanner.Visibility = Visibility.Collapsed;
            ScheduleStatusLabel.Text        = "Disabled";
            ScheduleStatusLabel.Foreground  = FindResource("TextMutedBrush") as System.Windows.Media.Brush;
        }

        WeekDayPanel.Visibility = (FreqWeekly.IsChecked == true)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Schedule_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_scheduleInitializing) return;
        WeekDayPanel.Visibility = (FreqWeekly.IsChecked == true)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        var schedule = BuildSchedule();
        var (ok, message) = ScheduledCleanupService.Apply(schedule);

        ScheduleSaveStatus.Visibility = Visibility.Visible;
        ScheduleSaveStatus.Text       = message;
        ScheduleSaveStatus.Foreground = new System.Windows.Media.SolidColorBrush(ok
            ? System.Windows.Media.Color.FromRgb(0x6E, 0xE7, 0xB7)
            : System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));

        if (ok && schedule.IsEnabled)
            ShowActiveSchedule(schedule);
        else if (!schedule.IsEnabled)
            ActiveScheduleBanner.Visibility = Visibility.Collapsed;
    }

    private CleanupSchedule BuildSchedule()
    {
        int hour   = HourCombo.SelectedIndex;
        int minute = MinuteCombo.SelectedIndex switch { 1 => 15, 2 => 30, 3 => 45, _ => 0 };
        var day    = WeekDayCombo.SelectedItem is DayOfWeek dow ? dow : DayOfWeek.Sunday;

        return new CleanupSchedule
        {
            IsEnabled = ScheduleEnabledCheck.IsChecked == true,
            Frequency = FreqWeekly.IsChecked == true
                ? ScheduleFrequency.Weekly : ScheduleFrequency.Daily,
            Hour    = hour,
            Minute  = minute,
            WeekDay = day
        };
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
