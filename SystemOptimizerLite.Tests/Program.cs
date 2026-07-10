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
Assert(Aggregate(OptimizationTargetState.Default, OptimizationTargetState.Diverged, OptimizationTargetState.Unknown) == OptimizationLiveState.Unknown, "Zero optimized targets with unresolved values must not aggregate to Mixed.");

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
    }
}
Assert(genericRegistryExplanations.Count == 0, "Generic registry explanations remain:\n" + string.Join("\n", genericRegistryExplanations));

var restoreTask = typeof(OptimizerService).GetMethod("SetScheduledTaskEnabledForRestore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var missingTaskResult = (RestoreResult)restoreTask.Invoke(null, new object[] { @"\SystemOptimizerLite\Tests\DefinitelyMissing", true })!;
Assert(missingTaskResult.Ok, "A scheduled task absent from the current Windows build must be skipped instead of failing default restore: " + missingTaskResult.Message);
Assert(missingTaskResult.Message.Contains("不适用项跳过", StringComparison.Ordinal), "Missing scheduled task result must explain the localized skip reason.");

Console.WriteLine("All WindowsLite reliability smoke tests passed.");
