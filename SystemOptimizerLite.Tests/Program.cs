using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SystemOptimizerLite;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

static HttpResponseMessage FullResponse(byte[] bytes, long? advertisedLength = null, HttpStatusCode status = HttpStatusCode.OK)
{
    var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
    if (advertisedLength != null) response.Content.Headers.ContentLength = advertisedLength;
    return response;
}

static HttpResponseMessage PartialResponse(byte[] bytes, long from, long to, long total)
{
    var response = FullResponse(bytes, bytes.Length, HttpStatusCode.PartialContent);
    response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, total);
    return response;
}

static void CreateCleanerFixture(string root, int xmlCount = 60, bool includeExe = true, bool includeCleanerDirectory = true)
{
    var portable = Path.Combine(root, "BleachBit-Portable");
    Directory.CreateDirectory(portable);
    if (includeExe) File.WriteAllText(Path.Combine(portable, "bleachbit_console.exe"), "fixture", Encoding.UTF8);
    if (!includeCleanerDirectory) return;
    var cleaners = Path.Combine(portable, "share", "cleaners");
    Directory.CreateDirectory(cleaners);
    for (var i = 0; i < xmlCount; i++)
        File.WriteAllText(Path.Combine(cleaners, $"cleaner-{i:00}.xml"), $"<cleaner id=\"fixture_{i}\"><label>Fixture {i}</label></cleaner>", Encoding.UTF8);
}

