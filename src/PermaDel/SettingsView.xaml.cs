using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PermaDel.Core;
using PermaDel.Services;

namespace PermaDel;

public sealed partial class SettingsView : UserControl
{
    private readonly nint _window;
    private bool _updating = true;

    /// <param name="window">The main window, which owns the identity verification prompt.</param>
    public SettingsView(nint window)
    {
        _window = window;
        InitializeComponent();

        PassesBox.Minimum = Shredder.MinPasses;
        PassesBox.Maximum = Shredder.MaxPasses;
        PassesBox.Value = AppSettings.DefaultPasses;
        ConfirmToggle.IsOn = AppSettings.ConfirmBeforeShredding;
        VerificationToggle.IsOn = AppSettings.RequireVerification;
        ThemeBox.SelectedIndex = (int)AppSettings.Theme;
        WelcomeToggle.IsOn = AppSettings.ShowWelcome;
        AboutCard.Description = $"Version {typeof(App).Assembly.GetName().Version?.ToString(3)} · MIT License · © 2026 Luka Stojiljkovic";

        _updating = false;
        RefreshContextMenuState();
    }

    public event EventHandler<int>? DefaultPassesChanged;

    public event EventHandler<ElementTheme>? ThemeChanged;

    /// <summary>Re-reads the registration, which can also change outside the app (for example via the installer).</summary>
    public void RefreshContextMenuState()
    {
        _updating = true;
        var supported = ShellIntegration.IsSupported;
        ContextMenuCard.Description = supported
            ? "Adds a right-click submenu for choosing the number of passes. Windows asks for administrator approval to change this."
            : "Available in the installed version of PermaDel on Windows 11.";
        ContextMenuToggle.IsEnabled = supported;
        ContextMenuToggle.IsOn = supported && ShellIntegration.IsRegistered;
        _updating = false;
    }

    private void OnPassesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updating || double.IsNaN(args.NewValue))
            return;

        AppSettings.DefaultPasses = (int)args.NewValue;
        DefaultPassesChanged?.Invoke(this, AppSettings.DefaultPasses);
    }

    private void OnConfirmToggled(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            AppSettings.ConfirmBeforeShredding = ConfirmToggle.IsOn;
    }

    /// <summary>Both turning verification on and turning it off require the user to verify first.</summary>
    private async void OnVerificationToggled(object sender, RoutedEventArgs e)
    {
        if (_updating)
            return;

        var requested = VerificationToggle.IsOn;
        VerificationToggle.IsEnabled = false;
        VerificationStatus.IsOpen = false;
        try
        {
            var message = requested ? "Verify it's you to require verification before shredding." : "Verify it's you to stop requiring verification before shredding.";
            if (await AccountVerification.VerifyAsync(_window, message))
                AppSettings.RequireVerification = requested;
        }
        catch (COMException ex)
        {
            VerificationStatus.Message = ex.Message;
            VerificationStatus.IsOpen = true;
        }

        _updating = true;
        VerificationToggle.IsOn = AppSettings.RequireVerification;
        VerificationToggle.IsEnabled = true;
        _updating = false;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating)
            return;

        AppSettings.Theme = (ElementTheme)ThemeBox.SelectedIndex;
        ThemeChanged?.Invoke(this, AppSettings.Theme);
    }

    private void OnWelcomeToggled(object sender, RoutedEventArgs e)
    {
        if (!_updating)
            AppSettings.ShowWelcome = WelcomeToggle.IsOn;
    }

    private async void OnContextMenuToggled(object sender, RoutedEventArgs e)
    {
        if (_updating)
            return;

        ContextMenuToggle.IsEnabled = false;
        ContextMenuStatus.IsOpen = false;

        var argument = ContextMenuToggle.IsOn ? ShellIntegration.RegisterArgument : ShellIntegration.UnregisterArgument;
        var exitCode = await ShellIntegration.RunElevatedAsync(argument);
        if (exitCode is not (0 or ShellIntegration.ErrorCancelled))
        {
            ContextMenuStatus.Message = $"Windows reported error 0x{exitCode:X8}.";
            ContextMenuStatus.IsOpen = true;
        }

        RefreshContextMenuState();
    }
}
