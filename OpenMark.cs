using System.Windows;

namespace WorktreeHelper;

/// <summary>
/// Says a row button's program already has this worktree open, so the button brings that
/// window back rather than starting another. The row button's template draws the mark, so
/// every button that can be open looks the same when it is.
/// </summary>
public static class OpenMark
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.RegisterAttached(
        "IsOpen", typeof(bool), typeof(OpenMark), new FrameworkPropertyMetadata(false));

    public static bool GetIsOpen(DependencyObject element) => (bool)element.GetValue(IsOpenProperty);

    public static void SetIsOpen(DependencyObject element, bool value) => element.SetValue(IsOpenProperty, value);
}
