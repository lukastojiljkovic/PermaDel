using Microsoft.UI.Xaml;
using PermaDel.Models;
using PermaDel.Services;

namespace PermaDel;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs()[1..];
        switch (arguments)
        {
            case [ShellIntegration.RegisterArgument]:
                Environment.Exit(await RunHelperAsync(ShellIntegration.RegisterAsync));
                return;
            case [ShellIntegration.UnregisterArgument]:
                Environment.Exit(await RunHelperAsync(ShellIntegration.UnregisterAsync));
                return;
        }

        _window = new MainWindow(ShredRequest.FromCommandLine(arguments));
        _window.Activate();
    }

    /// <summary>Windowless elevated mode used by Settings and the installer. The exit code carries the HRESULT back.</summary>
    private static async Task<int> RunHelperAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return 0;
        }
        catch (Exception ex)
        {
            return ex.HResult;
        }
    }
}
