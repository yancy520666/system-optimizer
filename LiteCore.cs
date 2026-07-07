using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;

namespace SystemOptimizerLite;

public enum ComponentState { Missing, Downloading, Online, Error }

public record CategoryInfo(string Name, string Description);
public record OptimizerItem(string Id, string Title, string Description, string Tab, string Group, string Risk, string YamlFile, bool SigRequired, string Icon, string Windows, string Category, bool DefaultChecked, bool EnabledByDefault, int Order);
public record CleanerItem(string Id, string CleanerId, string OptionId, string Title, string Description, string Group, string Risk, bool DefaultSelected);
public record CleanerPathInfo(string Path, string SizeText, long Bytes);
public record CleanerRunSummary(string Mode, int SelectedItems, int FileCount, long Bytes, string SpaceText, string OutputPreview, bool IsLoading, List<CleanerPathInfo> TopPaths);
public record CleanerRunResult(string Output, CleanerRunSummary Summary);
public record ToolItem(string Id, string Name, string Description, string AssetName, string DownloadUrl, string Sha256, bool Dangerous, List<string> Aliases);
public record DriverStatusItem(string Name, string Detail, string Status);
public record DeviceMatchInfo(string Manufacturer, string Model, string SerialNumber, string MatchSummary, string SupportPage, string DownloadUrl, string FileName, bool MatchSuccess);
public record DriverPageData(DeviceMatchInfo Device, List<DriverStatusItem> Drivers, DateTimeOffset CachedAt);
public record ComponentStatus(ComponentState State, string Text, string Version, int ReadyCount, int TotalCount, string Error = "");

public sealed class AppSettings
{
    public string ThemeMode { get; set; } = "Dark";
    public bool KeepLocalComponents { get; set; }
    public string LastSelectedPage { get; set; } = "系统优化";
}

public static class AppPaths
{
    public static readonly string LocalAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemOptimizerLite");
    public static readonly string Runtime = Path.Combine(LocalAppData, "runtime");
    public static readonly string Downloads = Path.Combine(LocalAppData, "downloads");
    public static readonly string OptimizerRuntime = Path.Combine(Runtime, "optimizerNXT");
    public static readonly string CleanerRuntime = Path.Combine(Runtime, "bleachbit");
    public static readonly string ToolRuntime = Path.Combine(Runtime, "system-tools");
    public static readonly string StatePath = Path.Combine(LocalAppData, "optimizer-state.json");
    public static readonly string SettingsPath = Path.Combine(LocalAppData, "settings.json");
    public static readonly string BackupRoot = Path.Combine(LocalAppData, "rollback-backups");
    public static readonly string ReportRoot = Path.Combine(LocalAppData, "rollback-reports");
    public static readonly string LogRoot = Path.Combine(LocalAppData, "logs");
    public static readonly string CacheRoot = Path.Combine(LocalAppData, "cache");
    public static readonly string DriverCachePath = Path.Combine(CacheRoot, "driver-status.json");

    public static string DataFile(string name) => Path.Combine(AppContext.BaseDirectory, "Data", name);

    public static void Ensure()
    {
        Directory.CreateDirectory(LocalAppData);
        Directory.CreateDirectory(Runtime);
        Directory.CreateDirectory(Downloads);
        Directory.CreateDirectory(LogRoot);
        Directory.CreateDirectory(CacheRoot);
    }

    public static void CleanupRuntimeComponents()
    {
        foreach (var path in new[] { OptimizerRuntime, CleanerRuntime, ToolRuntime, Downloads })
        {
            SafeDeleteDirectory(path);
        }
    }

    public static bool SafeDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return true;
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(entry, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(path, true);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            LogService.Write($"组件清理跳过：{path} - {ex.Message}");
            return false;
        }
    }
}

public static class LogService
{
    public static void Write(string message)
    {
        try
        {
            AppPaths.Ensure();
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(AppPaths.LogRoot, $"{DateTime.Now:yyyy-MM-dd}.log"), line, Encoding.UTF8);
        }
        catch { }
    }

    public static string ExportToDesktop()
    {
        AppPaths.Ensure();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var target = Path.Combine(desktop, $"SystemOptimizerLite-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        var files = Directory.Exists(AppPaths.LogRoot) ? Directory.GetFiles(AppPaths.LogRoot, "*.log").OrderBy(x => x).ToList() : new();
        using var output = new StreamWriter(target, false, Encoding.UTF8);
        foreach (var file in files)
        {
            output.WriteLine($"===== {Path.GetFileName(file)} =====");
            output.WriteLine(File.ReadAllText(file, Encoding.UTF8));
        }
        if (files.Count == 0) output.WriteLine("暂无日志。");
        return target;
    }
}

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static AppSettings Load()
    {
        AppPaths.Ensure();
        if (!File.Exists(AppPaths.SettingsPath)) return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath, Encoding.UTF8), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        AppPaths.Ensure();
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
    }
}

public static class AdminService
{
    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static void RestartAsAdministrator()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exe)) return;
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
        Application.Current.Shutdown();
    }
}

public static class EnvironmentService
{
    public static string RuntimeText()
    {
        var os = Environment.OSVersion.Version;
        var desktopRuntime = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App"));
        return os.Major >= 10 && desktopRuntime ? "运行环境正常" : "运行环境异常";
    }
}

public static class JsonUtil
{
    public static JsonDocument ReadDocument(string path) => JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
    public static string S(this JsonElement element, string name, string fallback = "")
        => element.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : fallback;
    public static bool B(this JsonElement element, string name, bool fallback = false)
        => element.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.False && fallback) : fallback;
}

public sealed class DownloadService : IDisposable
{
    private readonly HttpClient _client;

    public DownloadService()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("SystemOptimizerLite/3.0");
        _client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    public async Task DownloadFileAsync(string url, string targetPath, CancellationToken token, Action<long, long?>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var temp = targetPath + ".download";
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                TryDeleteFile(temp);
                using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                if (ShouldRetry(response.StatusCode))
                {
                    lastError = new HttpRequestException($"远端请求暂时受限：{(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
                    if (attempt < 5)
                    {
                        await DelayForRetryAsync(response, attempt, token);
                        continue;
                    }
                    break;
                }
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(temp);
                var buffer = new byte[128 * 1024];
                long received = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, token);
                    if (read <= 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    received += read;
                    progress?.Invoke(received, total);
                }
                output.Close();
                TryDeleteFile(targetPath);
                try
                {
                    File.Move(temp, targetPath, true);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    throw new InvalidOperationException("目标文件正在被占用或没有写入权限，请关闭相关组件后重试。", ex);
                }
                return;
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                if (attempt < 5)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), token);
                    continue;
                }
                break;
            }
            catch (TaskCanceledException ex) when (!token.IsCancellationRequested)
            {
                lastError = new TimeoutException("下载超时或网络速度过慢。", ex);
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), token);
                    continue;
                }
                break;
            }
        }

        TryDeleteFile(temp);
        if (lastError is HttpRequestException httpError && httpError.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("远端服务请求过于频繁，已自动重试但仍被限流。请稍后再试；如果已下载过组件，建议在设置中开启“保留本地组件”。");
        if (lastError is HttpRequestException http)
        {
            if (http.StatusCode != null)
                throw new InvalidOperationException($"远端下载失败：HTTP {(int)http.StatusCode} {http.StatusCode}。", http);
            throw new InvalidOperationException("无法连接远端下载服务，可能是网络异常、DNS 解析失败或 GitHub 连接不稳定。", http);
        }
        if (lastError is TimeoutException timeout)
            throw new InvalidOperationException(timeout.Message, timeout);
        throw lastError ?? new HttpRequestException($"下载失败：{url}");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            LogService.Write($"文件删除跳过：{path} - {ex.Message}");
        }
    }

    private static bool ShouldRetry(HttpStatusCode? statusCode)
        => statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static async Task DelayForRetryAsync(HttpResponseMessage response, int attempt, CancellationToken token)
    {
        var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt);
        if (delay > TimeSpan.FromSeconds(20)) delay = TimeSpan.FromSeconds(20);
        await Task.Delay(delay, token);
    }

    public static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var input = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(input));
    }

    public void Dispose() => _client.Dispose();
}

