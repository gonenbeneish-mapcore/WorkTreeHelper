<#
.SYNOPSIS
    Builds a small git repository whose worktrees are each left in a different state, for
    screenshots and for trying the app out without pointing it at anything real.

.DESCRIPTION
    Three things are created side by side, so removing them is removing three folders:

        <Root>\<Name>                  the repository itself, on main
        <Root>\<Name>.worktrees\*      one folder per worktree
        <Root>\<Name>.origin.git       a bare repository standing in for the remote

    The states are the point. Between them the four rows cover everything a row can show:
    nothing at all, uncommitted files, commits waiting to be pushed, and commits waiting to
    be pulled.

        rubber-duck    main                        clean and level
        duck-bath      fix/sinks-in-the-bath       1 file open, 3 commits to pull
        duck-bubbles   spike/extra-bubbles         1 commit to push
        duck-quack     feature/teach-it-to-quack   3 files open, 2 commits to push

    With -VisualStudio, duck-bath and duck-quack also carry a stand-in CreateVS-2026.bat, so
    their rows get the VS button and the other two do not - which is what the "in columns"
    layout needs rows to differ by. It is left out by default because the README's picture is
    taken from this repository, and most repositories have no such script. The stand-in only
    says what it is; there is no solution to generate.

    PR cannot be demonstrated this way: it wants a real github.com remote with an open pull
    request, which is the honest state for most repositories.

.PARAMETER Root
    Where to build it. Inside the project by default, in a folder git ignores: a script has
    no business scattering folders through the rest of someone's drive. The repository's own
    path is what the app shows in its title bar, so a screenshot reads better from somewhere
    shorter — the picture in the README was taken with -Root D:\git.

.PARAMETER VisualStudio
    Give two of the worktrees a stand-in Visual Studio script, so the VS button appears.

.PARAMETER Remove
    Delete the three folders and build nothing.

.EXAMPLE
    tools\make-demo-repo.ps1
    tools\make-demo-repo.ps1 -VisualStudio
    tools\make-demo-repo.ps1 -Root C:\temp -Name duck-pond
    tools\make-demo-repo.ps1 -Remove

    -Remove deletes what the same -Root and -Name would have built, so pass them again if you
    passed them in the first place.
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot "..\demo"),
    [string]$Name = "rubber-duck",
    [switch]$VisualStudio,
    [switch]$Remove
)

$ErrorActionPreference = "Stop"

$repo    = Join-Path $Root $Name
$trees   = Join-Path $Root "$Name.worktrees"
$origin  = Join-Path $Root "$Name.origin.git"

function Invoke-Git {
    # The arguments come as one array rather than as trailing words: PowerShell would read a
    # git flag such as -A as an abbreviation of this function's own -Arguments parameter.
    #
    # stderr is deliberately not redirected. Windows PowerShell wraps each line a native
    # command writes there in an error record, so "2>&1" would turn one of git's ordinary
    # warnings into a failure of this script.
    param([string]$In, [string[]]$Arguments)
    & git -C $In @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed in ${In} (exit $LASTEXITCODE)" }
}

# No BOM: git would carry one into the file and it would show up in diffs forever.
function Write-Text {
    param([string]$Path, [string]$Text)
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

foreach ($path in @($repo, $trees, $origin)) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}
if ($Remove) { "Removed $repo, $trees and $origin"; return }

New-Item -ItemType Directory -Path $Root -Force | Out-Null
$Root   = (Resolve-Path $Root).Path
$repo   = Join-Path $Root $Name
$trees  = Join-Path $Root "$Name.worktrees"
$origin = Join-Path $Root "$Name.origin.git"

# ---- the repository, and a bare one to push at ------------------------------
& git init --bare -q $origin
& git init -q -b main $repo
Invoke-Git $repo @('config', 'user.name', "Worktree Helper demo")
Invoke-Git $repo @('config', 'user.email', "demo@example.invalid")
Invoke-Git $repo @('config', 'commit.gpgsign', 'false')
# The demo's files are written with newlines; without this git warns about every one of them.
Invoke-Git $repo @('config', 'core.autocrlf', 'false')

