using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DeepDungeonDex;

public partial class Requests : IDisposable
{
    private readonly CancellationTokenSource _token = new();
    // _token.Token 在 _token.Dispose() 之後讀取會擲例外；背景工作一律用這份建構時抄下的。
    private readonly CancellationToken _stop;
    // 進行中的 RefreshFileList／RefreshLang 數；Dispose 等它歸零才放掉 Client 並讓 Handler 被收掉。
    private int _activeRefreshes;
    private int _langLoopStarted;
    private int _disposed;
    private readonly Regex _percentRegex = new("(%%)|(%)", RegexOptions.Compiled);
    private IPluginLog _log;
    private bool _loadedOnce;
    private int _sourceAnnounced;
    private static readonly string SourceHost = new Uri(BaseUrl).Host;

    // CreateGitHubHttpClient() 回的 client 擁有權在呼叫端 => Dispose() 必須收掉。
    public HttpClient Client;
    // 台服艦隊：資料改為自 host（org 的 data-tc 分支），不依賴上游 monsterdex.wildwolf.dev。
    // 上游那台一旦停供，這裡會 404 → Get() 走 catch → 退回內嵌的 data.dat（隨版釘死的快照）。
    public const string BaseUrl = "https://raw.githubusercontent.com/ffxiv-tc-port/MonsterDex/data-tc";
    public TimeSpan CacheTime = TimeSpan.FromHours(6);
    public StorageHandler Handler;
    public bool RequestingData { get; private set; }
    public bool RequestingLang { get; private set; }
    public bool IsRequesting => RequestingData || RequestingLang;
    public Action? IsRequestDone = null;
#if RELEASE
    private const bool Debug = false;
#else
    private const bool Debug = true;
#endif

    public Requests(IDalamudPluginInterface pluginInterface, StorageHandler handler, IPluginLog log)
    {
        _stop = _token.Token;
        Client = pluginInterface.CreateGitHubHttpClient();
        Client.DefaultRequestVersion = HttpVersion.Version20;
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MonsterDex", Assembly.GetExecutingAssembly().GetName().Version!.ToString()));
        Handler = handler;
        _log = log;
        RunInBackground(() => RefreshFileList(), "data refresh");
    }

    // 背景工作的例外在這裡被觀察並寫 log，不會變成終結器重擲的 Unobserved exception；取消（卸載）不算失敗。
    internal void RunInBackground(Func<Task> work, string name)
    {
        Task.Run(work).ContinueWith(
            t => _log.Error(t.Exception!.GetBaseException(), $"MonsterDex background {name} stopped unexpectedly"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public async Task<Dictionary<string, string[]>?> GetFileList()
    {
        try
        {
            _log.Verbose("Getting file list");
            var content = await Get("index.json");
            return string.IsNullOrWhiteSpace(content) ? null : JsonConvert.DeserializeObject<Dictionary<string, string[]>>(content);
        }
        catch (Exception e)
        {
            _stop.ThrowIfCancellationRequested();
            _log.Error(e, "");
            return null;
        }
    }

    public async Task RefreshFileList(bool continuous = true)
    {
        Interlocked.Increment(ref _activeRefreshes);
        try
        {
            _stop.ThrowIfCancellationRequested();
            await RefreshFileListCore(continuous);
        }
        finally
        {
            Interlocked.Decrement(ref _activeRefreshes);
        }
    }

    private async Task RefreshFileListCore(bool continuous)
    {
    StartRequest:
        RequestingData = true;
        var list = await GetFileList();
        if (list == null)
            goto RefreshEnd;

        Handler.AddStorage("index.json", list);

        _log.Verbose("Loading Types");
        var types = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.GetInterfaces().Contains(typeof(ILoad))).ToArray();

        foreach (var (className, files) in list)
        {
            var type = types.FirstOrDefault(t => t.Name == className);
            if (type == null)
                continue;
            _log.Verbose($"Loading Files: {files.Length} of Type: {className}");
            foreach (var file in files)
            {
                try
                {
                    _log.Verbose($"Loading File: {file}");
                    var content = await Get(file);
                    _log.Verbose($"Content length from file: {content?.Length}");
                    if (string.IsNullOrWhiteSpace(content))
                        continue;
                    _log.Verbose("Creating instance");
                    var instance = (ILoad)Activator.CreateInstance(type)!;
                    _log.Verbose("Loading content");
                    Handler.AddStorage(file, instance.Load(content));
                }
                catch (Exception e)
                {
                    _stop.ThrowIfCancellationRequested();
                    _log.Error(e, $"Failed processing File: {file}");
                }
            }
        }

        _loadedOnce = true;
        IsRequestDone?.Invoke();

    RefreshEnd:

        if (Interlocked.CompareExchange(ref _langLoopStarted, 1, 0) == 0)
            RunInBackground(() => RefreshLang(), "language refresh");
        RequestingData = false;
        if (continuous)
        {
            _log.Verbose($"Refreshing file list in {CacheTime:g}");
            await Task.Delay(CacheTime, _stop);
            if (!_stop.IsCancellationRequested)
                goto StartRequest;
        }
    }

    public async Task<string?> Get(string url)
    {
#pragma warning disable CS0162 // Unreachable code detected
        if (Debug)
            return await GetFromFile(url);
        try
        {
            _log.Verbose($"Requesting {BaseUrl}/{url}");
            var response = await Client.GetAsync($"{BaseUrl}/{url}", _stop);
            _log.Verbose($"Response: {response.StatusCode}");
            if (response.IsSuccessStatusCode)
            {
                if (Interlocked.Exchange(ref _sourceAnnounced, 1) == 0)
                    _log.Information($"MonsterDex data source reachable: {SourceHost} (first file: {url})");
                _log.Verbose("Reading string");
                return await response.Content.ReadAsStringAsync();
            }

            throw new HttpRequestException($"Request: {url}\nFailed with status code {response.StatusCode}");
        }
        catch (Exception e)
        {
            // 卸載中：不退回內嵌快照、不寫 log，讓整條下載以取消結束。
            _stop.ThrowIfCancellationRequested();
            // data-tc 缺檔的 404 是常態；只有「還沒載入過、要退回內嵌快照」那次值得 Warning。
            if (!_loadedOnce)
            {
                _log.Warning(e, $"MonsterDex download from {SourceHost} failed, falling back to bundled data: {url}");
                return await GetFromFile(url);
            }

            _log.Info(e, "Trying to get file from precompiled data");
            return null;
        }
#pragma warning restore CS0162 // Unreachable code detected
    }

    // 會被呼叫兩次：Main.Dispose 先停下載，DisposeDI 之後再呼叫一次。
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _token.Cancel();
        // 下載迴圈會寫 Handler、觸發視窗事件；等它們以取消退出才往下收（設上限，不讓卸載卡住）。
        // _log 刻意不清成 null：背景工作的最後一步可能還要寫 log。
        if (!SpinWait.SpinUntil(() => Volatile.Read(ref _activeRefreshes) == 0, TimeSpan.FromSeconds(3)))
            _log.Warning("MonsterDex background downloads did not stop within 3 s of unloading");
        _token.Dispose();
        _fileStream?.Dispose();
        Client.Dispose();
    }
}