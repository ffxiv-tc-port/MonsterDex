using DeepDungeonDex.Hooks;
using System.Numerics;

namespace DeepDungeonDex.Windows;

public class Floor : Window, IDisposable
{
    private IClientState _clientState;
    private ICondition _condition;
    private IFramework _framework;
    private IPluginLog _log;
    private StorageHandler _storage;
    private AddonAgent _addon;
    private Locale? _locale;
    private byte _debug;
    private string _dataPath = "";
    // 設定物件整個生命週期不換（StorageHandler 只在建構時放一次）⇒ 不必每幀到儲存區重找。
    private readonly Configuration _config;
    // 由 _dataPath 衍生的字串，只在主執行緒（GetData／Draw）讀寫；_dataPath 換了才重組。
    private string? _pathsFor;
    private string _floorsPath = "";
    private string _floorKeyPrefix = "";
    private int _floorKeyFor = -1;
    private string _floorKey = "";

    public Floor(StorageHandler storage, CommandHandler command, IFramework framework, ICondition condition, IClientState state, IPluginLog log, AddonAgent addon) : base("MonsterDex FloorGuide", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar)
    {
        _storage = storage;
        _framework = framework;
        _condition = condition;
        _clientState = state;
        _addon = addon;
        _log = log;
        command.AddCommand("debug_floor", args =>
        {
            var argArr = args.Split(' ');
            if (!byte.TryParse(argArr[0], out var id) || !ushort.TryParse(argArr[1], out var ter))
            {
                if (argArr[0] == "print")
                {
                    var floor = _debug == 0 ? _addon.Floor : _debug;
                    _log.Debug($"Currently displaying: {_dataPath}{floor}");
                    return;
                }
                _debug = 0;
                _dataPath = "";
                IsOpen = false;
                return;
            }

            _debug = id;
            TerritoryChanged(ter);
            _locale = _storage.GetInstance<Locale>(_dataPath + "/Floors.yml");
            IsOpen = true;

        }, show: false);
        command.AddCommand(new[] { "enable_floor", "e_floor", "enable_f", "ef" }, _addon.Restart, "Resets the floor getter function to try again");
        var config = _storage.GetInstance<Configuration>()!;
        _config = config;
        SizeConstraints = new WindowSizeConstraints
        {
            MaximumSize = new Vector2(800 * config.WindowSizeScaled, 600),
            MinimumSize = new Vector2(450 * config.WindowSizeScaled, 100)
        };
        BgAlpha = config.Opacity;
        framework.Update += GetData;
        config.OnChange += ConfigChanged;
        state.TerritoryChanged += TerritoryChanged;
        storage.StorageChanged += StorageChanged;
    }

    private void StorageChanged(StorageEventArgs obj)
    {
        if (obj.StorageType == typeof(Territories) || obj.StorageType == typeof(Locale))
            TerritoryChanged(_clientState.TerritoryType);
    }

    private void TerritoryChanged(ushort e)
    {
        var territories = _storage.GetInstance<Territories>();
        if (territories == null)
            return;
        _dataPath = territories.GetTerritoryName(e, _log);
        _locale = _storage.GetInstance<Locale>(_dataPath + "/Floors.yml");
    }

    private void GetData(IFramework framework)
    {
        if (_debug != 0)
            return;

        var dataPath = _dataPath;
        if (dataPath != "")
        {
            RefreshPaths(dataPath);
            if (!_addon.DirectorDisabled && !_config.HideFloor && _addon.Floor % 10 != 0 && _storage.GetInstance(_floorsPath) != null)
                IsOpen = true;
            else
                IsOpen = false;
        }
        else
            IsOpen = false;
    }

    private void ConfigChanged(Configuration config)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MaximumSize = new Vector2(800 * config.WindowSizeScaled, 600),
            MinimumSize = new Vector2(450 * config.WindowSizeScaled, 100)
        };
        BgAlpha = config.Opacity;
        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (config.ClickThrough && !Flags.HasFlag(ImGuiWindowFlags.NoInputs))
            Flags |= ImGuiWindowFlags.NoInputs;
        else if (!config.ClickThrough && Flags.HasFlag(ImGuiWindowFlags.NoInputs))
            Flags &= ~ImGuiWindowFlags.NoInputs;
    }

    public override void Draw()
    {
        RefreshPaths(_dataPath);
        var remap = (FloorData?)_storage.GetInstance(_floorsPath);
        if (remap == null) return;
        var floor = _debug == 0 ? _addon.Floor : _debug;
        floor = remap.FloorDictionary.GetValueOrDefault(floor, floor);
        using var _ = Font.Font.RegularFont.Push();
        try
        {
            ImGui.Text("樓層指南");
            ImGui.TextUnformatted(_locale?.GetLocale(FloorKey(floor)));
        }
        catch (Exception e)
        {
            _log.Error(e, "Error trying to draw floor guide.");
            // ignored
        }
    }

    private void RefreshPaths(string dataPath)
    {
        if (ReferenceEquals(dataPath, _pathsFor))
            return;
        _floorsPath = dataPath + "/Floors.yml";
        _floorKeyPrefix = dataPath[(dataPath.LastIndexOf("/", StringComparison.Ordinal) + 1)..];
        _floorKeyFor = -1;
        _pathsFor = dataPath;
    }

    private string FloorKey(byte floor)
    {
        if (_floorKeyFor != floor)
        {
            _floorKey = $"{_floorKeyPrefix}{floor}";
            _floorKeyFor = floor;
        }
        return _floorKey;
    }

    public void Dispose()
    {
        _framework.Update -= GetData;
        _clientState.TerritoryChanged -= TerritoryChanged;
        var _config = _storage.GetInstance<Configuration>()!;
        _config.OnChange -= ConfigChanged;
        _clientState = null!;
        _condition = null!;
        _framework = null!;
        _storage = null!;
        _addon = null!;
        _locale = null!;
        _log = null!;
    }
}
