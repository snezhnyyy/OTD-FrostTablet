using System.Globalization;
using System.Text.Json;
using OpenTabletDriver.Desktop;

namespace TabletDriver.UI;

internal sealed class Preferences
{
    public string Language { get; set; } = Loc.SystemDefault();
    public bool Light { get; set; }
    public double Width { get; set; } = 1024;
    public double Height { get; set; } = 760;
    internal static string FilePath => Path.Combine(AppInfo.Current.AppDataDirectory, "ux-settings.json");

    public static Preferences Read()
    {
        try { return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new(); }
        catch (IOException) { return new(); }
        catch (JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppInfo.Current.AppDataDirectory);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}
