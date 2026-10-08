using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PermaDel.Dialogs;

/// <summary>
/// Shows a window's dialogs with the shared defaults: the same XAML root,
/// theme and content-dialog style, plus opening links in the default browser.
/// </summary>
internal sealed class DialogService(XamlRoot root, ElementTheme theme)
{
    public async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = root;
        dialog.RequestedTheme = theme;
        dialog.Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"];
        return await dialog.ShowAsync();
    }

    public static async Task OpenAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }
}
