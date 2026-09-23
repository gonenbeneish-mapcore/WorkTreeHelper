using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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

    private ReleaseInfo? _update;
    /// <summary>
    /// A release newer than this copy, found by the check that runs at startup and daily.
    /// Null until one turns up, which is what keeps the button off the caption.
    /// </summary>
    public ReleaseInfo? Update
    {
        get => _update;
        private set
        {
            if (!Set(ref _update, value)) return;
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(UpdateTooltip));
            OnPropertyChanged(nameof(UpdateLabel));
            OnPropertyChanged(nameof(UpdateHeadline));
            OnPropertyChanged(nameof(UpdateDetail));
        }
    }
    public bool HasUpdate => Update is not null;

    /// <summary>
    /// What the caption button reads. The version is written on it rather than left to the
    /// tooltip: a button nobody hovers says nothing, and this one has one job to advertise.
    /// </summary>
    public string UpdateLabel => IsUpdating
        ? "Installing\u2026"
        : Update is { } release ? $"Update to {release.Version}" : "";

    /// <summary>The notice's first line: what is on offer.</summary>
    public string UpdateHeadline => Update is { } release ? $"{AppName} {release.Version} is available" : "";

    /// <summary>
    /// Its second line: the release's own headline if it wrote one, and otherwise the version
    /// being replaced, which is the next thing anyone asks.
    /// </summary>
    public string UpdateDetail
    {
        get
        {
            if (Update is not { } release) return "";

            // The notes arrive summarised, which takes off the headings and the bold markers
            // but leaves the bullets — they read fine in a tooltip and look like a stray
            // dash at the start of a line of their own.
            var first = release.Notes.Split('\n')
                .Select(l => l.Trim().TrimStart('-', '*', '\u2022').Trim())
                .FirstOrDefault(l => l.Length > 0);
            return first is { Length: > 0 } ? first : $"You are running {UpdateService.Current}";
        }
    }

    /// <summary>
    /// What the app calls itself and which version this is, for the icon it is shown on. The
    /// update button carries the version on offer; this one says what is actually running.
    /// </summary>
    public string AppTooltip => $"{AppName} {UpdateService.Current}";

    /// <summary>
    /// What the button offers, named and numbered against what is running now, and what the
    /// release says is new in it.
    /// </summary>
    public string UpdateTooltip
    {
        get
        {
            if (Update is not { } release) return "";

            var text = $"{AppName} {release.Version} is available (this is {UpdateService.Current})";
            if (release.Notes.Length > 0) text += $"\n\n{release.Notes}";
            return text + "\n\nClick to install it and restart";
        }
    }

    private bool _isUpdating;
    /// <summary>An install is under way; the button stops accepting another press.</summary>
    public bool IsUpdating
    {
        get => _isUpdating;
        private set
        {
            if (!Set(ref _isUpdating, value)) return;
            OnPropertyChanged(nameof(UpdateLabel));
        }
    }

    private bool _showUpdateToast;
    /// <summary>Whether the update notice is up. See <see cref="AnnounceUpdate"/>.</summary>
    public bool ShowUpdateToast
    {
        get => _showUpdateToast;
        private set => Set(ref _showUpdateToast, value);
    }

    private bool _showTaskbarQuestion;
    /// <summary>
    /// Whether the one-time taskbar-or-tray bar is up. Asked in the window rather than in a
    /// dialog: the window is already on screen at first run, and a modal would be the only one
    /// in the app and the only thing needing its own caption.
    /// </summary>
    public bool ShowTaskbarQuestion
    {
        get => _showTaskbarQuestion;
        private set => Set(ref _showTaskbarQuestion, value);
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

    /// <summary>
    /// The window's own title. The caption is drawn by the app, but this is what the shell
    /// shows — including on the taskbar button, when that is switched on — so it carries the
    /// repository's name rather than its whole path.
    /// </summary>
    public string WindowTitle => HasRepo ? $"{AppName}  —  {RepoName}" : AppName;

    /// <summary>
    /// What the close button promises, which depends on where the app is living: in the
    /// taskbar the window is the app and closing it exits.
    /// </summary>
    public string CloseHint => ShowInTaskbar
        ? "Close and exit the app (Alt+F4). Esc minimises."
        : "Close to the tray (Esc)";

    // ---- Options -----------------------------------------------------------
    //
    // Each of these is bound two ways by the options window, and applies the moment it is
    // changed: there is no OK to press, so there is nothing to forget to press.

    /// <summary>Taskbar rather than the notification area. The same switch as the menu's.</summary>
    public bool LivesInTaskbar
    {
        get => ShowInTaskbar;
        set { if (value != ShowInTaskbar) ApplyShowInTaskbar(value); }
    }

    /// <summary>Whether a row may offer the other kind of Visual Studio once one is open.</summary>
    public bool AllowSecondVisualStudio
    {
        get => _settings.AllowSecondVisualStudio;
        set
        {
            if (_settings.AllowSecondVisualStudio == value) return;
            _settings.AllowSecondVisualStudio = value;
            _settings.Save();
            foreach (var wt in Worktrees) wt.AllowSecondVisualStudio = value;
            OnPropertyChanged(nameof(AllowSecondVisualStudio));
            // One button more or fewer per row changes how wide the window wants to be.
            AutoSize();
        }
    }

    /// <summary>Whether each kind of button keeps its own column down the list.</summary>
    public bool AlignColumns
    {
        get => _settings.AlignColumns;
        set
        {
            if (_settings.AlignColumns == value) return;
            _settings.AlignColumns = value;
            _settings.Save();
            OnPropertyChanged(nameof(AlignColumns));
            UpdateHeldColumns();
            AutoSize();
        }
    }

    // In columns, a button a row lacks keeps its place when some other row has one. These say
    // which places are kept; the row template reads them through SlotVisibility.

    private bool _holdVsColumn, _holdVsFolderColumn, _holdPrColumn;

    public bool HoldVsColumn
    {
        get => _holdVsColumn;
        private set => Set(ref _holdVsColumn, value);
    }

    public bool HoldVsFolderColumn
    {
        get => _holdVsFolderColumn;
        private set => Set(ref _holdVsFolderColumn, value);
    }

    public bool HoldPrColumn
    {
        get => _holdPrColumn;
        private set => Set(ref _holdPrColumn, value);
    }

    /// <summary>Works out again which columns are kept, from what every row is showing.</summary>
    private void UpdateHeldColumns()
    {
        var align = AlignColumns;
        HoldVsColumn = align && Worktrees.Any(w => w.ShowVisualStudioChooser || w.ShowSolutionButton);
        HoldVsFolderColumn = align && Worktrees.Any(w => w.ShowFolderButton);
        HoldPrColumn = align && Worktrees.Any(w => w.HasPullRequest);
    }

    /// <summary>A row's buttons changed, which may open or close a column for every row.</summary>
    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Worktree.ShowVisualStudioChooser) or nameof(Worktree.ShowSolutionButton)
            or nameof(Worktree.ShowFolderButton) or nameof(Worktree.HasPullRequest))
            UpdateHeldColumns();
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        // From the tray menu the window may be away, and the options belong to it.
        if (!IsVisible || WindowState == WindowState.Minimized) ShowFromTray();
        new OptionsWindow(this).ShowDialog();
    }

    private const string AppName = "Worktree Helper";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        InputBindings.Add(new KeyBinding(new RelayCommand(_ => _ = RefreshOrCommitAsync()), Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => SelectFolder()), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => DismissWindow()), Key.Escape, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => EditPath()), Key.L, ModifierKeys.Control));
        // The context-menu key, where every other window puts its menu.
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => ShowAppMenu(atPointer: false)), Key.Apps, ModifierKeys.None));

        // Every row is watched for the buttons it shows, because in columns one row's PR is
        // what keeps a PR column open for all the others.
        Worktrees.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (Worktree wt in e.OldItems) wt.PropertyChanged -= Row_PropertyChanged;
            if (e.NewItems is not null)
                foreach (Worktree wt in e.NewItems) wt.PropertyChanged += Row_PropertyChanged;
            UpdateHeldColumns();
        };

        IsPinned = _settings.Pinned;
        // Before the handle exists, which is the one place setting this costs nothing: WPF then
        // simply creates the window with the taskbar style it is going to keep.
        ShowInTaskbar = _settings.ShowInTaskbar;
        ShowTaskbarQuestion = !_settings.AskedAboutTaskbar;

        // Whatever an earlier update renamed aside is of no further use.
        UpdateService.CleanUpPreviousVersion();

        _updateCheck.Tick += (_, _) => _ = CheckForUpdateAsync();
        _updateCheck.Start();

        _toastLife.Tick += (_, _) => HideUpdateToast();

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

            // After the list, so a slow or unreachable GitHub delays nothing the user came for.
            _ = CheckForUpdateAsync();
        };
        Activated += (_, _) =>
        {
            _ = UpdateOpenStateAsync();
            // Cheap, and it catches a switch between light and dark made while the window
            // was away: Windows repaints the body but leaves the caption as it was.
            TitleBar.Match(this);
            TitleBar.Round(this);
            ApplyIcon();
        };
        Deactivated += (_, _) => _deactivatedUtc = DateTime.UtcNow;

        // Minimizing is the taskbar's way of putting the window away, so it gets what hiding
        // to the tray gets: the spot it was left in on the way out, a fresh list on the way
        // back. Worktrees come and go while the window is down.
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
            {
                SavePlacement();
                return;
            }

            _ = RefreshAsync();
            AnnounceUpdate();
        };

        // Only when the app lives there. A taskbar button and a notification-area icon are
        // two answers to the same question — where this app is to be found — and having both
        // put it in two places at once.
        SetTrayIcon(!ShowInTaskbar);

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

            // With a taskbar button the window is the app, so closing it closes the app. In
            // the notification area the icon is the app and the window is only ever put away
            // — closing it there would leave no way to say so.
            if (ShowInTaskbar)
            {
                SavePlacement();
                _exiting = true;
                return;
            }

            e.Cancel = true;
            HideToTray();
        };

        // The app shuts down explicitly (ShutdownMode), so that nothing closes it while it is
        // living in the tray with no window open. The other side of that: once the window has
        // genuinely closed, something has to say so.
        Closed += (_, _) => Application.Current.Shutdown();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        RestorePlacement();
        TitleBar.Match(this);

        ApplyIcon();

        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool _) =>
            {
                // A second launch broadcasts instead of starting a rival copy of the app.
                if ((uint)msg == SingleInstance.ShowMessage) ShowFromTray();

                // Windows announces a change of theme here. The icon has a colourway for
                // each, and the tray icon has to be told even while the window is away,
                // which is the whole reason this is a message hook and not the Activated
                // handler above.
                if (msg == WM_SETTINGCHANGE) Dispatcher.BeginInvoke(ApplyIcon);

                return IntPtr.Zero;
            });
    }

    /// <summary>Windows broadcasts this when a setting changes, the theme among them.</summary>
    private const int WM_SETTINGCHANGE = 0x001A;

    /// <summary>
    /// Puts the colourway that suits the current theme on the title bar, the taskbar button
    /// and the notification area. Cheap enough to call whenever it might have changed.
    /// </summary>
    private void ApplyIcon()
    {
        var dark = AppIcon.IsDark(this);
        if (_iconIsDark == dark) return;
        _iconIsDark = dark;

        Icon = AppIcon.Image(this);

        var (cx, cy) = TrayIcon.IconSize;
        _tray?.SetIcon(AppIcon.CreateHandle(this, cx, cy));
    }

    /// <summary>Which colourway is up, so the work is skipped when nothing has changed.</summary>
    private bool? _iconIsDark;

    // ---- Repo selection ----------------------------------------------------

    private void SelectFolder_Click(object sender, RoutedEventArgs e) => SelectFolder();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ChooseTaskbar_Click(object sender, RoutedEventArgs e) => AnswerTaskbarQuestion(true);

    private void ChooseTrayOnly_Click(object sender, RoutedEventArgs e) => AnswerTaskbarQuestion(false);

    private void AnswerTaskbarQuestion(bool taskbar)
    {
        _settings.AskedAboutTaskbar = true;
        ShowTaskbarQuestion = false;
        ApplyShowInTaskbar(taskbar);

        // The bar is gone, so the window is taller than its contents; nothing else would
        // notice until the next refresh.
        AutoSize();

        // Anything the question was standing in the way of can be said now.
        AnnounceUpdate();
    }

    private void TrayShowInTaskbar_Click(object sender, RoutedEventArgs e)
    {
        // WPF has already flipped IsChecked to what was asked for by the time this runs.
        var wanted = (sender as MenuItem)?.IsChecked ?? ShowInTaskbar;
        ApplyShowInTaskbar(wanted);
    }

    /// <summary>
    /// Switches the taskbar button on or off and remembers it.
    /// </summary>
    /// <remarks>
    /// Safe to do while the window is up: on .NET 10 this only adds or removes WS_EX_APPWINDOW
    /// and the hidden owner window, so the handle — and with it the caption colour, the rounded
    /// corners and the hook that a second launch broadcasts to — survives. The DWM attributes
    /// are re-applied anyway, being two idempotent calls the Activated handler already makes,
    /// and Topmost is re-asserted because swapping the owner can disturb the Z-order.
    /// </remarks>
    private void ApplyShowInTaskbar(bool wanted)
    {
        ShowInTaskbar = wanted;
        _settings.ShowInTaskbar = wanted;
        _settings.Save();

        SetTrayIcon(!wanted);
        OnPropertyChanged(nameof(CloseHint));
        OnPropertyChanged(nameof(LivesInTaskbar));

        TitleBar.Match(this);
        TitleBar.Round(this);
        Topmost = true;
        Topmost = IsPinned;

        Report(wanted
            ? "In the taskbar. Closing the window exits; right-click the caption for this menu."
            : "In the system tray. Closing the window puts it away.");
    }

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
        // Refresh is what someone presses when they want the app to go and look, so it asks
        // GitHub about a newer release as well. The automatic check is a daily one, which can
        // leave a release sitting unnoticed for most of a day. Not awaited: the worktrees are
        // what was asked for, and they should not wait on GitHub.
        _ = CheckForUpdateAsync();

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
            _tray?.SetTooltip($"{AppTooltip} — {RepoName}: {count}");
            await UpdateLaunchersAsync();
            await UpdatePullRequestsAsync(cts.Token);
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
            incoming.AllowSecondVisualStudio = _settings.AllowSecondVisualStudio;
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

    // ---- Updates ------------------------------------------------------------

    /// <summary>How often the app looks again while it sits in the tray.</summary>
    private readonly DispatcherTimer _updateCheck = new() { Interval = TimeSpan.FromDays(1) };

    /// <summary>
    /// Asks GitHub for the latest release. Says nothing when there is none, when there is no
    /// network, or when this copy is already it: a background check is not worth a message.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        if (IsUpdating) return;
        Update = await UpdateService.CheckAsync();
        AnnounceUpdate();
    }

    private void Update_Click(object sender, RoutedEventArgs e) => _ = InstallUpdateAsync();

    // ---- The update notice --------------------------------------------------

    /// <summary>How long the notice stays up on its own.</summary>
    private static readonly TimeSpan ToastLife = TimeSpan.FromSeconds(9);

    private readonly DispatcherTimer _toastLife = new() { Interval = ToastLife };

    /// <summary>The version the notice has already been shown for, so it is shown once.</summary>
    private Version? _announced;

    /// <summary>
    /// Puts the notice up for a release the user has not been told about yet.
    /// </summary>
    /// <remarks>
    /// Only while the window is actually on screen. The check also runs on a timer, and an
    /// announcement made to a window that is hidden in the tray or minimized is one the user
    /// never sees; there is nothing to do but wait, so showing the window calls this again.
    /// </remarks>
    private void AnnounceUpdate()
    {
        if (Update is not { } release || IsUpdating) return;
        if (!IsVisible || WindowState == WindowState.Minimized) return;
        // The one-time taskbar question sits exactly where the notice lands, and covers the
        // two buttons that answer it. That question is asked once in the life of the app;
        // an update can wait the few seconds it takes to answer.
        if (ShowTaskbarQuestion) return;
        if (_announced == release.Version) return;

        _announced = release.Version;
        ShowUpdateToast = true;
        UpdateToast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        _toastLife.Stop();
        _toastLife.Start();
    }

    /// <summary>
    /// Takes the notice away. The caption button stays: the notice is how an update
    /// announces itself, not the only way back to it.
    /// </summary>
    private void HideUpdateToast()
    {
        _toastLife.Stop();
        if (!ShowUpdateToast) return;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => ShowUpdateToast = false;
        UpdateToast.BeginAnimation(OpacityProperty, fade);
    }

    private void DismissUpdateToast_Click(object sender, RoutedEventArgs e) => HideUpdateToast();

    private void UpdateToast_MouseEnter(object sender, MouseEventArgs e) => _toastLife.Stop();

    private void UpdateToast_MouseLeave(object sender, MouseEventArgs e)
    {
        if (ShowUpdateToast) _toastLife.Start();
    }

    /// <summary>
    /// Installs the waiting release over this copy and restarts into it.
    /// </summary>
    /// <remarks>
    /// The download and the file swap both happen while the app is still whole, so a failure
    /// at either step leaves it running and untouched. Only once the new exe is in place does
    /// it let go of the tray icon and the single-instance mutex — the copy about to start
    /// needs that mutex, or it would see this one and simply ask it to show itself.
    /// </remarks>
    private async Task InstallUpdateAsync()
    {
        if (Update is not { } release || IsUpdating) return;

        HideUpdateToast();
        IsUpdating = true;
        try
        {
            Report($"Downloading {AppName} {release.Version}…");
            var newExe = await UpdateService.DownloadAsync(release);

            Report($"Installing {AppName} {release.Version}…");
            UpdateService.Apply(newExe);

            var target = Environment.ProcessPath!;
            SavePlacement();
            _exiting = true;
            ReleaseTray();
            SingleInstance.Release();

            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _exiting = false;
            Fail($"Could not install {AppName} {release.Version}: {ex.Message}");
        }
        finally
        {
            IsUpdating = false;
        }
    }

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

    /// <summary>
    /// What Escape does: put the window away. That is the tray when the app lives there, and
    /// the taskbar otherwise — a key this easy to hit should not be able to close the app.
    /// </summary>
    private void DismissWindow()
    {
        if (ShowInTaskbar) WindowState = WindowState.Minimized;
        else HideToTray();
    }

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
        // A release found while the window was away has waited for this.
        AnnounceUpdate();
    }

    /// <summary>
    /// The app’s own menu, opened from the window rather than from the notification area.
    /// </summary>
    /// <remarks>
    /// In the taskbar there is no tray icon to right-click, and this is the only way to the
    /// menu — including the item that hands the app back to the notification area. Three
    /// ways in, because being unable to find it would strand someone in the taskbar: the
    /// caption icon, which is where Windows has always kept a window’s menu; a right-click
    /// anywhere on the caption; and the context-menu key.
    /// </remarks>
    private void ShowAppMenu(bool atPointer)
    {
        if (FindResource("TrayMenu") is not ContextMenu menu) return;

        PopulateTrayMenu(menu);
        // "Show Worktree Helper" is no use on a window that is already in front.
        if (FindTrayItem(menu, TrayShowTag) is { } show) show.Visibility = Visibility.Collapsed;

        // None of the notification-area placement: this menu has a target on screen, so it
        // can simply open against it.
        menu.BeginAnimation(OpacityProperty, null);
        menu.Opacity = 1;
        menu.HorizontalOffset = 0;
        menu.VerticalOffset = 0;
        menu.PlacementTarget = atPointer ? this : TitleBarIcon;
        menu.Placement = atPointer ? PlacementMode.MousePoint : PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void Caption_RightClick(object sender, MouseButtonEventArgs e)
    {
        ShowAppMenu(atPointer: true);
        e.Handled = true;
    }

    private void TitleBarIcon_Click(object sender, MouseButtonEventArgs e)
    {
        ShowAppMenu(atPointer: false);
        e.Handled = true;
    }

    private int _trayAnchorX, _trayAnchorY;

    private void ShowTrayMenu(int screenX, int screenY)
    {
        if (FindResource("TrayMenu") is not ContextMenu menu) return;

        PopulateTrayMenu(menu);
        if (FindTrayItem(menu, TrayShowTag) is { } show) show.Visibility = Visibility.Visible;

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

        // The tick can be made stale by anything that edits the settings file, so it is read
        // afresh rather than left where it was last put.
        if (FindTrayItem(menu, TrayTaskbarTag) is { } taskbar) taskbar.IsChecked = ShowInTaskbar;

        if (Worktrees.Count == 0) return;

        // Straight below "Show Worktree Helper", found rather than assumed: a fixed index here
        // is a trap for whoever adds the next permanent item.
        var at = menu.Items.IndexOf(FindTrayItem(menu, TrayShowTag)) + 1;
        if (at <= 0) at = 1;
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

    // Tags identifying the tray menu's permanent items. Anything whose Tag is neither a
    // Worktree nor TrayWorktreeMarker survives PopulateTrayMenu's sweep, which is what keeps
    // these in place; the values are what the markup sets.
    private const string TrayShowTag = "tray.show";
    private const string TrayTaskbarTag = "tray.taskbar";

    private static MenuItem? FindTrayItem(ContextMenu menu, string tag)
        => menu.Items.OfType<MenuItem>().FirstOrDefault(i => (i.Tag as string) == tag);

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

    /// <summary>
    /// Puts the notification-area icon up or takes it down, to match where the app is living.
    /// </summary>
    private void SetTrayIcon(bool wanted)
    {
        if (wanted == _tray is not null) return;

        if (!wanted)
        {
            ReleaseTray();
            return;
        }

        var (cx, cy) = TrayIcon.IconSize;
        _tray = new TrayIcon(AppTooltip, AppIcon.CreateHandle(this, cx, cy));
        _tray.Clicked += ToggleWindow;
        _tray.ContextMenuRequested += ShowTrayMenu;
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
                    var studio = VisualStudioInstances.Open();
                    return targets
                        .Select(w => (
                            Code: codeNames.Contains(w.Name),
                            Solution: studio.Any(i => i.Holds(w.Path) && i.Mode == VisualStudioMode.Solution),
                            Folder: studio.Any(i => i.Holds(w.Path) && i.Mode == VisualStudioMode.Folder)))
                        .ToArray();
                });

                for (var i = 0; i < targets.Count; i++)
                {
                    targets[i].IsOpenInVsCode = flags[i].Code;
                    targets[i].IsSolutionOpen = flags[i].Solution;
                    targets[i].IsFolderOpen = flags[i].Folder;
                }
            }
            while (_openStateStale);
        }
        catch { /* best-effort decoration; leave the flags as they were */ }
        finally { _updatingOpenState = false; }
    }

    /// <summary>
    /// Attaches each worktree's open pull request, if its branch has one. One query answers
    /// for the whole repository, so this costs the same whether there are two worktrees or ten.
    /// </summary>
    private async Task UpdatePullRequestsAsync(CancellationToken ct)
    {
        var targets = Worktrees.Where(w => w.Branch.Length > 0).ToList();
        if (targets.Count == 0) return;

        var open = await PullRequests.OpenByBranchAsync(RepoPath, ct);
        if (ct.IsCancellationRequested) return;

        foreach (var worktree in targets)
            worktree.PullRequest = open.GetValueOrDefault(worktree.Branch);
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
        if (!_userPlaced || !IsVisible) return;

        // A minimized window's own Left and Top are the shell's parking spot off the side of
        // the screen; RestoreBounds still holds the corner the user left it in.
        var corner = WindowState == WindowState.Normal ? new Point(Left, Top) : RestoreBounds.Location;
        if (double.IsNaN(corner.X) || double.IsInfinity(corner.X)) return;

        _settings.WindowLeft = corner.X;
        _settings.WindowTop = corner.Y;
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
    private void MenuOpenVisualStudio_Click(object sender, RoutedEventArgs e)
        => WithWorktree(sender, wt => AskHowToOpenVisualStudio(wt.Path, anchor: null));
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
    private Task WatchForSolutionAsync(string folder)
        => WatchForWindowAsync(folder, w => w.IsSolutionOpen, attempts: 60, TimeSpan.FromSeconds(10));

    /// <summary>Opening a folder needs no generating, so the window arrives in seconds.</summary>
    private Task WatchForFolderAsync(string folder)
        => WatchForWindowAsync(folder, w => w.IsFolderOpen, attempts: 20, TimeSpan.FromSeconds(3));

    private void OpenPullRequest_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is PullRequest pr) OpenPullRequest(pr);
    }

    private void MenuOpenPullRequest_Click(object sender, RoutedEventArgs e)
        => WithWorktree(sender, wt => { if (wt.PullRequest is { } pr) OpenPullRequest(pr); });

    private void OpenPullRequest(PullRequest pr)
    {
        try
        {
            Launcher.OpenInBrowser(pr.Url);
            Report($"Opened pull request #{pr.Number}");
        }
        catch (Exception ex)
        {
            Fail($"Could not open pull request #{pr.Number}: {ex.Message}");
        }
    }

    /// <summary>The button shown while nothing has the worktree open: it asks which way in.</summary>
    private void OpenVisualStudio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string folder } button) AskHowToOpenVisualStudio(folder, button);
    }

    /// <summary>The solution button, shown once either mode is open.</summary>
    private void SolutionVisualStudio_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string folder) _ = ReachVisualStudioAsync(folder, VisualStudioMode.Solution);
    }

    /// <summary>The folder button, shown once either mode is open.</summary>
    private void FolderVisualStudio_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string folder) _ = ReachVisualStudioAsync(folder, VisualStudioMode.Folder);
    }

    /// <summary>
    /// Focuses the instance that has this worktree open the given way, or starts one if none
    /// has. No menu either way: each button stands for one mode, so there is nothing to ask.
    /// </summary>
    /// <remarks>
    /// The lookup is done afresh rather than read from the flags the refresh left behind: those
    /// can be a minute old, and an instance can close between a refresh and a click.
    /// </remarks>
    private async Task ReachVisualStudioAsync(string folder, VisualStudioMode mode)
    {
        try
        {
            // Off the UI thread: a COM call into an application that may be mid-build.
            var open = await Task.Run(() => VisualStudioInstances.Holding(folder, mode));

            if (open is not null)
            {
                if (VisualStudioInstances.Focus(open)) Report($"Focused Visual Studio: {open.Label}");
                else Fail($"Could not focus {open.Label}; that window may have closed.");
                return;
            }

            if (mode == VisualStudioMode.Solution) RunVisualStudioScript(folder);
            else OpenFolderInVisualStudio(folder);
        }
        catch (Exception ex)
        {
            Fail($"Could not open Visual Studio: {ex.Message}");
        }
    }

    /// <summary>
    /// Offers the two ways into Visual Studio, because only the user knows which is wanted:
    /// the script generates the Windows solution, while opening the folder is what the builds
    /// configured through CMake are worked on.
    /// </summary>
    /// <remarks>
    /// Only while nothing has it open. Once something does, each mode gets a button of its own
    /// — one to focus what is running, one to start the other way — because two instances on
    /// one worktree is a supported way to work, and a menu that asked again every time would
    /// stand between the user and the second one.
    /// </remarks>
    private void AskHowToOpenVisualStudio(string folder, FrameworkElement? anchor)
    {
        var menu = new ContextMenu();
        if (anchor is not null)
        {
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.Bottom;
        }

        // The same two marks the row carries once something is open, so the choice made here
        // and the button it turns into are recognisably the same thing.
        menu.Items.Add(Choice(
            $"Generate the solution and open it  ({Launcher.VisualStudioScript})",
            "Runs the script, which generates the solution and opens it. This is the Windows build.",
            FindResource("VsSolutionIcon"),
            () => RunVisualStudioScript(folder),
            preferred: true));

        menu.Items.Add(Choice(
            "Open this folder in Visual Studio",
            "Opens the worktree as a folder, for the builds configured through CMake.",
            FindResource("VsFolderIcon"),
            () => OpenFolderInVisualStudio(folder)));

        menu.IsOpen = true;

        static MenuItem Choice(string header, string explanation, object icon, Action run, bool preferred = false)
        {
            var item = new MenuItem { Header = header, ToolTip = explanation, Icon = icon };
            if (preferred) item.FontWeight = FontWeights.SemiBold;
            item.Click += (_, _) => run();
            return item;
        }
    }

    private void RunVisualStudioScript(string folder)
    {
        try
        {
            Launcher.OpenInVisualStudio(folder);
            // Not "opened": the script has minutes of work to do before the IDE appears.
            Report($"Running {Launcher.VisualStudioScript}: {folder}");
            _ = WatchForSolutionAsync(folder);
        }
        catch (Exception ex)
        {
            Fail($"Could not run {Launcher.VisualStudioScript}: {ex.Message}");
        }
    }

    private void OpenFolderInVisualStudio(string folder)
    {
        try
        {
            Launcher.OpenFolderInVisualStudio(folder);
            Report($"Opening in Visual Studio: {folder}");
            _ = WatchForFolderAsync(folder);
        }
        catch (Exception ex)
        {
            Fail($"Could not open Visual Studio: {ex.Message}");
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
