using System.Text.Json;
using SystemOptimizerLite;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
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
