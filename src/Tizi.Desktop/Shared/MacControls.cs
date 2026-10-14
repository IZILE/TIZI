using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using System.Globalization;

namespace Tizi.Desktop;

/// <summary>The Mac's regular controls and tool-header typography, without Fluent's large touch targets.</summary>
internal static class MacControls
{
    internal static void Apply(Application app)
    {
        app.Styles.Add(new Style(x => x.OfType<Window>())
        {
            Setters = { new Setter(TemplatedControl.FontSizeProperty, 14.0), new Setter(TemplatedControl.FontFamilyProperty, UiFonts.Interface) },
        });
        app.Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                new Setter(Layoutable.MinHeightProperty, 36.0),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(16, 6)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(18)),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        });
        app.Styles.Add(new Style(x => x.OfType<Button>().Class("plain"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
            },
        });
        app.Styles.Add(new Style(x => x.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                new Setter(Layoutable.MinHeightProperty, 36.0),
                // Both choice and numeric arrows are centered 18.5 DIP from the field's right edge.
                new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 6, 14.5, 6)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(18)),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Left),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        });
        app.Styles.Add(new Style(x => x.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                new Setter(Layoutable.MinHeightProperty, 36.0),
                // TextPresenter reserves one DIP for its caret; keep the text origin at 12 DIP.
                new Setter(TemplatedControl.PaddingProperty, new Thickness(13, 7, 12, 7)),
                new Setter(Layoutable.MinWidthProperty, 0.0),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(9)),
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(TextBox.TextAlignmentProperty, TextAlignment.Left),
            },
        });
        // A multiline editor starts at the first line, including empty imports.
        app.Styles.Add(new Style(x => x.OfType<TextBox>().PropertyEquals(TextBox.AcceptsReturnProperty, true))
        {
            Setters = { new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Top), new Setter(TextBox.TextAlignmentProperty, TextAlignment.Left) },
        });
        app.Styles.Add(new Style(x => x.OfType<CheckBox>())
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, 14.0),
                new Setter(Layoutable.MinHeightProperty, 22.0),
            },
        });
        // Resource names come from the pinned Avalonia 12.1.3 slider template.
        app.Resources["SliderHorizontalHeight"] = 22.0;
        app.Resources["SliderHorizontalThumbWidth"] = 14.0;
        app.Resources["SliderHorizontalThumbHeight"] = 14.0;
        app.Resources["SliderThumbCornerRadius"] = new CornerRadius(6);
        app.Resources["SliderThumbBackground"] = new SolidColorBrush(Color.FromRgb(230, 230, 230));
        app.Resources["SliderThumbBackgroundPointerOver"] = Brushes.White;
        app.Styles.Add(new StyleInclude(new Uri("avares://TIZI/"))
        {
            Source = new Uri("avares://TIZI/Shared/MacControls.axaml"),
        });
    }

    internal static Grid Row(string label, Control field, double labelWidth = 130)
    {
        field.VerticalAlignment = VerticalAlignment.Center;
        if (field is ComboBox)
        {
            field.Width = double.NaN;
            field.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else field.HorizontalAlignment = double.IsNaN(field.Width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},8,*"), Tag = "labeled-row" };
        var text = new TextBlock
        {
            [!TextBlock.TextProperty] = UiText.Bind(label), TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Avalonia.Automation.AutomationProperties.SetLabeledBy(field, text);
        Grid.SetColumn(field, 2);
        row.Children.Add(text);
        row.Children.Add(field);
        return row;
    }

    internal static Grid AmountRow(string label, Slider slider, Control readout, double labelWidth = 130)
    {
        slider.Width = double.NaN;
        slider.VerticalAlignment = VerticalAlignment.Center;
        readout.VerticalAlignment = VerticalAlignment.Center;
        if (readout is TextBlock text) text.TextAlignment = TextAlignment.Center;
        var amounts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,44") };
        Grid.SetColumn(readout, 2);
        amounts.Children.Add(slider);
        amounts.Children.Add(readout);
        return Row(label, amounts, labelWidth);
    }

    internal static void StyleNumericFields(Control window)
    {
        foreach (var field in window.GetLogicalDescendants().Concat(window.GetVisualDescendants()).OfType<TextBox>().Distinct())
        {
            if (field.AcceptsReturn || field.Classes.Contains("text-input")) continue;
            if (!field.Classes.Contains("numeric")) continue;
            field.TextAlignment = TextAlignment.Left;
            field.VerticalContentAlignment = VerticalAlignment.Center;
        }
    }

    internal static Grid Actions(CheckBox preview, params Button[] actions)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 14, 0, 0) };
        preview.VerticalAlignment = VerticalAlignment.Center;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in actions) buttons.Children.Add(button);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(preview);
        row.Children.Add(buttons);
        return row;
    }
}
