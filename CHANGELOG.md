# Changelog

## v1.4.0

- **Two Visual Studio instances on one worktree.** While nothing is open the `VS` button asks
  which way in; once something is open it becomes one button per way — the open one tints and
  focuses its window, the other starts the second instance without asking again. Previously the
  first instance was focused and the second could not be started from the app at all.
  - The two are told apart by what Visual Studio says it has open: a solution instance names
    the solution file, a folder instance names the folder. Measured on 18.0, so no window
    titles, which a custom title template can strip the folder's name out of.
  - The folder button carries a folder with `VS` written inside it.
  - A folder launch no longer polls for ten minutes waiting on a flag that could not tell the
    two modes apart.
- **Taskbar or tray, asked once.** The first run offers a taskbar button as well as the tray
  icon; the answer is remembered and can be changed from **Show in taskbar** on the tray menu.
  Off means tray-only, as before. With it on, the window's taskbar button can be right-clicked
  and pinned, which is how a tray app gets pinned at all — and it takes an Alt+Tab entry and
  can be minimised.
- The window title is the repository's name rather than its full path, since that is now a
  taskbar button's label.

## v1.3.0 — unreleased

- **A pull request is a click away.** A worktree whose branch has an open pull request gets a
  `PR` button at the end of its row, named and numbered in its tooltip, that opens it in the
  browser. Read from the GitHub API over the repository's own remote, so it works for any
  repository on github.com: public ones answer with nothing installed, private ones need a
  token, which is looked for in `GH_TOKEN`/`GITHUB_TOKEN`, then the GitHub CLI if it happens to
  be installed, then git's own credential helper — asked in a way that cannot raise a prompt.
  Where none of that answers, no buttons appear and nothing is said.
- **Two ways into Visual Studio.** With nothing already open, the `VS` button now asks which is
  wanted: generate the solution with the script, or open the worktree as a folder for the
  builds configured through CMake. An instance that already has the worktree open is still
  simply brought forward, without asking. The button still appears only for worktrees carrying
  the script.

- **It updates itself.** On startup and once a day it asks GitHub for the latest release; if
  that is newer than the copy running, a download button appears in the title bar after the
  folder one, naming the version it offers and what the release says is new in it. Pressing it fetches the release, puts the new exe
  where this one lives and restarts into it.
  - Nothing is touched until the download has arrived and the exe inside it has been checked
    to report the version the release claims, so a failed or interrupted update leaves the app
    running exactly as it was.
  - The running exe is renamed aside rather than overwritten — Windows allows the first and
    refuses the second — and that copy is cleared away at the next start.
  - A check that finds nothing says nothing: no release, no network and already-current all
    look the same from the outside. Drafts and pre-releases are ignored.

## v1.2.0

- **A Visual Studio button, for repositories that have a script to open one.** A worktree
  holding `CreateVS-2026.bat` in its root gets a `VS` button; one that does not shows nothing
  there, so the app stays useful for any other project. The script runs in a visible console
  on purpose — it takes minutes, prints its progress and stops for input — with the worktree
  as its working directory, which is where it builds.
  - **It marks and focuses like the VS Code button does.** Window titles cannot tell one
    worktree from another here, because every worktree of a repository generates a solution
    of the same name. Visual Studio registers its automation object in the Running Object
    Table instead, and that knows the solution's real path, so the button tints when *that*
    worktree is open and focuses that window when pressed again.
- **The title bar carries the app's controls.** The repository path is the window title, and
  the folder, refresh and pin buttons sit at the end of the caption, which removed a row of
  its own. Click the path (or `Ctrl+L`) to type one — still the only way to name a folder
  through a symbolic link.
- **Rounded corners**, which a window drawing its own caption has to ask Windows for.
- **A third less window.** 705×227 down to 657×169 at 125% scaling, without changing a font
  size: tighter padding, one line per row, and minimums low enough that the frame can follow
  the content instead of pinning it.

## v1.1.0

- **What is waiting in each worktree.** A dot and a count for uncommitted paths, `↑2` / `↓1`
  for commits ahead of and behind upstream, from one `git status --porcelain=v2 --branch` per
  worktree run after the list is on screen. A worktree that is clean and level shows nothing,
  so a quiet row means there is nothing to come back to.
- **The tray menu lists the worktrees.** Click one and it opens in VS Code without the window
  ever appearing.
- **A row menu**: the same actions plus Copy path and Copy branch name, and double-click to
  open.
- **One instance.** A second launch shows the copy already in the tray rather than adding a
  second tray icon and a second writer of the same settings file.
- **Fixes**
  - Windows could no longer be logged off or restarted with the app running, because it
    cancelled every window close.
  - The tray icon lingered after any exit but the tray menu's own Exit.
  - Clicking the tray icon hid a window that was merely behind another one, rather than
    raising it.
  - A missing `git` was reported as "not a git repository", blaming the folder for it.
  - Cancelling a refresh left the git process running.
  - Failures now get a bar of their own, where the part of a git error that says what went
    wrong can be read in full and copied, instead of being truncated into the title bar.
- **Tests** for the two parsers and the symbolic-link rewriting — the places the bugs were.

## v1.0.0

First versioned build: the tray icon, the worktree list with its branches, the VS Code /
terminal / Explorer buttons, VS Code open-window detection, and paths kept in the shape they
were given through symbolic links and junctions.
