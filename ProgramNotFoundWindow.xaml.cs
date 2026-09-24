using System.Windows;

namespace WorktreeHelper;

/// <summary>
/// Tells the user a button's program was not found. <see cref="Window.ShowDialog"/> is true
/// when they chose to locate it themselves.
/// </summary>
public partial class ProgramNotFoundWindow : Window
{
    public ProgramNotFoundWindow(Window owner, ExternalTool tool)
    {
        InitializeComponent();
        Owner = owner;
        Icon = owner.Icon;
        Title = $"{tool.Name} not found";
        Headline.Text = $"{tool.Name} was not found on this computer.";
        Detail.Text = $"If it is installed somewhere else, locate {tool.ExeName} and its button will use that.";
        LocateButton.Content = $"Locate {tool.ExeName}…";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // The same caption as the rest of the app, rather than the Windows accent.
        TitleBar.Match(this);
    }

    private void Locate_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
