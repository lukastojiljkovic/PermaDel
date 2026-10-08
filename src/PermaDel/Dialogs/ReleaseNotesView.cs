using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PermaDel.Core.Updates;

namespace PermaDel.Dialogs;

/// <summary>
/// Renders parsed release notes as a readable list: one block per section, one
/// hanging-indented row per item, with bold, links and inline code kept from the
/// item's markdown.
/// </summary>
internal static class ReleaseNotesView
{
    private static readonly Regex Inline = new(
        @"`(?<code>[^`]+)`|\[(?<label>[^\]]+)\]\((?<url>[^)\s]+)\)|(?<bare>https://\S+)|(?<bold>\*\*(?<boldText>[^*]+)\*\*)",
        RegexOptions.Compiled);

    /// <summary>Builds the sections; empty notes become a single placeholder line.</summary>
    public static StackPanel Build(ReleaseNotes notes)
    {
        var panel = new StackPanel { Spacing = 20 };
        if (notes.IsEmpty)
        {
            panel.Children.Add(new TextBlock { Text = "This release has no notes.", TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        foreach (var section in notes.Sections)
            panel.Children.Add(BuildSection(section));
        return panel;
    }

    private static StackPanel BuildSection(ReleaseNotesSection section)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (section.Title.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = section.Title,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            });
        }

        foreach (var item in section.Items)
            panel.Children.Add(BuildItem(item));
        return panel;
    }

    private static Grid BuildItem(string markdown)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = "\u2022",
            Margin = new Thickness(2, 0, 10, 0),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        AddInlines(text, markdown);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private static void AddInlines(TextBlock block, string markdown)
    {
        var index = 0;
        foreach (Match match in Inline.Matches(markdown))
        {
            if (match.Index > index)
                block.Inlines.Add(new Run { Text = markdown[index..match.Index] });

            if (match.Groups["code"].Success)
                block.Inlines.Add(new Run { Text = match.Groups["code"].Value });
            else if (match.Groups["bold"].Success)
                block.Inlines.Add(new Bold { Inlines = { new Run { Text = match.Groups["boldText"].Value } } });
            else if (TryCreateLink(match, out var link))
                block.Inlines.Add(link);
            else
                block.Inlines.Add(new Run { Text = match.Value });

            index = match.Index + match.Length;
        }
        if (index < markdown.Length)
            block.Inlines.Add(new Run { Text = markdown[index..] });
    }

    /// <summary>Only <c>https</c> links become hyperlinks; anything else stays text.</summary>
    private static bool TryCreateLink(Match match, out Hyperlink link)
    {
        link = null!;
        if (!match.Groups["url"].Success && !match.Groups["bare"].Success)
            return false;

        var url = match.Groups["url"].Success ? match.Groups["url"].Value : match.Groups["bare"].Value;
        var label = match.Groups["label"].Success ? match.Groups["label"].Value : url;
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        link = new Hyperlink { NavigateUri = uri };
        link.Inlines.Add(new Run { Text = label });
        return true;
    }
}
