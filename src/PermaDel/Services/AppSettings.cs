using Microsoft.UI.Xaml;
using Microsoft.Win32;
using PermaDel.Core;
using PermaDel.Core.Updates;

namespace PermaDel.Services;

/// <summary>
/// User preferences stored under HKCU\Software\PermaDel. The registry is used so the native File Explorer
/// extension and the installer can read and write the same values without parsing files.
/// </summary>
internal static class AppSettings
{
    private const string KeyPath = @"Software\PermaDel";

    public static int DefaultPasses
    {
        get => Math.Clamp(Read(nameof(DefaultPasses), 3), Shredder.MinPasses, Shredder.MaxPasses);
        set => Write(nameof(DefaultPasses), Math.Clamp(value, Shredder.MinPasses, Shredder.MaxPasses));
    }

    public static bool ConfirmBeforeShredding
    {
        get => Read(nameof(ConfirmBeforeShredding), 1) != 0;
        set => Write(nameof(ConfirmBeforeShredding), value ? 1 : 0);
    }

    /// <summary>Whether Windows Hello or the account password is required before shredding. Set by the installer.</summary>
    public static bool RequireVerification
    {
        get => Read(nameof(RequireVerification), 1) != 0;
        set => Write(nameof(RequireVerification), value ? 1 : 0);
    }

    public static bool ShowWelcome
    {
        get => Read(nameof(ShowWelcome), 1) != 0;
        set => Write(nameof(ShowWelcome), value ? 1 : 0);
    }

    public static ElementTheme Theme
    {
        get => (ElementTheme)Read(nameof(Theme), (int)ElementTheme.Default) is var theme && Enum.IsDefined(theme) ? theme : ElementTheme.Default;
        set => Write(nameof(Theme), (int)value);
    }

    /// <summary>Whether the background update check runs at startup. On by default.</summary>
    public static bool CheckForUpdatesAutomatically
    {
        get => Read(nameof(CheckForUpdatesAutomatically), 1) != 0;
        set => Write(nameof(CheckForUpdatesAutomatically), value ? 1 : 0);
    }

    /// <summary>When the update check last reached GitHub; written by the update service, never by the UI.</summary>
    public static DateTimeOffset? LastUpdateCheckUtc
    {
        get => ReadTime(nameof(LastUpdateCheckUtc));
        set => WriteTime(nameof(LastUpdateCheckUtc), value);
    }

    /// <summary>The version the app last started as, so the first launch after an update can show what changed.</summary>
    public static string? LastRunVersion
    {
        get => ReadString(nameof(LastRunVersion));
        set => WriteString(nameof(LastRunVersion), value);
    }

    /// <summary>The same store, as the update service sees it.</summary>
    public static IUpdatePreferences UpdatePreferences { get; } = new UpdatePreferencesAdapter();

    private sealed class UpdatePreferencesAdapter : IUpdatePreferences
    {
        public bool CheckForUpdatesAutomatically
        {
            get => AppSettings.CheckForUpdatesAutomatically;
            set => AppSettings.CheckForUpdatesAutomatically = value;
        }

        public DateTimeOffset? LastUpdateCheckUtc
        {
            get => AppSettings.LastUpdateCheckUtc;
            set => AppSettings.LastUpdateCheckUtc = value;
        }
    }

    private static int Read(string name, int fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) is int value ? value : fallback;
    }

    private static void Write(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    /// <summary>Times are QWORDs of Unix seconds.</summary>
    private static DateTimeOffset? ReadTime(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) is long seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    private static void WriteTime(string name, DateTimeOffset? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (value is { } time)
            key.SetValue(name, time.ToUnixTimeSeconds(), RegistryValueKind.QWord);
        else
            key.DeleteValue(name, throwOnMissingValue: false);
    }

    private static string? ReadString(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as string;
    }

    private static void WriteString(string name, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (value is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, value, RegistryValueKind.String);
    }
}
