using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PermaDel.Core.Updates;

namespace PermaDel.Dialogs;

/// <summary>The update dialogs PermaDel shows: what's new, and what to do when an update stops.</summary>
internal static class UpdatePrompts
{
    private const double MaxWidth = 640;
    private const double NotesMaxHeight = 420;

    /// <summary>Shows a release's notes and returns <see langword="true"/> when the user chose to update.</summary>
    public static async Task<bool> ShowReleaseNotesAsync(DialogService dialogs, ReleaseInfo release, bool canUpdate)
    {
        var dialog = new ContentDialog
        {
            Title = $"What's new in PermaDel {release.Version.ToString(3)}",
            Content = BuildContent(ReleaseNotes.FromReleaseBody(release.Body), release.PublishedAt, release.PageUrl),
            PrimaryButtonText = "Update now",
            IsPrimaryButtonEnabled = canUpdate,
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = MaxWidth;
        return await dialogs.ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>Shows what changed in the version the app was just updated to.</summary>
    public static async Task ShowInstalledNotesAsync(DialogService dialogs, Version version, ReleaseNotes notes)
    {
        var url = $"https://github.com/lukastojiljkovic/PermaDel/releases/tag/v{version.ToString(3)}";
        var dialog = new ContentDialog
        {
            Title = $"PermaDel was updated to {version.ToString(3)}",
            Content = BuildContent(notes, publishedAt: null, url),
            CloseButtonText = "Got it",
        };
        dialog.Resources["ContentDialogMaxWidth"] = MaxWidth;
        await dialogs.ShowAsync(dialog);
    }

    /// <summary>Tells the user why the update stopped and offers the release page.</summary>
    public static async Task ShowUpdateFailureAsync(DialogService dialogs, string message, string? releasePageUrl)
    {
        var dialog = new ContentDialog
        {
            Title = "The update could not be installed",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Close",
            SecondaryButtonText = releasePageUrl is null ? string.Empty : "Open the release page",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialogs.ShowAsync(dialog) == ContentDialogResult.Secondary && releasePageUrl is not null)
            await DialogService.OpenAsync(releasePageUrl);
    }

    private static ScrollViewer BuildContent(ReleaseNotes notes, DateTimeOffset? publishedAt, string releaseUrl)
    {
        var stack = new StackPanel();
        if (publishedAt is { } published)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"Released on {published.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}",
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(0, 0, 0, 16),
            });
        }

        stack.Children.Add(ReleaseNotesView.Build(notes));

        var link = new HyperlinkButton
        {
            Content = "See the full release notes on GitHub",
            Padding = new Thickness(0),
            Margin = new Thickness(0, 20, 0, 0),
        };
        link.Click += async (_, _) => await DialogService.OpenAsync(releaseUrl);
        stack.Children.Add(link);

        return new ScrollViewer
        {
            Content = stack,
            MaxHeight = NotesMaxHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }
}