Write-Text (Join-Path $repo "README.md") "# Rubber Duck`n`nIt listens. That is the whole product.`n"
Write-Text (Join-Path $repo "duck.sh") "quack()`n{`n    echo `"quack`"`n}`n"
Invoke-Git $repo @('add', '-A')
Invoke-Git $repo @('commit', '-qm', "The duck exists")
Invoke-Git $repo @('remote', 'add', 'origin', $origin)
Invoke-Git $repo @('push', '-q', '-u', 'origin', 'main')

# ---- one worktree per state -------------------------------------------------
$quack   = Join-Path $trees "duck-quack"
$bath    = Join-Path $trees "duck-bath"
$bubbles = Join-Path $trees "duck-bubbles"

Invoke-Git $repo @('worktree', 'add', '-q', '-b', 'feature/teach-it-to-quack', $quack, 'main')
Invoke-Git $repo @('worktree', 'add', '-q', '-b', 'fix/sinks-in-the-bath', $bath, 'main')
Invoke-Git $repo @('worktree', 'add', '-q', '-b', 'spike/extra-bubbles', $bubbles, 'main')

# Asked for, two of them carry the Visual Studio script, committed before anything is pushed
# so the states below come out the same as without it.
if ($VisualStudio) {
    $standIn = "@echo off`r`necho This is the Worktree Helper demo repository. There is no solution to generate:`r`necho this file is here so the VS button has something to stand for.`r`npause`r`n"
    foreach ($tree in @($quack, $bath)) {
        Write-Text (Join-Path $tree "CreateVS-2026.bat") $standIn
        Invoke-Git $tree @('add', '-A')
        Invoke-Git $tree @('commit', '-qm', "Visual Studio, of a sort")
    }
}

# quack: two commits not yet pushed, and three files still open
Invoke-Git $quack @('push', '-q', '-u', 'origin', 'feature/teach-it-to-quack')
Write-Text (Join-Path $quack "duck.sh") "quack()`n{`n    echo `"QUACK`"`n}`n"
Invoke-Git $quack @('commit', '-qam', "Louder")
Write-Text (Join-Path $quack "squeak.sh") "squeak()`n{`n    echo `"squeak`"`n}`n"
Invoke-Git $quack @('add', '-A')
Invoke-Git $quack @('commit', '-qm', "A second noise")
Write-Text (Join-Path $quack "honk.sh") "honk`n"
Write-Text (Join-Path $quack "TODO.md") "- teach it to quack`n"
Write-Text (Join-Path $quack "README.md") "# Rubber Duck`n`nIt listens. Soon it will answer.`n"

# bubbles: one commit waiting to go out
Invoke-Git $bubbles @('push', '-q', '-u', 'origin', 'spike/extra-bubbles')
Write-Text (Join-Path $bubbles "bubbles.sh") "bubbles()`n{`n    echo `"blub`"`n}`n"
Invoke-Git $bubbles @('add', '-A')
Invoke-Git $bubbles @('commit', '-qm', "Blub")

# bath: pushed, and then the branch moved on without it. The commits are made on a throwaway
# worktree and pushed over the branch, because git will not check one branch out twice.
Invoke-Git $bath @('push', '-q', '-u', 'origin', 'fix/sinks-in-the-bath')
$scratch = Join-Path $trees "_scratch"
Invoke-Git $repo @('branch', '-q', 'bath-scratch', 'fix/sinks-in-the-bath')
Invoke-Git $repo @('worktree', 'add', '-q', $scratch, 'bath-scratch')
foreach ($step in @(
    @{ File = "plug.sh";     Text = "plug()`n{`n    echo `"plugged`"`n}`n"; Message = "Plug the hole" },
    @{ File = "ballast.txt"; Text = "a little weight`n";                    Message = "Ballast" },
    @{ File = "weight.txt";  Text = "a little more`n";                      Message = "More weight" })) {
    Write-Text (Join-Path $scratch $step.File) $step.Text
    Invoke-Git $scratch @('add', '-A')
    Invoke-Git $scratch @('commit', '-qm', $step.Message)
}
Invoke-Git $scratch @('push', '-q', 'origin', 'bath-scratch:fix/sinks-in-the-bath')
Invoke-Git $repo @('worktree', 'remove', '--force', $scratch)
Invoke-Git $repo @('branch', '-q', '-D', 'bath-scratch')
Invoke-Git $bath @('fetch', '-q', 'origin')
Write-Text (Join-Path $bath "wip.txt") "half a thought`n"

# ---- say what was built -----------------------------------------------------
""
"Built $repo"
""
& git -C $repo worktree list
""
"Point the app at $repo. Remove it all again with -Remove."
