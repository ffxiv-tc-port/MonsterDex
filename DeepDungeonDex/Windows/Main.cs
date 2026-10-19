using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Utility;
using DeepDungeonDex.Hooks;
using DeepDungeonDex.Weather;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using System;
using System.Numerics;
using BattleChara = FFXIVClientStructs.FFXIV.Client.Game.Character.BattleChara;
using STask = System.Threading.Tasks.Task;

namespace DeepDungeonDex.Windows;

public partial class Main : Window, IDisposable
{
#pragma warning disable CS8618
    private static Mob _currentMob;
#pragma warning restore CS8618
    // 🔴 不跨幀保存原生指標：GetData（Framework.Update）當下把 Draw 要用的欄位抄成受管理值，Draw 只讀快照。
    // 目標死亡／消失／切區之後原位址就不再屬於這隻怪，解參考是 AccessViolationException，任何 try/catch 都攔不到。
    private static uint _currentNpcNameId;
    private static string _currentNpcName = "";
    private static byte _currentNpcSubKind;
    private static uint? _currentNpcForayElement;
    private static bool _hasCurrentNpc;
    // 名字字串只由目標的 64 位元組名字欄決定；位元組沒變就沿用上次解出的字串（只存受管理的複本，不存指標）。
    private readonly byte[] _nameBytes = new byte[64];
    private bool _hasNameCache;
    private string _nameSeText = "";
    private string _nameCsText = "";
    private ICondition _condition;
    private ITargetManager _target;
    private IFramework _framework;
    private ITextureProvider _textureProvider;
    private IClientState _clientState;
    private IPluginLog _log;
    private StorageHandler _storage;
    private Configuration _config;
    private WeatherManager _weatherManager;
    private Locale[] _locale = Array.Empty<Locale>();
    private uint _targetId;
    private bool _debug;
    private IDalamudTextureWrap? _unknown;
    private AddonAgent _addon;
    // 台服艦隊：資料回報導到本 org 的 fork（資料也是本 org 自 host 的 data-tc 分支），
    // 不要送去上游 wolfcomp —— 那邊收不到也修不了繁中資料。
    private const string _githubIssuePath = "https://github.com/ffxiv-tc-port/MonsterDex/issues/new?template=fix_node.yaml";
    private string IssueUrl => $"{_githubIssuePath}&mob_id={_currentNpcNameId}%20-%20{_currentNpcName}&content_type={_addon.ContentType:G}&version={Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

    public Main(StorageHandler storage, CommandHandler command, ITargetManager target, IFramework framework, IClientState state, ICondition condition, ITextureProvider textureProvider, IPluginLog log, AddonAgent addon, WeatherManager weatehrManager) : base("MonsterDex MobView", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar)
    {
        _condition = condition;
        _target = target;
        _storage = storage;
        storage.StorageChanged += Storage_StorageChanged;
        _framework = framework;
        _textureProvider = textureProvider;
        _clientState = state;
        _log = log;
        _addon = addon;
        _weatherManager = weatehrManager;
        var instance = this;
        _config = _storage.GetInstance<Configuration>()!;
        SizeConstraints = new WindowSizeConstraints
        {
            MaximumSize = new Vector2(400 * _config.WindowSizeScaled, 600),
            MinimumSize = new Vector2(250 * _config.WindowSizeScaled, 100)
        };
        BgAlpha = _config.Opacity;
        _framework.RunOnTick(LoadIcons);
        framework.Update += GetData;
        command.AddCommand("debug_mob", (args) =>
        {
            if (!uint.TryParse(args.Split(' ')[0], out var id))
            {
                _debug = false;
                IsOpen = false;
                return;
            }

            _debug = true;
            instance.SetTarget(id);
            instance.IsOpen = true;
        }, show: false);
        _config.OnChange += ConfigChanged;
    }

    private void Storage_StorageChanged(StorageEventArgs e)
    {
        if (e.StorageType == typeof(Locale))
        {
            _locale = _storage.GetInstances<Locale>();
        }
    }

