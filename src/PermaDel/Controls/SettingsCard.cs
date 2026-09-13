using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PermaDel.Controls;

/// <summary>A Windows 11 Settings style row: glyph, header and description, with the setting's control as content.</summary>
public sealed partial class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = Register(nameof(Header));
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description));
    public static readonly DependencyProperty GlyphProperty = Register(nameof(Glyph));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty));
}
