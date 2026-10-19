using Dalamud.Plugin.Ipc;

namespace DeepDungeonDex;

/// <summary>
/// 對艦隊其他外掛開放的 IPC。刻意使用 Dalamud 原生的 <see cref="ICallGateProvider{T1,TRet}"/>，
/// 不引入 ECommons（本外掛沒有這個相依，為了 EzIPC 而加會把相依樹整個放大）。
/// </summary>
/// <remarks>
/// 契約（消費端請逐字照抄，不要用推的）：
/// <list type="bullet">
/// <item>名稱：<c>MonsterDex.GetMobVulnerabilities</c></item>
/// <item>形狀：<c>Func&lt;uint,int&gt;</c>，參數＝BNpcName row id（即 <c>IBattleChara.NameId</c>，不是 DataId／BaseId／ObjectId）</item>
/// <item>回傳 <c>-1</c>＝沒有這隻怪的資料、資料尚未載入完成，或非頭目的資料沒寫弱點（不知道）；頭目沒寫弱點回 <c>0</c></item>
/// <item>否則為 bitflags：bit0＝可眩暈 bit1＝可加重 bit2＝可睡眠 bit3＝可束縛 bit4＝可減速</item>
/// </list>
/// <para>
/// bit5 以上目前未定義，消費端不要對它們做任何假設（保留給未來擴充「已確認／未確認」兩態）。
/// </para>
/// </remarks>
public class IpcProvider : IDisposable
{
    public const string GetMobVulnerabilitiesName = "MonsterDex.GetMobVulnerabilities";

    // 🔴 這組位元的順序**刻意**與內部的 Storage.Weakness 不同
    //（Weakness 是 Stun / Heavy / Slow / Sleep / Bind ＝ 0x01 / 0x02 / 0x04 / 0x08 / 0x10）。
    // 千萬不要直接 (int)mob.Weakness 轉型出去——Slow 與 Sleep、Bind 會靜默錯位。
    private const int BitStun = 1 << 0;
    private const int BitHeavy = 1 << 1;
    private const int BitSleep = 1 << 2;
    private const int BitBind = 1 << 3;
    private const int BitSlow = 1 << 4;

    private StorageHandler _storage;
    private IPluginLog _log;
    private ICallGateProvider<uint, int>? _getMobVulnerabilities;
    private bool _loggedFirstCall;
    private bool _disposed;

    public IpcProvider(IDalamudPluginInterface pluginInterface, StorageHandler storage, IPluginLog log)
    {
        _storage = storage;
        _log = log;
        _getMobVulnerabilities = pluginInterface.GetIpcProvider<uint, int>(GetMobVulnerabilitiesName);
        _getMobVulnerabilities.RegisterFunc(GetMobVulnerabilities);
        // 要使用者回報的診斷。
        _log.Information($"[MonsterDex] 已註冊 IPC：{GetMobVulnerabilitiesName}");
    }

    /// <summary>
    /// 查一隻怪可以吃哪些行動抑制類狀態。詳細契約見類別註解。
    /// </summary>
    public int GetMobVulnerabilities(uint nameId)
    {
        try
        {
            var data = _storage.GetInstances<MobData>();

            if (!_loggedFirstCall)
            {
                _loggedFirstCall = true;
                _log.Information(
                    $"[MonsterDex] IPC 首次被呼叫（nameId={nameId}）：目前載入 {data.Length} 份 MobData、" +
                    $"共 {data.Sum(t => t.MobDictionary.Count)} 筆怪物資料。");
            }

            // 資料還沒載入完成（或整個載入失敗）——與「查無此怪」一樣回 -1。
            if (data.Length == 0)
                return -1;

            var mob = data.GetData(nameId);
            if (mob == null)
                return -1;

            // 非頭目的資料沒寫 Weakness＝不知道；落到下面會算成 0，被消費端讀成「明確全部不吃」。
            if (mob.IsWeaknessUnknown)
                return -1;

            var weakness = mob.Weakness;
            var result = 0;

            // ⚠️ 資料模型把「不確定」編成 base bit ＋ (base bit << 6)，例如 StunUnknown = 0x40 | Stun。
            // 所以 HasFlag(Stun) 對「已確認可暈」與「沒人測過但推定可暈」兩者都成立 ＝ 寬鬆邊。
            // 這是刻意的：消費端拿到 1 代表「值得一試」，而不是「保證成功」。
            if (weakness.HasFlag(Weakness.Stun)) result |= BitStun;
            if (weakness.HasFlag(Weakness.Heavy)) result |= BitHeavy;
            if (weakness.HasFlag(Weakness.Sleep)) result |= BitSleep;
            if (weakness.HasFlag(Weakness.Bind)) result |= BitBind;
            if (weakness.HasFlag(Weakness.Slow)) result |= BitSlow;

            return result;
        }
        catch (Exception e)
        {
            // StorageHandler.Storage 是普通 Dictionary，載入執行緒可能正在寫入 ⇒ 列舉時可能擲例外。
            // 這是受管理例外（攔得到），對呼叫端一律退化成「沒有資料」而不是把例外丟過去。
            _log.Error(e, $"[MonsterDex] IPC {GetMobVulnerabilitiesName} 查詢 nameId={nameId} 失敗");
            return -1;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _getMobVulnerabilities?.UnregisterFunc();
        }
        catch (Exception e)
        {
            // 取消註冊擲例外不可以打斷後面的清理。
            _log?.Error(e, $"[MonsterDex] 取消註冊 IPC {GetMobVulnerabilitiesName} 失敗");
        }

        _getMobVulnerabilities = null;
        _storage = null!;
        _log = null!;
    }
}
