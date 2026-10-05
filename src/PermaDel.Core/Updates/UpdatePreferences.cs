namespace PermaDel.Core.Updates;

/// <summary>A snapshot of the two update-related preferences, for display.</summary>
public sealed record UpdatePreferences(bool CheckAutomatically, DateTimeOffset? LastCheckUtc);

/// <summary>
/// The app's settings store, seen by the update service. The WinUI layer
/// implements it over the registry; tests use an in-memory fake.
/// </summary>
public interface IUpdatePreferences
{
    /// <summary>The Settings toggle, on by default.</summary>
    bool CheckForUpdatesAutomatically { get; set; }

    /// <summary>
    /// When a check last reached the API, written by the service (never by the
    /// UI). Left alone after a failure so the next start can retry.
    /// </summary>
    DateTimeOffset? LastUpdateCheckUtc { get; set; }
}
