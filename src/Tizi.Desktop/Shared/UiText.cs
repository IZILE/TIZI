using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Tizi.Core;

namespace Tizi.Desktop;

internal static class UiText
{
    private static readonly Dictionary<string, string> Chinese = LoadTranslations();
    private static readonly List<WeakReference<Translation>> Labels = [];
    private static string _language = ReadLanguage();
    public static string Language
    {
        get => _language;
        set
        {
            if (value is not ("zh-CN" or "en")) throw new ArgumentException("Unsupported interface language.", nameof(value));
            _language = value;
            for (var i = Labels.Count - 1; i >= 0; i--)
                if (Labels[i].TryGetTarget(out var label)) label.Refresh();
                else Labels.RemoveAt(i);
        }
    }
    public static string Get(string text)
    {
        if (Language != "zh-CN") return text;
        var key = text.Replace("_", "");
        if (Chinese.TryGetValue(key, out var translated)) return translated;
        var spaced = System.Text.RegularExpressions.Regex.Replace(key, "([a-z])([A-Z])", "$1 $2");
        return Chinese.TryGetValue(spaced, out translated) ? translated : text;
    }

    public static BindingBase Bind(string text)
    {
        if (Labels.Count > 2048) Labels.RemoveAll(reference => !reference.TryGetTarget(out _));
        var label = new Translation(text);
        Labels.Add(new WeakReference<Translation>(label));
        return label.ToBinding();
    }

    public static void Set(AvaloniaObject owner, AvaloniaProperty property, string text) => owner.Bind(property, Bind(text));

    public static void WireChoices(Window window)
    {
        // Window initialization can precede the derived constructor's content. Content assignment is
        // still before Show and layout, so translated templates and numeric styling are ready for frame one.
        window.PropertyChanged += (_, change) =>
        {
            if (change.Property == ContentControl.ContentProperty) Prepare();
        };
        if (window.Content is not null) Prepare();
        void Prepare()
        {
            MacControls.StyleNumericFields(window);
            foreach (var box in window.GetLogicalDescendants().OfType<ComboBox>())
            {
                if (box.ItemTemplate is not null || box.Items.OfType<ComboBoxItem>().Any()) continue;
                box.ItemTemplate = new FuncDataTemplate<object>((value, _) => new TextBlock
                {
                    [!TextBlock.TextProperty] = Bind(value?.ToString() ?? ""),
                });
            }
        }
    }

    private sealed class Translation(string source) : IObservable<string>
    {
        private readonly List<IObserver<string>> _observers = [];
        public IDisposable Subscribe(IObserver<string> observer)
        {
            _observers.Add(observer);
            observer.OnNext(Get(source));
            return new Subscription(this, observer);
        }
        public void Refresh()
        {
            foreach (var observer in _observers.ToArray()) observer.OnNext(Get(source));
        }
        // The active binding keeps its subscription (and source) alive. Closed controls unsubscribe,
        // so the global weak registry never prevents a window or dialog from being collected.
        private sealed class Subscription(Translation owner, IObserver<string> observer) : IDisposable
        {
            private Translation? _owner = owner;
            public void Dispose()
            {
                _owner?._observers.Remove(observer);
                _owner = null;
            }
        }
    }
    public static string Format(string text, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Get(text), values);

    public static void SaveLanguage(string language)
    {
        Directory.CreateDirectory(UserData.DirectoryPath);
        File.WriteAllText(UserData.File("language.txt"), language);
    }

    private static string ReadLanguage()
    {
        try { return File.Exists(UserData.File("language.txt")) && File.ReadAllText(UserData.File("language.txt")).Trim() == "en" ? "en" : "zh-CN"; }
        catch (IOException) { return "zh-CN"; }
        catch (UnauthorizedAccessException) { return "zh-CN"; }
    }

    private static Dictionary<string, string> LoadTranslations()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("Tizi.Desktop.Locale.zh-CN.json");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (stream is not null && JsonSerializer.Deserialize<Dictionary<string, string>>(stream) is { } entries)
            foreach (var entry in entries) result[entry.Key] = entry.Value;
        return result;
    }
}
