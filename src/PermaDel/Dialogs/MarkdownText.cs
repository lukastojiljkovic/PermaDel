using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace PermaDel.Dialogs;

/// <summary>
/// Renders the small markdown subset a GitHub release body uses: headings,
/// bullet lists, paragraphs, bold, inline code and links. Everything else stays text; no
/// markdown package is involved.
/// </summary>
internal static class MarkdownText
{
    private static readonly Regex Inline = new(
        @"`(?<code>[^`]+)`|\[(?<label>[^\]]+)\]\((?<url>https?://[^)\s]+)\)|(?<bare>https?://\S+)|(?<bold>\*\*(?<boldText>[^*]+)\*\*)",
        RegexOptions.Compiled);

    public static RichTextBlock Build(string markdown)
    {
        var block = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
                continue;

            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            if (line.StartsWith("### ", StringComparison.Ordinal))
                paragraph.Inlines.Add(Heading(line[4..], 14));
            else if (line.StartsWith("## ", StringComparison.Ordinal))
                paragraph.Inlines.Add(Heading(line[3..], 16));
            else if (line.StartsWith("# ", StringComparison.Ordinal))
                paragraph.Inlines.Add(Heading(line[2..], 18));
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Run { Text = "\u2022  " });
                AddInline(paragraph, line[2..]);
            }
            else
            {
                AddInline(paragraph, line);
            }
            block.Blocks.Add(paragraph);
        }

        if (block.Blocks.Count == 0)
            block.Blocks.Add(new Paragraph { Inlines = { new Run { Text = "No release notes were published for this version." } } });
        return block;
    }

    private static Run Heading(string text, double size) =>
        new() { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold };

    private static void AddInline(Paragraph paragraph, string text)
    {
        var index = 0;
        foreach (Match match in Inline.Matches(text))
        {
            if (match.Index > index)
                paragraph.Inlines.Add(new Run { Text = text[index..match.Index] });

            if (match.Groups["code"].Success)
            {
                paragraph.Inlines.Add(new Run { Text = match.Groups["code"].Value, FontFamily = new FontFamily("Consolas") });
            }
            else if (match.Groups["bold"].Success)
            {
                paragraph.Inlines.Add(new Bold { Inlines = { new Run { Text = match.Groups["boldText"].Value } } });
            }
            else
            {
                var url = match.Groups["url"].Success ? match.Groups["url"].Value : match.Groups["bare"].Value;
                var label = match.Groups["label"].Success ? match.Groups["label"].Value : url;
                var link = new Hyperlink();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    link.NavigateUri = uri;
                link.Inlines.Add(new Run { Text = label });
                paragraph.Inlines.Add(link);
            }
            index = match.Index + match.Length;
        }
        if (index < text.Length)
            paragraph.Inlines.Add(new Run { Text = text[index..] });
    }
}
