using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PermaDel.Core;
using PermaDel.Models;

namespace PermaDel.Dialogs;

/// <summary>The choices a free-space wipe needs from the user.</summary>
internal sealed record WipeOptions(bool CleanFileTable);

/// <summary>Explains what wiping free space does and asks whether to start it.</summary>
internal static class WipePrompt
{
    private const double MaxWidth = 640;

    /// <summary>Shows the dialog and returns the chosen options, or null when the user cancelled.</summary>
    public static async Task<WipeOptions?> ShowAsync(DialogService dialogs, FileEntry drive, WipeDriveInfo info, bool isWindowsDrive)
    {
        var cleanFileTable = new CheckBox
        {
            IsChecked = true,
            Content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = "Also clean the file table" },
                    Hint("Small files can live inside the file table itself. This fills its free entries too."),
                },
            },
        };

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Body("Files you deleted the normal way can often be recovered, because Windows only marks their space as free. PermaDel writes random data over all the free space on this drive and then removes it. Your files are not touched."),
                Body($"{FileEntry.FormatBytes(info.FreeBytes)} of free space will be written over."),
            },
        };
        if (FreeSpaceWiper.CanCleanFileTable(info.FileSystem))
            content.Children.Add(cleanFileTable);
        content.Children.Add(Body("While it runs, the drive is full for a moment, so Windows or other apps may warn about low disk space."));
        if (isWindowsDrive)
            content.Children.Add(Notice("This is the Windows drive. Windows and your apps may slow down until the wipe ends."));
        if (info.IncursSeekPenalty == false)
            content.Children.Add(Notice("This drive is an SSD or flash drive. These move data around internally, so some deleted data may survive. For SSDs, BitLocker from the start is the reliable protection."));

        var dialog = new ContentDialog
        {
            Title = $"Wipe free space on {drive.Name}",
            Content = content,
            PrimaryButtonText = "Wipe free space",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = MaxWidth;
        return await dialogs.ShowAsync(dialog) == ContentDialogResult.Primary ? new WipeOptions(cleanFileTable.IsChecked == true) : null;
    }

    private static TextBlock Body(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    private static InfoBar Notice(string message) => new()
    {
        Severity = InfoBarSeverity.Warning,
        IsOpen = true,
        IsClosable = false,
        Message = message,
    };
}
