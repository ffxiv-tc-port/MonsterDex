using System.IO;
using Dalamud.Utility;

namespace DeepDungeonDex.Models;

public class Configuration
{
    private const byte Version = 4;
    public bool ClickThrough { get; set; }
    public bool HideRed { get; set; }
    public bool HideJob { get; set; }
    public bool HideFloor { get; set; }
    public bool HideSpawns { get; set; }
    public bool Debug { get; set; }
    public bool LoadAll { get; set; }
    public bool ShowCorrectionButton { get; set; }
    public int Locale { get; set; } = 0;
    public int FontSize { get; set; } = 16;
    public float Opacity { get; set; } = 1f;
    public ContentType EnabledContentTypes { get; set; } = ContentType.DeepDungeon;

    [JsonIgnore] public int PrevLocale;
    [JsonIgnore] public float RemoveScaling => 1 / ImGui.GetIO().FontGlobalScale;
    [JsonIgnore] public float WindowSizeScaled => Math.Max(FontSize / 16f, 1f) * RemoveScaling;
    [JsonIgnore] public Action<Configuration>? OnChange { get; set; }

    // 寫入走 WriteAllBytesSafe（暫存檔＋落盤＋換名；殘留的 .tmp 會被覆寫，不會從此存不進去）。
    // 失敗會擲例外、原檔維持上一次的內容；呼叫端負責接住（呼叫點在 Draw 內）。
    public void Save(string path)
    {
        if (PrevLocale != Locale && !LoadAll)
        {
            PrevLocale = Locale;
        }
        OnChange?.Invoke(this);
        var stream = new MemoryStream();
        BinaryWriter writer = new(stream);
        writer.Write(Version);
        byte flags = 0;
        flags |= (byte)(ClickThrough ? 1 << 1 : 0);
        flags |= (byte)(HideFloor ? 1 << 2 : 0);
        flags |= (byte)(HideSpawns ? 1 << 3 : 0);
        flags |= (byte)(Debug ? 1 << 4 : 0);
        flags |= (byte)(LoadAll ? 1 << 5 : 0);
        flags |= (byte)(ShowCorrectionButton ? 1 << 6 : 0);
        writer.Write(flags);
        writer.Write((uint)EnabledContentTypes);
        writer.Write(Locale);
        writer.Write(FontSize);
        writer.Write(Opacity);
        writer.Close();
        FilesystemUtil.WriteAllBytesSafe(path, stream.ToArray());
    }
}