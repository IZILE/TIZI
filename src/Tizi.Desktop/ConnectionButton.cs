using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Tizi.Desktop;

internal enum ConnectionPhase { Disconnected, Connecting, Connected, Disconnecting, Failed, Cancelling }

/// <summary>A stable action surface whose logo assembles only after the connection succeeds.</summary>
internal sealed class ConnectionButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    private ConnectionPhase _phase;
    internal ConnectionPhase Phase => _phase;
    internal ConnectionLogo Logo { get; } = new();
    internal static string StateText(ConnectionPhase phase) => UiText.Get(phase switch
    {
        ConnectionPhase.Connected => "Connected",
        ConnectionPhase.Connecting => "Connecting…",
        ConnectionPhase.Disconnecting => "Disconnecting…",
        ConnectionPhase.Cancelling => "Cancelling connection…",
        ConnectionPhase.Failed => "Connection failed",
        _ => "Disconnected"
    });

    internal ConnectionButton()
    {
        Width = 160; Height = 44; Padding = new Thickness(12, 4); CornerRadius = new(22);
        HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Center;
        HorizontalContentAlignment = HorizontalAlignment.Center; VerticalContentAlignment = VerticalAlignment.Center;
        Classes.Add("accent"); Classes.Add("connection-action"); Opacity = 1;
        AutomationProperties.SetAccessibilityView(Logo, AccessibilityView.Raw);
        Content = Logo;
        SetState(ConnectionPhase.Disconnected, false);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var face = e.NameScope.Find<Border>("buttonFace");
        Logo.SurfaceBrush = () => face?.Background ?? Background;
    }

    internal void SetState(ConnectionPhase phase, bool busy, string? error = null)
    {
        if (_phase != phase) { _phase = phase; Logo.SetPhase(phase); }
        Classes.Set("connection-error", phase == ConnectionPhase.Failed);
        IsEnabled = phase == ConnectionPhase.Connecting || !busy && phase is not (ConnectionPhase.Disconnecting or ConnectionPhase.Cancelling);
        var action = UiText.Get(busy && phase is (ConnectionPhase.Disconnected or ConnectionPhase.Connected) ? "Working…" : phase switch
        {
            ConnectionPhase.Connected => "Click to disconnect",
            ConnectionPhase.Connecting => "Click to cancel connection",
            ConnectionPhase.Disconnecting => "Disconnecting…",
            ConnectionPhase.Cancelling => "Cancelling connection…",
            ConnectionPhase.Failed => "Click to retry",
            _ => "Click to connect"
        });
        var status = StateText(phase);
        ToolTip.SetTip(this, phase is ConnectionPhase.Disconnecting or ConnectionPhase.Cancelling ? status : status + " · " + action + (phase == ConnectionPhase.Failed && error is { Length: > 0 } ? "\n" + error : ""));
        AutomationProperties.SetName(this, status);
        AutomationProperties.SetHelpText(this, phase == ConnectionPhase.Failed && error is { Length: > 0 } ? action + ". " + error : action);
    }
}
