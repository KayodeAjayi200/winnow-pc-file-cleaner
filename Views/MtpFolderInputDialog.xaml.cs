using System.Windows;

namespace FileTinder.Views;

public partial class MtpFolderInputDialog : Window
{
    public string? SelectedPath { get; private set; }
    private readonly string _deviceId;

    public MtpFolderInputDialog(string deviceId)
    {
        InitializeComponent();
        _deviceId = deviceId;
        PathBox.Text = @"\Internal Storage";
        PathBox.SelectAll();
        PathBox.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (string.IsNullOrEmpty(path)) return;
        SelectedPath = path;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
