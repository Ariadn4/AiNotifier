using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace AiNotifier;

public class NotifyServer : IDisposable
{
    private readonly HttpListener _listener;
    private CancellationTokenSource? _cts;
    private readonly Func<string> _statusProvider;

    // 跟踪每个 session 的最后信号类型，用于抑制 stop→notify 和连续相同信号
    private readonly Dictionary<string, string> _lastSignal = new();
    private readonly object _signalLock = new();

    public event Action<string?, bool>? NotifyRequested;   // (cwd, isDuplicate)
    public event Action<string?, bool>? StopRequested;     // (cwd, isDuplicate)
    public event Action? NudgeRequested;

    public NotifyServer(Func<string> statusProvider)
    {
        _statusProvider = statusProvider;
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://localhost:19836/");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        Task.Run(() => ListenLoop());
    }

    private async Task ListenLoop()
    {
        while (_listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                HandleRequest(context);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
        }
    }

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiNotifier", "request.log");

    /// <summary>启动时清理日志，只保留最近 12 小时的记录</summary>
    public static void CleanupLog()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            var cutoff = DateTime.Now.AddHours(-12);
            var kept = File.ReadAllLines(LogPath)
                .Where(line =>
                {
                    // 格式: [yyyy-MM-dd HH:mm:ss.fff] ...
                    if (line.Length < 25 || line[0] != '[') return false;
                    return DateTime.TryParse(line.Substring(1, 23), out var ts) && ts >= cutoff;
                })
                .ToList();
            File.WriteAllText(LogPath, string.Join("\n", kept) + (kept.Count > 0 ? "\n" : ""));
        }
        catch { }
    }

    private void LogRequest(HttpListenerContext context, string path)
    {
        try
        {
            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var method = context.Request.HttpMethod;
            var userAgent = context.Request.UserAgent ?? "(none)";
            var remoteIp = context.Request.RemoteEndPoint?.ToString() ?? "?";
            var line = $"[{time}] {method} {path} from={remoteIp} UA={userAgent}\n";
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, line);
        }
        catch { }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "";
        string responseText;
        int statusCode = 200;

        LogRequest(context, path);

        var queryParams = ParseQueryString(context.Request.RawUrl);
        var cwd = queryParams.GetValueOrDefault("cwd");
        var sid = queryParams.GetValueOrDefault("sid") ?? "";

        switch (path)
        {
            case "/notify":
                // 如果该 session 已经 stop 且没有新的 UserPromptSubmit，则全面抑制 notify
                bool fullSuppressed = false;
                bool notifyDup = false;
                lock (_signalLock)
                {
                    if (sid != "" && _lastSignal.TryGetValue(sid, out var lastNotify))
                    {
                        if (lastNotify == "stop")
                            fullSuppressed = true;
                        else if (lastNotify == "notify")
                            notifyDup = true;
                    }
                    if (!fullSuppressed && sid != "")
                        _lastSignal[sid] = "notify";
                }
                if (fullSuppressed)
                {
                    responseText = "OK (suppressed: already stopped)";
                }
                else
                {
                    responseText = notifyDup ? "OK (duplicate)" : "OK";
                    Application.Current.Dispatcher.BeginInvoke(() => NotifyRequested?.Invoke(cwd, notifyDup));
                }
                break;

            case "/stop":
                bool stopDup = false;
                lock (_signalLock)
                {
                    if (sid != "" && _lastSignal.TryGetValue(sid, out var lastStop) && lastStop == "stop")
                        stopDup = true;
                    if (sid != "")
                        _lastSignal[sid] = "stop";
                }
                responseText = stopDup ? "OK (duplicate)" : "OK";
                Application.Current.Dispatcher.BeginInvoke(() => StopRequested?.Invoke(cwd, stopDup));
                break;

            case "/start":
                responseText = "OK";
                // 新的 UserPromptSubmit 到达，清除该 session 的信号记录
                if (sid != "")
                {
                    lock (_signalLock)
                    {
                        _lastSignal.Remove(sid);
                    }
                }
                Application.Current.Dispatcher.BeginInvoke(() => NudgeRequested?.Invoke());
                break;

            case "/status":
                responseText = _statusProvider();
                break;

            default:
                responseText = "Not Found";
                statusCode = 404;
                break;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";
        var buffer = Encoding.UTF8.GetBytes(responseText);
        context.Response.ContentLength64 = buffer.Length;
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.Close();
    }

    /// <summary>
    /// 手动解析 URL 查询参数，自动处理编码：先尝试 UTF-8，无效则回退系统默认编码（中文 Windows 为 GBK）
    /// </summary>
    private static Dictionary<string, string> ParseQueryString(string rawUrl)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qIndex = rawUrl.IndexOf('?');
        if (qIndex < 0) return result;

        var query = rawUrl.Substring(qIndex + 1);
        foreach (var pair in query.Split('&'))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex < 0) continue;
            var key = pair.Substring(0, eqIndex);
            var rawValue = pair.Substring(eqIndex + 1);
            result[key] = DecodeUrlValue(rawValue);
        }
        return result;
    }

    private static string DecodeUrlValue(string raw)
    {
        // 将 percent-encoded 字符串解码为字节
        var bytes = new List<byte>();
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '%' && i + 2 < raw.Length
                && IsHex(raw[i + 1]) && IsHex(raw[i + 2]))
            {
                bytes.Add((byte)Convert.ToInt32(raw.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (raw[i] == '+')
            {
                bytes.Add((byte)' ');
            }
            else
            {
                bytes.Add((byte)raw[i]);
            }
        }

        var data = bytes.ToArray();

        // 先尝试 UTF-8
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return utf8.GetString(data);
        }
        catch { }

        // 回退到系统 ANSI 代码页（中文 Windows = GBK/CP936）
        // .NET 8 的 Encoding.Default 是 UTF-8，需要显式获取系统代码页
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var ansiCodePage = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            return Encoding.GetEncoding(ansiCodePage).GetString(data);
        }
        catch
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    public void Dispose()
    {
        _cts?.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}
