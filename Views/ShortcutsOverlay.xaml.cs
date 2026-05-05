using System.Windows;
using System.Windows.Input;
using FileTinder.ViewModels;

namespace FileTinder.Views;

public partial class ShortcutsOverlay : Window
{
    private record ShortcutRow(string Key1, string Key2, string Description);

    public ICommand CloseCommand { get; }

    public ShortcutsOverlay()
    {
        InitializeComponent();
        DataContext = this;
        CloseCommand = new RelayCommand(() => Close());

        ShortcutList.ItemsSource = new[]
        {
            new ShortcutRow("←",          "or swipe left",  "Delete file (to Recycle Bin)"),
            new ShortcutRow("→",          "or swipe right", "Keep file"),
            new ShortcutRow("B",          "",               "Add to review bucket"),
            new ShortcutRow("Ctrl+Z",     "↩  button",      "Undo last delete"),
            new ShortcutRow("O",          "",               "Open file in default app"),
            new ShortcutRow("Space",      "",               "Preview file"),
            new ShortcutRow("Del",        "",               "Delete (same as ←)"),
            new ShortcutRow("?",          "",               "Show / hide this overlay"),
            new ShortcutRow("Ctrl+Z",     "",               "Undo last swipe"),
            new ShortcutRow("R",          "",               "Reload current folder"),
            new ShortcutRow("Escape",     "",               "Close dialogs / overlays"),
        };
    }
}
