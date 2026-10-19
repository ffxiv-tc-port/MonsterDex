using Dalamud.Interface.ManagedFontAtlas;

namespace DeepDungeonDex.Font;

public class Font : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IFontAtlas _atlas;
    internal static IFontHandle RegularFont = null!;
    internal float FontSize = 16;

    public Font(IPluginLog log, IFontAtlas atlas)
    {
        _log = log;
        _atlas = atlas;
        // 刻意不再內嵌字型：字形全用 Dalamud 預設字型（台服是遊戲 AXIS 字型＋依 Dalamud 語言合併的系統中文字型），
        // 只保留外掛自己的字級設定。韓文不在這兩個來源裡，會畫成替代字元。
        RegularFont = _atlas.NewDelegateFontHandle(step =>
        {
            step.OnPreBuild(act =>
            {
                var font = act.AddDalamudDefaultFont(FontSize);
                act.SetFontScaleMode(font, FontScaleMode.UndoGlobalScale);
            });
        });
    }

    public void RegisterNewBuild(float size)
    {
        FontSize = size;
        _atlas.BuildFontsAsync();
    }

    public void Dispose()
    {
        RegularFont.Dispose();
    }
}