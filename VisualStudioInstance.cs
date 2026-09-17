using System.IO;

namespace WorktreeHelper;

/// <summary>The two ways a worktree can be open in Visual Studio.</summary>
public enum VisualStudioMode
{
    /// <summary>A generated solution, which is the Windows build.</summary>
    Solution,

    /// <summary>The worktree folder itself, which is how the CMake-configured builds are worked on.</summary>
    Folder,
}

/// <summary>One running Visual Studio, and what it has open.</summary>
/// <remarks>
/// Visual Studio reports both cases through the same automation property: a solution instance
/// names the solution file, and a folder instance names the folder. Measured on 18.0:
/// <c>C:\git\mapcore2\VS\MapCore.slnx</c> against <c>C:\git\mapcore2</c>. That one string is
/// therefore enough to tell the two modes apart and to say which worktree each belongs to —
/// no window titles, which a custom title template can strip the folder name out of, and no
/// process command line, which is absent when a folder is reopened from the recent list.
/// </remarks>
public sealed record VisualStudioInstance(string Opened, IntPtr Hwnd)
{
    /// <summary>Which of the two ways this instance was opened.</summary>
    public VisualStudioMode Mode => IsSolutionFile(Opened) ? VisualStudioMode.Solution : VisualStudioMode.Folder;

    /// <summary>True when this instance has <paramref name="worktree"/> open, either way.</summary>
    /// <remarks>
    /// Through <see cref="LinkPaths.IsUnder"/>, so a worktree named through a symbolic link
    /// still matches, and a sibling worktree whose name merely starts the same does not.
    /// </remarks>
    public bool Holds(string worktree) => Opened.Length > 0 && LinkPaths.IsUnder(Opened, worktree);

    /// <summary>What to call this instance in a menu: the solution's file name, or the folder's.</summary>
    public string Label
    {
        get
        {
            var name = Path.GetFileName(Opened.TrimEnd('\\', '/'));
            return name.Length > 0 ? name : Opened;
        }
    }

    /// <summary>
    /// Every solution extension Visual Studio opens, so that anything else — which is a folder
    /// — falls to the other mode rather than being mistaken for a solution.
    /// </summary>
    private static bool IsSolutionFile(string path) => Path.GetExtension(path).ToLowerInvariant()
        is ".sln" or ".slnx" or ".slnf";
}
