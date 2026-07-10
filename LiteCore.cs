using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;

namespace SystemOptimizerLite;

public enum ComponentState { Missing, Downloading, Online, Error }
public enum OptimizationLiveState { Default, Optimized, Mixed, ExternallyManaged, RestartRequired, Unsupported, Unknown }
public enum OptimizationTargetState { Default, Optimized, Diverged, Unavailable, ExternallyManaged, Unknown }
public enum RestoreItemStatus { Restored, AlreadyDefault, Partial, Skipped, Failed, RestartRequired }
public enum IdentityConfidence { High, Medium, Low }

public record CategoryInfo(string Name, string Description);
public record OptimizerItem(string Id, string Title, string Description, string Tab, string Group, string Risk, string YamlFile, bool SigRequired, string Icon, string Windows, string Category, bool DefaultChecked, bool EnabledByDefault, int Order);
public record CleanerItem(string Id, string CleanerId, string OptionId, string Title, string Description, string Group, string Risk, bool DefaultSelected);
public record CleanerPathInfo(string Path, string SizeText, long Bytes);
public record CleanerRunSummary(string Mode, int SelectedItems, int FileCount, long Bytes, string SpaceText, string OutputPreview, bool IsLoading, List<CleanerPathInfo> TopPaths);
public record CleanerRunResult(string Output, CleanerRunSummary Summary);
public record ToolItem(string Id, string Name, string Description, string AssetName, string DownloadUrl, string Sha256, bool Dangerous, List<string> Aliases);
public record DriverStatusItem(string Name, string Detail, string Status);
public record OptimizationContract(string Id, string YamlSha256, string SigSha256);
public record OptimizationTargetInfo(string Kind, string Target, OptimizationTargetState State, string Detail);
public record OptimizationStateInfo(string Id, OptimizationLiveState State, string Detail, int MatchedTargets, int TotalTargets, List<OptimizationTargetInfo>? Targets = null)
{
    public IReadOnlyList<OptimizationTargetInfo> TargetDetails => Targets ?? [];
}
public record RestorePlanItem(string Id, string Title, OptimizationLiveState CurrentState, string Detail, bool WillChange, bool RequiresRestart);
public record RestorePlan(List<RestorePlanItem> Items, DateTimeOffset CreatedAt)
{
    public int ChangeCount => Items.Count(x => x.WillChange);
}
public record RestoreItemResult(string Id, string Title, RestoreItemStatus Status, string Detail);
public record RestoreExecutionReport(List<RestoreItemResult> Items, DateTimeOffset CompletedAt, string ReportPath)
{
    public bool Ok => Items.All(x => x.Status is RestoreItemStatus.Restored or RestoreItemStatus.AlreadyDefault or RestoreItemStatus.Skipped or RestoreItemStatus.RestartRequired);
}
public record IdentityCandidate(string Kind, string Source, string Label, string Value, string MaskedValue, bool Valid, string Note);
public record DeviceIdentityAssessment(IdentityConfidence Confidence, string Summary, List<IdentityCandidate> Candidates, List<string> Conflicts, bool IsVirtualMachine, string StableDigest);
public record DeviceIdentity(
    string BiosManufacturer,
    string BiosSerialNumber,
    string ComputerManufacturer,
    string ComputerModel,
    string SystemSkuNumber,
    string ProductVendor,
    string ProductName,
    string ProductIdentifyingNumber,
    string ProductSkuNumber,
    string ProductUuid,
    string BaseBoardManufacturer,
    string BaseBoardProduct,
    string BaseBoardSerialNumber,
    string ChassisSerialNumber);
public record DeviceDisplayIdentifier(string Label, string Value);
public record DeviceMatchInfo(
    string Manufacturer,
    string Model,
    string SerialNumber,
    string MatchSummary,
    string SupportPage,
    string DownloadUrl,
    string FileName,
    bool MatchSuccess,
    string VendorDisplayName,
    string DeviceIdentifierLabel,
    string DeviceIdentifierValue,
    bool CanCopyIdentifier,
    string Sha256 = "",
    string[]? OfficialDomains = null,
    string VerifiedAt = "");
public record DriverPageData(DeviceMatchInfo Device, DeviceIdentityAssessment Identity, List<DriverStatusItem> Drivers, DateTimeOffset CachedAt);
public record ComponentStatus(ComponentState State, string Text, string Version, int ReadyCount, int TotalCount, string Error = "");

public static class DeviceDisplayIdentifierExtensions
{
    public static bool CanCopy(this DeviceDisplayIdentifier identifier)
        => !string.IsNullOrWhiteSpace(identifier.Value) && !identifier.Value.Equals("未能读取", StringComparison.OrdinalIgnoreCase);
}

public static class DeviceIdentifierResolver
{
    private const string UnknownValue = "未能读取";

    public static DeviceDisplayIdentifier Resolve(DeviceIdentity identity)
    {
        var vendor = ResolveVendor(identity);
        return vendor switch
        {
            "dell" => Pick("Service Tag", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel),
            "lenovo" => Pick("主机编号", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel),
            "hp" => Pick("序列号", identity.BiosSerialNumber, identity.ProductSkuNumber, identity.SystemSkuNumber, identity.ComputerModel),
            "asus" => Pick("产品型号", identity.ComputerModel, identity.ProductName, identity.BaseBoardProduct, identity.BiosSerialNumber),
            "acer" => Pick("SN / SNID", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel),
            "msi" => Pick("产品型号", identity.ComputerModel, identity.ProductName, identity.BaseBoardProduct, identity.BiosSerialNumber),
            "gigabyte" => Pick("产品型号", identity.BaseBoardProduct, identity.ComputerModel, identity.ProductName, identity.BiosSerialNumber),
            "surface" => Pick("Surface型号", identity.ComputerModel, identity.ProductName),
            "huawei" => Pick("序列号", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel),
            "honor" => Pick("SN号", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel),
            "xiaomi" => Pick("SN号", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel),
            "mechrevo" => Pick("SN序列号", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel),
            "machenike" => Pick("SN序列号", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel),
            "thunderobot" => Pick("产品型号", identity.ComputerModel, identity.ProductName, identity.BiosSerialNumber),
            "hasee" => Pick("SN序列号", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel),
            "dynabook" => Pick("产品型号", identity.ComputerModel, identity.ProductName, identity.BiosSerialNumber),
            "mainboard" => Pick("主板型号", identity.BaseBoardProduct, identity.ComputerModel, identity.ProductName),
            _ => Pick("识别码", identity.BiosSerialNumber, identity.ProductIdentifyingNumber, identity.ComputerModel, identity.ProductName, identity.BaseBoardProduct)
        };
    }

    public static string ResolveVendorDisplay(DeviceIdentity identity)
    {
        return ResolveVendor(identity) switch
        {
            "dell" => "Dell / Alienware",
            "lenovo" => "Lenovo / ThinkPad",
            "hp" => "HP / OMEN",
            "asus" => "ASUS / ROG",
            "acer" => "Acer",
            "msi" => "MSI",
            "gigabyte" => "GIGABYTE / AORUS",
            "surface" => "Microsoft Surface",
            "huawei" => "HUAWEI MateBook",
            "honor" => "HONOR MagicBook",
            "xiaomi" => "Xiaomi / RedmiBook",
            "mechrevo" => "机械革命",
            "machenike" => "机械师",
            "thunderobot" => "雷神 Thunderobot",
            "hasee" => "神舟 Hasee",
            "dynabook" => "Dynabook / Toshiba",
            "mainboard" => $"{Clean(identity.BaseBoardManufacturer)} 主板",
            _ => "暂无信息"
        };
    }

