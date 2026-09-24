<#
.SYNOPSIS
    Publishes the version WorktreeHelper.csproj names: pictures, release commit, build, tag and
    GitHub release, the way every release so far has been made by hand.

.DESCRIPTION
    Set <Version> in WorktreeHelper.csproj and write that version's section at the top of
    CHANGELOG.md, then run this. Everything else is committed beforehand; the changelog, the
    version and the pictures are what the release commit is made of.

    It checks before it changes anything, and stops at the first thing wrong:
      - the version is not tagged already, here or on GitHub
      - CHANGELOG.md has a section for it, with something in it
      - main on GitHub has not moved on from under this checkout
      - nothing is uncommitted but the changelog, the version and the pictures
      - gh is signed in, since it makes the release
      - the tests pass

    Then it:
      1. retakes the README's pictures with screenshots.ps1, from a build of this checkout
      2. commits "Release <version>": the changelog, the version and the pictures, if any
         of them changed
      3. builds dist\WorktreeHelper.exe with build.cmd, from that commit, and checks the exe
         says it is that version
      4. zips it as dist\WorkTreeHelper-V<version>.zip, which is what the app's own update
         looks for: a zip with the exe in it
      5. pushes the commit to main, tags it v<version>, and publishes the GitHub release with
         the changelog section as its text
      6. checks GitHub now offers it as the latest release

.PARAMETER DryRun
    Checks, tests and retakes the pictures, then stops: nothing is committed, pushed, tagged
    or published. The new pictures are left in docs\ to be looked at.

.PARAMETER SkipScreenshots
    Leaves the pictures as they are.

.PARAMETER DemoRoot
    Passed to screenshots.ps1: where the demo repository is built, which is the path the
    Options picture shows.

.PARAMETER Trailer
    Lines to end the release commit's message with, such as a Co-Authored-By.

.EXAMPLE
    tools\release.ps1 -DryRun
    tools\release.ps1
    tools\release.ps1 -DemoRoot D:\git\WorkTreeHelper\demo
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$SkipScreenshots,
    [string]$DemoRoot,
    [string]$Trailer
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

function Step([string]$Text) { ""; "== $Text" }
function Stop-Release([string]$Why) { throw "Not released: $Why" }

# git and gh write their progress to stderr. It is left alone rather than redirected: Windows
# PowerShell turns each redirected line into an error record, and with errors stopping the
# script, an ordinary "To https://github.com/..." would end it.
function Invoke-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { Stop-Release "$File $($Arguments -join ' ') failed (exit $LASTEXITCODE)." }
}

# ---- what is being released -------------------------------------------------
Step "Checking"
$csproj = [IO.File]::ReadAllText((Join-Path $root "WorktreeHelper.csproj"))
if ($csproj -notmatch '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>') { Stop-Release "WorktreeHelper.csproj has no <Version>." }
$version = $Matches[1]
$tag = "v$version"
"Version $version"

if (& git tag --list $tag) { Stop-Release "$tag is already tagged here." }
if (& git ls-remote --tags origin "refs/tags/$tag") { Stop-Release "$tag is already tagged on GitHub." }

$changelog = [IO.File]::ReadAllText((Join-Path $root "CHANGELOG.md")) -replace "`r`n", "`n"
$section = [regex]::Match($changelog, "(?ms)^## $([regex]::Escape($tag))[^\n]*\n(.*?)(?=^## |\z)")
if (-not $section.Success) { Stop-Release "CHANGELOG.md has no '## $tag' section." }
$notes = $section.Groups[1].Value.Trim()
if ($notes -notmatch '(?m)^- ') { Stop-Release "CHANGELOG.md's $tag section says nothing." }
"Release notes:"; $notes -split "`n" | ForEach-Object { "  $_" }

Invoke-Native git @('fetch', '-q', 'origin')
& git merge-base --is-ancestor origin/main HEAD
if ($LASTEXITCODE -ne 0) { Stop-Release "main on GitHub has commits this checkout does not. Bring them in first." }

