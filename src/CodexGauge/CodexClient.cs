using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodexGauge;

public sealed class CodexClient : IDisposable
{
    private Process? process;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writes = new(1, 1);
    private int nextId;
    public event Action? LimitsChanged;
    public string? Executable { get; private set; }

    public static string Locate()
    {
        var explicitPath = Environment.GetEnvironmentVariable("CODEX_GAUGE_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;
        // Prefer the desktop version already running, then its newest installed binary.
        foreach (var p in Process.GetProcessesByName("codex"))
        {
            using (p)
            {
                try { var path = p.MainModule?.FileName; if (path != null && File.Exists(path)) return path; }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(root))
        {
            var path = Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (path != null) return path;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(dir.Trim('"'), "codex.exe");
            if (File.Exists(path)) return path;
        }
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex");
        if (Directory.Exists(npm))
        {
            var path = Directory.EnumerateFiles(npm, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (path != null) return path;
        }
        throw new FileNotFoundException("Установите Codex или задайте CODEX_GAUGE_CODEX_PATH.");
    }

    private async Task ConnectAsync()
    {
        if (process != null && !process.HasExited) return;
        Disconnect();
        Executable = Locate();
        var info = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false), WorkingDirectory = AppContext.BaseDirectory
        };
        info.ArgumentList.Add("app-server"); info.ArgumentList.Add("--listen"); info.ArgumentList.Add("stdio://");
        process = Process.Start(info) ?? throw new IOException("Не удалось запустить Codex.");
        var current = process;
        _ = ReadLoop(current);
        _ = DrainErrors(current);
        await RequestAsync("initialize", new { clientInfo = new { name = "codex_gauge", title = "Codex Gauge", version = "0.1.0" } });
        await WriteAsync(new { method = "initialized" });
    }

    private static async Task DrainErrors(Process p)
    {
        try { while (await p.StandardError.ReadLineAsync() != null) { } } catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private async Task ReadLoop(Process p)
    {
        try
        {
            while (await p.StandardOutput.ReadLineAsync() is { } line)
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number) && pending.TryRemove(number, out var completion))
                    completion.TrySetResult(root.Clone());
                else if (root.TryGetProperty("method", out var method) && method.GetString() is "account/rateLimits/updated" or "account/login/completed")
                    LimitsChanged?.Invoke();
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or JsonException or InvalidOperationException) { }
        finally
        {
            if (ReferenceEquals(process, p))
                foreach (var pair in pending.ToArray())
                    if (pending.TryRemove(pair.Key, out var completion)) completion.TrySetException(new IOException("Подключение Codex закрыто."));
        }
    }

    private async Task WriteAsync(object message)
    {
        await writes.WaitAsync();
        try
        {
            var p = process ?? throw new IOException("Codex не подключён.");
            await p.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
            await p.StandardInput.FlushAsync();
        }
        finally { writes.Release(); }
    }

    private async Task<JsonElement> RequestAsync(string method, object? parameters = null)
    {
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await WriteAsync(new { id, method, @params = parameters });
            var reply = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
            if (reply.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                var auth = message.Contains("auth", StringComparison.OrdinalIgnoreCase) || message.Contains("login", StringComparison.OrdinalIgnoreCase);
                throw new CodexException(auth ? "Нужен вход в Codex" : "Codex не вернул лимиты", auth);
            }
            return reply.GetProperty("result").Clone();
        }
        finally { pending.TryRemove(id, out _); }
    }

    public async Task<JsonElement> ReadLimitsAsync()
    {
        try { await ConnectAsync(); return await RequestAsync("account/rateLimits/read"); }
        catch (CodexException) { throw; }
        catch { Disconnect(); throw; }
    }

    public async Task<string> LoginAsync()
    {
        await ConnectAsync();
        var result = await RequestAsync("account/login/start", new { type = "chatgpt" });
        var url = result.GetProperty("authUrl").GetString();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !(uri.Host == "auth.openai.com" || uri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase) || uri.Host == "chatgpt.com"))
            throw new IOException("Codex вернул неожиданный адрес входа.");
        return url!;
    }

    private void Disconnect()
    {
        var old = process; process = null;
        foreach (var pair in pending.ToArray())
            if (pending.TryRemove(pair.Key, out var completion)) completion.TrySetException(new IOException("Подключение Codex закрыто."));
        if (old == null) return;
        try { if (!old.HasExited) old.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        old.Dispose();
    }
    public void Dispose() => Disconnect();
}

public sealed class CodexException(string message, bool authenticationRequired) : Exception(message)
{
    public bool AuthenticationRequired { get; } = authenticationRequired;
}
