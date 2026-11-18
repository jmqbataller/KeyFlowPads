using System.Text.Json;
using KeyFlowPads.Audio;

namespace KeyFlowPads.App;

internal sealed class PlayerPreferences
{
    public static readonly string[] Profiles =
        ["Original", "Soprano", "Mezzo-Soprano", "Contralto (Alto)", "Tenor", "Baritone", "Bass"];
    internal static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyFlow Pads");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

    public bool AlwaysOnTop { get; set; } = true;
    public bool Compact { get; set; }
    public bool RememberLocation { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool RestoreKey { get; set; } = true;
    public bool RestoreVolume { get; set; } = true;
    public bool RestoreProfile { get; set; } = true;
    public bool BlockYouTubeAds { get; set; } = true;
    public Point? Location { get; set; }
    public string DeviceId { get; set; } = "";
    public int Volume { get; set; } = 65;
    public double Crossfade { get; set; } = 2;
    public double TransportFade { get; set; } = 0.2;
    public int SelectedKey { get; set; }
    public int Profile { get; set; }
    public int[] Offsets { get; set; } = new int[Profiles.Length];

    internal int SoundingKey => (SelectedKey + PadLibrary.Count + Offsets[Profile]) % PadLibrary.Count;

    public PlayerPreferences Copy()
    {
        var copy = (PlayerPreferences)MemberwiseClone();
        copy.Offsets = (int[])Offsets.Clone();
        return copy;
    }

    internal static PlayerPreferences Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(FilePath))
                return new();
            var prefs = JsonSerializer.Deserialize<PlayerPreferences>(File.ReadAllText(FilePath)) ?? new();
            prefs.Normalize();
            return prefs;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warning = "Saved settings could not be read. Defaults are in use.";
            return new();
        }
    }

    private void Normalize()
    {
        DeviceId ??= "";
        Volume = RestoreVolume ? Math.Clamp(Volume, 0, 100) : 65;
        SelectedKey = RestoreKey ? Math.Clamp(SelectedKey, 0, PadLibrary.Count - 1) : 0;
        Profile = RestoreProfile ? Math.Clamp(Profile, 0, Profiles.Length - 1) : 0;
        Crossfade = Math.Clamp(Crossfade, 0.5, 5);
        TransportFade = Math.Clamp(TransportFade, 0.05, 1);
        var stored = Offsets;
        Offsets = new int[Profiles.Length];
        if (stored is not null)
        {
            for (int index = 1; index < Math.Min(Profiles.Length, stored.Length); index++)
                Offsets[index] = Math.Clamp(stored[index], -6, 6);
        }
    }

    internal void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
