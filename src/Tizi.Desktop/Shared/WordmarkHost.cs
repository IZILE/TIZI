using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Globalization;

namespace Tizi.Desktop;

/// <summary>Align the uppercase ink without truncating the font's larger line box.</summary>
internal sealed class WordmarkHost : Decorator
{
    private static Geometry? LetterGeometry(TextBlock text, string value) => new FormattedText(value,
        CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
        text.FontSize, text.Foreground ?? Skin.LabelBrush).BuildGeometry(default);

    internal Rect[] StepBars
    {
        get
        {
            if (Child is not TextBlock text) return [];
            if (LetterGeometry(text, "T") is not { } letter) return [];
            var ink = letter.Bounds;
            if (ink.Width <= 0) return [];
            // Read the crossbar and stem from this exact font face rather than estimating stroke widths.
            double Edge(double low, double high, Func<double, bool> before)
            {
                for (var i = 0; i < 24; i++) { var mid = (low + high) / 2; if (before(mid)) low = mid; else high = mid; }
                return (low + high) / 2;
            }
            var stroke = Edge(ink.Top, ink.Center.Y, y => letter.FillContains(new(ink.Left + ink.Width * .15, y))) - ink.Top;
            var stemLeft = Edge(ink.Left, ink.Center.X, x => !letter.FillContains(new(x, ink.Center.Y)));
            var stemRight = Edge(ink.Center.X, ink.Right, x => letter.FillContains(new(x, ink.Center.Y)));
            var gap = stroke * .45;
            var bottom = LetterGeometry(text, text.Text ?? "")?.Bounds.Bottom ?? ink.Bottom;
            var y = text.Bounds.Y + bottom - stroke;
            var innerLeft = stemLeft - gap; var innerRight = stemRight + gap;
            // Match the outer ends of the T's crossbar, leaving a clear break at its stem.
            return [new(text.Bounds.X + ink.Left, y, innerLeft - ink.Left, stroke),
                new(text.Bounds.X + innerRight, y, ink.Right - innerRight, stroke)];
        }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Child is TextBlock text && LetterGeometry(text, text.Text ?? "") is { } letters)
            using (context.PushTransform(Matrix.CreateTranslation(text.Bounds.Position)))
                context.DrawGeometry(text.Foreground ?? Skin.LabelBrush, null, letters);
        foreach (var bar in StepBars) context.DrawRectangle((Child as TextBlock)?.Foreground ?? Skin.LabelBrush, null, bar);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(Child?.DesiredSize.Width ?? 0, 44);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is TextBlock text)
        {
            var ink = LetterGeometry(text, text.Text ?? "")?.Bounds ?? default;
            text.Arrange(new Rect(0, (finalSize.Height - ink.Height) / 2 - ink.Top,
                finalSize.Width, text.DesiredSize.Height));
        }
        return finalSize;
    }
}
