using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WorktreeHelper;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly Settings _settings = Settings.Load();
    private CancellationTokenSource? _refreshCts;
    private TrayIcon? _tray;
    private bool _exiting;

    public ObservableCollection<Worktree> Worktrees { get; } = new();

    private string _repoPath = "";
    public string RepoPath
    {
        get => _repoPath;
        private set
        {
            if (!Set(ref _repoPath, value)) return;
            OnPropertyChanged(nameof(HasRepo));
            OnPropertyChanged(nameof(ShowPathBox));
        }
    }
    public bool HasRepo => RepoPath.Length > 0;

    private bool _isEditingPath;
    /// <summary>The path box is open for typing, rather than hidden behind the caption.</summary>
    public bool IsEditingPath
    {
        get => _isEditingPath;
        private set { if (Set(ref _isEditingPath, value)) OnPropertyChanged(nameof(ShowPathBox)); }
    }

    /// <summary>
    /// Whether to give the path box a row of its own. Once a repository is chosen the caption
    /// shows its path, and the box is not needed again until that changes.
    /// </summary>
    public bool ShowPathBox => !HasRepo || IsEditingPath;

    /// <summary>The repository folder's own name, for the tray tooltip.</summary>
    private string RepoName => Path.GetFileName(RepoPath.TrimEnd('\\', '/'));

    private bool _hasWorktrees;
    public bool HasWorktrees
    {
        get => _hasWorktrees;
        private set { if (Set(ref _hasWorktrees, value)) OnPropertyChanged(nameof(ShowEmptyHint)); }
    }
    public bool ShowEmptyHint => !HasWorktrees;

    private bool _isPinned;
    /// <summary>
    /// The pin toggle. While it is held down the window sits above other windows, so it
    /// stays visible next to whatever the worktree buttons launch.
    /// </summary>
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (!Set(ref _isPinned, value)) return;
            Topmost = value;
            _settings.Pinned = value;
            _settings.Save();
        }
    }

    private bool _isDragOver;
    /// <summary>A folder is over the window and would be accepted if it were dropped.</summary>
    public bool IsDragOver
    {
        get => _isDragOver;
        private set => Set(ref _isDragOver, value);
    }

    private string _status = "";
    public string Status
    {
        get => _status;
        private set
        {
            if (!Set(ref _status, value)) return;
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(CaptionText));
        }
    }

    private string _error = "";
    /// <summary>
    /// The last failure, spelled out under the list. The title bar carries the status line,
    /// which is enough for "3 worktrees" and truncates anything as long as a git error.
    /// </summary>
    public string Error
    {
        get => _error;
        private set { if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => Error.Length > 0;

    /// <summary>What the title bar says when nothing has just happened.</summary>
    private string _restingStatus = "";

    private readonly DispatcherTimer _statusReset = new() { Interval = TimeSpan.FromSeconds(6) };

    /// <summary>
    /// Reports something that just happened, and clears any failure still on screen. The
    /// message gives way to the worktree count shortly after, rather than standing in the
    /// title bar claiming something that is minutes old.
    /// </summary>
    private void Report(string status)
    {
        Status = status;
        Error = "";
        _statusReset.Stop();
        _statusReset.Start();
    }

    /// <summary>Reports the state the title bar settles on: what the list currently holds.</summary>
    private void Rest(string status)
    {
        _restingStatus = status;
        Status = status;
        Error = "";
        _statusReset.Stop();
    }

    /// <summary>Reports a failure: short in the title bar, in full in the bar under the list.</summary>
    private void Fail(string message)
    {
        Status = message;
        Error = message;
    }

    /// <summary>
    /// What the caption reads: the repository path once one is loaded, briefly displaced by
    /// whatever just happened. A worktree count went here once and only said what the list
    /// below it already showed.
    /// </summary>
    public string CaptionText => Status.Length == 0 ? AppName : Status;

    /// <summary>The window's own title. Nothing draws it — the caption is ours — but the
    /// shell still reads it.</summary>
    public string WindowTitle => HasRepo ? $"{AppName}  —  {RepoPath}" : AppName;

    private const string AppName = "Worktree Helper";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        InputBindings.Add(new KeyBinding(new RelayCommand(_ => _ = RefreshOrCommitAsync()), Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => SelectFolder()), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => HideToTray()), Key.Escape, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => EditPath()), Key.L, ModifierKeys.Control));

        IsPinned = _settings.Pinned;

        _statusReset.Tick += (_, _) =>
        {
            _statusReset.Stop();
            // A failure stays put: it is still true until something else happens.
            if (!HasError) Status = _restingStatus;
        };

        Loaded += async (_, _) =>
        {
            var last = _settings.LastRepoPath;
            if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
                await SetRepoAsync(last);
        };
        Activated += (_, _) =>
        {
            _ = UpdateOpenStateAsync();
            // Cheap, and it catches a switch between light and dark made while the window
            // was away: Windows repaints the body but leaves the caption as it was.
            TitleBar.Match(this);
        TitleBar.Round(this);
        };
        Deactivated += (_, _) => _deactivatedUtc = DateTime.UtcNow;

        _tray = new TrayIcon(AppName);
        _tray.Clicked += ToggleWindow;
        _tray.ContextMenuRequested += ShowTrayMenu;

        var app = Application.Current;
        // Windows is logging off or restarting. Without this the close below is cancelled
        // and the app becomes the thing holding up the shutdown.
        app.SessionEnding += (_, _) => _exiting = true;
        // Every exit, not just the tray's Exit item: an icon whose process is gone stays
        // in the notification area until something makes the shell notice.
        app.Exit += (_, _) =>
        {
            SavePlacement();
            ReleaseTray();
        };

        LocationChanged += (_, _) =>
        {
            // A move the app made itself is not the user choosing a spot.
            if (_placingWindow) return;
            _userPlaced = true;
        };

        Closing += (_, e) =>
        {
            if (_exiting) return;
            // The app lives in the tray: closing the window only hides it.
            e.Cancel = true;
            HideToTray();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        RestorePlacement();
        TitleBar.Match(this);

        // A second launch broadcasts instead of starting a rival copy of the app.
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool _) =>
            {
                if ((uint)msg == SingleInstance.ShowMessage) ShowFromTray();
                return IntPtr.Zero;
            });
    }

    // ---- Repo selection ----------------------------------------------------

    private void SelectFolder_Click(object sender, RoutedEventArgs e) => SelectFolder();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Moves the window, since the caption is drawn by the app.</summary>
    private void Caption_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        // Throws if the button came back up between the event and the call.
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    /// <summary>Claims the press so clicking the path edits it instead of dragging.</summary>
    private void CaptionPath_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void CaptionPath_Click(object sender, MouseButtonEventArgs e) => EditPath();

    /// <summary>Opens the path box and puts the caret in it, ready to be typed over.</summary>
    private void EditPath()
    {
        IsEditingPath = true;
        // The box is only in the tree once ShowPathBox has been applied.
        Dispatcher.InvokeAsync(() =>
        {
            RepoPathBox.Focus();
            RepoPathBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private async Task CommitPathAsync(string text)
    {
        await SetRepoAsync(text);
        // A path that was rejected stays on screen with the message, to be corrected.
        if (HasRepo && !HasError) IsEditingPath = false;
    }

    private void RepoPathBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _ = CommitPathAsync(RepoPathBox.Text);
                break;

            // Escape abandons the edit rather than hiding the window: leaving it to the
            // window's own binding would throw away what was typed and the window with it.
            case Key.Escape when HasRepo:
                RepoPathBox.Text = RepoPath;
                IsEditingPath = false;
                e.Handled = true;
                break;
        }
    }

    private void SelectFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select a folder inside a git repository",
            Multiselect = false,
            InitialDirectory = HasRepo ? RepoPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dlg.ShowDialog(this) != true) return;

        // The browser always hands back link targets. When that target is just another
        // name for the folder already selected, keep the name the user picked instead of
        // swapping D:\git\repo in for the C:\git\repo they chose.
        if (HasRepo && LinkPaths.SameFolder(dlg.FolderName, RepoPath))
        {
            _ = RefreshAsync();
            return;
        }
        _ = SetRepoAsync(dlg.FolderName);
    }

    private async Task SetRepoAsync(string input)
    {
        // Typed or pasted text: Explorer's "Copy as path" wraps the path in quotes.
        var folder = input.Trim().Trim('"');
        if (folder.Length == 0) return;
        try
        {
            // GetFullPath normalises without following links, so a path the user gave
            // through a symbolic link or junction stays in that form.
            folder = Path.GetFullPath(folder);
        }
        catch (Exception ex)
        {
            Fail($"Not a usable path: {ex.Message}");
            return;
        }
        if (!Directory.Exists(folder))
        {
            Fail($"No such folder: {folder}");
            return;
        }
        try
        {
            if (!await GitService.IsGitRepoAsync(folder))
            {
                Fail($"Not a git repository: {folder}");
                return;
            }
        }
        catch (GitNotFoundException ex)
        {
            Fail(ex.Message);
            return;
        }
        RepoPath = folder;
        _settings.LastRepoPath = folder;
        _settings.Save();
        await RefreshAsync();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshOrCommitAsync();

    /// <summary>
    /// Refreshing also commits a path typed into the box but not yet entered, so editing
    /// the path and reaching for Refresh does what it looks like it does.
    /// </summary>
    private Task RefreshOrCommitAsync()
    {
        var typed = ShowPathBox ? RepoPathBox.Text.Trim().Trim('"') : "";
        return typed.Length > 0 && !string.Equals(typed, RepoPath, StringComparison.OrdinalIgnoreCase)
            ? SetRepoAsync(typed)
            : RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!HasRepo) return;

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _refreshCts, cts)?.Cancel();
        Report("Reading worktrees…");
        try
        {
            var list = await GitService.ListWorktreesAsync(RepoPath, cts.Token);
            if (cts.IsCancellationRequested) return;

            MergeWorktrees(list);
            HasWorktrees = Worktrees.Count > 0;
            Rest(RepoPath);
            var count = $"{Worktrees.Count} worktree{(Worktrees.Count == 1 ? "" : "s")}";
            _tray?.SetTooltip($"{AppName} — {RepoName}: {count}");
            await UpdateLaunchersAsync();
            AutoSize();
            await UpdateOpenStateAsync();
            await UpdateStatusesAsync(cts.Token);
            // The drift counts arrive after that first sizing and take width of their own,
            // which the branch column would otherwise give up by trimming its text.
            if (!cts.IsCancellationRequested) AutoSize();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Worktrees.Clear();
            HasWorktrees = false;
            Fail(ex is GitNotFoundException ? ex.Message : $"git error: {ex.Message}");
        }
        finally
        {
            // Superseded refreshes dispose their own token the same way, once the git call
            // they started has actually unwound.
            Interlocked.CompareExchange(ref _refreshCts, null, cts);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Brings the list into line with what git just reported, keeping the row objects that
    /// have not changed.
    /// </summary>
    /// <remarks>
    /// Clearing and refilling would be shorter, but every row would lose the VS Code and
    /// drift markers it is already showing and get them back a few hundred milliseconds
    /// later — a blink on every refresh, and the window refreshes each time it is shown.
    /// </remarks>
    private void MergeWorktrees(IReadOnlyList<Worktree> fresh)
    {
        for (var i = Worktrees.Count - 1; i >= 0; i--)
            if (!fresh.Any(f => SamePath(f, Worktrees[i])))
                Worktrees.RemoveAt(i);

        for (var i = 0; i < fresh.Count; i++)
        {
            var incoming = fresh[i];
            var existing = IndexOfPath(incoming);

            if (existing >= 0 && Worktrees[existing].Matches(incoming))
            {
                if (existing != i) Worktrees.Move(existing, i);
                continue;
            }

            if (existing >= 0) Worktrees.RemoveAt(existing);
            Worktrees.Insert(i, incoming);
        }
    }

    private int IndexOfPath(Worktree wt)
    {
        for (var i = 0; i < Worktrees.Count; i++)
            if (SamePath(Worktrees[i], wt)) return i;
        return -1;
    }

    private static bool SamePath(Worktree a, Worktree b)
        => string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);

    // ---- Tray ---------------------------------------------------------------

    private DateTime _deactivatedUtc = DateTime.MinValue;

    private void ToggleWindow()
    {
        if (IsVisible && WindowState != WindowState.Minimized && WasInFront()) HideToTray();
        else ShowFromTray();
    }

    /// <summary>
    /// Whether the window was the one in front when the tray icon was clicked — a window
    /// buried behind VS Code should come forward, not disappear.
    /// </summary>
    /// <remarks>
    /// IsActive cannot answer this on its own: clicking the notification area activates the
    /// shell first, so the window has already been deactivated by the time the click is
    /// delivered. A deactivation this recent is the evidence that it was in front.
    /// </remarks>
    private bool WasInFront()
        => IsActive || DateTime.UtcNow - _deactivatedUtc < TimeSpan.FromMilliseconds(400);

    private void HideToTray()
    {
        SavePlacement();
        Hide();
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        // Worktrees come and go while the window is away; never show a stale list.
        _ = RefreshAsync();
        // A tray click leaves the shell as the foreground process, so Activate alone
        // can leave the window behind others. Drop back to whatever the pin says, not
        // to false, or showing from the tray would quietly unpin the window.
        Topmost = true;
        Topmost = IsPinned;
    }

    private int _trayAnchorX, _trayAnchorY;

    private void ShowTrayMenu(int screenX, int screenY)
    {
        if (FindResource("TrayMenu") is not ContextMenu menu) return;

        PopulateTrayMenu(menu);

        _trayAnchorX = screenX;
        _trayAnchorY = screenY;

        // A notification-area menu grows up and to the left of its icon, but WPF anchors a
        // popup by its top-left corner and only knows the menu's real size once the popup
        // exists — so open it at the cursor and correct it in Opened. Custom placement,
        // which does run before the popup is shown, was measured landing hundreds of pixels
        // out for a menu with no on-screen placement target.
        //
        // The menu is transparent until it has been moved, so the correction never shows.
        var dpi = VisualTreeHelper.GetDpi(this);
        menu.Placement = PlacementMode.AbsolutePoint;
        menu.HorizontalOffset = screenX / dpi.DpiScaleX;
        menu.VerticalOffset = screenY / dpi.DpiScaleY;
        menu.BeginAnimation(OpacityProperty, null); // drop the reveal from the last open
        menu.Opacity = 0;
        menu.Opened += TrayMenu_Opened;
        menu.IsOpen = true;
    }

    private void TrayMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Opened -= TrayMenu_Opened;

        PopupPlacement.AnchorBottomRight(menu, _trayAnchorX, _trayAnchorY);
        PopupPlacement.Activate(menu);
        menu.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(90)));
    }

    /// <summary>
    /// Lists the worktrees in the tray menu, so the app's whole job — open that worktree in
    /// VS Code — can be done from the notification area without the window ever appearing.
    /// </summary>
    /// <remarks>
    /// Rebuilt on each open rather than bound, because the fixed items around it are markup
    /// and the list is short. Every inserted item carries the worktree in its Tag, which is
    /// also what marks it for removal next time.
    /// </remarks>
    private void PopulateTrayMenu(ContextMenu menu)
    {
        for (var i = menu.Items.Count - 1; i >= 0; i--)
            if (menu.Items[i] is FrameworkElement { Tag: Worktree or TrayWorktreeMarker })
                menu.Items.RemoveAt(i);

        if (Worktrees.Count == 0) return;

        var at = 1; // straight below "Show Worktree Helper"
        foreach (var wt in Worktrees)
        {
            var item = new MenuItem
            {
                Header = wt.DisplayBranch.Length > 0 ? $"{wt.Name}  —  {wt.DisplayBranch}" : wt.Name,
                // Right-aligned and muted, which is exactly where the drift belongs.
                InputGestureText = wt.StatusSummary,
                ToolTip = wt.Path,
                Tag = wt,
            };
            item.Click += TrayWorktree_Click;
            menu.Items.Insert(at++, item);
        }
        menu.Items.Insert(at, new Separator { Tag = TrayWorktreeMarker.Instance });
    }

    /// <summary>Marks the separator that PopulateTrayMenu adds, so it can take it away again.</summary>
    private sealed class TrayWorktreeMarker
    {
        public static readonly TrayWorktreeMarker Instance = new();
    }

    private void TrayWorktree_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is Worktree wt) OpenVsCode(wt.Path);
    }

    private void TrayShow_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _exiting = true;
        ReleaseTray();
        Application.Current.Shutdown();
    }

    private void ReleaseTray()
    {
        _tray?.Dispose();
        _tray = null;
    }

    // ---- VS Code open state -------------------------------------------------

    private bool _updatingOpenState;
    private bool _openStateStale;

    /// <summary>Flags the worktrees that already have a VS Code or Visual Studio window.</summary>
    /// <remarks>
    /// A request arriving mid-run is remembered and served by another pass rather than
    /// dropped: activating the window during a refresh used to leave the flags stale until
    /// the next refresh.
    /// </remarks>
    private async Task UpdateOpenStateAsync()
    {
        if (Worktrees.Count == 0) return;
        if (_updatingOpenState)
        {
            _openStateStale = true;
            return;
        }

        _updatingOpenState = true;
        try
        {
            do
            {
                _openStateStale = false;

                // Enumerating windows, and asking Visual Studio what it has open, are both
                // too slow to sit on the UI thread — and a busy Visual Studio can take its
                // time answering.
                var targets = Worktrees.ToList();
                var flags = await Task.Run(() =>
                {
                    var codeNames = VsCodeWindows.OpenFolderNames();
                    var solutions = VisualStudioInstances.OpenSolutions();
                    return targets
                        .Select(w => (
                            Code: codeNames.Contains(w.Name),
                            Studio: solutions.Any(s => LinkPaths.IsUnder(s, w.Path))))
                        .ToArray();
                });

                for (var i = 0; i < targets.Count; i++)
                {
                    targets[i].IsOpenInVsCode = flags[i].Code;
                    targets[i].IsOpenInVisualStudio = flags[i].Studio;
                }
            }
            while (_openStateStale);
        }
        catch { /* best-effort decoration; leave the flags as they were */ }
        finally { _updatingOpenState = false; }
    }

    /// <summary>
    /// Flags the worktrees that carry the Visual Studio script. Runs before the window is
    /// sized, since the button it governs takes width of its own.
    /// </summary>
    private async Task UpdateLaunchersAsync()
    {
        var targets = Worktrees.ToList();
        if (targets.Count == 0) return;

        var found = await Task.Run(() => targets.Select(w => Launcher.HasVisualStudioScript(w.Path)).ToArray());
        for (var i = 0; i < targets.Count; i++)
            targets[i].HasVisualStudio = found[i];
    }

    /// <summary>
    /// Fills in what is uncommitted and how far each branch has drifted. One git call per
    /// worktree, so it runs after the list is on screen rather than holding it up.
    /// </summary>
    private async Task UpdateStatusesAsync(CancellationToken ct)
    {
        var targets = Worktrees.Where(w => !w.IsBare).ToList();
        if (targets.Count == 0) return;

        try
        {
            var statuses = await Task.WhenAll(targets.Select(w => GitService.ReadStatusAsync(w.Path, ct)));
            if (ct.IsCancellationRequested) return;
            for (var i = 0; i < targets.Count; i++)
                targets[i].Status = statuses[i];
        }
        catch (OperationCanceledException) { /* a newer refresh is already on its way */ }
    }

    // ---- Auto-sizing --------------------------------------------------------

    /// <summary>Widest the window will grow on its own, however long the paths are.</summary>
    private const double MaxAutoWidth = 1200;

    /// <summary>
    /// Resizes the window to fit the worktree list, capped to the monitor's work area.
    /// The window is not user-resizable, so this is the only thing that sets its size.
    /// </summary>
    private void AutoSize()
    {
        if (WindowState != WindowState.Normal) return; // don't fight a maximized window

        var work = MonitorWorkArea.For(this);
        MaxWidth = Math.Min(work.Width, MaxAutoWidth);
        MaxHeight = work.Height;
        SizeToContent = SizeToContent.WidthAndHeight;

        // SizeToContent takes effect on the next layout pass. Once it has, release the
        // caps and pull the window back onto the monitor if it outgrew it.
        Dispatcher.InvokeAsync(() =>
        {
            SizeToContent = SizeToContent.Manual;
            MaxWidth = MaxHeight = double.PositiveInfinity;
            // The window grew or shrank, so the corner it is parked in has moved with it.
            if (_userPlaced) MoveIntoView(work);
            else AnchorNearTray();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Gap left between the window and the corner it is parked in.</summary>
    private const double TrayGap = 12;

    private bool _placingWindow;
    private bool _userPlaced;

    /// <summary>
    /// Puts the window back where the user left it, or - the first time, before they have
    /// left it anywhere - in the corner by the notification area it is opened from.
    /// </summary>
    private void RestorePlacement()
    {
        if (_settings.WindowLeft is not double left || _settings.WindowTop is not double top)
        {
            AnchorNearTray();
            return;
        }

        _userPlaced = true;
        Place(left, top);
        // The monitor it was left on may be gone, or smaller than it was.
        MoveIntoView(MonitorWorkArea.For(this));
    }

    private void AnchorNearTray()
    {
        var work = MonitorWorkArea.For(this);
        Place(work.Right - Width - TrayGap, work.Bottom - Height - TrayGap);
    }

    private void Place(double left, double top)
    {
        _placingWindow = true;
        Left = left;
        Top = top;
        _placingWindow = false;
    }

    private void SavePlacement()
    {
        if (!_userPlaced || WindowState != WindowState.Normal || !IsVisible) return;
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save();
    }

    private void MoveIntoView(Rect work)
    {
        _placingWindow = true;
        if (Left + Width > work.Right) Left = work.Right - Width;
        if (Top + Height > work.Bottom) Top = work.Bottom - Height;
        if (Left < work.Left) Left = work.Left;
        if (Top < work.Top) Top = work.Top;
        _placingWindow = false;
    }

    // ---- Drag & drop --------------------------------------------------------

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        var folder = DroppedFolder(e);
        IsDragOver = folder is not null;
        e.Effects = folder is not null ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e) => IsDragOver = false;

    private void Window_Drop(object sender, DragEventArgs e)
    {
        IsDragOver = false;
        if (DroppedFolder(e) is { } folder) _ = SetRepoAsync(folder);
    }

    private static string? DroppedFolder(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } items) return null;
        var p = items[0];
        if (Directory.Exists(p)) return p;
        if (File.Exists(p)) return Path.GetDirectoryName(p);
        return null;
    }

    // ---- Row actions --------------------------------------------------------

    private void OpenCode_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string folder) OpenVsCode(folder);
    }

    private void OpenVsCode(string folder)
    {
        try
        {
            if (VsCodeWindows.TryFocus(folder))
            {
                Report($"Focused VS Code: {folder}");
                return;
            }
            Launcher.OpenInVsCode(folder);
            Report($"Opened VS Code: {folder}");
            _ = WatchForVsCodeWindowAsync(folder);
        }
        catch (Exception ex)
        {
            Fail($"Could not open VS Code: {ex.Message}");
        }
    }

    /// <summary>Double-clicking a row opens it, the way double-clicking a folder does.</summary>
    /// <remarks>
    /// MouseLeftButtonDown with a click count rather than MouseDoubleClick: the row is a
    /// Border, and that event belongs to Control.
    /// </remarks>
    private void Row_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if ((sender as FrameworkElement)?.DataContext is not Worktree wt) return;
        OpenVsCode(wt.Path);
        e.Handled = true;
    }

    // Row context menu: the actions the buttons offer, plus the ones they cannot.
    private void MenuOpenCode_Click(object sender, RoutedEventArgs e) => WithWorktree(sender, wt => OpenVsCode(wt.Path));
    private void MenuOpenVisualStudio_Click(object sender, RoutedEventArgs e) => WithWorktree(sender, wt => _ = OpenVisualStudioAsync(wt.Path));
    private void MenuOpenTerminal_Click(object sender, RoutedEventArgs e) => WithWorktree(sender, wt => Run(Launcher.OpenTerminal, wt.Path, "terminal"));
    private void MenuOpenExplorer_Click(object sender, RoutedEventArgs e) => WithWorktree(sender, wt => Run(Launcher.OpenInExplorer, wt.Path, "Explorer"));
    private void MenuCopyPath_Click(object sender, RoutedEventArgs e) => WithWorktree(sender, wt => Copy(wt.Path, "path"));
    private void MenuCopyBranch_Click(object sender, RoutedEventArgs e) => WithWorktree(sender, wt => Copy(wt.Branch.Length > 0 ? wt.Branch : wt.ShortHead, "branch"));

    private static void WithWorktree(object sender, Action<Worktree> action)
    {
        if ((sender as FrameworkElement)?.DataContext is Worktree wt) action(wt);
    }

    private void Copy(string text, string what)
    {
        try
        {
            // The clipboard belongs to whichever process last opened it, so this does fail.
            Clipboard.SetDataObject(text, copy: true);
            Report($"Copied {what}: {text}");
        }
        catch (Exception ex)
        {
            Fail($"Could not copy the {what}: {ex.Message}");
        }
    }

    /// <summary>
    /// Watches for the window a launch is about to produce, so the button lights up on its
    /// own.
    /// </summary>
    /// <remarks>
    /// An editor takes a while to put a window on screen — long after the launch returns —
    /// and the only other thing that looks is this window being activated. Refreshing in that
    /// gap found nothing, and the mark appeared later for no reason the user could see.
    /// </remarks>
    private async Task WatchForWindowAsync(string folder, Func<Worktree, bool> isOpen, int attempts, TimeSpan every)
    {
        var name = VsCodeWindows.LeafName(folder);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            await Task.Delay(every);
            await UpdateOpenStateAsync();

            var row = Worktrees.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
            if (row is null || isOpen(row)) return;
        }
    }

    /// <summary>VS Code puts a window up within seconds.</summary>
    private Task WatchForVsCodeWindowAsync(string folder)
        => WatchForWindowAsync(folder, w => w.IsOpenInVsCode, attempts: 15, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The Visual Studio script generates the solution before the IDE appears, which takes
    /// minutes and can stop for input, so this looks less often and for far longer.
    /// </summary>
    private Task WatchForVisualStudioAsync(string folder)
        => WatchForWindowAsync(folder, w => w.IsOpenInVisualStudio, attempts: 60, TimeSpan.FromSeconds(10));

    private void OpenVisualStudio_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string folder) _ = OpenVisualStudioAsync(folder);
    }

    private async Task OpenVisualStudioAsync(string folder)
    {
        try
        {
            // Off the UI thread: this is a COM call into an application that may be mid-build.
            if (await Task.Run(() => VisualStudioInstances.TryFocus(folder)))
            {
                Report($"Focused Visual Studio: {folder}");
                return;
            }

            Launcher.OpenInVisualStudio(folder);
            // Not "opened": the script has minutes of work to do before the IDE appears.
            Report($"Running {Launcher.VisualStudioScript}: {folder}");
            _ = WatchForVisualStudioAsync(folder);
        }
        catch (Exception ex)
        {
            Fail($"Could not run {Launcher.VisualStudioScript}: {ex.Message}");
        }
    }

    private void OpenTerminal_Click(object sender, RoutedEventArgs e) => RunAction(sender, Launcher.OpenTerminal, "terminal");
    private void OpenExplorer_Click(object sender, RoutedEventArgs e) => RunAction(sender, Launcher.OpenInExplorer, "Explorer");

    private void RunAction(object sender, Action<string> action, string what)
    {
        if ((sender as Button)?.Tag is string folder) Run(action, folder, what);
    }

    private void Run(Action<string> action, string folder, string what)
    {
        try
        {
            action(folder);
            Report($"Opened {what}: {folder}");
        }
        catch (Exception ex)
        {
            Fail($"Could not open {what}: {ex.Message}");
        }
    }

    // ---- INotifyPropertyChanged --------------------------------------------

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}
