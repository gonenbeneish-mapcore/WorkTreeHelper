using System.Windows;

namespace WorktreeHelper;

/// <summary>
/// The app's options. It holds no state of its own: its controls are bound to the main
/// window's properties, which save and apply as they change.
/// </summary>
public partial class OptionsWindow : Window
{
    public OptionsWindow(MainWindow owner)
    {
        InitializeComponent();
        // Owned, so it stays above the main window even when that one is pinned on top.
        Owner = owner;
        DataContext = owner;
        Icon = owner.Icon;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // The same caption as the window it belongs to, rather than the Windows accent.
        TitleBar.Match(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