    public void ConfigChanged(Configuration config)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MaximumSize = new Vector2(400 * config.WindowSizeScaled, 600),
            MinimumSize = new Vector2(250 * config.WindowSizeScaled, 100)
        };
        BgAlpha = config.Opacity;
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (config.ClickThrough && !Flags.HasFlag(ImGuiWindowFlags.NoInputs))
            Flags |= ImGuiWindowFlags.NoInputs;
        else if (!config.ClickThrough && Flags.HasFlag(ImGuiWindowFlags.NoInputs))
            Flags &= ~ImGuiWindowFlags.NoInputs;
    }

    public void Dispose()
    {
        _config.OnChange -= ConfigChanged;
        _framework.Update -= GetData;
        _storage.GetInstance<Configuration>()!.OnChange -= ConfigChanged;
        _unknown?.Dispose();
        _locale = null!;
        _currentMob = null!;
        // 這幾個是 static，外掛重載時會殘留 ⇒ 一併清掉，避免下一輪拿到上一輪的怪名。
        _hasCurrentNpc = false;
        _currentNpcName = "";
        _currentNpcNameId = 0;
        _currentNpcForayElement = null;
        _textureProvider = null!;
        _clientState = null!;
        _condition = null!;
        _target = null!;
        _storage = null!;
        _framework = null!;
    }

    private void SetTarget(uint id)
    {
        _targetId = id;
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (_currentMob is not null && _currentMob.Id == id)
        {
            return;
        }

        var data = _storage.GetInstances<MobData>().GetData(_targetId);
        if (data == null)
        {
            // 設定可讓深宮以外的內容類型（例如外景）也開窗，那裡查無資料是常態；只有深宮裡查無才值得 Information。
            var noDataMsg = $"No data for {_targetId} setting unknowns.";
            if (_addon.ContentType.HasFlag(ContentType.DeepDungeon))
                _log.Information(noDataMsg);
            else
                _log.Debug(noDataMsg);
            data = new Mob
            {
                Aggro = Aggro.Undefined,
                Id = id,
                Threat = Threat.Undefined,
                Weakness = Weakness.BindUnknown | Weakness.HeavyUnknown | Weakness.SleepUnknown | Weakness.SlowUnknown | Weakness.StunUnknown,
                InstanceContentType = _addon.ContentType,
                IsGenerated = true
            };
            if (_clientState.TerritoryType is >= 561 and <= 565 or >= 593 and <= 607)
                data.Weakness |= Weakness.UndeadUnknown;
        }

        _log.Verbose("Set target to:\n" + data);

        _currentMob = data;
    }

    private unsafe void GetData(IFramework framework)
    {
        if (_debug)
        {
            return;
        }

        if (_condition[ConditionFlag.PvPDisplayActive] || !EnabledContents())
        {
            IsOpen = false;
            return;
        }

        if (_target.Target is not IBattleNpc { BattleNpcKind: BattleNpcSubKind.Enemy } npc)
        {
            if (!_debug)
            {
                IsOpen = false;
                _currentMob = null!;
                _hasCurrentNpc = false;
            }
            return;
        }

        SetTarget(npc.NameId);

        var address = (BattleChara*)npc.Address;
        CaptureNpcSnapshot(address, npc);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (_currentMob is null)
        {
            IsOpen = false;
            return;
        }
        var name = address != null ? _nameSeText : npc.Name.ToString();
        if (_currentMob.Name != name)
            _currentMob.Name = name;
        IsOpen = HasSomethingToShow();
    }

    // 只在 GetData 內、npc 剛取得的同一幀呼叫。兩個字串與舊寫法逐字相同：Dalamud 的 Name 與 CS 的 NameString 都只讀這 64 位元組。
    private unsafe void RefreshNameCache(BattleChara* npc, IBattleNpc wrapper)
    {
        var raw = npc->Character.GameObject.Name;
        if (_hasNameCache && raw.SequenceEqual(_nameBytes))
            return;
        _nameSeText = wrapper.Name.ToString();
        _nameCsText = npc->NameString;
        raw.CopyTo(_nameBytes);
        _hasNameCache = true;
    }

    /// <summary>
    /// 在指標仍然有效的那一刻（Framework.Update 回呼內，物件剛從 ITargetManager 取得）把 Draw 會用到的
    /// 欄位抄成受管理值。之後所有繪製一律只讀這些快照，不再持有原生指標。
    /// </summary>
    private unsafe void CaptureNpcSnapshot(BattleChara* npc, IBattleNpc wrapper)
    {
        if (npc == null)
        {
            _hasCurrentNpc = false;
            return;
        }

        RefreshNameCache(npc, wrapper);
        _currentNpcNameId = npc->NameId;
        _currentNpcName = _nameCsText;
        _currentNpcSubKind = npc->Character.GameObject.SubKind;
        var forayInfo = npc->GetForayInfo();
        _currentNpcForayElement = forayInfo != null ? (uint)forayInfo->Element : null;
        _hasCurrentNpc = true;
    }

    private bool EnabledContents() => _addon.ContentType.HasAnyFlag(_config.EnabledContentTypes);

    public override bool DrawConditions()
    {
        // Block if in "The Final Verse (Quantum)"
        return _clientState.TerritoryType != 1311;
    }

    private enum MobView
    {
        Blank,
        DeepDungeon,
        Eureka,
        NoDisplay,
        UnknownContent,
    }

    // Draw 畫哪一種由這裡決定；GetData 的開窗判斷讀同一份，不另寫一套條件。
    private MobView GetMobView()
    {
        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (_currentMob.InstanceContentType)
        {
            case ContentType.DeepDungeon:
                return MobView.DeepDungeon;
            case ContentType.Eureka:
#if DEBUG
                return MobView.Eureka;
#endif
            default:
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (_addon.ContentType)
                {
                    case ContentType.OceanFishing:
                    case ContentType.BondingCeremony:
                    case ContentType.CrystallineConflict:
                    case ContentType.Trial:
                    case ContentType.Raid:
                    case ContentType.UnrealTrial:
                    case ContentType.Mahjong:
                    case ContentType.GoldSaucer:
                    case ContentType.RivalWing:
                    case ContentType.TripleTriad:
                    case ContentType.PublicTripleTriad:
                    case ContentType.LeapOfFaith:
                    case ContentType.Frontlines:
                        return MobView.Blank;
                    case ContentType.Eureka:
                    case ContentType.IslandSanctuary:
                    case ContentType.Diadem:
                    case ContentType.Bozja:
                    case ContentType.None:
                        return MobView.NoDisplay;
                    default:
                        return MobView.UnknownContent;
                }
        }
    }

    // 查無資料的怪若只會畫出「無法顯示內容」那段字，就沒有任何怪物資訊可看 ⇒ 不開窗。
    // 有資料的怪照舊開窗；debug_mob 不經過這裡。
    private bool HasSomethingToShow() => !(_currentMob.IsGenerated && GetMobView() == MobView.NoDisplay);

    public override void Draw()
    {
        using var _ = Font.Font.RegularFont.Push();
        switch (GetMobView())
        {
            case MobView.DeepDungeon:
                DrawDeepDungeonData();
                break;
            case MobView.Eureka:
                DrawEurekaData();
                break;
            case MobView.NoDisplay:
                DrawNoDisplay();
                break;
            case MobView.UnknownContent:
                DrawUnknownContent();
                break;
        }
    }

    private void DrawNoDisplay() => ImGui.TextUnformatted(string.Format(_locale.GetLocale("NoDisplay"), _addon.ContentType.ToString("G")));

    private void DrawUnknownContent()
    {
        ImGui.TextUnformatted(string.Format(_locale.GetLocale("UnknownContent"), _currentNpcName, _currentNpcNameId));
        // ReSharper disable once InvertIf
        if (ImGui.Button(_locale.GetLocale("CreateDataIssue")))
        {
            Util.OpenLink(IssueUrl);
        }

        if (_config.Debug && _hasCurrentNpc)
        {
            ImGui.TextUnformatted("Debug information");
            if (_currentNpcForayElement != null)
                ImGui.TextUnformatted($"NamePlateIconId: {_currentNpcForayElement.Value + 60650}");
            ImGui.TextUnformatted($"SubKind: {_currentNpcSubKind}");
            ImGui.TextUnformatted($"Current Time: {((Time)Framework.GetServerTime()).GetEorzeanTime()}");
            ImGui.TextUnformatted($"Weather: {_addon.Weather}");
            ImGui.TextUnformatted("WeatherIds:");
            foreach (var se in _addon.WeatherIds.Where(t => t != 0).Select(t => $"{t}: {_weatherManager.GetWeatherName(t)}").ToList())
            {
                ImGui.Text(se);
            }
        }
    }

    private async STask LoadIcons()
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeepDungeonDex.UnknownDebuf.png");
        if (stream is null)
        {
            _log.Error("Failed to load unknown debuf icon.");
            return;
        }
        _unknown = await _textureProvider.CreateFromImageAsync(stream, debugName: "DeepDungeonDex.UnknownDebuf");
    }

    private static Vector2 _uv0 = new(0, 0);
    private static Vector2 _uv1 = new(1, 1);
    private static Vector4 _color = new(1, 1, 1, 1);
    private static Vector4 _unknownColor = new(0.75f, 0.75f, 0.75f, 0.75f);
    private static Vector4 _notActiveColor = new(0.5f, 0.5f, 0.5f, 0.5f);

    private void DrawWeaknessIcon(uint iconId, Vector2 size, Weakness weakness, Weakness check)
    {
        var cursor = ImGui.GetCursorPos();
        var color = GetColor(weakness, check);
        ImGui.Image(_textureProvider.GetFromGameIcon(iconId + 200000).GetWrapOrEmpty().Handle, size, _uv0, _uv1, color);
        if (weakness.HasUnknownFlag(check))
        {
            ImGui.SetCursorPos(cursor);
            ImGui.Image(_unknown!.Handle, size, _uv0, _uv1, color);
        }
    }

    private void DrawIcon(uint iconId, Vector2 size, Vector4 color)
    {
        ImGui.Image(_textureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty().Handle, size, _uv0, _uv1, color);
    }

    private Vector4 GetColor(Weakness weakness, Weakness check)
    {
        return weakness.HasUnknownFlag(check) ? _unknownColor : weakness.HasFlag(check) ? _color : _notActiveColor;
    }

    private static void PrintTextWithColor(string? text, uint color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }
}