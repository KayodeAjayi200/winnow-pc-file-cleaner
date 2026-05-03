using System.Windows;

namespace FileTinder.Views;

public partial class NameDialog : Window
{
    public string BucketName => NameBox.Text.Trim();

    public NameDialog(string prompt = "Name your bucket:", string initial = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        NameBox.Text    = initial;
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();

        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)  Confirm();
            if (e.Key == System.Windows.Input.Key.Escape) Cancel();
        };
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)    => Confirm();
    private void CancelBtn_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
        DialogResult = true;
        Close();
    }

    private void Cancel()
    {
        DialogResult = false;
        Close();
    }
}
