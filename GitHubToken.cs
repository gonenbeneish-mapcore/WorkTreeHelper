namespace WorktreeHelper;

/// <summary>
/// A token for the GitHub API, from wherever this machine already keeps one, so the app's
/// calls are made as the user rather than anonymously.
/// </summary>
/// <remarks>
/// Anonymous calls are limited to 60 an hour for each public IP address, shared by everyone
/// behind it, an office for instance, and by every anonymous call to the API from there, not
/// only this app's. A call made with a token counts against that user's own 5,000 an hour.
///
/// Looked for in the places a developer machine keeps one: the usual environment variables,
/// the GitHub CLI if it happens to be there, and finally git's own credential helper, asked in
/// a way that cannot pop a prompt. Where none of them has one, the calls go anonymously, which
/// public repositories still answer.
/// </remarks>
internal static class GitHubToken
{
    /// <summary>The token, or null when this machine keeps none.</summary>
    /// <param name="workingDirectory">
    /// Where git and gh are run. A repository's folder, where there is one, so a credential
    /// helper configured for that repository alone is the one asked.
    /// </param>
    public static async Task<string?> FindAsync(string workingDirectory, CancellationToken ct)
    {
        foreach (var name in new[] { "GH_TOKEN", "GITHUB_TOKEN" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } fromEnvironment)
                return fromEnvironment;

        // Only if it is installed; this is not a dependency, just a place to look.
        if ((await Commands.RunAsync("gh", "auth token", workingDirectory, ct).ConfigureAwait(false)).Trim() is { Length: > 0 } fromCli)
            return fromCli;

        return await FromCredentialHelperAsync(workingDirectory, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks git for the credential it already holds for github.com.
    /// </summary>
    /// <remarks>
    /// Interactivity is turned off twice over — the config switch and the environment variable
    /// — because a credential helper with nothing stored would otherwise put a sign-in dialog
    /// on screen, which is not a thing a background check should ever do.
    /// </remarks>
    private static async Task<string?> FromCredentialHelperAsync(string workingDirectory, CancellationToken ct)
    {
        var output = await Commands.RunAsync(
            "git", "-c credential.interactive=false credential fill", workingDirectory, ct,
            input: "protocol=https\nhost=github.com\n\n",
            environment: ("GIT_TERMINAL_PROMPT", "0")).ConfigureAwait(false);

        foreach (var line in output.Split('\n'))
            if (line.StartsWith("password=", StringComparison.Ordinal))
                return line["password=".Length..].Trim();

        return null;
    }
}
