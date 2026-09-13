using Microsoft.UI.Xaml.Controls;

namespace PermaDel;

/// <summary>Explains what PermaDel does when it starts, until the user opts out.</summary>
public sealed partial class WelcomeDialog : ContentDialog
{
    public WelcomeDialog() => InitializeComponent();

    public bool DontShowAgain => DontShowAgainBox.IsChecked == true;
}
