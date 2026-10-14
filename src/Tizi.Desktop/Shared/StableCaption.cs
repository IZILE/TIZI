using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Tizi.Desktop;

/// <summary>Reserve the natural width of alternate captions using the current font and language.</summary>
internal sealed class StableCaption : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);
    private string[] _variants = [];
    internal string[] Variants
    {
        get => _variants;
        set { if (_variants.SequenceEqual(value)) return; _variants = value; InvalidateMeasure(); }
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        var width = CaptionWidth.Maximum(_variants.Select(UiText.Get), FontFamily, FontStyle, FontWeight, FontStretch, FontSize);
        return new Size(Math.Max(measured.Width, width), measured.Height);
    }
}

internal sealed class StableCaptionButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    internal Func<IEnumerable<string>>? AlternateCaptions { get; init; }
    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        var width = CaptionWidth.Maximum(AlternateCaptions?.Invoke() ?? [], FontFamily, FontStyle, FontWeight, FontStretch, FontSize);
        return new Size(Math.Max(measured.Width, width + Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right), measured.Height);
    }
}

internal sealed class StableChoice : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);
    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        var captions = Items.Select(value => UiText.Get(value?.ToString() ?? ""));
        var width = CaptionWidth.Maximum(captions, FontFamily, FontStyle, FontWeight, FontStretch, FontSize);
        return new Size(Math.Max(measured.Width, width + Padding.Left + Padding.Right + 18
            + BorderThickness.Left + BorderThickness.Right), measured.Height);
    }
}

internal static class CaptionWidth
{
    internal static double Maximum(IEnumerable<string> captions, FontFamily family, FontStyle style,
        FontWeight weight, FontStretch stretch, double size) => captions.Select(text =>
            Math.Ceiling(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(family, style, weight, stretch), size, Brushes.White).WidthIncludingTrailingWhitespace))
        .DefaultIfEmpty(0).Max();
}
