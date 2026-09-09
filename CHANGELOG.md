# Changelog

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
