using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Windows.Management.Deployment;

namespace PermaDel.Services;

/// <summary>
/// Manages the sparse package that gives PermaDel the package identity File Explorer requires before it loads
/// "Shred with PermaDel" into the Windows 11 context menu. The package is unsigned, so Windows only lets an
/// administrator register it.
/// </summary>
internal static class ShellIntegration
{
    public const string RegisterArgument = "--register-shell";
    public const string UnregisterArgument = "--unregister-shell";

    /// <summary>ERROR_CANCELLED, returned when the UAC prompt is declined.</summary>
    public const int ErrorCancelled = 1223;

    private const string PackageName = "PermaDel.ShellExtension";

    private static readonly string PackagePath = Path.Combine(AppContext.BaseDirectory, "PermaDel.ShellExtension.msix");

    /// <summary>The extension ships with installed builds and needs Windows 11.</summary>
    [SupportedOSPlatformGuard("windows10.0.22000")]
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && File.Exists(PackagePath);

    public static bool IsRegistered => FindPackages(new PackageManager()).Any();

    public static async Task RegisterAsync()
    {
        if (!IsSupported)
            throw new PlatformNotSupportedException("The File Explorer extension requires an installed copy of PermaDel on Windows 11.");

        var options = new AddPackageOptions
        {
            ExternalLocationUri = new Uri(AppContext.BaseDirectory),
            AllowUnsigned = true,
            ForceUpdateFromAnyVersion = true,
        };
        await new PackageManager().AddPackageByUriAsync(new Uri(PackagePath), options);
    }

    public static async Task UnregisterAsync()
    {
        var manager = new PackageManager();
        foreach (var package in FindPackages(manager).ToList())
            await manager.RemovePackageAsync(package.Id.FullName);
    }

    /// <summary>Runs PermaDel elevated with the given argument and returns its exit code, or <see cref="ErrorCancelled"/>.</summary>
    public static async Task<int> RunElevatedAsync(string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, argument) { UseShellExecute = true, Verb = "runas" })!;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return ErrorCancelled;
        }
    }

    private static IEnumerable<Windows.ApplicationModel.Package> FindPackages(PackageManager manager) =>
        manager.FindPackagesForUser(string.Empty).Where(package => package.Id.Name == PackageName);
}
