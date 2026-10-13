using Avalonia;
using Avalonia.Animation;
using Avalonia.Data;
using Avalonia.Controls;
using Avalonia.Media;

namespace Tizi.Desktop;

/// <summary>Retarget at the displayed value, including an away-and-back change within one frame.</summary>
internal sealed class Motion : AvaloniaObject
{
    public static readonly AttachedProperty<double> OpacityProperty =
        AvaloniaProperty.RegisterAttached<Motion, Animatable, double>("Opacity", double.NaN);
    public static readonly AttachedProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.RegisterAttached<Motion, Border, IBrush?>("Background");
    public static readonly AttachedProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.RegisterAttached<Motion, Border, IBrush?>("BorderBrush");

    static Motion()
    {
        OpacityProperty.Changed.AddClassHandler<Animatable>((subject, args) =>
        {
            var opacity = args.GetNewValue<double>();
            if (double.IsFinite(opacity)) Set(subject, Visual.OpacityProperty, opacity);
        });
        BackgroundProperty.Changed.AddClassHandler<Border>((subject, args) => Set(subject, Border.BackgroundProperty, args.GetNewValue<IBrush?>()));
        BorderBrushProperty.Changed.AddClassHandler<Border>((subject, args) => Set(subject, Border.BorderBrushProperty, args.GetNewValue<IBrush?>()));
    }

    public static double GetOpacity(Animatable subject) => subject.GetValue(OpacityProperty);
    public static void SetOpacity(Animatable subject, double value) => subject.SetValue(OpacityProperty, value);
    public static IBrush? GetBackground(Border subject) => subject.GetValue(BackgroundProperty);
    public static void SetBackground(Border subject, IBrush? value) => subject.SetValue(BackgroundProperty, value);
    public static IBrush? GetBorderBrush(Border subject) => subject.GetValue(BorderBrushProperty);
    public static void SetBorderBrush(Border subject, IBrush? value) => subject.SetValue(BorderBrushProperty, value);

    internal static void Set(Animatable subject, AvaloniaProperty property, object? value)
    {
        // Avalonia 12.1.3 can restart from the abandoned base target when the new target equals
        // the animated value. Hold that displayed value while cancelling only this property's motion.
        if (Same(subject.GetValue(property), value) && subject.IsAnimating(property)
            && subject.Transitions is { } transitions)
        {
            if (transitions.OfType<TransitionBase>().Any(item => item.Property == property))
            {
                // A style may share its collection with other controls. Replace only this subject's
                // collection, retaining the same transition instances for its other properties.
                var remaining = new Transitions();
                foreach (var transition in transitions.Where(item => item is not TransitionBase known || known.Property != property)) remaining.Add(transition);
                using var held = subject.SetValue(property, value, BindingPriority.Animation);
                try { subject.Transitions = remaining; subject.SetValue(property, value); }
                finally { subject.Transitions = transitions; }
                return;
            }
        }
        subject.SetValue(property, value);
    }

    private static bool Same(object? first, object? second) => Equals(first, second)
        || (first is ISolidColorBrush a && second is ISolidColorBrush b && a.Color == b.Color && a.Opacity == b.Opacity);
}
