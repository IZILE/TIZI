using Avalonia.Controls;

namespace Tizi.Desktop;

// Content stays fully opaque above MovingRows' shared moving selection surface.
internal sealed class ProviderCardButton : Button
{
    internal ProviderCardButton() => Classes.Add("provider-card");
}