public sealed class OptimizerService
{
    public const string PinnedVersion = "v1.0.1";
    private const string OptimizerUrl = "https://github.com/hellzerg/optimizerNXT/releases/download/v1.0.1/optimizerNXT.exe";
    private const string OptimizerHash = "397A7374CFF9B23F9DDABFD0D36FF71A20CFF46F6DF57EB929F6C1DD7842FC7F";
    private const string YamlBase = "https://raw.githubusercontent.com/hellzerg/optimizerNXT/v1.0.1/yaml/";
    private const int MaxInputBackups = 20;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private int _installing;
    private string _installMessage = "";

    public bool IsInstalling => _installing == 1;

    public async Task<List<OptimizerItem>> GetItemsAsync()
    {
        await Task.Yield();
        using var doc = JsonUtil.ReadDocument(AppPaths.DataFile("optimizer-packages.json"));
        return doc.RootElement.EnumerateArray()
            .Select((e, index) => new OptimizerItem(e.S("id"), e.S("title"), e.S("description"), e.S("tab"), e.S("group", "其他"), e.S("risk", "normal"), e.S("yamlFile"), e.B("sigRequired", true), e.S("icon", "•"), e.S("windows", "All"), e.S("category"), e.B("defaultChecked"), e.B("enabledByDefault"), index))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.YamlFile))
            .ToList();
    }

    public async Task<ComponentStatus> GetComponentStatusAsync()
    {
        var items = await GetItemsAsync();
        var ready = ReadyCount(items);
        if (IsInstalling) return new ComponentStatus(ComponentState.Downloading, string.IsNullOrWhiteSpace(_installMessage) ? "获取远端服务中" : _installMessage, PinnedVersion, ready, items.Count);
        if (ready == items.Count && IsExeValid()) return new ComponentStatus(ComponentState.Online, $"OptimizerNXT 在线 · YAML {ready}/{items.Count}", PinnedVersion, ready, items.Count);
        return new ComponentStatus(ComponentState.Missing, $"Optimizer 组件离线 · YAML {ready}/{items.Count}", PinnedVersion, ready, items.Count);
    }

    public async Task<bool> IsAppliedAsync(string id)
    {
        await Task.Yield();
        return ReadMutableState().Applied.TryGetValue(id, out var entry) && entry.AppliedAt != null;
    }

    public async Task InstallAsync(CancellationToken token, Action<string>? status = null, bool force = false)
    {
        if (Interlocked.Exchange(ref _installing, 1) == 1) return;
        try
        {
            AppPaths.Ensure();
            if (force)
            {
                StopStaleOptimizerProcesses();
                _installMessage = "正在替换 OptimizerNXT 组件";
                status?.Invoke(_installMessage);
                if (Directory.Exists(AppPaths.OptimizerRuntime) && !AppPaths.SafeDeleteDirectory(AppPaths.OptimizerRuntime))
                    throw new InvalidOperationException("本地 OptimizerNXT 组件正在被占用，无法重新获取。请关闭相关组件后重试。");
            }
            Directory.CreateDirectory(AppPaths.OptimizerRuntime);
            using var dl = new DownloadService();
            var exe = Path.Combine(AppPaths.OptimizerRuntime, "optimizerNXT.exe");
            if (!File.Exists(exe) || DownloadService.Sha256(exe) != OptimizerHash)
            {
                _installMessage = "正在下载 OptimizerNXT 主程序";
                status?.Invoke(_installMessage);
                await dl.DownloadFileAsync(OptimizerUrl, exe, token);
                _installMessage = "正在校验 OptimizerNXT 主程序";
                status?.Invoke(_installMessage);
                if (DownloadService.Sha256(exe) != OptimizerHash)
                {
                    try { File.Delete(exe); } catch { }
                    throw new InvalidOperationException("optimizerNXT.exe SHA256 校验失败");
                }
            }

            var items = await GetItemsAsync();
            var downloadJobs = new List<(OptimizerItem Item, string Url, string Path)>();
            foreach (var item in items)
            {
                var yamlPath = Path.Combine(AppPaths.OptimizerRuntime, item.YamlFile);
                var sigPath = Path.Combine(AppPaths.OptimizerRuntime, item.YamlFile + ".sig");
                if (!File.Exists(yamlPath))
                    downloadJobs.Add((item, YamlBase + item.YamlFile, yamlPath));
                if (item.SigRequired && !File.Exists(sigPath))
                    downloadJobs.Add((item, YamlBase + item.YamlFile + ".sig", sigPath));
            }

            if (downloadJobs.Count > 0)
            {
                var completed = 0;
                using var gate = new SemaphoreSlim(4);
                _installMessage = $"正在下载签名 YAML：0/{downloadJobs.Count}";
                status?.Invoke(_installMessage);
                await Task.WhenAll(downloadJobs.Select(async job =>
                {
                    await gate.WaitAsync(token);
                    try
                    {
                        await dl.DownloadFileAsync(job.Url, job.Path, token);
                        var done = Interlocked.Increment(ref completed);
                        _installMessage = $"正在下载签名 YAML：{done}/{downloadJobs.Count}";
                        status?.Invoke(_installMessage);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
            }
            File.WriteAllText(Path.Combine(AppPaths.OptimizerRuntime, "version.json"), JsonSerializer.Serialize(new { version = PinnedVersion, installedAt = DateTimeOffset.Now, sha256 = OptimizerHash }, JsonOptions), Encoding.UTF8);
            LogService.Write("OptimizerNXT 组件安装完成");
        }
        finally
        {
            _installMessage = "";
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    public async Task<string> ApplyAsync(OptimizerItem item, CancellationToken token)
    {
        var status = await GetComponentStatusAsync();
        if (status.State != ComponentState.Online) throw new InvalidOperationException("OptimizerNXT 组件未就绪，无法执行系统优化。");
        BackupInputs();
        var state = ReadMutableState();
        if (state.Applied.TryGetValue(item.Id, out var existing) && existing.AppliedAt != null)
            return "该项目已处于优化开启状态，无需重复执行。";
        if (!state.Applied.ContainsKey(item.Id))
            state.Applied[item.Id] = new AppliedEntry(item.Id, item.Title, item.YamlFile, CaptureSnapshot(item), null);
        var yaml = Path.Combine(AppPaths.OptimizerRuntime, item.YamlFile);
        var sig = yaml + ".sig";
        if (!File.Exists(yaml) || (item.SigRequired && !File.Exists(sig))) throw new InvalidOperationException("YAML 或签名文件缺失，已阻止执行。");
        StopStaleOptimizerProcesses();
        var exe = Path.Combine(AppPaths.OptimizerRuntime, "optimizerNXT.exe");
        var result = await RunProcessAsync(exe, $"apply \"{yaml}\"", AppPaths.OptimizerRuntime, token, TimeSpan.FromSeconds(180));
        if (result.HadKnownInstanceError)
        {
            StopStaleOptimizerProcesses();
            await Task.Delay(800, token);
            result = await RunProcessAsync(exe, $"apply \"{yaml}\"", AppPaths.OptimizerRuntime, token, TimeSpan.FromSeconds(180));
        }
        if (!result.Succeeded)
        {
            if (result.TimedOut) throw new InvalidOperationException($"优化执行超过 180 秒仍未返回完成状态，已自动结束组件进程。\n\n{TrimFailureOutput(result.Output)}");
            throw new InvalidOperationException(TrimFailureOutput(result.Output));
        }
        state.Applied[item.Id] = state.Applied[item.Id] with { AppliedAt = DateTimeOffset.Now };
        WriteState(state);
        LogService.Write($"应用优化：{item.Title}");
        return "优化已执行，可再次点击该项目回退。";
    }

    public async Task<string> RestoreAsync(string id)
    {
        var state = ReadMutableState();
        if (!state.Applied.TryGetValue(id, out var entry) || entry.Snapshot == null)
            throw new InvalidOperationException("该项目没有可恢复快照，已保留当前状态。");
        var report = RestoreSnapshot(entry.Snapshot);
        if (!report.Ok)
        {
            await Task.Yield();
            var failed = string.Join("\n", report.Results.Where(x => !x.Ok).Take(6).Select(x => $"{x.Type} {x.Target}：{x.Message}"));
            LogService.Write($"回退优化失败：{entry.Title}，已保留开启状态");
            throw new InvalidOperationException($"回退未完成，已保留该项目的开启状态，可稍后再次尝试。\n\n{failed}");
        }
        state.Applied.Remove(id);
        WriteState(state);
        await Task.Yield();
        LogService.Write($"回退优化：{entry.Title}，结果 {report.Ok}");
        return "回退已完成。";
    }

    public async Task<string> RestoreAllAsync()
    {
        var state = ReadMutableState();
        var ids = state.Applied.Keys.ToList();
        BackupInputs();
        var results = new List<object>();
        foreach (var id in ids)
        {
            if (!state.Applied.TryGetValue(id, out var entry) || entry.Snapshot == null) continue;
            var report = RestoreSnapshot(entry.Snapshot);
            results.Add(new { id, entry.Title, report.Ok, report.Results });
            if (report.Ok) state.Applied.Remove(id);
        }
        WriteState(state);
        Directory.CreateDirectory(AppPaths.ReportRoot);
        var reportPath = Path.Combine(AppPaths.ReportRoot, $"optimizer-rollback-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { restoredAt = DateTimeOffset.Now, results }, JsonOptions), Encoding.UTF8);
        await Task.Yield();
        LogService.Write($"恢复默认：{ids.Count} 项，报告 {reportPath}");
        return ids.Count == 0 ? "没有可恢复的优化快照。" : $"已恢复 {ids.Count} 个优化项，报告已保存。";
    }

    private static int ReadyCount(List<OptimizerItem> items) => items.Count(IsInstalled);

    private static bool IsExeValid()
    {
        var exe = Path.Combine(AppPaths.OptimizerRuntime, "optimizerNXT.exe");
        return File.Exists(exe) && DownloadService.Sha256(exe) == OptimizerHash;
    }

    private static bool IsInstalled(OptimizerItem item)
    {
        var yaml = Path.Combine(AppPaths.OptimizerRuntime, item.YamlFile);
        var sig = yaml + ".sig";
        return IsExeValid() && File.Exists(yaml) && (!item.SigRequired || File.Exists(sig));
    }

    private static Snapshot CaptureSnapshot(OptimizerItem item)
    {
        var targets = ExtractSnapshotTargets(Path.Combine(AppPaths.OptimizerRuntime, item.YamlFile));
        var snapshot = new Snapshot { CapturedAt = DateTimeOffset.Now, Commands = targets.Commands };
        foreach (var reg in targets.Registry) snapshot.Registry.Add(CaptureRegistry(reg.Hive, reg.Path, reg.Name));
        foreach (var service in targets.Services) snapshot.Services.Add(CaptureService(service));
        foreach (var file in targets.Files) snapshot.Files.Add(CaptureFile(file));
        return snapshot;
    }

    private static SnapshotTargets ExtractSnapshotTargets(string yaml)
    {
        var targets = new SnapshotTargets();
        if (!File.Exists(yaml)) return targets;
        string? hive = null, path = null, mode = null;
        var inFiles = false;
        foreach (var raw in File.ReadLines(yaml, Encoding.UTF8))
        {
            var line = Regex.Replace(raw, @"\s+#.*$", "").Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var top = Regex.Match(line, @"^([A-Za-z0-9_.-]+):\s*$");
            if (top.Success && !raw.StartsWith(' ') && !raw.StartsWith('\t'))
            {
                var section = top.Groups[1].Value.ToLowerInvariant();
                inFiles = Regex.IsMatch(section, "^(files?|filesystem|deletefiles|removefiles|folders|directories)$");
                if (!section.StartsWith("registr")) { hive = null; path = null; mode = null; }
            }
            var hiveMatch = Regex.Match(line, @"^(?:-\s*)?hive:\s*(HK[A-Z]+)", RegexOptions.IgnoreCase);
            if (hiveMatch.Success) { hive = hiveMatch.Groups[1].Value.ToUpperInvariant(); path = null; mode = null; inFiles = false; continue; }
            if (hive != null && Regex.IsMatch(line, @"^path:\s*", RegexOptions.IgnoreCase)) { path = CleanYamlScalar(Regex.Replace(line, @"^path:\s*", "", RegexOptions.IgnoreCase)); continue; }
            var modeMatch = Regex.Match(line, @"^(add|set|values|delete|remove|removevalues|deletevalues):\s*$", RegexOptions.IgnoreCase);
            if (hive != null && path != null && modeMatch.Success) { mode = modeMatch.Groups[1].Value; continue; }
            if (hive != null && path != null && mode != null)
            {
                var key = Regex.Match(line, "^['\"]?([^'\":{}]+)['\"]?\\s*:\\s*\\{");
                var list = Regex.Match(line, "^-\\s*['\"]?([^'\"\\s]+)['\"]?\\s*$");
                var name = Regex.Match(line, @"^name:\s*(.+)$", RegexOptions.IgnoreCase);
                var valueName = key.Success ? key.Groups[1].Value : list.Success ? list.Groups[1].Value : name.Success ? name.Groups[1].Value : "";
                if (!string.IsNullOrWhiteSpace(valueName)) targets.Registry.Add(new RegistryTarget(hive, path, CleanYamlScalar(valueName)));
            }
            if (Regex.IsMatch(line, @"^(stop|disable|enable|demand|start|services):\s*$", RegexOptions.IgnoreCase)) { mode = "service"; continue; }
            if (mode == "service")
            {
                var service = Regex.Match(line, @"^(?:-\s*)?name:\s*([A-Za-z0-9_.-]+)$", RegexOptions.IgnoreCase);
                var simple = Regex.Match(line, @"^-\s*([A-Za-z0-9_.-]+)\s*$");
                var serviceName = service.Success ? service.Groups[1].Value : simple.Success ? simple.Groups[1].Value : "";
                if (!string.IsNullOrWhiteSpace(serviceName)) targets.Services.Add(serviceName);
            }
            if (Regex.IsMatch(line, @"^(files?|filesystem|deletefiles|removefiles|folders|directories):\s*$", RegexOptions.IgnoreCase)) { inFiles = true; continue; }
            if (inFiles)
            {
                var direct = Regex.Match(line, @"^-\s*(.+)$");
                var pair = Regex.Match(line, @"^(?:path|target):\s*(.+)$", RegexOptions.IgnoreCase);
                var filePath = direct.Success ? direct.Groups[1].Value : pair.Success ? pair.Groups[1].Value : "";
                filePath = CleanYamlScalar(filePath);
                if (!string.IsNullOrWhiteSpace(filePath) && (filePath.Contains('\\') || filePath.Contains('/') || filePath.Contains('%'))) targets.Files.Add(filePath);
            }
            if (Regex.IsMatch(line, @"-\s*.*\b(bcdedit|powercfg|netsh|schtasks)\b", RegexOptions.IgnoreCase))
                targets.Commands.Add(line.TrimStart('-', ' '));
        }
        targets.Registry = targets.Registry.DistinctBy(x => $"{x.Hive}\\{x.Path}\\{x.Name}".ToLowerInvariant()).ToList();
        targets.Services = targets.Services.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        targets.Files = targets.Files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        targets.Commands = targets.Commands.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return targets;
    }

    private static string CleanYamlScalar(string value) => value.Trim().Trim('"', '\'');

    private static RegistrySnapshot CaptureRegistry(string hive, string path, string name)
    {
        using var key = OpenKey(hive, path, false);
        var exists = key != null;
        object? value = null;
        var valueExists = false;
        var kind = "";
        try
        {
            value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            valueExists = value != null;
            kind = valueExists ? key!.GetValueKind(name).ToString() : "";
        }
        catch { }
        return value switch
        {
            byte[] bytes => new RegistrySnapshot(hive, path, name, exists, valueExists, kind, null, null, bytes, null),
            string[] arr => new RegistrySnapshot(hive, path, name, exists, valueExists, kind, null, arr, null, null),
            int i => new RegistrySnapshot(hive, path, name, exists, valueExists, kind, null, null, null, i),
            long l => new RegistrySnapshot(hive, path, name, exists, valueExists, kind, null, null, null, l),
            _ => new RegistrySnapshot(hive, path, name, exists, valueExists, kind, value?.ToString(), null, null, null)
        };
    }

    private static ServiceSnapshot CaptureService(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}", false);
        return new ServiceSnapshot(name, key != null, key?.GetValue("Start")?.ToString());
    }

    private static FileSnapshot CaptureFile(string pathText)
    {
        var expanded = Environment.ExpandEnvironmentVariables(pathText);
        if (!File.Exists(expanded) && !Directory.Exists(expanded)) return new FileSnapshot(pathText, expanded, false, "Missing", null, "");
        if (Directory.Exists(expanded)) return new FileSnapshot(pathText, expanded, true, "Directory", null, "");
        var info = new FileInfo(expanded);
        if (info.Length > 5 * 1024 * 1024) return new FileSnapshot(pathText, expanded, true, "File", null, "文件超过 5MB，仅记录存在状态。");
        return new FileSnapshot(pathText, expanded, true, "File", Convert.ToBase64String(File.ReadAllBytes(expanded)), "");
    }

    private static RestoreReport RestoreSnapshot(Snapshot snapshot)
    {
        var results = new List<RestoreResult>();
        foreach (var item in snapshot.Registry)
        {
            try
            {
                using var key = item.KeyExists ? (OpenKey(item.Hive, item.Path, true) ?? CreateKey(item.Hive, item.Path)) : OpenKey(item.Hive, item.Path, true);
                if (key != null)
                {
                    if (!item.ValueExists) key.DeleteValue(item.Name, false);
                    else key.SetValue(item.Name, RegistryValue(item), ParseKind(item.Kind));
                }
                results.Add(new RestoreResult("registry", $"{item.Hive}\\{item.Path}\\{item.Name}", true, ""));
            }
            catch (Exception ex) { results.Add(new RestoreResult("registry", item.Name, false, ex.Message)); }
        }
        foreach (var service in snapshot.Services)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service.Name}", true);
                if (key != null && service.Exists && int.TryParse(service.StartValue, out var start)) key.SetValue("Start", start, RegistryValueKind.DWord);
                results.Add(new RestoreResult("service", service.Name, true, ""));
            }
            catch (Exception ex) { results.Add(new RestoreResult("service", service.Name, false, ex.Message)); }
        }
        foreach (var file in snapshot.Files)
        {
            try
            {
                if (file.Exists && file.Type == "File" && !string.IsNullOrWhiteSpace(file.DataBase64))
                {
                    var parent = Path.GetDirectoryName(file.ExpandedPath);
                    if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                    File.WriteAllBytes(file.ExpandedPath, Convert.FromBase64String(file.DataBase64));
                }
                else if (file.Exists && file.Type == "Directory" && !Directory.Exists(file.ExpandedPath)) Directory.CreateDirectory(file.ExpandedPath);
                else if (!file.Exists)
                {
                    if (File.Exists(file.ExpandedPath)) File.Delete(file.ExpandedPath);
                    if (Directory.Exists(file.ExpandedPath)) Directory.Delete(file.ExpandedPath, true);
                }
                results.Add(new RestoreResult("file", file.ExpandedPath, true, ""));
            }
            catch (Exception ex) { results.Add(new RestoreResult("file", file.ExpandedPath, false, ex.Message)); }
        }
        foreach (var command in snapshot.Commands)
            results.Add(new RestoreResult("command", command, true, "命令型修改无法自动推导回退命令，已记录但未执行。"));
        return new RestoreReport(results.All(x => x.Ok), results);
    }

    private static object RegistryValue(RegistrySnapshot item)
    {
        if (item.BinaryValue != null) return item.BinaryValue;
        if (item.StringArray != null) return item.StringArray;
        if (item.IntegerValue != null) return item.Kind.Equals("QWord", StringComparison.OrdinalIgnoreCase) ? item.IntegerValue.Value : (int)item.IntegerValue.Value;
        return item.StringValue ?? "";
    }

    private static RegistryValueKind ParseKind(string kind) => Enum.TryParse<RegistryValueKind>(kind, out var parsed) ? parsed : RegistryValueKind.String;
    private static RegistryKey? OpenKey(string hive, string path, bool writable) => HiveRoot(hive)?.OpenSubKey(path, writable);
    private static RegistryKey? CreateKey(string hive, string path) => HiveRoot(hive)?.CreateSubKey(path, true);
    private static RegistryKey? HiveRoot(string hive) => hive.ToUpperInvariant() switch
    {
        "HKCU" => Registry.CurrentUser,
        "HKLM" => Registry.LocalMachine,
        "HKCR" => Registry.ClassesRoot,
        "HKU" => Registry.Users,
        "HKCC" => Registry.CurrentConfig,
        _ => null
    };

    private static OptimizerState ReadMutableState()
    {
        AppPaths.Ensure();
        if (!File.Exists(AppPaths.StatePath)) return new();
        try { return JsonSerializer.Deserialize<OptimizerState>(File.ReadAllText(AppPaths.StatePath, Encoding.UTF8), JsonOptions) ?? new(); }
        catch { return new(); }
    }

    private static void WriteState(OptimizerState state)
    {
        AppPaths.Ensure();
        File.WriteAllText(AppPaths.StatePath, JsonSerializer.Serialize(state, JsonOptions), Encoding.UTF8);
    }

    private static void BackupInputs()
    {
        AppPaths.Ensure();
        var dir = Path.Combine(AppPaths.BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(dir);
        foreach (var file in new[] { AppPaths.StatePath, AppPaths.SettingsPath })
            if (File.Exists(file)) File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), true);
        PruneInputBackups();
    }

    private static void PruneInputBackups()
    {
        try
        {
            if (!Directory.Exists(AppPaths.BackupRoot)) return;
            var dirs = Directory.GetDirectories(AppPaths.BackupRoot)
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(x => x.CreationTimeUtc)
                .Skip(MaxInputBackups)
                .ToList();
            foreach (var dir in dirs)
            {
                try { dir.Delete(true); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    LogService.Write($"旧备份清理跳过：{dir.FullName} - {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Write($"旧备份清理失败：{ex.Message}");
        }
    }

    private static void StopStaleOptimizerProcesses()
    {
        var expected = Path.GetFullPath(Path.Combine(AppPaths.OptimizerRuntime, "optimizerNXT.exe"));
        foreach (var process in Process.GetProcessesByName("optimizerNXT"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!Path.GetFullPath(path).Equals(expected, StringComparison.OrdinalIgnoreCase)) continue;
                    process.Kill(true);
                    if (!process.WaitForExit(5000))
                        LogService.Write($"OptimizerNXT 残留进程结束等待超时：{process.Id}");
                    else
                        LogService.Write($"已清理 OptimizerNXT 残留进程：{process.Id}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                {
                    LogService.Write($"OptimizerNXT 残留进程清理跳过：{process.Id} - {ex.Message}");
                }
            }
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(string file, string args, string cwd, CancellationToken token, TimeSpan timeout)
    {
        var output = new StringBuilder();
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logStart = DateTime.Now;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(timeout);

        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        void AppendLine(string? line)
        {
            if (line == null) return;
            lock (output) output.AppendLine(line);
            if (IsOptimizerSuccess(line) || IsOptimizerKnownError(line))
                marker.TrySetResult();
        }

        process.OutputDataReceived += (_, e) => AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(e.Data);

        if (!process.Start()) throw new InvalidOperationException("无法启动组件。");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;
        try
        {
            var exitTask = process.WaitForExitAsync(timeoutCts.Token);
            var completed = await Task.WhenAny(exitTask, marker.Task);
            if (completed == marker.Task && !process.HasExited)
            {
                await Task.Delay(1200, token);
                if (!process.HasExited)
                {
                    try { process.Kill(true); } catch { }
                }
            }
            else
            {
                await exitTask;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            timedOut = true;
            try { if (!process.HasExited) process.Kill(true); } catch { }
            var captured = output.ToString();
            LogService.Write($"OptimizerNXT 执行超时：{Path.GetFileName(file)} {args}\n{captured}");
        }

        try { if (process.HasExited) process.WaitForExit(); } catch { }
        var exitCode = process.HasExited ? process.ExitCode : 0;
        var logOutput = ReadOptimizerLogsSince(logStart);
        var combined = (output + Environment.NewLine + logOutput).Trim();
        var hadKnownError = IsOptimizerKnownError(combined);
        var succeeded = !timedOut && !hadKnownError && (exitCode == 0 || IsOptimizerSuccess(combined));
        return new ProcessResult(exitCode, combined, succeeded, timedOut, hadKnownError);
    }

    private static bool IsOptimizerSuccess(string text)
        => text.Contains("Finished executing YAML stage", StringComparison.OrdinalIgnoreCase)
           || text.Contains("Finished", StringComparison.OrdinalIgnoreCase)
           || text.Contains("Exiting.", StringComparison.OrdinalIgnoreCase)
           || text.Contains("Completed", StringComparison.OrdinalIgnoreCase)
           || text.Contains("[INFO]", StringComparison.OrdinalIgnoreCase) && text.Contains("Exiting", StringComparison.OrdinalIgnoreCase);

    private static bool IsOptimizerKnownError(string text)
        => text.Contains("Another instance", StringComparison.OrdinalIgnoreCase)
           || text.Contains("Console.ReadKey", StringComparison.OrdinalIgnoreCase)
           || text.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase);

    private static string ReadOptimizerLogsSince(DateTime startedAt)
    {
        try
        {
            var dir = Path.Combine(AppPaths.OptimizerRuntime, "optimizerNXT-logs");
            if (!Directory.Exists(dir)) return "";
            var builder = new StringBuilder();
            foreach (var file in Directory.GetFiles(dir, "*.log").Where(x => File.GetLastWriteTime(x) >= startedAt.AddSeconds(-2)).OrderBy(x => x))
            {
                builder.AppendLine($"===== {Path.GetFileName(file)} =====");
                builder.AppendLine(File.ReadAllText(file, Encoding.UTF8));
            }
            return builder.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static string TrimFailureOutput(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "OptimizerNXT 未返回可识别输出。";
        return text.Length > 3000 ? text[..3000] + "\n输出较长，已截断显示，完整内容请查看日志。" : text;
    }
}

public sealed class CleanerService
{
    public const string PinnedVersion = "v6.0.2";
    private const string CleanerUrl = "https://download.bleachbit.org/BleachBit-6.0.2-portable.zip";
    private const string CleanerHash = "0322920A592BA311CB374B4F44053EE35F793C5A5690F1F8CB419B57321B2883";
    private int _installing;
    private string _installMessage = "";

    public bool IsInstalling => _installing == 1;

    private static readonly List<CleanerItem> FallbackItems = new()
    {
        new("system.tmp", "system", "tmp", "系统 · 临时文件", "清理 Windows 与应用产生的临时文件。", "Windows", "normal", true),
        new("system.recycle_bin", "system", "recycle_bin", "系统 · 回收站", "清空回收站内容。", "Windows", "normal", true),
        new("windows_explorer.thumbnails", "windows_explorer", "thumbnails", "资源管理器 · 缩略图缓存", "清理缩略图缓存。", "资源管理器", "normal", true),
        new("microsoft_edge.cache", "microsoft_edge", "cache", "Edge · 缓存文件", "清理 Edge 网页缓存。", "浏览器", "normal", true),
        new("google_chrome.cache", "google_chrome", "cache", "Chrome · 缓存文件", "清理 Chrome 网页缓存。", "浏览器", "normal", true),
        new("firefox.cache", "firefox", "cache", "Firefox · 缓存文件", "清理 Firefox 网页缓存。", "浏览器", "normal", true)
    };

    public async Task<ComponentStatus> GetStatusAsync()
    {
        await Task.Yield();
        if (IsInstalling) return new ComponentStatus(ComponentState.Downloading, string.IsNullOrWhiteSpace(_installMessage) ? "获取远端服务中" : _installMessage, PinnedVersion, IsInstalled() ? 1 : 0, 1);
        return IsInstalled()
            ? new ComponentStatus(ComponentState.Online, $"BleachBit {PinnedVersion} 在线", PinnedVersion, 1, 1)
            : new ComponentStatus(ComponentState.Missing, "BleachBit 组件离线", PinnedVersion, 0, 1);
    }

    public bool IsInstalled() => File.Exists(ExePath);

    private static string ExePath => Directory.Exists(AppPaths.CleanerRuntime)
        ? Directory.GetFiles(AppPaths.CleanerRuntime, "bleachbit_console.exe", SearchOption.AllDirectories).FirstOrDefault() ?? Path.Combine(AppPaths.CleanerRuntime, "bleachbit_console.exe")
        : Path.Combine(AppPaths.CleanerRuntime, "bleachbit_console.exe");

    public async Task InstallAsync(CancellationToken token, Action<string>? status = null, bool force = false)
    {
        if (Interlocked.Exchange(ref _installing, 1) == 1) return;
        try
        {
            if (IsInstalled() && !force)
            {
                LogService.Write("BleachBit 组件已存在，跳过重复安装");
                return;
            }
            AppPaths.Ensure();
            using var dl = new DownloadService();
            Directory.CreateDirectory(AppPaths.Downloads);
            var zip = Path.Combine(AppPaths.Downloads, "BleachBit-6.0.2-portable.zip");
            if (force)
            {
                _installMessage = "正在替换 BleachBit 组件";
                status?.Invoke(_installMessage);
                if (Directory.Exists(AppPaths.CleanerRuntime) && !AppPaths.SafeDeleteDirectory(AppPaths.CleanerRuntime))
                    throw new InvalidOperationException("本地 BleachBit 组件正在被占用，无法重新获取。请关闭相关清理进程后重试，或在设置中开启“保留本地组件”。");
                try { if (File.Exists(zip)) File.Delete(zip); } catch { }
            }
            _installMessage = "正在下载 BleachBit portable 组件";
            status?.Invoke(_installMessage);
            await dl.DownloadFileAsync(CleanerUrl, zip, token);
            _installMessage = "正在校验 BleachBit portable 组件";
            status?.Invoke(_installMessage);
            if (DownloadService.Sha256(zip) != CleanerHash) throw new InvalidOperationException("BleachBit portable SHA256 校验失败");
            if (Directory.Exists(AppPaths.CleanerRuntime) && !AppPaths.SafeDeleteDirectory(AppPaths.CleanerRuntime))
                throw new InvalidOperationException("本地 BleachBit 组件正在被占用，无法重新获取。请关闭相关清理进程后重试，或在设置中开启“保留本地组件”。");
            Directory.CreateDirectory(AppPaths.CleanerRuntime);
            _installMessage = "正在解压 BleachBit 组件";
            status?.Invoke(_installMessage);
            ZipFile.ExtractToDirectory(zip, AppPaths.CleanerRuntime, true);
            if (!File.Exists(ExePath)) throw new InvalidOperationException("解压后未找到 bleachbit_console.exe");
            File.WriteAllText(Path.Combine(AppPaths.CleanerRuntime, "version.json"), JsonSerializer.Serialize(new { version = PinnedVersion, installedAt = DateTimeOffset.Now, downloadUrl = CleanerUrl }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            LogService.Write("BleachBit 组件安装完成");
        }
        finally
        {
            _installMessage = "";
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    public async Task<List<CleanerItem>> GetItemsAsync()
    {
        await Task.Yield();
        if (!IsInstalled()) return new List<CleanerItem>();
        var root = CleanerRoot();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return FallbackItems;

        var items = new List<CleanerItem>();
        foreach (var file in Directory.GetFiles(root, "*.xml", SearchOption.TopDirectoryOnly).OrderBy(x => x))
        {
            try
            {
                var doc = XDocument.Load(file);
                var cleaner = doc.Root;
                if (cleaner == null || cleaner.Name.LocalName != "cleaner") continue;
                var cleanerId = cleaner.Attribute("id")?.Value ?? Path.GetFileNameWithoutExtension(file);
                var cleanerLabel = TextOf(cleaner.Element("label"), CleanerDisplayName(cleanerId));
                var cleanerDescription = TextOf(cleaner.Element("description"), "");
                var group = CleanerGroup(cleanerId, cleanerLabel);

                foreach (var option in cleaner.Elements("option"))
                {
                    var optionId = option.Attribute("id")?.Value;
                    if (string.IsNullOrWhiteSpace(optionId)) continue;
                    var id = $"{cleanerId}.{optionId}";
                    var optionLabel = TextOf(option.Element("label"), optionId);
                    var description = TextOf(option.Element("description"), cleanerDescription);
                    var risk = CleanerRisk(cleanerId, optionId, optionLabel, description);
                    items.Add(new CleanerItem(
                        id,
                        cleanerId,
                        optionId,
                        $"{cleanerLabel} · {CleanerOptionName(optionId, optionLabel)}",
                        CleanerDescription(cleanerLabel, optionId, optionLabel, description),
                        group,
                        risk,
                        risk != "high" && DefaultCleanerOption(optionId, optionLabel)));
                }
            }
            catch (Exception ex)
            {
                LogService.Write($"清理项解析失败：{file} - {ex.Message}");
            }
        }

        return items.Count > 0 ? items : FallbackItems;
    }

    public async Task<CleanerRunResult> RunAsync(string mode, IEnumerable<string> cleanerIds, CancellationToken token)
    {
        if (!IsInstalled()) throw new InvalidOperationException("BleachBit 组件未安装。");
        var ids = cleanerIds.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (ids.Count == 0) throw new InvalidOperationException("请选择至少一个清理项目。");
        var psi = new ProcessStartInfo(ExePath, $"{mode} {string.Join(" ", ids)}") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 BleachBit。");
        var output = await process.StandardOutput.ReadToEndAsync(token);
        var error = await process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var result = (output + Environment.NewLine + error).Trim();
        LogService.Write($"BleachBit {mode}：{string.Join(",", ids)}");
        result = string.IsNullOrWhiteSpace(result) ? "操作完成。" : result;
        return new CleanerRunResult(result, ParseCleanerSummary(mode, ids.Count, result));
    }

    private static string? CleanerRoot()
    {
        if (!Directory.Exists(AppPaths.CleanerRuntime)) return null;
        return Directory.GetDirectories(AppPaths.CleanerRuntime, "cleaners", SearchOption.AllDirectories)
            .FirstOrDefault(x => x.Contains($"{Path.DirectorySeparatorChar}share{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            ?? Directory.GetDirectories(AppPaths.CleanerRuntime, "cleaners", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string TextOf(XElement? element, string fallback)
        => string.IsNullOrWhiteSpace(element?.Value) ? fallback : element!.Value.Trim();

    private static string CleanerDisplayName(string cleanerId) => cleanerId switch
    {
        "adobe_reader" => "Adobe Reader",
        "google_chrome" => "Chrome",
        "microsoft_edge" => "Edge",
        "windows_explorer" => "资源管理器",
        "windows_defender" => "Defender",
        "system" => "系统",
        _ => CultureName(cleanerId)
    };

    private static string CleanerOptionName(string optionId, string label)
    {
        var lower = $"{optionId} {label}".ToLowerInvariant();
        if (lower.Contains("session")) return "会话数据";
        if (lower.Contains("form")) return "表单记录";
        if (lower.Contains("crash")) return "崩溃报告";
        if (lower.Contains("memory dump")) return "内存转储";
        if (lower.Contains("cache")) return "缓存文件";
        if (lower.Contains("cookie")) return "Cookie";
        if (lower.Contains("history")) return "历史记录";
        if (lower.Contains("mru") || lower.Contains("recent")) return "最近项目";
        if (lower.Contains("tmp") || lower.Contains("temp")) return "临时文件";
        if (lower.Contains("password")) return "密码";
        if (lower.Contains("backup")) return "备份文件";
        if (lower.Contains("log")) return "日志文件";
        if (lower.Contains("thumbnail")) return "缩略图缓存";
        if (lower.Contains("free disk")) return "擦除空闲空间";
        return string.IsNullOrWhiteSpace(label) ? CultureName(optionId) : label;
    }

    private static string CleanerDescription(string cleanerLabel, string optionId, string optionLabel, string description)
    {
        var optionName = CleanerOptionName(optionId, optionLabel);
        var text = $"{optionId} {optionLabel} {description}".ToLowerInvariant();
        var effect = optionName switch
        {
            "Cookie" => "会清除网站保存的登录和偏好信息，可能需要重新登录。",
            "历史记录" => "会删除访问记录和使用痕迹，适合需要保护隐私时使用。",
            "密码" => "会删除已保存的密码或凭据，使用前请确认已经备份。",
            "会话数据" => "会清除应用或浏览器的会话状态，可能关闭自动恢复的标签页。",
            "表单记录" => "会删除自动填充表单和输入记录，减少隐私痕迹。",
            "擦除空闲空间" => "会覆盖磁盘空闲区域以降低恢复概率，耗时较长且不建议频繁执行。",
            "缩略图缓存" => "会清理图片和文件预览缓存，系统会在需要时重新生成。",
            "日志文件" => "会删除运行日志和诊断记录，通常可释放少量空间。",
            "临时文件" => "会删除应用运行留下的临时文件，通常可以安全释放磁盘空间。",
            "缓存文件" => "会删除可重新生成的缓存内容，通常可以安全释放磁盘空间。",
            "备份文件" => "会删除应用生成的旧备份或残留文件，执行前请确认不再需要。",
            "崩溃报告" => "会删除崩溃诊断报告，适合不再排查问题时清理。",
            "内存转储" => "会删除蓝屏或崩溃转储文件，可释放较大空间但会影响后续故障分析。",
            _ => text.Contains("download") ? "会清理下载或更新残留内容，执行前请确认没有需要保留的文件。" : "会清理该项目产生的无用数据，用于释放磁盘空间。"
        };
        return $"{cleanerLabel} 的 {optionName}，{effect}";
    }

    private static CleanerRunSummary ParseCleanerSummary(string mode, int selectedItems, string output)
    {
        var files = MatchNumber(output, @"(?:Files\s+(?:to\s+be\s+deleted|deleted)|Deleted\s+files|文件(?:数|数量)?)\s*[:：]\s*([0-9,]+)");
        if (files == 0)
            files = Regex.Matches(output, @"(?im)^\s*(?:delete|remove|unlink|would\s+delete)\s+", RegexOptions.IgnoreCase).Count;

        var spaceText = MatchText(output, @"(?:Disk\s+space\s+(?:to\s+be\s+recovered|recovered)|Recovered\s+disk\s+space|Freed\s+disk\s+space|释放(?:的)?(?:磁盘)?空间)\s*[:：]\s*([^\r\n]+)");
        var bytes = ParseSizeToBytes(spaceText);
        if (string.IsNullOrWhiteSpace(spaceText)) spaceText = bytes > 0 ? FormatBytes(bytes) : "暂未从输出中识别";

        var preview = string.IsNullOrWhiteSpace(output) ? "无详细输出。" : output;
        if (preview.Length > 1200) preview = preview[..1200] + "\n输出较长，已截断显示，完整内容请查看日志。";
        var topPaths = ExtractCleanerPaths(output).OrderByDescending(x => x.Bytes).ThenBy(x => x.Path).Take(5).ToList();
        return new CleanerRunSummary(mode, selectedItems, files, bytes, spaceText.Trim(), preview, false, topPaths);
    }

    private static List<CleanerPathInfo> ExtractCleanerPaths(string output)
    {
        var results = new Dictionary<string, CleanerPathInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (!Regex.IsMatch(line, @"(?i)\b(delete|deleted|remove|removed|unlink|would\s+delete)\b")) continue;

            var path = ExtractPathFromCleanerLine(line);
            var bytes = ParseLineSize(line);
            var display = string.IsNullOrWhiteSpace(path) ? CleanerOutputItemName(line) : path;
            if (string.IsNullOrWhiteSpace(display) || results.ContainsKey(display)) continue;
            if (bytes <= 0 && !string.IsNullOrWhiteSpace(path)) bytes = TryGetPathSize(path);
            results[display] = new CleanerPathInfo(display, bytes > 0 ? FormatBytes(bytes) : "大小未识别", bytes);
        }
        return results.Values.ToList();
    }

    private static string CleanerOutputItemName(string line)
    {
        var item = Regex.Replace(line, @"(?i)^\s*(would\s+delete|delete(?:d)?|remove(?:d)?|unlink)\s*[:：-]?\s*", "").Trim();
        item = Regex.Replace(item, @"\s+", " ");
        return item.Length > 160 ? item[..160] + "..." : item;
    }

    private static string ExtractPathFromCleanerLine(string line)
    {
        var quoted = Regex.Match(line, "\"([^\"]+)\"");
        if (quoted.Success) return quoted.Groups[1].Value.Trim();

        var drive = Regex.Match(line, @"([A-Za-z]:\\.+)$");
        if (drive.Success) return drive.Groups[1].Value.Trim();

        var slash = Regex.Match(line, @"((?:/|\\\\)[^\r\n]+)$");
        return slash.Success ? slash.Groups[1].Value.Trim() : "";
    }

    private static long ParseLineSize(string line)
    {
        var matches = Regex.Matches(line.Replace(",", ""), @"([0-9]+(?:\.[0-9]+)?)\s*([KMGT]?i?B|bytes?|KB|MB|GB|TB)", RegexOptions.IgnoreCase);
        return matches.Count == 0 ? 0 : ParseSizeToBytes(matches[^1].Value);
    }

    private static long TryGetPathSize(string path)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            if (File.Exists(expanded)) return new FileInfo(expanded).Length;
            if (Directory.Exists(expanded))
                return Directory.EnumerateFiles(expanded, "*", SearchOption.AllDirectories).Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch { return 0L; }
                });
        }
        catch { }
        return 0;
    }

    private static int MatchNumber(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value.Replace(",", ""), out var value) ? value : 0;
    }

    private static string MatchText(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static long ParseSizeToBytes(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var match = Regex.Match(text.Replace(",", ""), @"([0-9]+(?:\.[0-9]+)?)\s*([KMGT]?i?B|bytes?|字节|KB|MB|GB|TB)?", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, out var value)) return 0;
        var unit = match.Groups[2].Value.ToUpperInvariant();
        var factor = unit switch
        {
            "KB" or "KIB" => 1024d,
            "MB" or "MIB" => 1024d * 1024,
            "GB" or "GIB" => 1024d * 1024 * 1024,
            "TB" or "TIB" => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };
        return (long)(value * factor);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.##} {units[index]}";
    }

    private static string CleanerGroup(string cleanerId, string label)
    {
        var id = cleanerId.ToLowerInvariant();
        if (id is "system" or "windows_explorer" or "windows_defender" or "internet_explorer") return "Windows";
        if (id.Contains("chrome") || id.Contains("edge") || id.Contains("firefox") || id.Contains("browser") || id.Contains("chromium") || id.Contains("brave") || id.Contains("waterfox") || id.Contains("librewolf")) return "浏览器";
        if (id.Contains("office") || id.Contains("adobe") || id.Contains("vscode") || id.Contains("zoom") || id.Contains("discord") || id.Contains("slack")) return "应用缓存";
        return "应用缓存";
    }

    private static string CleanerRisk(string cleanerId, string optionId, string label, string description)
    {
        var text = $"{cleanerId} {optionId} {label} {description}".ToLowerInvariant();
        return text.Contains("password") || text.Contains("cookie") || text.Contains("history") || text.Contains("form") || text.Contains("session") || text.Contains("free_disk_space") || text.Contains("free disk") ? "high" : "normal";
    }

    private static bool DefaultCleanerOption(string optionId, string label)
    {
        var text = $"{optionId} {label}".ToLowerInvariant();
        return text.Contains("cache") || text.Contains("tmp") || text.Contains("temp") || text.Contains("thumbnail") || text.Contains("log") || text.Contains("backup") || text.Contains("recycle");
    }

    private static string CultureName(string id)
        => string.Join(" ", id.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Length == 0 ? x : char.ToUpperInvariant(x[0]) + x[1..]));
}

public sealed class ToolboxService
{
    public async Task<List<ToolItem>> GetToolsAsync()
    {
        await Task.Yield();
        using var doc = JsonUtil.ReadDocument(AppPaths.DataFile("tools-manifest.json"));
        var result = new List<ToolItem>();
        foreach (var prop in doc.RootElement.GetProperty("tools").EnumerateObject())
        {
            var e = prop.Value;
            result.Add(new ToolItem(prop.Name, ToolName(prop.Name), e.S("description", ToolDescription(prop.Name)), e.S("assetName"), e.S("downloadUrl"), e.S("sha256"), Dangerous(prop.Name), ReadAliases(e)));
        }
        return result.OrderBy(x => ToolOrder(x.Id)).ToList();
    }

    public bool IsReady(ToolItem item)
    {
        var path = ResolveLocalPath(item);
        return File.Exists(path) && (string.IsNullOrWhiteSpace(item.Sha256) || DownloadService.Sha256(path).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> DownloadAndLaunchAsync(ToolItem item, CancellationToken token)
    {
        Directory.CreateDirectory(AppPaths.ToolRuntime);
        if (!IsReady(item))
        {
            if (string.IsNullOrWhiteSpace(item.DownloadUrl)) throw new InvalidOperationException("该工具未配置远端下载地址。");
            using var dl = new DownloadService();
            var target = StandardLocalPath(item);
            await dl.DownloadFileAsync(item.DownloadUrl, target, token);
            if (!string.IsNullOrWhiteSpace(item.Sha256) && !DownloadService.Sha256(target).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                throw new InvalidOperationException("工具 SHA256 校验失败。");
            }
        }
        var localPath = ResolveLocalPath(item);
        if (!File.Exists(localPath)) throw new InvalidOperationException("工具文件未找到，请重新获取或手动解压到本地组件目录。");
        Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true, WorkingDirectory = AppPaths.ToolRuntime });
        LogService.Write($"启动工具：{item.Name}");
        return $"已启动：{item.Name}";
    }

    private static List<string> ReadAliases(JsonElement element)
    {
        if (!element.TryGetProperty("aliases", out var aliases) || aliases.ValueKind != JsonValueKind.Array) return new List<string>();
        return aliases.EnumerateArray().Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ResolveLocalPath(ToolItem item)
    {
        foreach (var name in CandidateNames(item))
        {
            var path = Path.Combine(AppPaths.ToolRuntime, name);
            if (!File.Exists(path)) continue;
            if (string.IsNullOrWhiteSpace(item.Sha256) || DownloadService.Sha256(path).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return StandardLocalPath(item);
    }

    private static IEnumerable<string> CandidateNames(ToolItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.AssetName)) yield return item.AssetName;
        yield return item.Id;
        foreach (var alias in item.Aliases)
            if (!string.IsNullOrWhiteSpace(alias)) yield return alias;
    }

    private static string StandardLocalPath(ToolItem item) => Path.Combine(AppPaths.ToolRuntime, string.IsNullOrWhiteSpace(item.AssetName) ? item.Id : item.AssetName);
    private static bool Dangerous(string id) => id.Contains("activation", StringComparison.OrdinalIgnoreCase) || id.Contains("disable", StringComparison.OrdinalIgnoreCase) || id.Contains("visualFix", StringComparison.OrdinalIgnoreCase) || id.Contains("Hijack", StringComparison.OrdinalIgnoreCase);
    private static int ToolOrder(string id) => id switch
    {
        "activation" => 30,
        "oneKeyActivate" => 31,
        "sevenZip" => 80,
        "archiveTool" => 81,
        "dxRepair" => 10,
        "runtime" => 20,
        "driverToolVip" => 40,
        "disableUac" => 50,
        "disableSecurityUpdate" => 51,
        "disableCoreIsolation" => 52,
        "geekUninstaller" => 70,
        "browserHijackClean" => 90,
        "visualFix" => 100,
        _ => 999
    };
    private static string ToolName(string id) => id switch
    {
        "dxRepair" => "DX 修复",
        "runtime" => "微软运行库",
        "activation" => "Windows 激活工具",
        "disableUac" => "关闭 UAC",
        "disableSecurityUpdate" => "关闭系统更新",
        "driverToolVip" => "驱动总裁 VIP",
        "disableCoreIsolation" => "关闭内核隔离",
        "sevenZip" => "7-Zip",
        "geekUninstaller" => "Geek 卸载工具",
        "browserHijackClean" => "浏览器劫持清理",
        "oneKeyActivate" => "命令行工具激活 Windows",
        "archiveTool" => "rar备用解压工具",
        "visualFix" => "Win11 视觉修复",
        _ => id
    };
    private static string ToolDescription(string id) => id switch
    {
        "runtime" => "安装常用运行环境，建议装机后使用。",
        "driverToolVip" => "辅助安装常用硬件驱动。",
        "browserHijackClean" => "清理重装系统后的浏览器劫持项。",
        "visualFix" => "尝试修复 Windows 11 视觉显示异常。",
        _ => "从固定 GitHub 工具仓库按需下载并启动。"
    };
}

public sealed class DriverService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public void ClearCache()
    {
        try { if (File.Exists(AppPaths.DriverCachePath)) File.Delete(AppPaths.DriverCachePath); } catch { }
    }

    public async Task<DriverPageData> GetPageDataAsync(bool forceRefresh = false)
    {
        AppPaths.Ensure();
        if (!forceRefresh && File.Exists(AppPaths.DriverCachePath))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<DriverPageData>(File.ReadAllText(AppPaths.DriverCachePath, Encoding.UTF8), JsonOptions);
                if (cached != null && DateTimeOffset.Now - cached.CachedAt < TimeSpan.FromHours(12))
                    return cached;
            }
            catch { }
        }

        var data = new DriverPageData(await DetectAsync(), await GetDriverStatusAsync(), DateTimeOffset.Now);
        File.WriteAllText(AppPaths.DriverCachePath, JsonSerializer.Serialize(data, JsonOptions), Encoding.UTF8);
        return data;
    }

    public async Task<DeviceMatchInfo> DetectAsync()
    {
        using var device = await PowerShellJsonAsync("""
$cs = Get-CimInstance Win32_ComputerSystem
$bios = Get-CimInstance Win32_BIOS
[PSCustomObject]@{ Manufacturer=$cs.Manufacturer; Model=$cs.Model; SerialNumber=$bios.SerialNumber } | ConvertTo-Json -Compress
""");
        var manufacturer = device.RootElement.S("Manufacturer", "未知厂商");
        var model = device.RootElement.S("Model", "未知型号");
        var serial = device.RootElement.S("SerialNumber", "未知 SN");
        using var centers = JsonUtil.ReadDocument(AppPaths.DataFile("driver-centers.json"));
        foreach (var brand in centers.RootElement.EnumerateObject())
        {
            var aliases = brand.Value.TryGetProperty("aliases", out var a) ? a.EnumerateArray().Select(x => x.ToString()).ToList() : new();
            var brandMatched = aliases.Any(x => manufacturer.Contains(x, StringComparison.OrdinalIgnoreCase)) || manufacturer.Contains(brand.Name, StringComparison.OrdinalIgnoreCase);
            if (!brandMatched) continue;
            var support = brand.Value.S("supportPage");
            var queryPage = SupportQueryUrl(brand.Name, support, serial, model);
            if (brand.Value.TryGetProperty("models", out var models))
            {
                foreach (var candidate in models.EnumerateArray())
                {
                    var keywords = candidate.TryGetProperty("matchKeywords", out var ks) ? ks.EnumerateArray().Select(x => x.ToString()) : Enumerable.Empty<string>();
                    if (keywords.Any(k => model.Contains(k, StringComparison.OrdinalIgnoreCase) || serial.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new DeviceMatchInfo(manufacturer, model, serial, candidate.S("driverCenterName", brand.Value.S("brandName", brand.Name)), candidate.S("officialPage", queryPage), candidate.S("downloadUrl"), candidate.S("fileName"), true);
                    }
                }
            }
            return new DeviceMatchInfo(manufacturer, model, serial, brand.Value.S("brandName", brand.Name), queryPage, "", "", true);
        }
        return new DeviceMatchInfo(manufacturer, model, serial, "未匹配到本地厂商库，请手动打开厂商官网。", "", "", "", false);
    }

    public async Task<string> DownloadDriverPackageAsync(DeviceMatchInfo info, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl)) throw new InvalidOperationException("该匹配项未提供可直接下载的驱动包。");
        Directory.CreateDirectory(AppPaths.Downloads);
        var name = string.IsNullOrWhiteSpace(info.FileName) ? Path.GetFileName(new Uri(info.DownloadUrl).AbsolutePath) : info.FileName;
        var target = Path.Combine(AppPaths.Downloads, name);
        using var dl = new DownloadService();
        await dl.DownloadFileAsync(info.DownloadUrl, target, token);
        LogService.Write($"下载驱动包：{target}");
        return target;
    }

    public async Task<List<DriverStatusItem>> GetDriverStatusAsync()
    {
        using var doc = await PowerShellJsonAsync("""
$signed = @{}
Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceID } | ForEach-Object { $signed[$_.DeviceID] = $_ }
Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -and $_.PNPClass -in @('Display','MEDIA','Net','Bluetooth','System') } | Select-Object -First 160 | ForEach-Object {
  $d = $signed[$_.DeviceID]
  [PSCustomObject]@{
    DeviceName=$_.Name
    DriverVersion=if($d){$d.DriverVersion}else{''}
    Manufacturer=if($d){$d.Manufacturer}else{$_.Manufacturer}
    DeviceClass=$_.PNPClass
    ErrorCode=$_.ConfigManagerErrorCode
  }
} | ConvertTo-Json -Compress
""");
        var list = new List<DriverStatusItem>();
        var root = doc.RootElement;
        var elements = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : root.ValueKind == JsonValueKind.Object ? new List<JsonElement> { root } : new();
        foreach (var e in elements)
        {
            var cls = e.S("DeviceClass");
            var className = DriverClassName(cls);
            var code = e.TryGetProperty("ErrorCode", out var c) && c.TryGetInt32(out var parsed) ? parsed : 0;
            var version = e.S("DriverVersion");
            var status = code == 0 && !string.IsNullOrWhiteSpace(version)
                ? "设备存在且驱动反馈正常"
                : code == 28 || string.IsNullOrWhiteSpace(version)
                    ? "驱动缺失或未正确安装"
                    : $"设备状态异常，错误代码 {code}";
            list.Add(new DriverStatusItem(e.S("DeviceName"), $"{className} · {e.S("Manufacturer")} · {(string.IsNullOrWhiteSpace(version) ? "未识别版本" : version)}", status));
        }
        return list.Take(36).ToList();
    }

    private static string DriverClassName(string cls) => cls.ToUpperInvariant() switch
    {
        "DISPLAY" => "显卡",
        "MEDIA" => "音频设备",
        "NET" => "网络设备",
        "BLUETOOTH" => "蓝牙设备",
        "SYSTEM" => "系统设备",
        _ => string.IsNullOrWhiteSpace(cls) ? "未知设备" : cls
    };

    private static string SupportQueryUrl(string brand, string fallback, string serial, string model)
    {
        var sn = Uri.EscapeDataString(string.IsNullOrWhiteSpace(serial) || serial.Contains("未知", StringComparison.OrdinalIgnoreCase) ? model : serial);
        var query = string.IsNullOrWhiteSpace(sn) ? "" : sn;
        return brand.ToLowerInvariant() switch
        {
            "dell" when !string.IsNullOrWhiteSpace(query) => $"https://www.dell.com/support/home/product-support/servicetag/{query}/drivers",
            "lenovo" when !string.IsNullOrWhiteSpace(query) => $"https://pcsupport.lenovo.com/us/en/search?query={query}",
            "hp" when !string.IsNullOrWhiteSpace(query) => $"https://support.hp.com/us-en/search?q={query}",
            "asus" when !string.IsNullOrWhiteSpace(query) => $"https://www.asus.com/supportonly/{Uri.EscapeDataString(model)}/helpdesk_download/",
            "acer" when !string.IsNullOrWhiteSpace(query) => $"https://www.acer.com/support/drivers-and-manuals?search={query}",
            _ => fallback
        };
    }

    private static async Task<JsonDocument> PowerShellJsonAsync(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(encoded);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 PowerShell。");
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(output) ? "{}" : output);
    }
}

public sealed record ProcessResult(int ExitCode, string Output, bool Succeeded, bool TimedOut, bool HadKnownInstanceError);
public sealed record RegistryTarget(string Hive, string Path, string Name);
public sealed class SnapshotTargets
{
    public List<RegistryTarget> Registry { get; set; } = new();
    public List<string> Services { get; set; } = new();
    public List<string> Files { get; set; } = new();
    public List<string> Commands { get; set; } = new();
}
public sealed record RegistrySnapshot(string Hive, string Path, string Name, bool KeyExists, bool ValueExists, string Kind, string? StringValue, string[]? StringArray, byte[]? BinaryValue, long? IntegerValue);
public sealed record ServiceSnapshot(string Name, bool Exists, string? StartValue);
public sealed record FileSnapshot(string Path, string ExpandedPath, bool Exists, string Type, string? DataBase64, string Note);
public sealed record RestoreResult(string Type, string Target, bool Ok, string Message);
public sealed record RestoreReport(bool Ok, List<RestoreResult> Results);
public sealed class Snapshot
{
    public DateTimeOffset CapturedAt { get; set; }
    public List<RegistrySnapshot> Registry { get; set; } = new();
    public List<ServiceSnapshot> Services { get; set; } = new();
    public List<FileSnapshot> Files { get; set; } = new();
    public List<string> Commands { get; set; } = new();
}
public sealed record AppliedEntry(string Id, string Title, string YamlFile, Snapshot? Snapshot, DateTimeOffset? AppliedAt);
public sealed class OptimizerState { public Dictionary<string, AppliedEntry> Applied { get; set; } = new(); }
