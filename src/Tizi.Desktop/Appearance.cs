using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using Tizi.Core;

namespace Tizi.Desktop;

internal sealed record AppearancePreferences(string Theme="Dark");

/// <summary>Small interface preferences are loaded before creating the first window.</summary>
internal static class Appearance
{
    internal static AppearancePreferences Current { get; private set; }=new();
    internal static event Action? Changed;
    internal static void Initialize(Application app)
    {
        Current=Read();app.ActualThemeVariantChanged+=(_,_)=>ApplyPalette(app);
        Apply(app);
    }
    private static AppearancePreferences Normalize(AppearancePreferences value)=>value with {Theme=value.Theme is "Light" or "System"?value.Theme:"Dark"};
    internal static AppearancePreferences Read()
    {
        try{return Normalize(JsonSerializer.Deserialize<AppearancePreferences>(File.ReadAllText(UserData.File("appearance.json")))??new());}
        catch(Exception failure) when(failure is IOException or UnauthorizedAccessException or JsonException){return new();}
    }
    internal static void Set(string theme,bool persist)
    {
        var next=Normalize(new(theme));if(next==Current&&!persist)return;
        if(persist)
        {
            Directory.CreateDirectory(UserData.DirectoryPath);var target=UserData.File("appearance.json");var temporary=target+".tmp";
            File.WriteAllText(temporary,JsonSerializer.Serialize(next));File.Move(temporary,target,true);
        }
        if(next==Current)return;Current=next;if(Application.Current is {} app)Apply(app);
    }
    private static void Apply(Application app)
    {
        app.RequestedThemeVariant=Current.Theme switch {"Light"=>ThemeVariant.Light,"System"=>ThemeVariant.Default,_=>ThemeVariant.Dark};
        ApplyPalette(app);
    }
    private static void ApplyPalette(Application app)
    {Skin.ApplyAppearance(app,app.ActualThemeVariant);Changed?.Invoke();}
}
