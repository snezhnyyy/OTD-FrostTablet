using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace TabletDriver.UI;

/// <summary>
/// Runtime-switchable UI strings. Every embedded Localization/*.json is a language;
/// English is the reference and the fallback for any key a translation lacks.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>Language code → name in that language, in menu order.</summary>
    public static readonly (string Code, string Name)[] Languages =
    [
        ("en", "English"),
        ("ru", "Русский"),
        ("uk", "Українська"),
        ("es", "Español"),
        ("pt", "Português"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("zh", "中文（简体）"),
        ("ja", "日本語"),
    ];

    private const string Prefix = "OpenTabletDriver.UX.Avalonia.Localization.";
    private readonly Dictionary<string, Dictionary<string, string>> tables = new();
    private readonly Dictionary<string, string> english;
    private Dictionary<string, string> current;

    public static Loc Instance { get; } = new();

    private Loc()
    {
        var assembly = typeof(Loc).Assembly;
        foreach (var (code, _) in Languages)
        {
            using var stream = assembly.GetManifestResourceStream(Prefix + code + ".json");
            if (stream != null)
                tables[code] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }
        english = tables["en"];
        current = english;
    }

    public string Language { get; private set; } = "en";

    private static readonly Dictionary<string, string> English = new()
    {
        ["en"] = "English", ["ru"] = "Russian", ["uk"] = "Ukrainian", ["es"] = "Spanish", ["pt"] = "Portuguese",
        ["de"] = "German", ["fr"] = "French", ["zh"] = "Chinese", ["ja"] = "Japanese",
    };

    public static string EnglishName(string code) => English.GetValueOrDefault(code, code);

    public static string NameOf(string code) => Languages.FirstOrDefault(l => l.Code == code).Name ?? code;

    /// <summary>The UI language matching the OS culture, English otherwise.</summary>
    public static string SystemDefault()
    {
        var code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == code) ? code : "en";
    }

    public string this[string key] => current.TryGetValue(key, out var value) ? value : english.GetValueOrDefault(key, key);

    /// <summary>Translates a known English text (built-in plugin names etc.), otherwise returns it unchanged.</summary>
    public string Known(string text) => current.TryGetValue(text, out var value) ? value : text;

    /// <summary>Keys present in English but missing in a translation (used by the consistency check).</summary>
    public IEnumerable<(string Language, string Key)> MissingKeys() =>
        tables.Where(t => t.Key != "en").SelectMany(t => english.Keys.Where(k => !t.Value.ContainsKey(k)).Select(k => (t.Key, k)));

    public void Select(string language)
    {
        Language = tables.ContainsKey(language) ? language : "en";
        current = tables[Language];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
