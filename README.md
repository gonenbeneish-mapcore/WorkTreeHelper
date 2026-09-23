# Worktree Helper

Every folder your project is checked out into, in one small window, with a button to open each
one where you work.

![The window listing four worktrees of a project, each with its branch, what is uncommitted or unpushed in it, and its row of buttons](docs/screenshot.png)

## What it is

Git can have the same project checked out into several folders at once — typically one per
branch, so you can leave one thing half-finished and pick up another without putting it away
first. Those folders are called **worktrees**, and after a week or two it is easy to forget
which ones you have and what you left in them.

This lists them. One row each: the folder's name, the branch it is on, and — only when there is
something to say — a dot with the number of files you have changed and `↑`/`↓` counts for
commits not yet pushed or pulled. **A quiet row means nothing is waiting in it.**

## Using it

**Point it at your project.** Drag the folder onto the window, or press the folder button in the
title bar. Any folder inside the project will do; it finds the rest by itself. It remembers,
so this is a first-run thing.

**Open one.** The buttons at the right of each row, in order:

| Button | Opens |
|---|---|
| `{}` | **VS Code.** Double-clicking the row does the same. Highlighted when that folder is already open — then it brings that window forward rather than opening a second one. |
| terminal | A **terminal** in that folder. |
| folder | That folder in **File Explorer**. |
| `VS` | **Visual Studio** — only appears for projects set up for it, which is why it is not in the picture; see [The Visual Studio button](#the-visual-studio-button). |
| `PR` | The open **pull request** for that branch, in your browser. Only on branches that have one. |

**Right-click a row** for the same things plus **Copy path** and **Copy branch name**. Hovering a
row shows its full path, and hovering the counts says what they mean in words. `F5` re-reads
everything.

## Where it lives

The first run asks where you want it: the **taskbar** or the **system tray**. One or the other —
and either answer can be changed later.

| | System tray | Taskbar |
|---|---|---|
| Where to find it | Icon in the notification area | A taskbar button, and Alt+Tab |
| Clicking it | Shows and hides the window | Minimises and restores the window |
| Its menu | Right-click the icon: the worktrees, so one can be opened without the window appearing at all | Click the icon in the title bar, or right-click the title bar |
| Closing the window | Puts it away; **Exit** on the menu quits | Quits the app. `Esc` minimises instead |

To change your mind, open **Options** (the gear in the title bar, or on that menu). It applies
straight away.

## Options

The gear in the title bar opens them; every change applies as you make it.

| Option | Choices |
|---|---|
| **Where it lives** | **System tray** (the default) or **Taskbar** — see above. |
| **A second Visual Studio** | **Allowed** (the default): once the solution or the folder is open, the row offers the other one too. **Not allowed**: it only offers to bring back the one that is open. |
| **Buttons** | **Packed** (the default): each row’s buttons sit together, so the window is as small as it can be. **In columns**: each kind of button keeps its own column down the list, so every `PR` lines up, with a gap in a row that has none. |

## Install

Grab `WorkTreeHelper-V*.zip` from the releases and unzip `WorktreeHelper.exe` anywhere — there
is no installer, and the app writes only `%APPDATA%\WorktreeHelper\settings.json`.

It needs:

| Requirement | Why |
|---|---|
| **.NET 10 Desktop Runtime**, x64 | The exe is ~400 KB: it carries the app, not the runtime. The *Desktop* runtime, not the plain one — this is WPF. <https://dotnet.microsoft.com/download/dotnet/10.0> |
| **`git` on PATH** | Every listing is `git worktree list` / `git status` under the hood. |
| Windows 10 or 11, x64 | Published `win-x64`. |

VS Code and Windows Terminal are optional — those buttons fall back to `code` on PATH and to
PowerShell. The exe is unsigned, so Windows shows a SmartScreen prompt the first time
(**More info** → **Run anyway**).

## Updating

It asks GitHub for the latest release at startup, whenever you press **Refresh**, and once a day
otherwise. When there is a newer one it says so: a notice under the title bar naming the version,
with **Install** on it, gone after a few seconds — and a button carrying that version stays at
the front of the title bar. Either one fetches the release, puts the new exe where this one
lives, and restarts into it.

Nothing is disturbed until the download has arrived and the exe inside it reports the version the
release claims, so a failed or interrupted update leaves the app as it was; the copy it replaces
is kept as `WorktreeHelper.exe.old` until the next start. Drafts and pre-releases are never
offered, and a check that finds nothing says nothing. Updating needs write access to wherever the
exe sits, so a copy under `Program Files` will report that it cannot install itself.

## Other things worth knowing

- **One instance.** Launching it again shows the copy already running.
- **It sizes itself** to the list, capped to the monitor, and parks in the corner by the
  notification area until you move it — then it remembers where you put it.
- **The pin** keeps the window above other windows, and survives a restart.
- **The icon follows your theme**: a dark disc on a light taskbar, a pale one on a dark
  taskbar, so it stays findable either way. Clicking it in the title bar opens the app’s
  menu, and hovering it says which version this is.
- WPF on .NET 10 with the Fluent theme, following the Windows light/dark setting live, and
  Per-Monitor V2 DPI aware. Built for Windows 11; on Windows 10 the title-bar colour and
  rounded corners simply do not apply.

## Build

Requires the .NET 10 SDK (bundled with Visual Studio 2026, or from
<https://dotnet.microsoft.com/download>) and `git` on PATH.

```bat
build.cmd
```

which is `dotnet publish` into `dist\WorktreeHelper.exe`, framework-dependent and single-file.
For development, `dotnet run` or open `WorktreeHelper.csproj`.

```powershell
tools\make-icon.ps1
```

redraws `app.ico` and `app-dark.ico` — the two colourways — at every size the shell asks
for. Drawn at each size rather than scaled down from one large bitmap, so 16px stays sharp.

```bat
test.cmd
```

covers the parsers, the update service and the symbolic-link rewriting — the places the bugs
actually were. The link tests build junctions in `%TEMP%`, which needs no elevation; where even
that is refused they assert nothing rather than failing on the environment.

```powershell
tools\make-demo-repo.ps1
```

builds `demo\rubber-duck`, which git ignores: a throwaway repository whose four worktrees
are each left in a different state — clean, files open, commits to push, commits to pull —
which is what the picture at the top of this file is of. Handy for trying a change against
something other than your own work. `-VisualStudio` gives two of the rows a stand-in Visual Studio
script so the VS button shows, `-Root` and `-Name` put it elsewhere, and `-Remove` takes it
away again.

## Symbolic links and junctions

A folder reached through a link has two names — `C:\git\repo` may be a link to `D:\git\repo` —
and git always reports the target. Typing the path (`Ctrl+L`) is the way to pick the link itself, because
the folder browser resolves links and can only ever hand back the target. When the chosen folder
is reached through one, the worktree paths are rewritten back through it
([`LinkPaths.cs`](LinkPaths.cs)), so the list and everything the buttons launch use the name you
picked. Each rewritten path is checked to be a real second name for that folder; anything outside
the link keeps the path git gave.

## The Visual Studio button

Opt-in by convention rather than configuration: a worktree whose root holds
**`CreateVS-2026.bat`** gets a `VS` button. Repositories without such a script show nothing
there, which is why the rest of the app works anywhere.

While nothing has the worktree open, one button asks which way in is wanted:

- **Generate the solution and open it** — runs the script, in a visible console, since it takes
  minutes, prints progress and can stop for input. Its working directory is the worktree, which
  is where it builds. The script opens the IDE itself when it finishes.
- **Open this folder in Visual Studio** — hands the worktree to `devenv` as a folder, for the
  builds configured through CMake. Visual Studio is located through `vswhere`, which ships with
  its installer, rather than by guessing at a path.

Once either way is open, that single button becomes one per way: the open one tints and focuses
its window, the other starts the second instance with no further asking. Two instances on one
worktree — the Windows solution and the folder — is a supported way to work.

Which is which comes from the **Running Object Table** rather than window titles: every worktree
generates a solution of the same name, so the captions are identical, and a custom title template
can leave the folder's name out entirely. The automation object instead names what it has open —
`…\mapcore2\VS\MapCore.slnx` for a solution, `…\mapcore2` for a folder — which carries both the
worktree and the mode. An instance running elevated while this app is not cannot be seen, since
the two do not share a Running Object Table.

## The pull request button

A worktree whose branch has an open pull request gets a `PR` button at the end of its row, which
opens it in the default browser; hovering names and numbers it, and says if it is still a draft.
The repository is taken from `remote.origin.url`, so this needs no configuration and nothing
installed — but a **private** repository needs a token to read, and the app will not ask you for
one. It looks in `GH_TOKEN`, `GITHUB_TOKEN`, then `gh auth token` if the GitHub CLI happens to be
installed, then git's own credential helper, which is invoked with `credential.interactive=false`
and `GIT_TERMINAL_PROMPT=0` so that a machine with nothing stored cannot be made to raise a
sign-in dialog by a background refresh. Where none of those answer, no buttons appear. A public
repository needs none of it.

> VS Code exposes no API for "which folders are open", and its process list only ever names the
> folder the *first* window was launched with, so that detection matches window titles instead.
> Because `window.title` is user-configurable, every `" - "`-separated segment is compared
> against the worktree's folder name rather than assuming the default template. Two folders
> sharing a leaf name are therefore indistinguishable.

## Files

| File | Purpose |
|------|---------|
| `App.xaml(.cs)` | Fluent theme following the OS theme; the single-instance gate |
| `SingleInstance.cs` | Mutex plus a broadcast that raises the copy already running |
| `app.manifest` | PerMonitorV2 DPI awareness, long paths |
| `MainWindow.xaml(.cs)` | UI and view logic |
| `OptionsWindow.xaml(.cs)` | The options, bound straight to the main window’s own properties |
| `TitleBar.cs` | Caption colour and rounded corners, via DWM |
| `GitService.cs` | Runs and parses `git worktree list --porcelain` and `git status --porcelain=v2` |
| `TrayIcon.cs` | The notification-area icon, straight through `Shell_NotifyIcon` |
| `PopupPlacement.cs` | Puts the tray menu on the cursor and gives it the foreground |
| `LinkPaths.cs` | Rewrites git's paths back through the symbolic link you picked |
| `Launcher.cs` | Opens VS Code / terminal / Explorer / the Visual Studio script |
| `VsCodeWindows.cs` | Finds and focuses the VS Code window that has a worktree open |
| `VisualStudioInstances.cs` | The same for Visual Studio, through the Running Object Table |
| `VisualStudioInstance.cs` | One instance: what it has open, and which of the two ways |
| `PullRequests.cs` | The open pull requests of the repository, by branch |
| `UpdateService.cs` | Finds a newer GitHub release, and installs it over this copy |
| `MonitorWorkArea.cs` | Work area of the monitor the window is on, for the auto-size cap |
| `AppIcon.cs` | Picks the icon colourway that suits the theme, and builds the tray’s copy of it |
| `Settings.cs` | JSON settings in `%APPDATA%` |
| `tests\WorktreeHelper.Tests` | xunit cover for the parsers, the update service and the link rewriting |

## License

[Apache 2.0](LICENSE).
