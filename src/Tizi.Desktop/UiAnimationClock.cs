using Avalonia.Animation;
using System.Reflection;

namespace Tizi.Desktop;

// Test clock against the pinned Avalonia version; normal UI uses its compositor clock.
internal sealed class UiAnimationClock : IDisposable
{
    private static readonly Type ClockType = typeof(Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", true)!;
    private static readonly PropertyInfo ClockProperty = typeof(Animatable).GetProperty("Clock", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo PulseMethod = ClockType.GetMethod("Pulse", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly object _clock = Activator.CreateInstance(ClockType, nonPublic: true)!;
    private readonly (Animatable Subject, object? Clock)[] _previous;
    internal UiAnimationClock(params Animatable[] subjects)
    {
        _previous = subjects.Select(subject => (subject, ClockProperty.GetValue(subject))).ToArray();
        foreach (var (subject, _) in _previous)
        { var transitions = subject.Transitions; subject.Transitions = null; ClockProperty.SetValue(subject, _clock); subject.Transitions = transitions; }
        Pulse(0);
    }
    internal void Pulse(int milliseconds) => PulseMethod.Invoke(_clock, [TimeSpan.FromMilliseconds(milliseconds)]);
    public void Dispose() { foreach (var (subject, clock) in _previous) ClockProperty.SetValue(subject, clock); }
}