var navigationGeometry = typeof(MainWindow).GetMethod("NavigationIconGeometry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
foreach (var page in new[] { "系统优化", "系统清理", "驱动管理", "工具箱", "未知页面" })
{
    var geometry = (System.Windows.Media.Geometry)navigationGeometry.Invoke(null, new object[] { page })!;
    Assert(geometry.IsFrozen && !geometry.Bounds.IsEmpty && geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0,
        $"Navigation icon geometry for {page} must be frozen and have non-empty bounds.");
}

var downloadTestRoot = Path.Combine(Path.GetTempPath(), $"WindowsLite-download-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(downloadTestRoot);
try
{
    var payload = Encoding.UTF8.GetBytes("0123456789");
    var payloadHash = Hash(payload);

    var resumeHandler = new SequenceHttpHandler(
        _ => FullResponse(payload[..5], payload.Length),
        _ => PartialResponse(payload[5..], 5, 9, payload.Length));
    var resumeTarget = Path.Combine(downloadTestRoot, "resume.bin");
    using (var download = new DownloadService(resumeHandler, (_, _) => Task.CompletedTask))
        await download.DownloadFileAsync("https://example.test/resume", resumeTarget, CancellationToken.None, expectedSha256: payloadHash);
    Assert(File.ReadAllBytes(resumeTarget).SequenceEqual(payload), "A truncated download must resume to the exact payload.");
    Assert(resumeHandler.RangeStarts.SequenceEqual(new long?[] { null, 5 }), "The retry after truncation must request the remaining range.");

    var ignoredRangeHandler = new SequenceHttpHandler(
        _ => FullResponse(payload[..5], payload.Length),
        _ => FullResponse(payload, payload.Length));
    var ignoredRangeTarget = Path.Combine(downloadTestRoot, "ignored-range.bin");
    using (var download = new DownloadService(ignoredRangeHandler, (_, _) => Task.CompletedTask))
        await download.DownloadFileAsync("https://example.test/ignored-range", ignoredRangeTarget, CancellationToken.None, expectedSha256: payloadHash);
    Assert(File.ReadAllBytes(ignoredRangeTarget).SequenceEqual(payload), "A server that ignores Range must trigger a clean full-file replacement.");
    Assert(ignoredRangeHandler.RangeStarts.SequenceEqual(new long?[] { null, 5 }), "The ignored-Range scenario must still attempt one resume request.");

    var hashRetryHandler = new SequenceHttpHandler(
        _ => FullResponse(Encoding.UTF8.GetBytes("abcdefghij"), payload.Length),
        _ => FullResponse(payload, payload.Length));
    var hashRetryTarget = Path.Combine(downloadTestRoot, "hash-retry.bin");
    using (var download = new DownloadService(hashRetryHandler, (_, _) => Task.CompletedTask))
        await download.DownloadFileAsync("https://example.test/hash", hashRetryTarget, CancellationToken.None, expectedSha256: payloadHash);
    Assert(hashRetryHandler.RangeStarts.SequenceEqual(new long?[] { null, null }), "A hash mismatch must discard the corrupt full file instead of resuming it.");

    var oldTarget = Path.Combine(downloadTestRoot, "old-target.bin");
    var oldBytes = Encoding.UTF8.GetBytes("known-good-old-file");
    File.WriteAllBytes(oldTarget, oldBytes);
    var failureHandler = new SequenceHttpHandler(
        _ => FullResponse(payload[..5], payload.Length),
        _ => FullResponse(payload[..5], payload.Length),
        _ => FullResponse(payload[..5], payload.Length));
    var failed = false;
    try
    {
        using var download = new DownloadService(failureHandler, (_, _) => Task.CompletedTask);
        await download.DownloadFileAsync("https://example.test/fail", oldTarget, CancellationToken.None, expectedSha256: payloadHash);
    }
    catch (InvalidOperationException)
    {
        failed = true;
    }
    Assert(failed, "A persistently truncated transfer must fail after the retry limit.");
    Assert(File.ReadAllBytes(oldTarget).SequenceEqual(oldBytes), "A failed download must not overwrite the old target file.");
    Assert(!File.Exists(oldTarget + ".download"), "A failed operation must clean its private temporary file.");

    var retryDelays = new List<TimeSpan>();
    var limitedResponses = Enumerable.Range(0, 4)
        .Select<int, Func<HttpRequestMessage, HttpResponseMessage>>(_ => _ =>
        {
            var response = FullResponse([], 0, HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return response;
        })
        .Append(_ => FullResponse(payload, payload.Length))
        .ToArray();
    var limitedHandler = new SequenceHttpHandler(limitedResponses);
    var limitedTarget = Path.Combine(downloadTestRoot, "limited.bin");
    using (var download = new DownloadService(limitedHandler, (delay, _) => { retryDelays.Add(delay); return Task.CompletedTask; }))
        await download.DownloadFileAsync("https://example.test/limited", limitedTarget, CancellationToken.None, expectedSha256: payloadHash);
    Assert(limitedHandler.RangeStarts.Count == 5, "HTTP 429 must be retried up to the dedicated five-attempt limit.");
    Assert(retryDelays.Count == 4 && retryDelays.All(delay => delay <= TimeSpan.FromSeconds(20)), "Retry-After delays must be honored but capped at 20 seconds.");
}
finally
{
    Directory.Delete(downloadTestRoot, true);
}

var cleanerTestRoot = Path.Combine(Path.GetTempPath(), $"WindowsLite-cleaner-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(cleanerTestRoot);
try
{
    var complete = Path.Combine(cleanerTestRoot, "complete");
    CreateCleanerFixture(complete);
    var completeValidation = CleanerService.ValidateInstallation(complete);
    Assert(completeValidation.IsValid && completeValidation.XmlCount == 60, "A complete BleachBit fixture must pass the 60-XML integrity check.");

    var missingExe = Path.Combine(cleanerTestRoot, "missing-exe");
    CreateCleanerFixture(missingExe, includeExe: false);
    Assert(!CleanerService.ValidateInstallation(missingExe).IsValid, "A BleachBit directory without the console executable must be incomplete.");

    var missingCleaners = Path.Combine(cleanerTestRoot, "missing-cleaners");
    CreateCleanerFixture(missingCleaners, includeCleanerDirectory: false);
    Assert(!CleanerService.ValidateInstallation(missingCleaners).IsValid, "A BleachBit directory without share\\cleaners must be incomplete.");

    var shortCatalog = Path.Combine(cleanerTestRoot, "short-catalog");
    CreateCleanerFixture(shortCatalog, xmlCount: 59);
    Assert(!CleanerService.ValidateInstallation(shortCatalog).IsValid, "A BleachBit directory with fewer than 60 XML definitions must be incomplete.");

    var finalDirectory = Path.Combine(cleanerTestRoot, "promote-final");
    Directory.CreateDirectory(finalDirectory);
    File.WriteAllText(Path.Combine(finalDirectory, "old.marker"), "old");
    var staging = Path.Combine(cleanerTestRoot, "promote-staging");
    CreateCleanerFixture(staging);
    var backup = Path.Combine(cleanerTestRoot, "promote-backup");
    CleanerService.PromoteInstallation(staging, finalDirectory, backup);
    Assert(CleanerService.ValidateInstallation(finalDirectory).IsValid, "A validated staging directory must be promoted atomically.");
    Assert(!Directory.Exists(backup), "The old component backup must be removed after a successful promotion.");

    var rollbackFinal = Path.Combine(cleanerTestRoot, "rollback-final");
    Directory.CreateDirectory(rollbackFinal);
    File.WriteAllText(Path.Combine(rollbackFinal, "old.marker"), "old");
    var rollbackStaging = Path.Combine(cleanerTestRoot, "rollback-staging");
    CreateCleanerFixture(rollbackStaging);
    var rollbackBackup = Path.Combine(cleanerTestRoot, "rollback-backup");
    var rollbackObserved = false;
    try
    {
        CleanerService.PromoteInstallation(rollbackStaging, rollbackFinal, rollbackBackup, (source, destination) =>
        {
            if (source.Equals(rollbackStaging, StringComparison.OrdinalIgnoreCase)) throw new IOException("simulated switch failure");
            Directory.Move(source, destination);
        });
    }
    catch (InvalidOperationException)
    {
        rollbackObserved = true;
    }
    Assert(rollbackObserved, "A simulated directory switch failure must be reported.");
    Assert(File.Exists(Path.Combine(rollbackFinal, "old.marker")), "A failed directory switch must restore the old component directory.");
}
finally
{
    Directory.Delete(cleanerTestRoot, true);
}

if (Environment.GetEnvironmentVariable("WINDOWSLITE_NETWORK_TEST") == "1")
{
    const string bleachBitUrl = "https://download.bleachbit.org/BleachBit-6.0.2-portable.zip";
    const string bleachBitSha256 = "0322920A592BA311CB374B4F44053EE35F793C5A5690F1F8CB419B57321B2883";
    var integrationRoot = Path.Combine(Path.GetTempPath(), $"WindowsLite-network-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(integrationRoot);
    try
    {
        var zip = Path.Combine(integrationRoot, "BleachBit-6.0.2-portable.zip");
        using (var download = new DownloadService())
            await download.DownloadFileAsync(bleachBitUrl, zip, CancellationToken.None, expectedSha256: bleachBitSha256);
        var extracted = Path.Combine(integrationRoot, "extracted");
        ZipFile.ExtractToDirectory(zip, extracted);
        var validation = CleanerService.ValidateInstallation(extracted);
        Assert(validation.IsValid && validation.XmlCount == 60, $"The official BleachBit archive failed integration validation: {validation.Reason}");
        Console.WriteLine($"BleachBit network integration passed: {new FileInfo(zip).Length} bytes, {validation.XmlCount} cleaner XML files.");
    }
    finally
    {
        Directory.Delete(integrationRoot, true);
    }
}

if (Environment.GetEnvironmentVariable("WINDOWSLITE_DRIVER_TEST") == "1")
{
    var driverService = new DriverService();
    driverService.ClearCache();
    try
    {
        var first = await driverService.GetPageDataAsync(forceRefresh: true, token: CancellationToken.None);
        var reused = await driverService.GetPageDataAsync(token: CancellationToken.None);
        Assert(ReferenceEquals(first, reused), "Repeated driver-page reads in one process must reuse the in-memory page cache.");

        var refreshed = await driverService.GetPageDataAsync(forceRefresh: true, token: CancellationToken.None);
        Assert(!ReferenceEquals(first, refreshed), "A manual driver refresh must create a new page-data snapshot.");
        Assert(refreshed.CachedAt >= first.CachedAt, "A manual driver refresh must not return an older cache timestamp.");
        Console.WriteLine($"Driver cache integration passed: first={first.CachedAt:O}, refreshed={refreshed.CachedAt:O}.");
    }
    finally
    {
        driverService.ClearCache();
    }
}

var optimizerPath = Path.Combine(AppContext.BaseDirectory, "Data", "optimizer-packages.json");
var contractPath = Path.Combine(AppContext.BaseDirectory, "Data", "optimization-contracts.json");
using var optimizer = JsonDocument.Parse(File.ReadAllText(optimizerPath));
using var contracts = JsonDocument.Parse(File.ReadAllText(contractPath));
Assert(contracts.RootElement.GetProperty("schemaVersion").GetInt32() == 2, "Optimization contracts must use schema v2.");
var items = optimizer.RootElement.EnumerateArray().ToList();
Assert(items.Count == 24, $"Expected 24 reversible optimizer items, got {items.Count}.");
var ids = items.Select(x => x.GetProperty("id").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
Assert(!ids.Contains("DisableWindowsDefenderModern"), "Defender optimization must not be exposed.");
Assert(!ids.Contains("DisableSystemRestore"), "System Restore must not be a reversible optimizer item.");
Assert(!ids.Contains("UninstallOneDrive"), "OneDrive uninstall must not be a reversible optimizer item.");
var contractIds = contracts.RootElement.GetProperty("contracts").EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
Assert(ids.SetEquals(contractIds), "Every optimizer item must have exactly one pinned contract.");
foreach (var contract in contracts.RootElement.GetProperty("contracts").EnumerateObject())
{
    Assert(contract.Value.GetProperty("yamlSha256").GetString()?.Length == 64, $"{contract.Name} YAML hash is invalid.");
    Assert(contract.Value.GetProperty("sigSha256").GetString()?.Length == 64, $"{contract.Name} signature hash is invalid.");
}
Assert(contracts.RootElement.GetProperty("contracts").GetProperty("PerformanceTweaks").GetProperty("profile").GetString() == "safe-performance", "Performance tweaks must use the safe profile.");
Assert(contracts.RootElement.GetProperty("contracts").GetProperty("DisableGameBarXbox").GetProperty("profile").GetString() == "safe-gamebar", "Xbox tweaks must preserve core Xbox services.");
Assert(contracts.RootElement.GetProperty("contracts").GetProperty("DisableChromeTelemetry").GetProperty("profile").GetString() == "safe-chrome", "Chrome telemetry must not use an irreversible process deny rule.");
Assert(contracts.RootElement.GetProperty("contracts").GetProperty("DisableTelemetryServices").GetProperty("profile").GetString() == "safe-telemetry", "Windows telemetry must use the filtered profile.");

using var tools = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "tools-manifest.json")));
Assert(tools.RootElement.GetProperty("tools").TryGetProperty("disableSecurityUpdate", out var updateTool), "The advanced Windows Update tool must remain in the toolbox catalog.");
Assert(updateTool.GetProperty("description").GetString()?.Contains("Defender", StringComparison.OrdinalIgnoreCase) == true, "The combined update/security tool must disclose Defender behavior.");
Assert(updateTool.GetProperty("sha256").GetString()?.Length == 64, "The combined update/security tool must retain a pinned hash.");
Assert(tools.RootElement.GetProperty("tools").TryGetProperty("smartDns", out var smartDnsManifest), "The smart DNS tool must be present in the toolbox catalog.");
Assert(smartDnsManifest.GetProperty("assetName").GetString() == "smart-dns-switcher.bat", "The smart DNS release asset name is invalid.");
Assert(smartDnsManifest.GetProperty("size").GetInt64() == 10784, "The smart DNS release asset size is invalid.");
Assert(smartDnsManifest.GetProperty("sha256").GetString() == "D2431CBC67B0522626CDE6CA411277E5DCFE05174E9E675DCDF220E9BD218E00", "The smart DNS release asset hash is invalid.");
Assert(smartDnsManifest.GetProperty("downloadUrl").GetString()?.EndsWith("/tools-v1/smart-dns-switcher.bat", StringComparison.Ordinal) == true, "The smart DNS release URL is invalid.");
Assert(smartDnsManifest.GetProperty("description").GetString()?.Contains("恢复 DHCP", StringComparison.Ordinal) == true, "The smart DNS description must disclose DHCP restoration.");
var smartDnsTool = (await new ToolboxService().GetToolsAsync()).Single(x => x.Id == "smartDns");
Assert(smartDnsTool.Name == "智能选择DNS工具", "The smart DNS display name is invalid.");
Assert(smartDnsTool.Dangerous, "The smart DNS tool must require the advanced-tool confirmation.");

var cleanupRoot = Path.Combine(Path.GetTempPath(), "WindowsLite-cache-test-" + Guid.NewGuid().ToString("N"));
try
{
    var cacheFile = Path.Combine(cleanupRoot, "cache", "driver-status.json");
    var transientArchive = Path.Combine(cleanupRoot, "cache", "transient", "BleachBit-6.0.2-portable.zip");
    var optimizerLog = Path.Combine(cleanupRoot, "runtime", "optimizerNXT", "optimizerNXT-logs", "scan.log");
    var retainedComponent = Path.Combine(cleanupRoot, "runtime", "optimizerNXT", "optimizerNXT.exe");
    var runtimePartial = retainedComponent + ".download";
    var runtimeTemporary = Path.Combine(cleanupRoot, "runtime", "bleachbit.staging-test", "extract.tmp");
    var legacyArchive = Path.Combine(cleanupRoot, "downloads", "BleachBit-6.0.2-portable.zip");
    var downloadPartial = Path.Combine(cleanupRoot, "downloads", "driver.zip.download");
    var retainedDriver = Path.Combine(cleanupRoot, "downloads", "verified-driver.exe");
    var retainedSettings = Path.Combine(cleanupRoot, "settings.json");
    var retainedLog = Path.Combine(cleanupRoot, "logs", "today.log");
    var retainedBackup = Path.Combine(cleanupRoot, "rollback-backups", "snapshot.json");
    var retainedReport = Path.Combine(cleanupRoot, "rollback-reports", "report.json");
    foreach (var file in new[] { cacheFile, transientArchive, optimizerLog, retainedComponent, runtimePartial, runtimeTemporary, legacyArchive, downloadPartial, retainedDriver, retainedSettings, retainedLog, retainedBackup, retainedReport })
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "test");
    }

    AppPaths.CleanupTransientCaches(cleanupRoot);
    Assert(!Directory.Exists(Path.Combine(cleanupRoot, "cache")), "The transient cache directory must be removed.");
    Assert(!File.Exists(optimizerLog) && !File.Exists(runtimePartial) && !File.Exists(runtimeTemporary) && !File.Exists(legacyArchive) && !File.Exists(downloadPartial), "Transient component artifacts must be removed.");
    Assert(File.Exists(retainedComponent) && File.Exists(retainedDriver), "Installed components and verified driver packages must be retained.");
    Assert(File.Exists(retainedSettings) && File.Exists(retainedLog) && File.Exists(retainedBackup) && File.Exists(retainedReport), "Settings, logs, rollback data, and reports must be retained.");
}
finally
{
    AppPaths.SafeDeleteDirectory(cleanupRoot);
}

var aggregate = typeof(OptimizerService).GetMethod("AggregateTargetStates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
OptimizationLiveState Aggregate(params OptimizationTargetState[] states) => (OptimizationLiveState)aggregate.Invoke(null, new object[] { states })!;
Assert(Aggregate(OptimizationTargetState.Default, OptimizationTargetState.Default) == OptimizationLiveState.Default, "All-default targets must aggregate to Default.");
Assert(Aggregate(OptimizationTargetState.Default, OptimizationTargetState.Unoptimized) == OptimizationLiveState.Default, "Existing non-optimized values must remain safely switchable instead of becoming Unknown.");
Assert(Aggregate(OptimizationTargetState.Optimized, OptimizationTargetState.Optimized) == OptimizationLiveState.Optimized, "All-optimized targets must aggregate to Optimized.");
Assert(Aggregate(OptimizationTargetState.Optimized, OptimizationTargetState.Default) == OptimizationLiveState.Mixed, "Optimized plus default targets must aggregate to Mixed.");
Assert(Aggregate(OptimizationTargetState.Optimized, OptimizationTargetState.Unoptimized) == OptimizationLiveState.Mixed, "Optimized plus preserved external values must aggregate to Mixed.");
Assert(Aggregate(OptimizationTargetState.Default, OptimizationTargetState.Diverged, OptimizationTargetState.Unknown) == OptimizationLiveState.NeedsReview, "Zero optimized targets with unresolved values must require review instead of becoming Mixed.");
Assert(Aggregate(OptimizationTargetState.Optimized, OptimizationTargetState.Unavailable) == OptimizationLiveState.OptimizedWithSkips, "Missing optional targets must be excluded from the applicable denominator.");
Assert(Aggregate(OptimizationTargetState.Optimized, OptimizationTargetState.Skipped) == OptimizationLiveState.OptimizedWithSkips, "Safety-blocked legacy targets must remain visible as skips.");
Assert(Aggregate(OptimizationTargetState.Default, OptimizationTargetState.Unavailable) == OptimizationLiveState.Default, "A missing target must not turn an otherwise-default item into Mixed.");

Assert(new OptimizerState().SchemaVersion == 4, "Optimizer state must use schema v4 for rollback history.");
Assert(typeof(OptimizerService).GetMethod("RestoreAsync")!.ReturnType == typeof(Task<RollbackExecutionReport>), "RestoreAsync must return a structured verified rollback report.");
Assert(typeof(OptimizerService).GetMethods().Any(x => x.Name == "BuildApplyPlanAsync" && x.GetParameters().FirstOrDefault()?.ParameterType == typeof(string))
    && typeof(OptimizerService).GetMethod("ApplyRemainingAsync") != null, "Mixed-state remaining-target APIs are missing.");
var classifySnapshot = typeof(OptimizerService).GetMethod("ClassifySnapshotRollback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
RollbackOutcome SnapshotOutcome(bool actionsOk, int verified, int total) => (RollbackOutcome)classifySnapshot.Invoke(null, new object[] { actionsOk, verified, total })!;
Assert(SnapshotOutcome(true, 1, 1) == RollbackOutcome.RestoredToSnapshot, "A rollback may succeed only after every target verifies.");
Assert(SnapshotOutcome(true, 0, 1) == RollbackOutcome.Failed, "A command exit code without state verification must not report success.");
Assert(SnapshotOutcome(false, 1, 2) == RollbackOutcome.Partial, "Partially restored snapshots must remain retryable.");
var classifyDefault = typeof(OptimizerService).GetMethod("ClassifyDefaultRollback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
RollbackOutcome DefaultOutcome(bool failed, bool immediateDefault, bool delayedDefault) => (RollbackOutcome)classifyDefault.Invoke(null, new object[] { failed, immediateDefault, delayedDefault })!;
Assert(DefaultOutcome(false, false, false) == RollbackOutcome.ExternallyReapplied, "A value such as AllowTelemetry that persists after a successful delete must be reported as externally reapplied.");

var registrySnapshotEquals = typeof(OptimizerService).GetMethod("RegistrySnapshotEquals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var snapshotDword0 = new RegistrySnapshot("HKLM", @"SOFTWARE\Test", "Value", true, true, "DWord", null, null, null, 0);
var snapshotDword1 = snapshotDword0 with { IntegerValue = 1 };
var snapshotString0 = snapshotDword0 with { Kind = "String", IntegerValue = null, StringValue = "0" };
Assert((bool)registrySnapshotEquals.Invoke(null, new object[] { snapshotDword0, snapshotDword0 })!, "Identical registry snapshots must verify.");
Assert(!(bool)registrySnapshotEquals.Invoke(null, new object[] { snapshotDword0, snapshotDword1 })!, "Registry rollback verification must detect a wrong value.");
Assert(!(bool)registrySnapshotEquals.Invoke(null, new object[] { snapshotDword0, snapshotString0 })!, "Registry rollback verification must detect a wrong value type.");

Assert(typeof(OptimizerService).GetMethod("SkipTargetAsync") != null && typeof(OptimizerService).GetMethod("RemoveTargetSkipAsync") != null, "Per-target skip APIs are missing.");
var stableDigest = typeof(OptimizerService).GetMethod("StableTargetDigest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var applyUserSkips = typeof(OptimizerService).GetMethod("ApplyUserSkipDecisions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var targetName = @"HKCU\SOFTWARE\SystemOptimizerLite\TestValue";
var targetDigest = (string)stableDigest.Invoke(null, new object[] { targetName })!;
var defaultTarget = new OptimizationTargetInfo("注册表", targetName, OptimizationTargetState.Default, "test", "registry:test", TargetApplicability.Applicable, TargetOutcome.NotOptimized);
var defaultLive = new OptimizationStateInfo("Test", OptimizationLiveState.Default, "test", 0, 1, new List<OptimizationTargetInfo> { defaultTarget });
var skipDecision = new OptimizationSkipDecision("Test", "registry:test", "contract", Environment.OSVersion.Version.Build, targetDigest, "用户选择跳过", DateTimeOffset.Now);
var skippedLive = (OptimizationStateInfo)applyUserSkips.Invoke(null, new object[] { defaultLive, new List<OptimizationSkipDecision> { skipDecision } })!;
Assert(skippedLive.TargetDetails.Single().Applicability == TargetApplicability.UserSkipped && skippedLive.TargetDetails.Single().State == OptimizationTargetState.Skipped, "A user-skipped target must have a distinct skipped state.");
Assert(skippedLive.TotalTargets == 0 && skippedLive.SkippedTargets == 1 && skippedLive.State == OptimizationLiveState.Default, "A skipped default target must leave the item unoptimized and outside the applicable denominator.");
var excludeSnapshotTargets = typeof(OptimizerService).GetMethod("ExcludeSnapshotTargets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var skipSnapshot = new Snapshot
{
    Registry = new List<RegistrySnapshot>
    {
        new("HKCU", @"SOFTWARE\SystemOptimizerLite", "Skipped", true, false, "", null, null, null, null),
        new("HKCU", @"SOFTWARE\SystemOptimizerLite", "Kept", true, false, "", null, null, null, null)
    }
};
var skippedSnapshotDigest = (string)stableDigest.Invoke(null, new object[] { @"HKCU\SOFTWARE\SystemOptimizerLite\Skipped" })!;
var filteredSnapshot = (Snapshot)excludeSnapshotTargets.Invoke(null, new object[] { skipSnapshot, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { skippedSnapshotDigest } })!;
Assert(filteredSnapshot.Registry.Count == 1 && filteredSnapshot.Registry[0].Name == "Kept", "Whole-item rollback must not touch a user-skipped target.");

var kindMatches = typeof(OptimizerService).GetMethod("RegistryKindMatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var registryMatches = typeof(OptimizerService).GetMethod("RegistryMatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var qmDesired = new RegistryDesired("HKCU", @"SOFTWARE\Microsoft\Office\16.0\Common", "QMEnable", "DWord", "0", false, "Disable Office QM and Feedback");
Assert((bool)kindMatches.Invoke(null, new object[] { Microsoft.Win32.RegistryValueKind.DWord, "DWord" })!, "QMEnable DWORD type must be recognized as compatible.");
Assert(!(bool)registryMatches.Invoke(null, new object[] { 2, qmDesired })!, "QMEnable=2 must be recognized as a legitimate non-optimized value, not the YAML value 0.");

var trusted = new DeviceIdentity("OEM BIOS", "ABC12345", "Contoso", "Model 1", "SKU", "Contoso", "Model 1", "ABC12345", "SKU", "11111111-2222-3333-4444-555555555555", "Contoso", "Board 1", "BOARD123", "BOARD123");
var trustedResult = DeviceIdentifierResolver.Assess(trusted);
Assert(trustedResult.Confidence == IdentityConfidence.High, "Matching OEM serial fields should be high confidence.");
Assert(trustedResult.Candidates.All(x => !x.MaskedValue.Contains("ABC12345", StringComparison.Ordinal)), "Raw serial must not appear in masked candidates.");

var conflict = trusted with { ProductIdentifyingNumber = "DIFFERENT", ProductUuid = "00000000-0000-0000-0000-000000000000" };
var conflictResult = DeviceIdentifierResolver.Assess(conflict);
Assert(conflictResult.Confidence == IdentityConfidence.Low, "Conflicting firmware serials must be low confidence.");
Assert(conflictResult.Conflicts.Count > 0, "Low confidence conflict must include an explanation.");
var confidenceText = typeof(MainWindow).GetMethod("IdentityConfidenceText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
Assert(!((string)confidenceText.Invoke(null, new object[] { IdentityConfidence.High })!).Contains("高置信度", StringComparison.Ordinal), "The driver UI must not display the high-confidence label.");

var navigationIconGeometry = typeof(MainWindow).GetMethod("NavigationIconGeometry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
object NavigationGeometry(string page) => navigationIconGeometry.Invoke(null, new object[] { page })!;
foreach (var page in new[] { "系统优化", "系统清理", "驱动管理", "工具箱", "未定义页面" })
{
    var geometry = NavigationGeometry(page);
    var bounds = geometry.GetType().GetProperty("Bounds")!.GetValue(geometry)!;
    var boundsType = bounds.GetType();
    var width = (double)boundsType.GetProperty("Width")!.GetValue(bounds)!;
    var height = (double)boundsType.GetProperty("Height")!.GetValue(bounds)!;
    Assert(width > 0 && height > 0, $"Navigation icon geometry for {page} must have visible bounds.");
}
Assert(ReferenceEquals(NavigationGeometry("设置"), NavigationGeometry("未定义页面")), "Unknown navigation pages must use the cached default vector icon.");

var runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemOptimizerLite", "runtime", "optimizerNXT");
var genericRegistryExplanations = new List<string>();
if (Directory.Exists(runtime))
{
    var parser = typeof(OptimizerService).GetMethod("ExtractSnapshotTargets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    foreach (var item in items)
    {
        var yamlName = item.GetProperty("yamlFile").GetString()!;
        var yamlPath = Path.Combine(runtime, yamlName);
        if (!File.Exists(yamlPath)) continue;
        var parsed = (SnapshotTargets)parser.Invoke(null, new object[] { yamlPath })!;
        Assert(parsed.RegistryDesired.Count + parsed.ServiceDesired.Count + parsed.Commands.Count > 0, $"{yamlName} has no verifiable targets.");
        Assert(parsed.Commands.All(command => command.Contains("schtasks", StringComparison.OrdinalIgnoreCase) || command.Contains("fsutil", StringComparison.OrdinalIgnoreCase) || command.Contains("icacls", StringComparison.OrdinalIgnoreCase)), $"{yamlName} contains an unsupported command.");
        foreach (var registry in parsed.RegistryDesired)
        {
            var explanation = RegistryEffectCatalog.Describe(registry);
            Assert(!string.IsNullOrWhiteSpace(registry.ActionName), $"{yamlName} registry target {registry.Name} lost its YAML action context.");
            Assert(explanation.Contains("功能作用：", StringComparison.Ordinal) && explanation.Contains("优化后效果：", StringComparison.Ordinal), $"{yamlName} registry target {registry.Name} has no complete Chinese explanation.");
            Assert(explanation.Length >= 20, $"{yamlName} registry target {registry.Name} explanation is too vague.");
            if (explanation.Contains("该优化项目对应的 Windows 或软件策略参数", StringComparison.Ordinal)) genericRegistryExplanations.Add($"{yamlName}: {registry.Name} ({registry.ActionName})");
        }
        foreach (var service in parsed.ServiceDesired)
        {
            var explanation = SystemTargetEffectCatalog.DescribeService(service.Name, service.Mode);
            Assert(explanation.Contains("功能作用：") && explanation.Contains("优化后效果：") && explanation.Contains("潜在影响："), $"{yamlName} service {service.Name} has no complete Chinese explanation.");
        }
        foreach (var command in parsed.Commands)
        {
            var explanation = SystemTargetEffectCatalog.DescribeCommand(command);
            Assert(explanation.Contains("功能作用：") && explanation.Contains("优化后效果：") && explanation.Contains("潜在影响："), $"{yamlName} command has no complete Chinese explanation.");
        }
        if (parsed.UnsupportedActions.Count > 0)
            Assert(yamlName is "disable-chrome-telemetry.yaml" or "disable-telemetry-services.yaml", $"{yamlName} contains an undeclared unsupported action.");
    }

    var safetyProfile = typeof(OptimizerService).GetMethod("ApplySafetyProfile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    SnapshotTargets Parsed(string file) => (SnapshotTargets)parser.Invoke(null, new object[] { Path.Combine(runtime, file) })!;
    (SnapshotTargets Effective, List<OptimizationTargetInfo> Skipped) Profile(string id, string file)
    {
        var itemElement = items.Single(x => x.GetProperty("id").GetString() == id);
        var item = new OptimizerItem(id, itemElement.GetProperty("title").GetString()!, itemElement.GetProperty("description").GetString()!, itemElement.GetProperty("tab").GetString()!, itemElement.GetProperty("group").GetString()!, itemElement.GetProperty("risk").GetString()!, file, true, "", itemElement.GetProperty("windows").GetString()!, itemElement.GetProperty("category").GetString()!, false, false, 0);
        return ((SnapshotTargets, List<OptimizationTargetInfo>))safetyProfile.Invoke(null, new object[] { item, Parsed(file) })!;
    }
    var performance = Profile("PerformanceTweaks", "enable-performance-tweaks.yaml");
    Assert(performance.Effective.RegistryDesired.Count == 1 && performance.Effective.RegistryDesired[0].Name == "EnableTransparency", "Safe performance profile must only keep the reversible transparency target.");
    Assert(performance.Effective.ServiceDesired.Count == 0 && performance.Effective.Commands.Count == 0, "Safe performance profile must not disable services or execute legacy commands.");
    var xbox = Profile("DisableGameBarXbox", "enable-game-mode.yaml");
    Assert(xbox.Effective.ServiceDesired.Count == 0 && xbox.Effective.Commands.Count == 0, "Safe Xbox profile must preserve Xbox services and scheduled tasks.");
    var chrome = Profile("DisableChromeTelemetry", "disable-chrome-telemetry.yaml");
    Assert(chrome.Effective.RegistryDesired.Count == 6 && chrome.Skipped.Any(x => x.Kind == "进程控制"), "Safe Chrome profile must keep policy values and skip processControl.");
    var telemetry = Profile("DisableTelemetryServices", "disable-telemetry-services.yaml");
    Assert(telemetry.Effective.ServiceDesired.All(x => x.Name is "DiagTrack" or "dmwappushservice"), "Safe telemetry profile contains an unapproved service.");
    Assert(telemetry.Effective.Commands.All(x => x.Contains("Customer Experience Improvement Program", StringComparison.OrdinalIgnoreCase) || x.Contains("Sqm-Tasks", StringComparison.OrdinalIgnoreCase) || x.Contains("Feedback\\Siuf", StringComparison.OrdinalIgnoreCase)), "Safe telemetry profile contains an unrelated task or command.");
}
Assert(genericRegistryExplanations.Count == 0, "Generic registry explanations remain:\n" + string.Join("\n", genericRegistryExplanations));

var restoreTask = typeof(OptimizerService).GetMethod("SetScheduledTaskEnabledForRestore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var missingTaskResult = (RestoreResult)restoreTask.Invoke(null, new object[] { @"\SystemOptimizerLite\Tests\DefinitelyMissing", true })!;
Assert(missingTaskResult.Ok, "A scheduled task absent from the current Windows build must be skipped instead of failing default restore: " + missingTaskResult.Message);
Assert(missingTaskResult.Message.Contains("不适用项跳过", StringComparison.Ordinal), "Missing scheduled task result must explain the localized skip reason.");

var serviceDescription = SystemTargetEffectCatalog.DescribeService("WSearch", "disable");
Assert(serviceDescription.Contains("功能作用：") && serviceDescription.Contains("优化后效果：") && serviceDescription.Contains("潜在影响："), "Service descriptions must be complete Chinese explanations.");
var taskDescription = SystemTargetEffectCatalog.DescribeTask(@"\Microsoft\Office\OfficeTelemetryAgentLogOn");
Assert(taskDescription.Contains("Office 遥测", StringComparison.Ordinal), "Known scheduled tasks must have a specific Chinese explanation.");

var cleanerService = new CleanerService();
if (cleanerService.IsInstalled())
{
    var cleanerItems = await cleanerService.GetItemsAsync();
    Assert(cleanerItems.Count >= 267, $"Expected the XML catalog plus built-in system cleaners, got {cleanerItems.Count}.");
    Assert(cleanerItems.Single(x => x.Id == "system.tmp").DefaultSelected, "system.tmp must be part of balanced defaults.");
    Assert(cleanerItems.Single(x => x.Id == "windows_explorer.thumbnails").DefaultSelected, "Balanced defaults must include the reviewed thumbnail cache.");
    Assert(cleanerItems.Where(x => x.DefaultSelected).All(x => x.Applicable && x.Safety is CleanerSafetyTier.Recommended or CleanerSafetyTier.BalancedOnly), "Default cleaners must be applicable and explicitly reviewed.");
    string[] forbiddenDefaults = ["firefox.backup", "vscode.backup", "windows_defender.logs", "hexchat.logs", "system.recycle_bin", "vlc.memory_dump"];
    foreach (var id in forbiddenDefaults)
        Assert(!cleanerItems.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.DefaultSelected ?? true, $"Sensitive cleaner {id} must not be selected by default.");
}

Console.WriteLine("All WindowsLite reliability smoke tests passed.");

if (Environment.GetEnvironmentVariable("WINDOWSLITE_AUDIT") == "1")
{
    var service = new OptimizerService();
    var auditIds = new[] { "DisableOfficeTelemetry", "DisableFirefoxTelemetry", "PerformanceTweaks", "DisableGameBarXbox", "DisableTelemetryServices" };
    var auditItems = (await service.GetItemsAsync()).Where(x => auditIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    var live = await service.GetLiveStatesAsync(auditItems, forceRefresh: true);
    foreach (var item in auditItems)
    {
        var state = live[item.Id];
        Console.WriteLine($"AUDIT {item.Id}: {state.State}; applicable={state.MatchedTargets}/{state.TotalTargets}; skipped={state.SkippedTargets}; {state.Detail}");
        foreach (var target in state.TargetDetails.Where(x => x.State is OptimizationTargetState.Unknown or OptimizationTargetState.Diverged or OptimizationTargetState.Unavailable).Take(8))
            Console.WriteLine($"  {target.Kind} {target.Target}: {target.State}/{target.Applicability} - {target.Detail.Split('\n').Last()}");
    }
    if (cleanerService.IsInstalled())
    {
        var catalog = await cleanerService.GetItemsAsync();
        Console.WriteLine($"AUDIT Cleaner: definitions={catalog.Count}; applicable={catalog.Count(x => x.Applicable)}; balanced-defaults={catalog.Count(x => x.DefaultSelected)}");
    }
}

internal sealed class SequenceHttpHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

    public List<long?> RangeStarts { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RangeStarts.Add(request.Headers.Range?.Ranges.SingleOrDefault()?.From);
        if (_responses.Count == 0) throw new HttpRequestException("No queued response remains for the test request.");
        var response = _responses.Dequeue()(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
