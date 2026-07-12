using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileTinder.Services;
using FileTinder.ViewModels;
using WpfBrush = System.Windows.Media.Brush;

namespace FileTinder.Views;

public partial class WhatsNewDialog : Window
{
    public ICommand CloseCommand { get; }

    public WhatsNewDialog(string version, string notes)
    {
        InitializeComponent();
        DataContext = this;
        CloseCommand = new RelayCommand(() => { UpdateService.SaveLastSeenVersion(version); Close(); });

        TitleText.Text   = "🎉  What's new in Winnow";
        VersionText.Text = $"Version {version}";

        RenderNotes(notes);
    }

    private void GotIt_Click(object sender, RoutedEventArgs e)
        => CloseCommand.Execute(null);

    // ── Simple markdown-ish renderer ──────────────────────────────────────────

    private void RenderNotes(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            AddBody("No release notes available.");
            return;
        }

        // Strip the boilerplate install/upgrade section at the bottom
        var cutoff = raw.IndexOf("---", StringComparison.Ordinal);
        var content = cutoff > 0 ? raw[..cutoff] : raw;

        foreach (var line in content.Split('\n'))
        {
            var l = line.TrimEnd('\r').Trim();

            if (l.StartsWith("### "))
            {
                AddSectionHeader(l[4..]);
            }
            else if (l.StartsWith("## "))
            {
                // Skip top-level version heading (redundant with TitleText)
            }
            else if (l.StartsWith("- "))
            {
                AddBullet(l[2..]);
            }
            else if (l == "---")
            {
                NotesPanel.Children.Add(new Separator
                {
                    Margin = new Thickness(0, 8, 0, 8),
                    Background = (WpfBrush)FindResource("BorderSubtleBrush")
                });
            }
            else if (!string.IsNullOrWhiteSpace(l))
            {
                AddBody(l);
            }
        }
    }

    private void AddSectionHeader(string text)
    {
        NotesPanel.Children.Add(new TextBlock
        {
            Text       = text,
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("TextPrimaryBrush"),
            Margin     = new Thickness(0, 14, 0, 6)
        });
    }

    private void AddBullet(string text)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new TextBlock
        {
            Text       = "•",
            FontSize   = 13,
            Foreground = (WpfBrush)FindResource("AccentBrandBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin     = new Thickness(0, 1, 0, 0)
        };
        Grid.SetColumn(dot, 0);

        var body = new TextBlock
        {
            Text        = text,
            FontSize    = 13,
            Foreground  = (WpfBrush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(body, 1);

        row.Children.Add(dot);
        row.Children.Add(body);
        NotesPanel.Children.Add(row);
    }

    private void AddBody(string text)
    {
        NotesPanel.Children.Add(new TextBlock
        {
            Text         = text,
            FontSize     = 13,
            Foreground   = (WpfBrush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, 4)
        });
    }
}
