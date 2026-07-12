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
            // Navigation / swiping
            new ShortcutRow("←  /  →",    "or drag card",   "Delete / Keep file"),
            new ShortcutRow("Del",         "",               "Delete (same as ←)"),
            new ShortcutRow("Ctrl+Z",      "↩ button",       "Undo last action"),
            // File actions
            new ShortcutRow("B",           "",               "Add to review bucket"),
            new ShortcutRow("O  /  Space", "",               "Open file in default app"),
            new ShortcutRow("L",           "",               "Show file in Explorer"),
            // App
            new ShortcutRow("R",           "",               "Reload current folder"),
            new ShortcutRow("?",           "",               "Show / hide this overlay"),
            new ShortcutRow("Escape",      "",               "Close dialogs / overlays"),
        };
    }
}
