using Microsoft.UI.Xaml;
using Microsoft.Win32;
using PermaDel.Core;

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
}
