using System;
using System.IO;
using System.Text.Json;

namespace CodexGauge;

public sealed class Preferences
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Size { get; set; } = 200;
    public int SizeVersion { get; set; }
    public bool Topmost { get; set; } = true;
    public string? SelectedKey { get; set; }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexGauge", "settings.json");
    public static Preferences Load()
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath));
            if (loaded == null) return new() { SizeVersion = 1 };
            if (loaded.SizeVersion == 0) { loaded.Size = Math.Round(loaded.Size / 1.5); loaded.SizeVersion = 1; }
            return loaded;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new() { SizeVersion = 1 }; }
    }
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this));
            File.Move(FilePath + ".tmp", FilePath, true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
