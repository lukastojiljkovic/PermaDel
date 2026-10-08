using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PermaDel.Core.Metadata;
using PermaDel.Models;

namespace PermaDel.Dialogs;

/// <summary>
/// Shows what PermaDel found in the selected files, in words, and asks whether to save a cleaned copy or
/// replace the originals.
/// </summary>
internal static class MetadataPrompt
{
    private const double MaxWidth = 640;
    private const double ListMaxHeight = 300;
    private const string ModeGroup = "MetadataMode";

    /// <summary>Returns the chosen mode, or <see langword="null"/> when the user cancelled.</summary>
    public static async Task<MetadataRemovalMode?> ShowAsync(DialogService dialogs, IReadOnlyList<MetadataFile> files)
    {
        var copyOption = new RadioButton { Content = "Save a cleaned copy next to each file", IsChecked = true, GroupName = ModeGroup };
        var replaceOption = new RadioButton { Content = "Replace the original files", GroupName = ModeGroup };
        var choices = new StackPanel { Spacing = 4 };
        choices.Children.Add(copyOption);
        choices.Children.Add(replaceOption);
        choices.Children.Add(Hint("The old versions stay in the drive's free space until Windows reuses it. Wipe the drive's free space afterwards if they must not be recoverable."));

        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new ScrollViewer
        {
            Content = BuildFileList(files),
            MaxHeight = ListMaxHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        if (files.Any(file => file.Inspection.Format == MetadataFormat.OfficeOpenXml))
            content.Children.Add(Caption("Comments and tracked changes keep their authors' names. Remove them in the Office app first."));
        if (files.Any(file => file.Inspection.MayShowSideways))
        {
            content.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Informational,
                Message = "Photos may show sideways in some apps if they relied on their orientation tag.",
            });
        }
        content.Children.Add(choices);

        var dialog = new ContentDialog
        {
            Title = "Remove metadata",
            Content = content,
            PrimaryButtonText = "Remove metadata",
            IsPrimaryButtonEnabled = files.Any(file => file.Inspection.CanClean && file.Inspection.HasMetadata),
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = MaxWidth;
        if (await dialogs.ShowAsync(dialog) != ContentDialogResult.Primary)
            return null;
        return replaceOption.IsChecked == true ? MetadataRemovalMode.Replace : MetadataRemovalMode.Copy;
    }

    /// <summary>One row per file: its icon, its name and what PermaDel found in it.</summary>
    private static StackPanel BuildFileList(IReadOnlyList<MetadataFile> files)
    {
        var list = new StackPanel { Spacing = 12 };
        foreach (var file in files)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new FontIcon
            {
                Glyph = file.Entry.Glyph,
                FontSize = 16,
                Margin = new Thickness(0, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
            });

            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = file.Entry.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Caption(Describe(file.Inspection)));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            list.Children.Add(row);
        }
        return list;
    }

    private static string Describe(MetadataInspection inspection)
    {
        if (!inspection.CanClean)
            return "PermaDel can't clean this kind of file yet.";
        if (!inspection.HasMetadata)
            return "Nothing to remove";
        return string.Join(", ", inspection.Categories.Select(Describe));
    }

    private static string Describe(MetadataCategory category) => category switch
    {
        MetadataCategory.CameraDetails => "Camera details",
        MetadataCategory.Location => "Location",
        MetadataCategory.DateTaken => "Date taken",
        MetadataCategory.EditingSoftware => "Editing software",
        MetadataCategory.AuthorAndComments => "Author and comments",
        MetadataCategory.Copyright => "Copyright",
        MetadataCategory.Thumbnail => "Thumbnail",
        MetadataCategory.OtherDetails => "Other details",
        MetadataCategory.Author => "Author",
        MetadataCategory.LastSavedBy => "Last saved by",
        MetadataCategory.Company => "Company",
        MetadataCategory.Manager => "Manager",
        MetadataCategory.CustomProperties => "Custom properties",
        _ => "Other details",
    };

    private static TextBlock Hint(string text)
    {
        var block = Caption(text);
        block.TextWrapping = TextWrapping.Wrap;
        block.Margin = new Thickness(28, 0, 0, 0);
        return block;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
    };
}
