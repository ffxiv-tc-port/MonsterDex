using System.Numerics;

namespace DeepDungeonDex.Windows;

internal class Debug : Window, IDisposable
{
    private StorageHandler _storage;
    private Debug _instance;
    private Requests _requests;

    public Debug(StorageHandler storage, CommandHandler handler, Requests requests) : base("MonsterDex Debug Window", ImGuiWindowFlags.NoCollapse)
    {
        _storage = storage;
        _instance = this;
        _requests = requests;
        handler.AddCommand("debug_window", () => _instance.IsOpen = true, "Shows all the data loaded into the plugin.");
    }

    public void Dispose()
    {
        _instance = null!;
        _storage = null!;
        _requests = null!;
    }

    public override void Draw()
    {
        using var _ = Font.Font.RegularFont.Push();

        var config = _storage.GetInstance<Configuration>()!;
        if (!config.LoadAll)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
            ImGui.TextUnformatted("未啟用全字型載入，部分字符將無法顯示。");
            ImGui.PopStyleColor();
        }

        if (_requests.IsRequesting)
        {
            ImGui.TextUnformatted("正在從 Github 請求資料……");
        }

        ImGui.TextUnformatted("語言清單：");
        ImGui.BeginTable("##LanguageKeyList", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        ImGui.TableSetupColumn("名稱");
        ImGui.TableSetupColumn("數值");
        ImGui.TableHeadersRow();
        foreach (var (key, value) in _storage.GetInstances<LocaleKeys>().SelectMany(t => t.LocaleDictionary))
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(key);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value);
        }
        ImGui.EndTable();

        ImGui.TextUnformatted("資料：");
        ImGui.BeginTable("##StorageList", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        ImGui.TableSetupColumn("名稱");
        ImGui.TableSetupColumn("類型");
        ImGui.TableSetupColumn("數值");
        ImGui.TableHeadersRow();
        foreach (var (key, value) in _storage.Storage.OrderBy(t => t.Value.GetType().Name.GetHashCode()))
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(key);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(value.GetType().Name);
            ImGui.TableNextColumn();
            switch (value)
            {
                case FloorData floor:
                    if (floor.FloorDictionary != null)
                        foreach (var (floorKey, floorValue) in floor.FloorDictionary)
                        {
                            ImGui.TextUnformatted($"{floorKey} 對應至 {floorValue}");
                        }
                    break;
                case MobData mob:
                    foreach (var (mobKey, mobValue) in mob.MobDictionary)
                    {
                        ImGui.TextUnformatted($"{mobKey}");
                        ImGui.Indent();
                        ImGui.TextUnformatted($"名稱: {mobValue.Name}");
                        ImGui.SameLine(0, 14);
                        ImGui.TextUnformatted($"仇恨: {mobValue.Aggro}");
                        ImGui.SameLine(0, 14);
                        ImGui.TextUnformatted($"威脅: {mobValue.Threat}");
                        ImGui.SameLine(0, 14);
                        ImGui.TextUnformatted($"弱點: {mobValue.Weakness}");
                        ImGui.SameLine(0, 14);
                        ImGui.TextUnformatted($"內容類型: {mobValue.InstanceContentType}");
                        ImGui.Unindent();
                    }
                    break;
                case Locale locale:
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
                    ImGui.TextUnformatted("翻譯內容僅顯示前 30 個字元！");
                    ImGui.PopStyleColor();
                    foreach (var (translationKey, translationValue) in locale.TranslationDictionary)
                    {
                        ImGui.TextUnformatted($"{translationKey}: {translationValue}");
                    }

                    break;
                case Territories territories:
                    foreach (var (name, ids) in territories.TerritoryDictionary)
                    {
                        ImGui.TextUnformatted($"{name}");
                        ImGui.Indent();
                        foreach (var id in ids)
                        {
                            ImGui.TextUnformatted($"{id[0]} - {id[1]}");
                            ImGui.SameLine(0, 14);
                        }
                        ImGui.Unindent();
                        ImGui.NewLine();
                    }

                    break;
            }
        }
        ImGui.EndTable();
    }
}