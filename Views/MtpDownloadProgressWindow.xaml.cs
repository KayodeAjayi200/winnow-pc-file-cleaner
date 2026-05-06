using System;
using System.Windows;

namespace FileTinder.Views;

public partial class MtpDownloadProgressWindow : Window
{
    public event Action? Cancelled;

    private readonly long _totalBytes;

    public MtpDownloadProgressWindow(string fileName, long totalBytes)
    {
        InitializeComponent();
        _totalBytes      = totalBytes;
        FileNameText.Text = $"Downloading: {fileName}";
        StatusText.Text   = totalBytes > 0
            ? $"0 B / {FormatBytes(totalBytes)}"
            : "Downloading…";
    }

    public void Report(long written, long total)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (total > 0)
            {
                ProgressBar.Value = (double)written / total * 100;
                StatusText.Text   = $"{FormatBytes(written)} / {FormatBytes(total)}";
            }
            else
            {
                StatusText.Text = $"{FormatBytes(written)} downloaded…";
            }
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Cancelled?.Invoke();
        Close();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024)          return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
