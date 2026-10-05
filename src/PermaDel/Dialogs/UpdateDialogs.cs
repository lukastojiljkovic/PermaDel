using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PermaDel.Core.Updates;

namespace PermaDel.Dialogs;

/// <summary>The update dialogs PermaDel shows: what's new, and what to do when an update stops.</summary>
internal static class UpdateDialogs
{
    /// <summary>Renders a release's notes with a link to its page.</summary>
    public static async Task ShowReleaseNotesAsync(XamlRoot root, ElementTheme theme, ReleaseInfo release)
    {
        var scroll = new ScrollViewer
        {
            Content = MarkdownText.Build(release.Body),
            MaxHeight = 360,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = $"What's new in PermaDel {release.Version.ToString(3)}",
            Content = scroll,
            PrimaryButtonText = "Close",
            SecondaryButtonText = "Open the release page",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;
        if (await dialog.ShowAsync() == ContentDialogResult.Secondary)
            await OpenAsync(release.PageUrl);
    }

    /// <summary>Tells the user why the update stopped and offers the release page.</summary>
    public static async Task ShowUpdateFailureAsync(XamlRoot root, ElementTheme theme, string message, string? releasePageUrl)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = "The update could not be installed",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Close",
            SecondaryButtonText = releasePageUrl is null ? string.Empty : "Open the release page",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Secondary && releasePageUrl is not null)
            await OpenAsync(releasePageUrl);
    }

    public static async Task OpenAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }
}
