# Worktree Helper

A small Windows tray app that lists the git worktrees of a repository and opens any of them
in VS Code, a terminal, or File Explorer — and tells you at a glance which one you left work
in.

![The window listing three worktrees with their branches, uncommitted counts and per-row action buttons](docs/screenshot.png)

Each row is one worktree: its folder name, the branch it is on, and — only when there is
something to say — a dot with the number of uncommitted paths and `↑`/`↓` counts against its
upstream. A quiet row means there is nothing waiting in it.

- **Lives in the tray.** Left-click toggles the window, right-click lists the worktrees so one
  can be opened in VS Code without the window ever appearing. Closing the window (or `Esc`)
  hides it; **Exit** on the tray menu quits.
- **Knows what is already open.** The VS Code button is tinted for a worktree that already has
  a window, and clicking focuses that window instead of opening a second one.
- **Keeps the paths you gave it.** A folder reached through a symbolic link or junction has two
  names, and git always reports the target; see [Symbolic links](#symbolic-links-and-junctions).
- **Sizes itself** to the list after every load, capped to the monitor's work area, and parks in
  the corner by the notification area until you move it — then it remembers where you put it.
- **One instance.** Launching it again shows the copy already in the tray.
- **Taskbar or tray.** Asked once on the first run, changeable afterwards from the tray menu.
- **Updates itself.** See [Updating](#updating).
- WPF on .NET 10 with the Fluent theme, following the Windows light/dark setting live, and
  Per-Monitor V2 DPI aware. Built for Windows 11; on Windows 10 the title-bar colour and
  rounded corners simply do not apply.
- Hovering the app icon says which version this is.
- Remembers the repository, the window position and the pin in
  `%APPDATA%\WorktreeHelper\settings.json`.

## Install

Grab `WorkTreeHelper-V*.zip` from the releases and unzip `WorktreeHelper.exe` anywhere — there
is no installer, and the app writes only the settings file above.

It needs:

| Requirement | Why |
|---|---|
| **.NET 10 Desktop Runtime**, x64 | The exe is ~350 KB: it carries the app, not the runtime. The *Desktop* runtime, not the plain one — this is WPF. <https://dotnet.microsoft.com/download/dotnet/10.0> |
| **`git` on PATH** | Every listing is `git worktree list` / `git status` under the hood. |
| Windows 10 or 11, x64 | Published `win-x64`. |

VS Code and Windows Terminal are optional — those buttons fall back to `code` on PATH and to
PowerShell. The exe is unsigned, so Windows shows a SmartScreen prompt the first time
(**More info** → **Run anyway**).

## Updating

The app asks GitHub for the latest release when it starts, whenever you press **Refresh**, and
once a day otherwise. When one is newer than the copy you are running, it says so: a notice
slides under the title bar naming the version, with **Install** on it, and takes itself away
after a few seconds — or stays as long as the pointer is on it. A release found while the
window is in the tray is announced the next time you open it.

The notice leaves behind the button that does the work: first in the title bar, carrying the
version it offers, and hovering it adds the version you have and the first few lines of that
release's notes. Pressing either fetches the release, puts the new exe where this one lives,
and restarts into it.

Nothing is disturbed until the download has arrived and the exe inside it has been checked to
report the version the release claims, so an interrupted or failed update leaves the app as it
was. The copy it replaces is kept beside it as `WorktreeHelper.exe.old` and cleared away at the
next start. Drafts and pre-releases are never offered, and a check that finds nothing — no
release, no network, already current — says nothing.

Updating needs write access to wherever the exe sits, so a copy under `Program Files` will
report that it cannot install and needs replacing by hand.

## Taskbar or tray

The first run asks whether to take a taskbar button as well as the tray icon. Either answer is
remembered, and **Show in taskbar** on the tray menu changes it at any time — it applies
immediately, without a restart.

Tray-only is the default and how the app is meant to live. With the taskbar button on, the window
also takes an Alt+Tab entry, minimises and restores as the taskbar button is clicked, and that
button can be right-clicked and pinned — which is the way to pin a tray-resident app to the
taskbar.

## Use

1. Point it at a repository: the **folder** button in the title bar (`Ctrl+O`), or drop a
   folder onto the window, or click the path in the title bar (`Ctrl+L`) and type one. Any
   folder inside the repository or inside any of its worktrees will do — `git worktree list`
   reports all of them from any of them.
2. Per row, from the buttons on the right:
   - **`{}`** — Visual Studio Code. Tinted when that worktree already has a window, and then
     it focuses rather than launches. Double-clicking the row does the same thing.
   - **terminal** — Windows Terminal (`wt -d`), falling back to PowerShell.
   - **folder** — File Explorer.
   - **`VS`** — only for repositories that carry a Visual Studio script; see below. Once
     something is open there are two: the solution, and a folder marked `VS` for the other way in.
   - **`PR`** — only when that branch has an open pull request; opens it in the browser.
3. **Right-click a row** for the same actions plus **Copy path** and **Copy branch name**.
   Hovering a row shows its full path; hovering the drift counts spells them out in words and
   names the upstream they are measured against.
4. **Refresh** (`F5`) re-reads everything, and so does showing the window from the tray. The
   pin keeps the window above other windows, and survives a restart.

## Build

Requires the .NET 10 SDK (bundled with Visual Studio 2026, or from
<https://dotnet.microsoft.com/download>) and `git` on PATH.

```bat
build.cmd
```

which is `dotnet publish` into `dist\WorktreeHelper.exe`, framework-dependent and single-file.
For development, `dotnet run` or open `WorktreeHelper.csproj`.

```bat
test.cmd
```

covers the two parsers and the symbolic-link rewriting — the places the bugs actually were.
The link tests build junctions in `%TEMP%`, which needs no elevation; where even that is
refused they assert nothing rather than failing on the environment.

## Symbolic links and junctions

A folder reached through a link has two names — `C:\git\repo` may be a link to `D:\git\repo` —
and git always reports the target. Typing the path is the way to pick the link itself, because
the folder browser resolves links and can only ever hand back the target. When the chosen
folder is reached through one, the worktree paths are rewritten back through it
([`LinkPaths.cs`](LinkPaths.cs)), so the list and everything the buttons launch use the name
you picked. Each rewritten path is checked to be a real second name for that folder; anything
outside the link keeps the path git gave.

## The Visual Studio button

This one is opt-in by convention rather than configuration: a worktree whose root holds
**`CreateVS-2026.bat`** gets a `VS` button. Repositories without such a script show nothing
there, which is why the rest of the app works anywhere.

While nothing has the worktree open, one button asks which way in is wanted:

- **Generate the solution and open it** — runs the script, in a visible console, since it takes
  minutes, prints progress and can stop for input. Its working directory is the worktree, which
  is where it builds. The script opens the IDE itself when it finishes.
- **Open this folder in Visual Studio** — hands the worktree to `devenv` as a folder, for the
  builds configured through CMake. Visual Studio is located through `vswhere`, which ships with
  its installer, rather than by guessing at a path.

Once either way is open, that single button is replaced by one per way: the open one tints and
focuses its window, the other starts the second instance with no further asking. Two instances
on one worktree — the Windows solution and the folder — is a supported way to work.

Which is which comes from the **Running Object Table** rather than window titles: every worktree
generates a solution of the same name, so the captions are identical, and a custom title template
can leave the folder's name out entirely. The automation object instead names what it has open —
`…\mapcore2\VS\MapCore.slnx` for a solution, `…\mapcore2` for a folder — which carries both the
worktree and the mode. An instance running elevated while this app is not cannot be seen, since
the two do not share a Running Object Table.

## The pull request button

A worktree whose branch has an open pull request gets a `PR` button at the end of its row,
which opens it in the default browser; hovering names and numbers it, and says if it is still a
draft. The repository is taken from `remote.origin.url`, so this needs no configuration and
nothing installed — but a **private** repository needs a token to read, and the app will not ask
you for one. It looks in `GH_TOKEN`, `GITHUB_TOKEN`, then `gh auth token` if the GitHub CLI
happens to be installed, then git's own credential helper, which is invoked with
`credential.interactive=false` and `GIT_TERMINAL_PROMPT=0` so that a machine with nothing stored
cannot be made to raise a sign-in dialog by a background refresh. Where none of those answer,
no buttons appear. A public repository needs none of it.

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
| `Settings.cs` | JSON settings in `%APPDATA%` |
| `tests\WorktreeHelper.Tests` | xunit cover for the parsers and the link rewriting |

## License

[Apache 2.0](LICENSE).
