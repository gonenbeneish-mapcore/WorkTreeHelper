using System.Windows;

namespace WorktreeHelper;

/// <summary>
/// The app's options. It holds no state of its own: its controls are bound to the main
/// window's properties, which save and apply as they change.
/// </summary>
public partial class OptionsWindow : Window
{
    private readonly MainWindow _owner;

    public OptionsWindow(MainWindow owner)
    {
        InitializeComponent();
        _owner = owner;
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

    protected override void OnClosed(EventArgs e)
    {
        // A closed window can outlive its closing by a long way, and while it holds the main
        // window as its data it is still bound to every option, answering each change.
        DataContext = null;
        base.OnClosed(e);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Checked also fires when the binding ticks a button to show the setting; the properties
    // ignore a value they already hold, so that costs nothing.
    private void Tray_Checked(object sender, RoutedEventArgs e) => _owner.LivesInTaskbar = false;
    private void Taskbar_Checked(object sender, RoutedEventArgs e) => _owner.LivesInTaskbar = true;
    private void Packed_Checked(object sender, RoutedEventArgs e) => _owner.AlignColumns = false;
    private void Columns_Checked(object sender, RoutedEventArgs e) => _owner.AlignColumns = true;

    private void Browse_Click(object sender, RoutedEventArgs e) => _owner.BrowseForRepository(this);

    /// <summary>
    /// Opens the repository at the typed path. A path that fails leaves the message under the
    /// box and the text as typed, to be corrected; one that works shows up in the box as the
    /// main window has it, which is how a link that was followed would show.
    /// </summary>
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        OpenButton.IsEnabled = false;
        try
        {
            await _owner.OpenRepositoryAsync(PathBox.Text);
        }
        finally
        {
            OpenButton.IsEnabled = true;
        }
    }
}