# The release commit carries the changelog, the version and the pictures; anything else
# uncommitted would be released without being in the release.
$ownFiles = @('CHANGELOG.md', 'WorktreeHelper.csproj', 'docs/screenshot.png', 'docs/options.png')
$stray = & git status --porcelain | ForEach-Object { $_.Substring(3).Trim('"') } | Where-Object { $ownFiles -notcontains $_ }
if ($stray) { Stop-Release "uncommitted changes besides the changelog and version: $($stray -join ', ')" }

& gh auth status *> $null
if ($LASTEXITCODE -ne 0) { Stop-Release "gh is not signed in (gh auth login)." }

Step "Testing"
& cmd /c "`"$root\test.cmd`""
if ($LASTEXITCODE -ne 0) { Stop-Release "the tests failed." }

# ---- the pictures -----------------------------------------------------------
if (-not $SkipScreenshots) {
    Step "Retaking the README's pictures"
    # A build of its own, so the pictures are of this code rather than whatever dist holds.
    $pictureBuild = Join-Path $root "demo\.release-build"
    Invoke-Native dotnet @('build', 'WorktreeHelper.csproj', '-c', 'Release', '-v', 'q', '-nologo', '-o', $pictureBuild)
    $arguments = @{ Exe = (Join-Path $pictureBuild "WorktreeHelper.exe") }
    if ($DemoRoot) { $arguments.DemoRoot = $DemoRoot }
    & (Join-Path $PSScriptRoot "screenshots.ps1") @arguments
    Remove-Item $pictureBuild -Recurse -Force -ErrorAction SilentlyContinue
}

if ($DryRun) {
    Step "Dry run: stopping before anything is committed or published"
    & git status --short
    return
}

# ---- the release commit -----------------------------------------------------
Step "Committing"
Invoke-Native git (@('add', '--') + $ownFiles)
& git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    $message = "Release $version`n`nThe version goes to $version, and the changelog gains its section, which is also what the app`nshows as what's new."
    if ($Trailer) { $message += "`n`n$Trailer" }
    $file = Join-Path $env:TEMP "worktreehelper-release-message.txt"
    [IO.File]::WriteAllText($file, $message, (New-Object System.Text.UTF8Encoding($false)))
    Invoke-Native git @('commit', '-q', '-F', $file)
    Remove-Item $file
}
& git log --oneline -1

# ---- the build --------------------------------------------------------------
Step "Building"
& cmd /c "`"$root\build.cmd`""
if ($LASTEXITCODE -ne 0) { Stop-Release "build.cmd failed." }
$exe = Join-Path $root "dist\WorktreeHelper.exe"
$built = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
$head = (& git rev-parse HEAD).Trim()
if (-not $built.StartsWith("$version+$head")) { Stop-Release "the exe says it is $built, not $version from $head." }
"Built $built"

$zip = Join-Path $root "dist\WorkTreeHelper-V$version.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path $exe -DestinationPath $zip
"Packed $([IO.Path]::GetFileName($zip)), $([math]::Round((Get-Item $zip).Length / 1KB)) KB"

# ---- publishing -------------------------------------------------------------
Step "Publishing"
Invoke-Native git @('push', 'origin', 'HEAD:main')
Invoke-Native git @('tag', '-a', $tag, '-m', $tag, 'HEAD')
Invoke-Native git @('push', 'origin', $tag)

$notesFile = Join-Path $root "dist\release-notes.md"
[IO.File]::WriteAllText($notesFile, "$notes`n", (New-Object System.Text.UTF8Encoding($false)))
Invoke-Native gh @('release', 'create', $tag, $zip, '--title', $tag, '--notes-file', $notesFile, '--verify-tag')

$latest = & gh api "repos/{owner}/{repo}/releases/latest" --jq .tag_name
if ($latest -ne $tag) { Stop-Release "published, but GitHub's latest release is $latest, not $tag." }
""
"Released $tag."
