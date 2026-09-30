using System.Text.Json;

namespace Defuse.Desktop;

sealed class PlaybackLook
{
    public bool SubtitlesOn { get; set; }
    public string Language { get; set; } = "";
    public int FontSize { get; set; } = 100;
    public int Color { get; set; } = 0xFFFFFF;
    public int TextOpacity { get; set; } = 255;
    public int BackgroundOpacity { get; set; }
    public int Outline { get; set; } = 2;
    public int Volume { get; set; } = 100;

    public static PlaybackLook Current { get; private set; } = Load();

    public static PlaybackLook Load()
    {
        try
        {
            var path = Path.Combine(AppPaths.Root, "playback.json");
            if (!File.Exists(path))
                return new PlaybackLook();
            var look = JsonSerializer.Deserialize<PlaybackLook>(File.ReadAllText(path)) ?? new PlaybackLook();
            if (look.FontSize is 20 or 28 or 42)
                look.FontSize = 100;
            look.BackgroundOpacity = 0;
            look.Outline = 0;
            return look;
        }
        catch (Exception)
        {
            return new PlaybackLook();
        }
    }

    public void Save()
    {
        Write();
        App.Engine.SetSubtitleStyle(FontSize, Color, TextOpacity, BackgroundOpacity, Outline);
    }

    public void RememberVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        Write();
    }

    private void Write()
    {
        Current = this;
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(Path.Combine(AppPaths.Root, "playback.json"), JsonSerializer.Serialize(this));
    }
}
