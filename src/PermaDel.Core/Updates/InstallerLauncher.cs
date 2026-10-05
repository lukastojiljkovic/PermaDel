namespace PermaDel.Core.Updates;

/// <summary>What happened when the downloaded installer was started.</summary>
public enum InstallOutcome
{
    /// <summary>The installer process started. The app closes itself.</summary>
    Started,

    /// <summary>The user declined the UAC prompt. Not an error.</summary>
    Cancelled,

    /// <summary>The installer could not be started.</summary>
    Failed,
}

/// <summary>
/// Starts the verified installer. A seam so tests never execute anything.
/// </summary>
public interface IInstallerLauncher
{
    Task<InstallOutcome> LaunchAsync(string installerPath, CancellationToken cancellationToken = default);
}
