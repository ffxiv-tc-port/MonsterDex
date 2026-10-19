using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Graphics.Environment;
using ContentType = DeepDungeonDex.Storage.ContentType;

namespace DeepDungeonDex.Hooks;

public unsafe class AddonAgent : IDisposable
{
    private IFramework _framework;
    private IPluginLog _log;
    private EventFramework* _eventFramework;
    private EnvManager* _envManager;
    // 位址解不出時該項偵測停用（樓層／內容類型／天氣維持初值），不讓插件載入失敗。
    private readonly bool _eventFrameworkAvailable = EventFramework.Addresses.Instance.Value != 0;
    private readonly bool _envManagerAvailable = EnvManager.Addresses.Instance.Value != 0;
    public byte Floor { get; private set; }
    public bool DirectorDisabled { get; private set; }
    public ContentType ContentType { get; private set; } = ContentType.None;
    public byte Weather { get; private set; }
    public byte[] WeatherIds { get; private set; } = new byte[32];

    public AddonAgent(IFramework framework, IPluginLog log)
    {
        _framework = framework;
        _log = log;
        if (!_eventFrameworkAvailable)
            _log.Warning("[DeepDungeonDex] 找不到 EventFramework 的位址（遊戲改版後特徵碼對不上），樓層與內容類型偵測停用，其餘功能照常。");
        if (!_envManagerAvailable)
            _log.Warning("[DeepDungeonDex] 找不到 EnvManager 的位址（遊戲改版後特徵碼對不上），天氣偵測停用，其餘功能照常。");
        // 建構子不在框架執行緒上，不在這裡讀遊戲記憶體；訂閱後的第一個 Framework.Update 就會跑 OnUpdate。
        _framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        _eventFramework = _eventFrameworkAvailable ? EventFramework.Instance() : null;
        _envManager = _envManagerAvailable ? EnvManager.Instance() : null;
        if (!DirectorDisabled)
            CheckDirector();
        CheckWeather();
    }

    private void CheckWeather()
    {
        if (_envManager == null)
            return;

        Weather = _envManager->ActiveWeather;
        if (_envManager->EnvScene == null)
            return;

        // 內容沒變就沿用上一份（舊陣列從不就地改寫，拿到的人看到的仍是快照）。
        var ids = _envManager->EnvScene->WeatherIds;
        if (!ids.SequenceEqual(WeatherIds))
            WeatherIds = ids.ToArray();
    }

    private void CheckDirector()
    {
        try
        {
            if (!IsContentSafe())
            {
                ContentType = ContentType.None;
                return;
            }

            var activeInstance = _eventFramework->GetInstanceContentDirector();

            if (activeInstance != null)
            {
                ContentType = (ContentType)(1 << ((int)activeInstance->InstanceContentType));
                if (ContentType.HasFlag(ContentType.DeepDungeon))
                {
                    Floor = ((InstanceContentDeepDungeon*)activeInstance)->Floor;
                }
            }
            else
            {
                var activePublic = _eventFramework->GetPublicContentDirector();
                if (activePublic != null)
                {
                    ContentType = (ContentType)(1 << ((byte)activePublic->Type + 20));
                }
                else
                {
                    ContentType = ContentType.None;
                }
            }
        }
        catch (Exception e)
        {
            _log.Error(e, "Error trying to fetch ContentDirector disabling feature.");
            Dispose(false);
        }
    }

    private bool IsContentSafe()
    {
        // EventFramework 的 [StaticAddress] 帶 isPointer:true（return *ppEventFramework）
        // ⇒ 合法回 null（未登入／切區時），直接解參考會是 AccessViolationException。
        // AVE 在 .NET Core 是 corrupted-state exception，CheckDirector 外層的 try/catch 攔不到，
        // 所以守衛必須在解參考之前。
        if (_eventFramework == null)
            return false;

        var contentDirector = _eventFramework->GetContentDirector();

        if ((IntPtr)contentDirector == IntPtr.Zero)
            return false;

        var eventHandlerInfo = contentDirector->Director.EventHandlerInfo;

        return (IntPtr)eventHandlerInfo != IntPtr.Zero;
    }

    public void Restart()
    {
        if (!DirectorDisabled)
            return;
        DirectorDisabled = false;
    }

    public void Dispose(bool disposing)
    {
        DirectorDisabled = true;
        if (!disposing)
            return;

        _framework.Update -= OnUpdate;
        _framework = null!;
        _log = null!;
    }

    public void Dispose() => Dispose(true);
}