    public static DeviceIdentityAssessment Assess(DeviceIdentity identity)
    {
        var candidates = new List<IdentityCandidate>
        {
            Candidate("serial", "Win32_BIOS", "BIOS 序列号", identity.BiosSerialNumber),
            Candidate("serial", "Win32_ComputerSystemProduct", "产品识别号", identity.ProductIdentifyingNumber),
            Candidate("uuid", "Win32_ComputerSystemProduct", "产品 UUID", identity.ProductUuid),
            Candidate("serial", "Win32_BaseBoard", "主板序列号", identity.BaseBoardSerialNumber),
            Candidate("serial", "Win32_SystemEnclosure", "机箱序列号", identity.ChassisSerialNumber),
            Candidate("model", "Win32_ComputerSystem", "整机型号", identity.ComputerModel)
        };
        var conflicts = new List<string>();
        var bios = candidates[0];
        var product = candidates[1];
        var board = candidates[3];
        var chassis = candidates[4];
        if (bios.Valid && product.Valid && !Same(bios.Value, product.Value)) conflicts.Add("BIOS 序列号与产品识别号不一致");
        if (board.Valid && chassis.Valid && !Same(board.Value, chassis.Value)) conflicts.Add("主板序列号与机箱序列号不一致");

        var identityText = string.Join(' ', new[] { identity.ComputerManufacturer, identity.ProductVendor, identity.ComputerModel, identity.ProductName, identity.BiosManufacturer });
        var isVm = Has(identityText, "virtual", "vmware", "virtualbox", "qemu", "kvm", "hyper-v", "parallels", "xen");
        if (isVm) conflicts.Add("当前设备为虚拟环境，固件识别码不能作为 OEM 出厂序列号");

        var primaryAgreement = bios.Valid && product.Valid && Same(bios.Value, product.Value);
        var primaryAvailable = bios.Valid || product.Valid;
        var confidence = isVm || conflicts.Count > 0
            ? IdentityConfidence.Low
            : primaryAgreement
                ? IdentityConfidence.High
                : primaryAvailable || candidates.Any(x => x.Kind == "uuid" && x.Valid)
                    ? IdentityConfidence.Medium
                    : IdentityConfidence.Low;
        var summary = confidence switch
        {
            IdentityConfidence.High => "固件中的主要识别字段一致",
            IdentityConfidence.Medium => "仅有单一有效识别来源，建议在厂商官网再次确认",
            _ => conflicts.Count > 0 ? string.Join("；", conflicts) : "缺少可信的 OEM 识别字段"
        };
        var digestSource = string.Join('|', new[] { identity.ComputerManufacturer, identity.ComputerModel, identity.ProductUuid, identity.BiosSerialNumber }.Select(Clean).Select(x => x.ToUpperInvariant()));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestSource)))[..16];
        return new DeviceIdentityAssessment(confidence, summary, candidates, conflicts, isVm, digest);
    }

    public static string Mask(string? value)
    {
        var text = Clean(value);
        if (!IsValidHardwareValue(text)) return "未能读取";
        if (text.Length <= 4) return new string('•', text.Length);
        return $"{text[..2]}{new string('•', Math.Min(8, text.Length - 4))}{text[^2..]}";
    }

    private static IdentityCandidate Candidate(string kind, string source, string label, string value)
    {
        var clean = Clean(value);
        var valid = IsValidHardwareValue(clean);
        return new IdentityCandidate(kind, source, label, clean, valid ? Mask(clean) : "未能读取", valid, valid ? "固件报告值" : "空值或 OEM 占位值");
    }

    private static bool Same(string left, string right) => Clean(left).Equals(Clean(right), StringComparison.OrdinalIgnoreCase);

    public static bool IsValidHardwareValue(string? value)
    {
        var text = Clean(value);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var invalid = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "unknown", "none", "n/a", "not applicable", "not available", "default string",
            "to be filled by o.e.m.", "to be filled by oem", "system serial number",
            "system product name", "system manufacturer", "oem", "invalid", "00000000",
            "0000000000", "0123456789", "123456789"
        };
        if (invalid.Contains(text)) return false;
        var compact = text.Replace("-", "").Replace("{", "").Replace("}", "");
        return compact.Any(ch => ch != '0' && ch != 'F');
    }

    public static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private static DeviceDisplayIdentifier Pick(string label, params string[] values)
    {
        var value = values.Select(Clean).FirstOrDefault(IsValidHardwareValue);
        return string.IsNullOrWhiteSpace(value)
            ? new DeviceDisplayIdentifier("识别码", UnknownValue)
            : new DeviceDisplayIdentifier(label, value);
    }

    private static string ResolveVendor(DeviceIdentity identity)
    {
        var haystack = string.Join(" ", new[]
        {
            identity.ComputerManufacturer, identity.ProductVendor, identity.BiosManufacturer,
            identity.ComputerModel, identity.ProductName, identity.BaseBoardManufacturer
        });

        if (Has(haystack, "dell", "alienware")) return "dell";
        if (Has(haystack, "lenovo", "thinkpad", "thinkbook", "legion")) return "lenovo";
        if (Has(haystack, "hewlett-packard", "omen") || HasToken(haystack, "hp")) return "hp";
        if (Has(haystack, "asus", "asustek", "rog", "tuf")) return "asus";
        if (Has(haystack, "acer")) return "acer";
        if (Has(haystack, "micro-star") || HasToken(haystack, "msi")) return "msi";
        if (Has(haystack, "gigabyte", "aorus")) return "gigabyte";
        if (Has(haystack, "surface") || Has(haystack, "microsoft")) return "surface";
        if (Has(haystack, "huawei", "matebook")) return "huawei";
        if (Has(haystack, "honor", "magicbook")) return "honor";
        if (Has(haystack, "xiaomi", "redmi", "redmibook", "timi")) return "xiaomi";
        if (Has(haystack, "mechrevo", "机械革命")) return "mechrevo";
        if (Has(haystack, "machenike", "机械师")) return "machenike";
        if (Has(haystack, "thunderobot", "雷神", "haier")) return "thunderobot";
        if (Has(haystack, "hasee", "神舟", "shenzhou")) return "hasee";
        if (Has(haystack, "dynabook", "toshiba")) return "dynabook";
        if (!IsValidHardwareValue(identity.ComputerManufacturer) && Has(identity.BaseBoardManufacturer, "asus", "asustek", "micro-star", "msi", "gigabyte", "asrock"))
            return "mainboard";
        if (Has(identity.ComputerManufacturer, "tongfang") && Has(haystack, "mechrevo", "机械革命")) return "mechrevo";
        return "unknown";
    }

    private static bool Has(string text, params string[] keywords) => keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    private static bool HasToken(string text, string token) => Regex.IsMatch(text, $@"(^|[^A-Za-z0-9]){Regex.Escape(token)}([^A-Za-z0-9]|$)", RegexOptions.IgnoreCase);
}

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
        var isSupportedWindows = OperatingSystem.IsWindowsVersionAtLeast(10);
        var runtimeIsSupported = Environment.Version.Major >= 8 ||
                                 RuntimeInformation.FrameworkDescription.Contains(".NET 8", StringComparison.OrdinalIgnoreCase) ||
                                 RuntimeInformation.FrameworkDescription.Contains(".NET 9", StringComparison.OrdinalIgnoreCase);
        var architectureIsSupported = RuntimeInformation.ProcessArchitecture == Architecture.X64;

        return isSupportedWindows && runtimeIsSupported && architectureIsSupported ? "运行环境正常" : "运行环境异常";
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
    private static IReadOnlyDictionary<string, OptimizationContract>? _contracts;
    private static HashSet<string>? _disabledTaskCache;
    private static DateTimeOffset _disabledTaskCacheAt;
    private static readonly object LiveStateSync = new();
    private static Dictionary<string, OptimizationStateInfo>? _liveStateCache;
    private static DateTimeOffset _liveStateCacheAt;
    private static Task<Dictionary<string, OptimizationStateInfo>>? _liveStateScan;

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

    public IReadOnlyDictionary<string, OptimizationContract> GetContracts()
    {
        if (_contracts != null) return _contracts;
        using var doc = JsonUtil.ReadDocument(AppPaths.DataFile("optimization-contracts.json"));
        var result = new Dictionary<string, OptimizationContract>(StringComparer.OrdinalIgnoreCase);
        if (doc.RootElement.TryGetProperty("contracts", out var contracts))
        {
            foreach (var property in contracts.EnumerateObject())
                result[property.Name] = new OptimizationContract(property.Name, property.Value.S("yamlSha256"), property.Value.S("sigSha256"));
        }
        _contracts = result;
        return result;
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

    public async Task<Dictionary<string, OptimizationStateInfo>> GetLiveStatesAsync(IEnumerable<OptimizerItem>? source = null, bool forceRefresh = false)
    {
        var items = source?.ToList() ?? await GetItemsAsync();
        var ids = items.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Task<Dictionary<string, OptimizationStateInfo>> scanTask;
        lock (LiveStateSync)
        {
            if (!forceRefresh && _liveStateCache != null && ids.All(_liveStateCache.ContainsKey))
                return FilterLiveStates(_liveStateCache, ids);
            _liveStateScan ??= Task.Run(() => ScanLiveStates(items));
            scanTask = _liveStateScan;
        }

        Dictionary<string, OptimizationStateInfo> scanned;
        try { scanned = await scanTask; }
        finally
        {
            lock (LiveStateSync)
            {
                if (ReferenceEquals(_liveStateScan, scanTask)) _liveStateScan = null;
            }
        }
        lock (LiveStateSync)
        {
            _liveStateCache ??= new Dictionary<string, OptimizationStateInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in scanned) _liveStateCache[pair.Key] = pair.Value;
            _liveStateCacheAt = DateTimeOffset.Now;
            return FilterLiveStates(_liveStateCache, ids);
        }
    }

    public bool TryGetLiveStateCache(IEnumerable<OptimizerItem> source, out Dictionary<string, OptimizationStateInfo> states, out bool stale)
    {
        var ids = source.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (LiveStateSync)
        {
            if (_liveStateCache == null || !ids.All(_liveStateCache.ContainsKey))
            {
                states = new Dictionary<string, OptimizationStateInfo>(StringComparer.OrdinalIgnoreCase);
                stale = true;
                return false;
            }
            states = FilterLiveStates(_liveStateCache, ids);
            stale = DateTimeOffset.Now - _liveStateCacheAt >= TimeSpan.FromMinutes(5);
            return true;
        }
    }

    private static Dictionary<string, OptimizationStateInfo> ScanLiveStates(List<OptimizerItem> items)
    {
        var disabledTasks = QueryDisabledTasks();
        var result = new Dictionary<string, OptimizationStateInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) result[item.Id] = EvaluateLiveState(item, disabledTasks);
        return result;
    }

    private static Dictionary<string, OptimizationStateInfo> FilterLiveStates(Dictionary<string, OptimizationStateInfo> source, HashSet<string> ids) =>
        source.Where(x => ids.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

    public void InvalidateLiveStateCache()
    {
        lock (LiveStateSync)
        {
            _disabledTaskCache = null;
            _disabledTaskCacheAt = default;
            _liveStateCache = null;
            _liveStateCacheAt = default;
        }
    }

    public async Task<RestorePlan> BuildRestorePlanAsync(IEnumerable<string>? selectedIds = null)
    {
        var items = await GetItemsAsync();
        var selected = selectedIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected != null) items = items.Where(x => selected.Contains(x.Id)).ToList();
        var states = await GetLiveStatesAsync(items, forceRefresh: true);
        var plan = items.Select(item =>
        {
            var state = states[item.Id];
            var willChange = state.State is OptimizationLiveState.Optimized or OptimizationLiveState.Mixed or OptimizationLiveState.RestartRequired;
            return new RestorePlanItem(item.Id, item.Title, state.State, state.Detail, willChange, state.State == OptimizationLiveState.RestartRequired);
        }).ToList();
        var legacy = selected == null && (ReadMutableState().Applied.ContainsKey("DisableWindowsDefenderModern") || HasLegacyDefenderChanges());
        if (legacy) plan.Insert(0, new RestorePlanItem("LegacyDefenderRepair", "修复旧版 Defender 设置", OptimizationLiveState.Mixed, "检测到旧版本曾执行 Defender 禁用；仅提供一次性恢复。", true, true));
        return new RestorePlan(plan, DateTimeOffset.Now);
    }

    public async Task<RestoreExecutionReport> RestoreDefaultsAsync(RestorePlan plan)
    {
        var items = await GetItemsAsync();
        var lookup = items.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(AppPaths.BackupRoot);
        var emergency = new Dictionary<string, Snapshot>();
        foreach (var entry in plan.Items.Where(x => x.WillChange && lookup.ContainsKey(x.Id))) emergency[entry.Id] = CaptureSnapshot(lookup[entry.Id]);
        var emergencyPath = Path.Combine(AppPaths.BackupRoot, $"default-restore-snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        AtomicFile.Write(emergencyPath, JsonSerializer.Serialize(new { schemaVersion = 2, capturedAt = DateTimeOffset.Now, snapshots = emergency }, JsonOptions));

        var results = new List<RestoreItemResult>();
        foreach (var entry in plan.Items)
        {
            if (entry.Id == "LegacyDefenderRepair")
            {
                results.Add(await RepairLegacyDefenderAsync());
                continue;
            }
            if (!lookup.TryGetValue(entry.Id, out var item)) { results.Add(new RestoreItemResult(entry.Id, entry.Title, RestoreItemStatus.Skipped, "项目已不在当前优化目录中。")); continue; }
            if (!entry.WillChange) { results.Add(new RestoreItemResult(entry.Id, entry.Title, RestoreItemStatus.AlreadyDefault, "当前已处于安全默认基线。")); continue; }
            var actionResults = await Task.Run(() => RestoreBaseline(item));
            InvalidateLiveStateCache();
            var verified = (await GetLiveStatesAsync(new[] { item }))[item.Id];
            var failed = actionResults.Where(x => !x.Ok).ToList();
            var status = verified.State == OptimizationLiveState.Default && failed.Count == 0 ? RestoreItemStatus.Restored : failed.Count == actionResults.Count && failed.Count > 0 ? RestoreItemStatus.Failed : RestoreItemStatus.Partial;
            var detail = status == RestoreItemStatus.Restored ? "已恢复并通过实时验证。" : string.Join("；", failed.Take(4).Select(x => $"{x.Target}: {x.Message}"));
            results.Add(new RestoreItemResult(item.Id, item.Title, status, detail));
        }
        var reportPath = Path.Combine(AppPaths.ReportRoot, $"default-restore-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        Directory.CreateDirectory(AppPaths.ReportRoot);
        AtomicFile.Write(reportPath, JsonSerializer.Serialize(new { schemaVersion = 2, completedAt = DateTimeOffset.Now, results }, JsonOptions));
        LogService.Write($"安全默认恢复完成：{results.Count} 项，报告 {reportPath}");
        return new RestoreExecutionReport(results, DateTimeOffset.Now, reportPath);
    }

    private static OptimizationStateInfo EvaluateLiveState(OptimizerItem item, HashSet<string> disabledTasks)
    {
        if (!SupportsCurrentWindows(item.Windows)) return new OptimizationStateInfo(item.Id, OptimizationLiveState.Unsupported, "当前 Windows 版本不适用。", 0, 0);
        var yaml = Path.Combine(AppPaths.OptimizerRuntime, item.YamlFile);
        if (!File.Exists(yaml)) return new OptimizationStateInfo(item.Id, OptimizationLiveState.Unknown, "优化契约尚未下载，无法安全判断。", 0, 0);
        var targets = ExtractSnapshotTargets(yaml);
        var details = new List<OptimizationTargetInfo>();
        foreach (var desired in targets.RegistryDesired)
        {
            var target = $"{desired.Hive}\\{desired.Path}\\{desired.Name}";
            try
            {
                using var key = OpenKey(desired.Hive, desired.Path, false);
                var current = key?.GetValue(desired.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                var exists = current != null;
                if (desired.Delete)
                    details.Add(new OptimizationTargetInfo("注册表", target, exists ? OptimizationTargetState.Default : OptimizationTargetState.Optimized, exists ? "目标值仍存在，当前未应用删除优化。" : "目标值不存在，符合优化状态。"));
                else if (exists && RegistryMatches(current!, desired))
                    details.Add(new OptimizationTargetInfo("注册表", target, OptimizationTargetState.Optimized, "当前值与优化契约一致。"));
                else if (!exists)
                    details.Add(new OptimizationTargetInfo("注册表", target, OptimizationTargetState.Default, "工具写入值不存在，处于未配置状态。"));
                else
                    details.Add(new OptimizationTargetInfo("注册表", target, OptimizationTargetState.Diverged, "当前值既不匹配优化值，也不是未配置状态。"));
            }
            catch (Exception ex) { details.Add(new OptimizationTargetInfo("注册表", target, OptimizationTargetState.Unknown, ex.Message)); }
        }
        foreach (var desired in targets.ServiceDesired)
        {
            if (desired.Mode is "enable" or "start") continue;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{desired.Name}", false);
                if (key == null) { details.Add(new OptimizationTargetInfo("服务", desired.Name, OptimizationTargetState.Unavailable, "当前系统未安装该服务。")); continue; }
                var start = Convert.ToInt32(key.GetValue("Start", -1));
                var optimizedStart = desired.Mode == "disable" ? 4 : 3;
                var state = start == optimizedStart ? OptimizationTargetState.Optimized : start == DefaultServiceStart(desired.Name) ? OptimizationTargetState.Default : OptimizationTargetState.Diverged;
                details.Add(new OptimizationTargetInfo("服务", desired.Name, state, $"当前启动类型值：{start}。"));
            }
            catch (Exception ex) { details.Add(new OptimizationTargetInfo("服务", desired.Name, OptimizationTargetState.Unknown, ex.Message)); }
        }
        foreach (var command in targets.Commands)
        {
            if (TryTaskName(command, out var task))
            {
                var disabled = disabledTasks.Contains(NormalizeTask(task));
                details.Add(new OptimizationTargetInfo("计划任务", task, disabled ? OptimizationTargetState.Optimized : OptimizationTargetState.Default, disabled ? "计划任务已禁用。" : "计划任务处于启用状态。"));
            }
            else if (command.Contains("fsutil", StringComparison.OrdinalIgnoreCase) && command.Contains("disablelastaccess", StringComparison.OrdinalIgnoreCase))
            {
                var query = RunSystemCommand("fsutil.exe", "behavior query disablelastaccess");
                var match = Regex.Match(query.Output, @"=\s*(\d+)");
                var value = match.Success ? match.Groups[1].Value : "未知";
                var state = !query.Ok || !match.Success ? OptimizationTargetState.Unknown : value == "1" ? OptimizationTargetState.Optimized : value is "2" or "3" ? OptimizationTargetState.Default : OptimizationTargetState.Diverged;
                details.Add(new OptimizationTargetInfo("系统命令", "fsutil disablelastaccess", state, $"当前查询值：{value}。"));
            }
            else if (command.Contains("icacls", StringComparison.OrdinalIgnoreCase) && TryQuotedPath(command, out var path))
            {
                var acl = RunSystemCommand("icacls.exe", $"\"{Environment.ExpandEnvironmentVariables(path)}\"");
                var denied = acl.Output.Contains("SYSTEM:(DENY)", StringComparison.OrdinalIgnoreCase);
                var state = !acl.Ok ? OptimizationTargetState.Unknown : denied ? OptimizationTargetState.Optimized : OptimizationTargetState.Default;
                details.Add(new OptimizationTargetInfo("ACL", path, state, !acl.Ok ? "无法读取当前访问控制列表。" : denied ? "检测到 SYSTEM 拒绝规则。" : "未检测到工具设置的拒绝规则。"));
            }
            else details.Add(new OptimizationTargetInfo("命令", command, OptimizationTargetState.Unknown, "没有声明可验证的状态读取器。"));
        }
        var total = details.Count;
        var optimized = details.Count(x => x.State == OptimizationTargetState.Optimized);
        var defaults = details.Count(x => x.State == OptimizationTargetState.Default);
        var unresolved = total - optimized - defaults;
        if (total == 0) return new OptimizationStateInfo(item.Id, OptimizationLiveState.Unknown, "没有可验证目标。", 0, 0, details);
        var aggregate = AggregateTargetStates(details.Select(x => x.State));
        var detail = aggregate switch
        {
            OptimizationLiveState.ExternallyManaged => "部分目标由外部策略管理。",
            OptimizationLiveState.Optimized => "所有目标均处于优化状态。",
            OptimizationLiveState.Mixed => $"{optimized}/{total} 已优化，{defaults} 项为默认状态，{unresolved} 项无法确认。",
            OptimizationLiveState.Default => "当前符合安全默认基线。",
            _ => $"0/{total} 已优化，{defaults} 项为默认状态，{unresolved} 项无法确认。"
        };
        return new OptimizationStateInfo(item.Id, aggregate, detail, optimized, total, details);
    }

    private static OptimizationLiveState AggregateTargetStates(IEnumerable<OptimizationTargetState> source)
    {
        var states = source.ToList();
        if (states.Count == 0) return OptimizationLiveState.Unknown;
        if (states.Any(x => x == OptimizationTargetState.ExternallyManaged)) return OptimizationLiveState.ExternallyManaged;
        var optimized = states.Count(x => x == OptimizationTargetState.Optimized);
        if (optimized == states.Count) return OptimizationLiveState.Optimized;
        if (optimized > 0) return OptimizationLiveState.Mixed;
        return states.All(x => x == OptimizationTargetState.Default) ? OptimizationLiveState.Default : OptimizationLiveState.Unknown;
    }

    private static List<RestoreResult> RestoreBaseline(OptimizerItem item)
    {
        var targets = ExtractSnapshotTargets(Path.Combine(AppPaths.OptimizerRuntime, item.YamlFile));
        var results = new List<RestoreResult>();
        foreach (var desired in targets.RegistryDesired)
        {
            try
            {
                if (desired.Delete) { results.Add(new RestoreResult("registry", $"{desired.Hive}\\{desired.Path}\\{desired.Name}", false, "优化曾删除原值，安全基线无法推导其内容；请使用精确快照回退。")); continue; }
                using var key = OpenKey(desired.Hive, desired.Path, true);
                key?.DeleteValue(desired.Name, false);
                results.Add(new RestoreResult("registry", $"{desired.Hive}\\{desired.Path}\\{desired.Name}", true, "已移除工具写入值，恢复为未配置。"));
            }
            catch (Exception ex) { results.Add(new RestoreResult("registry", desired.Name, false, ex.Message)); }
        }
        foreach (var desired in targets.ServiceDesired.Where(x => x.Mode is "disable" or "demand"))
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{desired.Name}", true);
                if (key == null) { results.Add(new RestoreResult("service", desired.Name, true, "服务未安装，已跳过。")); continue; }
                key.SetValue("Start", DefaultServiceStart(desired.Name), RegistryValueKind.DWord);
                results.Add(new RestoreResult("service", desired.Name, true, "已恢复安全启动基线。"));
            }
            catch (Exception ex) { results.Add(new RestoreResult("service", desired.Name, false, ex.Message)); }
        }
        foreach (var command in targets.Commands)
        {
            if (TryTaskName(command, out var task))
            {
                var r = RunSystemCommand("schtasks.exe", $"/change /tn \"{task}\" /enable");
                results.Add(new RestoreResult("task", task, r.Ok || r.Output.Contains("cannot find", StringComparison.OrdinalIgnoreCase), r.Output));
            }
            else if (command.Contains("fsutil", StringComparison.OrdinalIgnoreCase) && command.Contains("disablelastaccess", StringComparison.OrdinalIgnoreCase))
            {
                var r = RunSystemCommand("fsutil.exe", "behavior set disablelastaccess 2");
                results.Add(new RestoreResult("command", "NTFS Last Access", r.Ok, r.Output));
            }
            else if (command.Contains("icacls", StringComparison.OrdinalIgnoreCase) && TryQuotedPath(command, out var path))
            {
                var r = RunSystemCommand("icacls.exe", $"\"{Environment.ExpandEnvironmentVariables(path)}\" /remove:d SYSTEM");
                results.Add(new RestoreResult("acl", path, r.Ok, r.Output));
            }
            else results.Add(new RestoreResult("command", command, false, "没有安全的默认恢复处理器。"));
        }
        return results;
    }

    private static async Task<RestoreItemResult> RepairLegacyDefenderAsync()
    {
        var state = ReadMutableState();
        RestoreReport report;
        if (state.Applied.TryGetValue("DisableWindowsDefenderModern", out var legacy) && legacy.Snapshot != null)
            report = RestoreSnapshot(legacy.Snapshot);
        else
            report = RepairDefenderBaseline();
        if (report.Ok)
        {
            state.Applied.Remove("DisableWindowsDefenderModern");
            WriteState(state);
        }
        await Task.Yield();
        return new RestoreItemResult("LegacyDefenderRepair", "修复旧版 Defender 设置", report.Ok ? RestoreItemStatus.RestartRequired : RestoreItemStatus.Partial, report.Ok ? "已恢复旧快照，重启后生效。" : "部分设置未恢复，请查看报告。 ");
    }

    private static bool HasLegacyDefenderChanges()
    {
        using var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender", false);
        if (new[] { "DisableAntiVirus", "DisableAntiSpyware", "DisableRealtimeMonitoring", "ServiceKeepAlive" }.Any(name => policy?.GetValue(name) != null)) return true;
        using var realtime = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", false);
        if (realtime?.GetValueNames().Any(name => name.StartsWith("Disable", StringComparison.OrdinalIgnoreCase)) == true) return true;
        foreach (var service in new[] { "WinDefend", "SecurityHealthService" })
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service}", false);
            if (Convert.ToInt32(key?.GetValue("Start", -1)) == 4) return true;
        }
        return false;
    }

    private static RestoreReport RepairDefenderBaseline()
    {
        var domain = RunSystemCommand("powershell.exe", "-NoProfile -Command \"(Get-CimInstance Win32_ComputerSystem).PartOfDomain\"");
        if (domain.Ok && domain.Output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase))
            return new RestoreReport(false, new List<RestoreResult> { new("policy", "Windows Defender", false, "设备已加入域，已阻止覆盖组织策略。") });
        var results = new List<RestoreResult>();
        void RemoveValues(string path, params string[] names)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path, true);
                foreach (var name in names) key?.DeleteValue(name, false);
                results.Add(new RestoreResult("registry", $"HKLM\\{path}", true, "已恢复为未配置。"));
            }
            catch (Exception ex) { results.Add(new RestoreResult("registry", path, false, ex.Message)); }
        }
        RemoveValues(@"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiVirus", "DisableSpecialRunningModes", "DisableRoutinelyTakingAction", "ServiceKeepAlive", "PUAProtection", "DisableAntiSpyware", "DisableRealtimeMonitoring");
        RemoveValues(@"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableBehaviorMonitoring", "DisableIOAVProtection", "DisableOnAccessProtection", "DisableRealtimeMonitoring", "DisableRoutinelyTakingAction", "DisableScanOnRealtimeEnable");
        foreach (var pair in new[] { ("WinDefend", 2), ("SecurityHealthService", 3), ("WdNisSvc", 3), ("SgrmBroker", 2) })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{pair.Item1}", true);
                if (key != null) key.SetValue("Start", pair.Item2, RegistryValueKind.DWord);
                results.Add(new RestoreResult("service", pair.Item1, true, key == null ? "服务不存在，已跳过。" : "已恢复启动基线。"));
            }
            catch (Exception ex) { results.Add(new RestoreResult("service", pair.Item1, false, ex.Message)); }
        }
        foreach (var task in new[] { @"\Microsoft\Windows\Windows Defender\Windows Defender Cache Maintenance", @"\Microsoft\Windows\Windows Defender\Windows Defender Cleanup", @"\Microsoft\Windows\Windows Defender\Windows Defender Scheduled Scan", @"\Microsoft\Windows\Windows Defender\Windows Defender Verification" })
        {
            var enabled = RunSystemCommand("schtasks.exe", $"/change /tn \"{task}\" /enable");
            results.Add(new RestoreResult("task", task, enabled.Ok || enabled.Output.Contains("cannot find", StringComparison.OrdinalIgnoreCase), enabled.Output));
        }
        return new RestoreReport(results.All(x => x.Ok), results);
    }

    private static bool RegistryMatches(object current, RegistryDesired desired)
    {
        var expected = desired.Value.Trim().Trim('"', '\'');
        if (desired.Kind.Equals("DWord", StringComparison.OrdinalIgnoreCase) && long.TryParse(expected, out var number))
        {
            var actual = Convert.ToInt64(current);
            if (number == uint.MaxValue && actual == -1) return true;
            return actual == number;
        }
        return current.ToString()?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool SupportsCurrentWindows(string windows)
    {
        if (windows.Equals("All", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(windows)) return true;
        var is11 = Environment.OSVersion.Version.Build >= 22000;
        return is11 ? windows.Contains("11") : windows.Contains("10");
    }

    private static readonly Dictionary<string, int> ServiceDefaultStarts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SysMain"] = 2, ["DiagTrack"] = 2, ["diagsvc"] = 3, ["dmwappushservice"] = 3,
        ["WSearch"] = 2, ["WerSvc"] = 3, ["PcaSvc"] = 3, ["Spooler"] = 2,
        ["wuauserv"] = 3, ["UsoSvc"] = 2, ["DoSvc"] = 2, ["BITS"] = 3,
        ["RemoteRegistry"] = 4, ["NvTelemetryContainer"] = 3
    };

    private static int DefaultServiceStart(string name) => ServiceDefaultStarts.TryGetValue(name, out var start) ? start : 3;

    private static HashSet<string> QueryDisabledTasks()
    {
        if (_disabledTaskCache != null && DateTimeOffset.Now - _disabledTaskCacheAt < TimeSpan.FromSeconds(30))
            return new HashSet<string>(_disabledTaskCache, StringComparer.OrdinalIgnoreCase);
        var script = "Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { -not $_.Settings.Enabled } | ForEach-Object { $_.TaskPath + $_.TaskName } | ConvertTo-Json -Compress";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var result = RunSystemCommand("powershell.exe", $"-NoProfile -EncodedCommand {encoded}", 20000);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.Output)) return set;
        try
        {
            using var doc = JsonDocument.Parse(result.Output);
            if (doc.RootElement.ValueKind == JsonValueKind.Array) foreach (var value in doc.RootElement.EnumerateArray()) set.Add(NormalizeTask(value.ToString()));
            else if (doc.RootElement.ValueKind == JsonValueKind.String) set.Add(NormalizeTask(doc.RootElement.ToString()));
        }
        catch { }
        _disabledTaskCache = new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
        _disabledTaskCacheAt = DateTimeOffset.Now;
        return set;
    }

    private static string NormalizeTask(string task) => task.Replace("\\\\", "\\").Trim().Trim('"');

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
                GetContracts().TryGetValue(item.Id, out var contract);
                if (!File.Exists(yamlPath) || contract == null || !DownloadService.Sha256(yamlPath).Equals(contract.YamlSha256, StringComparison.OrdinalIgnoreCase))
                    downloadJobs.Add((item, YamlBase + item.YamlFile, yamlPath));
                if (item.SigRequired && (!File.Exists(sigPath) || contract == null || !DownloadService.Sha256(sigPath).Equals(contract.SigSha256, StringComparison.OrdinalIgnoreCase)))
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
            foreach (var item in items)
                if (!IsInstalled(item)) throw new InvalidOperationException($"{item.Title} 的优化契约或文件校验失败，已阻止使用。");
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
        InvalidateLiveStateCache();
        LogService.Write($"应用优化：{item.Title}");
        return "优化已执行，可再次点击该项目回退。";
    }

    public async Task<string> RestoreAsync(string id)
    {
        var state = ReadMutableState();
        if (!state.Applied.TryGetValue(id, out var entry) || entry.Snapshot == null)
            throw new InvalidOperationException("该项目没有可恢复快照，已保留当前状态。");
        var report = RestoreSnapshot(entry.Snapshot);
        InvalidateLiveStateCache();
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
        var contracts = new OptimizerService().GetContracts();
        if (!contracts.TryGetValue(item.Id, out var contract)) return false;
        return IsExeValid() && File.Exists(yaml)
            && DownloadService.Sha256(yaml).Equals(contract.YamlSha256, StringComparison.OrdinalIgnoreCase)
            && (!item.SigRequired || File.Exists(sig) && DownloadService.Sha256(sig).Equals(contract.SigSha256, StringComparison.OrdinalIgnoreCase));
    }

    private static Snapshot CaptureSnapshot(OptimizerItem item)
    {
        var targets = ExtractSnapshotTargets(Path.Combine(AppPaths.OptimizerRuntime, item.YamlFile));
        var snapshot = new Snapshot { CapturedAt = DateTimeOffset.Now, Commands = targets.Commands };
        foreach (var reg in targets.Registry) snapshot.Registry.Add(CaptureRegistry(reg.Hive, reg.Path, reg.Name));
        foreach (var service in targets.Services) snapshot.Services.Add(CaptureService(service));
        foreach (var file in targets.Files) snapshot.Files.Add(CaptureFile(file));
        foreach (var command in targets.Commands)
        {
            if (TryTaskName(command, out var task)) snapshot.Tasks.Add(CaptureTask(task));
            else if (command.Contains("icacls", StringComparison.OrdinalIgnoreCase) && TryQuotedPath(command, out var aclPath)) snapshot.Acls.Add(CaptureAcl(aclPath));
            else snapshot.CommandStates.Add(CaptureCommand(command));
        }
        return snapshot;
    }

    private static SnapshotTargets ExtractSnapshotTargets(string yaml)
    {
        var targets = new SnapshotTargets();
        if (!File.Exists(yaml)) return targets;
        string? hive = null, path = null, mode = null, serviceMode = null;
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
            if (hiveMatch.Success) { hive = hiveMatch.Groups[1].Value.ToUpperInvariant(); path = null; mode = null; serviceMode = null; inFiles = false; continue; }
            if (hive != null && Regex.IsMatch(line, @"^path:\s*", RegexOptions.IgnoreCase)) { path = CleanYamlScalar(Regex.Replace(line, @"^path:\s*", "", RegexOptions.IgnoreCase)); continue; }
            var modeMatch = Regex.Match(line, @"^(add|set|values|delete|remove|removevalues|deletevalues):\s*$", RegexOptions.IgnoreCase);
            if (hive != null && path != null && modeMatch.Success) { mode = modeMatch.Groups[1].Value; continue; }
            if (hive != null && path != null && mode != null)
            {
                var typed = Regex.Match(line, "^(?:\"(?<name>[^\"]*)\"|'(?<name>[^']*)'|(?<name>[^:{}]+))\\s*:\\s*\\{\\s*type:\\s*(?<type>[^,}]+),\\s*value:\\s*(?<value>.*?)\\s*\\}\\s*$", RegexOptions.IgnoreCase);
                var key = Regex.Match(line, "^['\"]?([^'\":{}]+)['\"]?\\s*:\\s*\\{");
                var list = Regex.Match(line, "^-\\s*['\"]?([^'\"]+?)['\"]?\\s*$");
                var name = Regex.Match(line, @"^name:\s*(.+)$", RegexOptions.IgnoreCase);
                var valueName = typed.Success ? typed.Groups["name"].Value : key.Success ? key.Groups[1].Value : list.Success ? list.Groups[1].Value : name.Success ? name.Groups[1].Value : "";
                valueName = CleanYamlScalar(valueName);
                if (typed.Success)
                    targets.RegistryDesired.Add(new RegistryDesired(hive, path, valueName, CleanYamlScalar(typed.Groups["type"].Value), CleanYamlScalar(typed.Groups["value"].Value), false));
                else if (!string.IsNullOrWhiteSpace(valueName) && Regex.IsMatch(mode, "delete|remove", RegexOptions.IgnoreCase))
                    targets.RegistryDesired.Add(new RegistryDesired(hive, path, valueName, "", "", true));
                if (typed.Success || !string.IsNullOrWhiteSpace(valueName)) targets.Registry.Add(new RegistryTarget(hive, path, valueName));
            }
            var serviceHeader = Regex.Match(line, @"^(stop|disable|enable|demand|start|services):\s*$", RegexOptions.IgnoreCase);
            if (serviceHeader.Success) { mode = "service"; serviceMode = serviceHeader.Groups[1].Value.ToLowerInvariant(); hive = null; path = null; continue; }
            if (mode == "service")
            {
                var service = Regex.Match(line, @"^(?:-\s*)?name:\s*([A-Za-z0-9_.-]+)$", RegexOptions.IgnoreCase);
                var simple = Regex.Match(line, @"^-\s*([A-Za-z0-9_.-]+)\s*$");
                var serviceName = service.Success ? service.Groups[1].Value : simple.Success ? simple.Groups[1].Value : "";
                if (!string.IsNullOrWhiteSpace(serviceName))
                {
                    targets.Services.Add(serviceName);
                    if (serviceMode is "disable" or "enable" or "demand" or "start") targets.ServiceDesired.Add(new ServiceDesired(serviceName, serviceMode));
                }
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
            if (Regex.IsMatch(line, @"^-\s*.*\b(bcdedit|powercfg|netsh|schtasks(?:\.exe)?|fsutil|icacls)\b", RegexOptions.IgnoreCase))
                targets.Commands.Add(line.TrimStart('-', ' '));
        }
        targets.Registry = targets.Registry.DistinctBy(x => $"{x.Hive}\\{x.Path}\\{x.Name}".ToLowerInvariant()).ToList();
        targets.Services = targets.Services.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        targets.RegistryDesired = targets.RegistryDesired.DistinctBy(x => $"{x.Hive}\\{x.Path}\\{x.Name}".ToLowerInvariant()).ToList();
        targets.ServiceDesired = targets.ServiceDesired.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
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
        var query = RunSystemCommand("sc.exe", $"query \"{name}\"");
        return new ServiceSnapshot(name, key != null, key?.GetValue("Start")?.ToString(), key?.GetValue("DelayedAutoStart")?.ToString(), query.Ok && query.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase));
    }

    private static TaskSnapshot CaptureTask(string name)
    {
        var result = RunSystemCommand("schtasks.exe", $"/query /tn \"{name}\" /xml");
        return new TaskSnapshot(name, result.Ok, result.Ok && !result.Output.Contains("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase));
    }

    private static AclSnapshot CaptureAcl(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (!Directory.Exists(expanded) && !File.Exists(expanded)) return new AclSnapshot(expanded, false, "");
        var escaped = expanded.Replace("'", "''");
        var result = RunSystemCommand("powershell.exe", $"-NoProfile -Command \"(Get-Acl -LiteralPath '{escaped}').Sddl\"");
        return new AclSnapshot(expanded, result.Ok, result.Ok ? result.Output.Trim() : "");
    }

    private static CommandSnapshot CaptureCommand(string command)
    {
        if (command.Contains("fsutil", StringComparison.OrdinalIgnoreCase) && command.Contains("disablelastaccess", StringComparison.OrdinalIgnoreCase))
        {
            var result = RunSystemCommand("fsutil.exe", "behavior query disablelastaccess");
            var match = Regex.Match(result.Output, @"=\s*(\d+)");
            return new CommandSnapshot(command, "fsutil-disablelastaccess", match.Success ? match.Groups[1].Value : "", result.Ok && match.Success);
        }
        return new CommandSnapshot(command, "unsupported", "", false);
    }

    private static bool TryTaskName(string command, out string name)
    {
        var match = Regex.Match(command, "(?i)/tn\\s+(?:\"(?<name>[^\"]+)\"|'(?<name>[^']+)'|(?<name>\\S+))");
        name = match.Success ? match.Groups["name"].Value : "";
        return match.Success && command.Contains("schtasks", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryQuotedPath(string command, out string path)
    {
        var match = Regex.Match(command, "\"(?<path>[^\"]+)\"");
        path = match.Success ? match.Groups["path"].Value : "";
        return match.Success;
    }

    private static (bool Ok, string Output) RunSystemCommand(string file, string arguments, int timeoutMs = 12000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(psi);
            if (process == null) return (false, "无法启动进程");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMs)) { try { process.Kill(true); } catch { } return (false, "执行超时"); }
            return (process.ExitCode == 0, (output + Environment.NewLine + error).Trim());
        }
        catch (Exception ex) { return (false, ex.Message); }
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
                if (key != null && service.Exists && int.TryParse(service.StartValue, out var start))
                {
                    key.SetValue("Start", start, RegistryValueKind.DWord);
                    if (int.TryParse(service.DelayedAutoStart, out var delayed)) key.SetValue("DelayedAutoStart", delayed, RegistryValueKind.DWord);
                    else key.DeleteValue("DelayedAutoStart", false);
                    var runtime = RunSystemCommand("sc.exe", $"{(service.WasRunning ? "start" : "stop")} \"{service.Name}\"");
                    results.Add(new RestoreResult("service", service.Name, runtime.Ok || runtime.Output.Contains("already", StringComparison.OrdinalIgnoreCase), runtime.Ok ? "" : runtime.Output));
                }
                else results.Add(new RestoreResult("service", service.Name, !service.Exists, service.Exists ? "服务不存在，无法恢复" : "执行前不存在，无需恢复"));
            }
            catch (Exception ex) { results.Add(new RestoreResult("service", service.Name, false, ex.Message)); }
        }
        foreach (var task in snapshot.Tasks)
        {
            if (!task.Exists) { results.Add(new RestoreResult("task", task.Name, true, "执行前不存在，无需恢复")); continue; }
            var result = RunSystemCommand("schtasks.exe", $"/change /tn \"{task.Name}\" /{(task.Enabled ? "enable" : "disable")}");
            results.Add(new RestoreResult("task", task.Name, result.Ok, result.Output));
        }
        foreach (var acl in snapshot.Acls)
        {
            if (!acl.Exists || string.IsNullOrWhiteSpace(acl.Sddl)) { results.Add(new RestoreResult("acl", acl.Path, true, "执行前不存在，无需恢复")); continue; }
            var path = acl.Path.Replace("'", "''");
            var sddl = acl.Sddl.Replace("'", "''");
            var result = RunSystemCommand("powershell.exe", $"-NoProfile -Command \"$a=Get-Acl -LiteralPath '{path}';$a.SetSecurityDescriptorSddlForm('{sddl}');Set-Acl -LiteralPath '{path}' -AclObject $a\"");
            results.Add(new RestoreResult("acl", acl.Path, result.Ok, result.Output));
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
        foreach (var command in snapshot.CommandStates)
        {
            if (!command.Supported) { results.Add(new RestoreResult("command", command.Command, false, "该命令没有安全的逆操作，已阻止报告成功。")); continue; }
            if (command.Kind == "fsutil-disablelastaccess")
            {
                var result = RunSystemCommand("fsutil.exe", $"behavior set disablelastaccess {command.Value}");
                results.Add(new RestoreResult("command", command.Command, result.Ok, result.Output));
            }
            else results.Add(new RestoreResult("command", command.Command, false, "未知命令状态类型"));
        }
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
        try
        {
            var state = JsonSerializer.Deserialize<OptimizerState>(File.ReadAllText(AppPaths.StatePath, Encoding.UTF8), JsonOptions) ?? new();
            var expectedChecksum = state.Checksum;
            if (!string.IsNullOrWhiteSpace(expectedChecksum))
            {
                state.Checksum = "";
                var payload = JsonSerializer.Serialize(state, JsonOptions);
                var actualChecksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
                if (!actualChecksum.Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("优化状态校验和不匹配");
                state.Checksum = expectedChecksum;
            }
            state.SchemaVersion = 2;
            state.Applied ??= new();
            return state;
        }
        catch (Exception ex)
        {
            var quarantine = AppPaths.StatePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try { File.Move(AppPaths.StatePath, quarantine, true); } catch { }
            LogService.Write($"优化状态文件损坏，已隔离：{quarantine} - {ex.Message}");
            return new();
        }
    }

    private static void WriteState(OptimizerState state)
    {
        AppPaths.Ensure();
        state.SchemaVersion = 2;
        state.Checksum = "";
        var payload = JsonSerializer.Serialize(state, JsonOptions);
        state.Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        AtomicFile.Write(AppPaths.StatePath, JsonSerializer.Serialize(state, JsonOptions));
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
        "disableSecurityUpdate" => "Windows 更新与安全设置工具",
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
    private sealed record DriverStatusCache(List<DriverStatusItem> Drivers, DateTimeOffset CachedAt);
    private readonly object _pageCacheSync = new();
    private DriverPageData? _pageCache;
    private DateTimeOffset _pageCacheAt;
    private Task<DriverPageData>? _pageRefresh;

    public void ClearCache()
    {
        lock (_pageCacheSync)
        {
            _pageCache = null;
            _pageCacheAt = default;
        }
        try { if (File.Exists(AppPaths.DriverCachePath)) File.Delete(AppPaths.DriverCachePath); } catch { }
    }

    public bool TryGetPageCache(out DriverPageData? data, out bool stale)
    {
        lock (_pageCacheSync)
        {
            data = _pageCache;
            stale = data == null || DateTimeOffset.Now - _pageCacheAt >= TimeSpan.FromMinutes(30);
            return data != null;
        }
    }

    public async Task<DriverPageData> GetPageDataAsync(bool forceRefresh = false)
    {
        Task<DriverPageData> refreshTask;
        lock (_pageCacheSync)
        {
            if (!forceRefresh && _pageCache != null && DateTimeOffset.Now - _pageCacheAt < TimeSpan.FromMinutes(30)) return _pageCache;
            _pageRefresh ??= LoadPageDataAsync(forceRefresh);
            refreshTask = _pageRefresh;
        }
        try
        {
            var data = await refreshTask;
            lock (_pageCacheSync)
            {
                _pageCache = data;
                _pageCacheAt = DateTimeOffset.Now;
            }
            return data;
        }
        finally
        {
            lock (_pageCacheSync)
            {
                if (ReferenceEquals(_pageRefresh, refreshTask)) _pageRefresh = null;
            }
        }
    }

    private async Task<DriverPageData> LoadPageDataAsync(bool forceRefresh)
    {
        AppPaths.Ensure();
        var detected = await DetectDetailedAsync();
        List<DriverStatusItem>? drivers = null;
        var cachedAt = DateTimeOffset.Now;
        if (!forceRefresh && File.Exists(AppPaths.DriverCachePath))
        {
            try
            {
                var rawCache = File.ReadAllText(AppPaths.DriverCachePath, Encoding.UTF8);
                if (rawCache.Contains("\"Device\"", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("检测到包含旧版原始机器标识的缓存，立即重建。");
                var cached = JsonSerializer.Deserialize<DriverStatusCache>(rawCache, JsonOptions);
                if (cached != null && DateTimeOffset.Now - cached.CachedAt < TimeSpan.FromHours(12))
                {
                    drivers = cached.Drivers;
                    cachedAt = cached.CachedAt;
                }
            }
            catch { }
        }
        if (drivers == null)
        {
            drivers = await GetDriverStatusAsync();
            cachedAt = DateTimeOffset.Now;
            AtomicFile.Write(AppPaths.DriverCachePath, JsonSerializer.Serialize(new DriverStatusCache(drivers, cachedAt), JsonOptions));
        }
        return new DriverPageData(detected.Info, detected.Assessment, drivers, cachedAt);
    }

    public async Task<DeviceMatchInfo> DetectAsync() => (await DetectDetailedAsync()).Info;

    private async Task<(DeviceMatchInfo Info, DeviceIdentityAssessment Assessment)> DetectDetailedAsync()
    {
        using var device = await PowerShellJsonAsync("""
$cs = Get-CimInstance Win32_ComputerSystem
$bios = Get-CimInstance Win32_BIOS
$product = Get-CimInstance Win32_ComputerSystemProduct
$board = Get-CimInstance Win32_BaseBoard
$chassis = Get-CimInstance Win32_SystemEnclosure | Select-Object -First 1
[PSCustomObject]@{
  BiosManufacturer=$bios.Manufacturer
  BiosSerialNumber=$bios.SerialNumber
  ComputerManufacturer=$cs.Manufacturer
  ComputerModel=$cs.Model
  SystemSkuNumber=$cs.SystemSKUNumber
  ProductVendor=$product.Vendor
  ProductName=$product.Name
  ProductIdentifyingNumber=$product.IdentifyingNumber
  ProductSkuNumber=$product.SKUNumber
  ProductUuid=$product.UUID
  BaseBoardManufacturer=$board.Manufacturer
  BaseBoardProduct=$board.Product
  BaseBoardSerialNumber=$board.SerialNumber
  ChassisSerialNumber=$chassis.SerialNumber
} | ConvertTo-Json -Compress
""");
        var root = device.RootElement;
        var identity = new DeviceIdentity(
            root.S("BiosManufacturer"),
            root.S("BiosSerialNumber"),
            root.S("ComputerManufacturer"),
            root.S("ComputerModel"),
            root.S("SystemSkuNumber"),
            root.S("ProductVendor"),
            root.S("ProductName"),
            root.S("ProductIdentifyingNumber"),
            root.S("ProductSkuNumber"),
            root.S("ProductUuid"),
            root.S("BaseBoardManufacturer"),
            root.S("BaseBoardProduct"),
            root.S("BaseBoardSerialNumber"),
            root.S("ChassisSerialNumber"));
        var assessment = DeviceIdentifierResolver.Assess(identity);
        var manufacturer = DeviceIdentifierResolver.IsValidHardwareValue(identity.ComputerManufacturer) ? DeviceIdentifierResolver.Clean(identity.ComputerManufacturer) : "未知厂商";
        var model = DeviceIdentifierResolver.IsValidHardwareValue(identity.ComputerModel) ? DeviceIdentifierResolver.Clean(identity.ComputerModel) :
            DeviceIdentifierResolver.IsValidHardwareValue(identity.ProductName) ? DeviceIdentifierResolver.Clean(identity.ProductName) : "未知型号";
        var identifier = DeviceIdentifierResolver.Resolve(identity);
        var serial = identifier.CanCopy() ? identifier.Value : "未知 SN";
        var vendorDisplay = DeviceIdentifierResolver.ResolveVendorDisplay(identity);
        using var centers = JsonUtil.ReadDocument(AppPaths.DataFile("driver-centers.json"));
        foreach (var brand in centers.RootElement.EnumerateObject())
        {
            var aliases = brand.Value.TryGetProperty("aliases", out var a) ? a.EnumerateArray().Select(x => x.ToString()).ToList() : new();
            var brandMatched = aliases.Any(x => manufacturer.Contains(x, StringComparison.OrdinalIgnoreCase) || vendorDisplay.Contains(x, StringComparison.OrdinalIgnoreCase)) ||
                               manufacturer.Contains(brand.Name, StringComparison.OrdinalIgnoreCase) ||
                               vendorDisplay.Contains(brand.Name, StringComparison.OrdinalIgnoreCase);
            if (!brandMatched) continue;
            var support = brand.Value.S("supportPage");
            var queryPage = SupportQueryUrl(brand.Name, support, identifier.Value, model);
            if (brand.Value.TryGetProperty("models", out var models))
            {
                foreach (var candidate in models.EnumerateArray())
                {
                    var keywords = candidate.TryGetProperty("matchKeywords", out var ks) ? ks.EnumerateArray().Select(x => x.ToString()) : Enumerable.Empty<string>();
                    if (keywords.Any(k => model.Contains(k, StringComparison.OrdinalIgnoreCase) || identifier.Value.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        var matchedVendor = candidate.S("driverCenterName", brand.Value.S("brandName", vendorDisplay));
                        var domains = candidate.TryGetProperty("officialDomains", out var ds) ? ds.EnumerateArray().Select(x => x.ToString()).ToArray() : Array.Empty<string>();
                        var sha = candidate.S("sha256");
                        var verifiedAt = candidate.S("verifiedAt");
                        var directUrl = assessment.Confidence == IdentityConfidence.Low || string.IsNullOrWhiteSpace(sha) || string.IsNullOrWhiteSpace(verifiedAt) ? "" : candidate.S("downloadUrl");
                        return (new DeviceMatchInfo(manufacturer, model, serial, matchedVendor, candidate.S("officialPage", queryPage), directUrl, candidate.S("fileName"), true, matchedVendor, identifier.Label, identifier.Value, identifier.CanCopy(), sha, domains, verifiedAt), assessment);
                    }
                }
            }
            var brandName = brand.Value.S("brandName", vendorDisplay);
            return (new DeviceMatchInfo(manufacturer, model, serial, brandName, queryPage, "", "", true, brandName, identifier.Label, identifier.Value, identifier.CanCopy()), assessment);
        }
        return (new DeviceMatchInfo(manufacturer, model, serial, "暂无信息", "", "", "", false, vendorDisplay, identifier.Label, identifier.Value, identifier.CanCopy()), assessment);
    }

    public async Task<string> DownloadDriverPackageAsync(DeviceMatchInfo info, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl)) throw new InvalidOperationException("该匹配项未提供经过校验的直接下载包，请使用厂商官网入口。");
        if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("驱动包地址不是安全的 HTTPS 地址。");
        var allowed = info.OfficialDomains ?? Array.Empty<string>();
        if (allowed.Length == 0 || !allowed.Any(domain => uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("驱动包域名不在官方白名单中。");
        if (string.IsNullOrWhiteSpace(info.Sha256)) throw new InvalidOperationException("驱动包缺少固定 SHA-256，已阻止直接下载。");
        Directory.CreateDirectory(AppPaths.Downloads);
        var name = string.IsNullOrWhiteSpace(info.FileName) ? Path.GetFileName(uri.AbsolutePath) : info.FileName;
        var target = Path.Combine(AppPaths.Downloads, name);
        using var dl = new DownloadService();
        await dl.DownloadFileAsync(info.DownloadUrl, target, token);
        if (!DownloadService.Sha256(target).Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(target); } catch { }
            throw new InvalidOperationException("驱动包 SHA-256 校验失败，下载结果已删除。");
        }
        var escapedTarget = target.Replace("'", "''");
        using var signature = await PowerShellJsonAsync($"$s=Get-AuthenticodeSignature -LiteralPath '{escapedTarget}'; [PSCustomObject]@{{ Status=$s.Status.ToString(); Subject=$s.SignerCertificate.Subject }} | ConvertTo-Json -Compress", token);
        if (!signature.RootElement.S("Status").Equals("Valid", StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(target); } catch { }
            throw new InvalidOperationException("驱动包数字签名无效，下载结果已删除。");
        }
        LogService.Write($"下载并校验驱动包：{Path.GetFileName(target)}");
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
        var source = DeviceIdentifierResolver.IsValidHardwareValue(serial) && !serial.Equals("未能读取", StringComparison.OrdinalIgnoreCase) ? serial : model;
        var sn = Uri.EscapeDataString(DeviceIdentifierResolver.Clean(source));
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

    private static async Task<JsonDocument> PowerShellJsonAsync(string script, CancellationToken token = default)
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
        var outputTask = process.StandardOutput.ReadToEndAsync(token);
        var errorTask = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException("设备信息读取超过 25 秒，已停止本次检测。");
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"设备信息读取失败：{error.Trim()}");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("设备信息读取未返回有效数据。");
        try { return JsonDocument.Parse(output); }
        catch (JsonException ex) { throw new InvalidOperationException("设备信息返回格式不完整，请刷新后重试。", ex); }
    }
}

public static class AtomicFile
{
    public static void Write(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        if (File.Exists(path)) File.Move(temporary, path, true);
        else File.Move(temporary, path);
    }
}

public sealed record ProcessResult(int ExitCode, string Output, bool Succeeded, bool TimedOut, bool HadKnownInstanceError);
public sealed record RegistryTarget(string Hive, string Path, string Name);
public sealed record RegistryDesired(string Hive, string Path, string Name, string Kind, string Value, bool Delete);
public sealed record ServiceDesired(string Name, string Mode);
public sealed class SnapshotTargets
{
    public List<RegistryTarget> Registry { get; set; } = new();
    public List<string> Services { get; set; } = new();
    public List<string> Files { get; set; } = new();
    public List<string> Commands { get; set; } = new();
    public List<RegistryDesired> RegistryDesired { get; set; } = new();
    public List<ServiceDesired> ServiceDesired { get; set; } = new();
}
public sealed record RegistrySnapshot(string Hive, string Path, string Name, bool KeyExists, bool ValueExists, string Kind, string? StringValue, string[]? StringArray, byte[]? BinaryValue, long? IntegerValue);
public sealed record ServiceSnapshot(string Name, bool Exists, string? StartValue, string? DelayedAutoStart, bool WasRunning);
public sealed record TaskSnapshot(string Name, bool Exists, bool Enabled);
public sealed record AclSnapshot(string Path, bool Exists, string Sddl);
public sealed record CommandSnapshot(string Command, string Kind, string Value, bool Supported);
public sealed record FileSnapshot(string Path, string ExpandedPath, bool Exists, string Type, string? DataBase64, string Note);
public sealed record RestoreResult(string Type, string Target, bool Ok, string Message);
public sealed record RestoreReport(bool Ok, List<RestoreResult> Results);
public sealed class Snapshot
{
    public DateTimeOffset CapturedAt { get; set; }
    public List<RegistrySnapshot> Registry { get; set; } = new();
    public List<ServiceSnapshot> Services { get; set; } = new();
    public List<TaskSnapshot> Tasks { get; set; } = new();
    public List<AclSnapshot> Acls { get; set; } = new();
    public List<CommandSnapshot> CommandStates { get; set; } = new();
    public List<FileSnapshot> Files { get; set; } = new();
    public List<string> Commands { get; set; } = new();
}
public sealed record AppliedEntry(string Id, string Title, string YamlFile, Snapshot? Snapshot, DateTimeOffset? AppliedAt);
public sealed class OptimizerState
{
    public int SchemaVersion { get; set; } = 2;
    public string Checksum { get; set; } = "";
    public Dictionary<string, AppliedEntry> Applied { get; set; } = new();
}
