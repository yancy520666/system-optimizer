namespace SystemOptimizerLite;

/// <summary>把 OptimizerNXT YAML 中的注册表目标转换为面向用户的中文功能说明。</summary>
public static class RegistryEffectCatalog
{
    private static readonly Dictionary<string, string> ValueEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AllowTelemetry"] = "Windows 诊断与遥测数据的允许级别",
        ["MaxTelemetryAllowed"] = "Windows 允许收集的最高诊断数据级别",
        ["DisableTelemetry"] = "应用或组件的遥测数据收集",
        ["MetricsReportingEnabled"] = "使用情况和性能指标上报",
        ["DeviceMetricsReportingEnabled"] = "设备级使用指标上报",
        ["PersonalizationReportingEnabled"] = "个性化相关数据上报",
        ["EnableLogging"] = "诊断日志记录",
        ["VerboseLogging"] = "详细诊断日志记录",
        ["EnableUpload"] = "诊断日志或遥测文件上传",
        ["UserFeedbackAllowed"] = "应用内用户反馈与调查提示",
        ["DoNotShowFeedbackNotifications"] = "Windows 反馈通知",
        ["DisableFeedbackDialog"] = "Visual Studio 反馈对话框",
        ["CEIPEnable"] = "客户体验改善计划数据收集",
        ["QMEnable"] = "Office 质量监测与体验数据上报",
        ["DisableAIDataAnalysis"] = "使用诊断数据进行 AI 分析和个性化",
        ["AllowExperimentation"] = "微软下发实验性功能和配置测试",
        ["AllowAdvertising"] = "Windows 广告标识和个性化广告",
        ["DisableTailoredExperiencesWithDiagnosticData"] = "使用诊断数据提供定制体验",
        ["AllowCortana"] = "Cortana 助手",
        ["CortanaConsent"] = "当前用户对 Cortana 的授权",
        ["BingSearchEnabled"] = "开始菜单搜索中的 Bing 联网结果",
        ["DisableWebSearch"] = "Windows 搜索联网查询",
        ["ConnectedSearchUseWeb"] = "系统搜索使用互联网结果",
        ["ConnectedSearchUseWebOverMeteredConnections"] = "按流量计费网络上的联网搜索",
        ["DisableSearchBoxSuggestions"] = "搜索框中的云端建议和联网内容",
        ["AllowCloudSearch"] = "Windows 搜索读取云端内容",
        ["AllowSearchToUseLocation"] = "搜索服务使用设备位置",
        ["DeviceHistoryEnabled"] = "设备搜索历史记录",
        ["IsDeviceSearchHistoryEnabled"] = "当前用户的搜索历史记录",
        ["SafeSearchMode"] = "联网搜索的安全筛选级别",
        ["TurnOffWindowsCopilot"] = "Windows Copilot 功能",
        ["ShowCopilotButton"] = "任务栏 Copilot 按钮",
        ["WebWidgetAllowed"] = "Windows Web 小组件",
        ["EnableFeeds"] = "新闻和兴趣信息流",
        ["AllowNewsAndInterests"] = "任务栏新闻和兴趣功能",
        ["IsFeedsAvailable"] = "系统信息流入口是否可用",
        ["ShellFeedsTaskbarOpenOnHover"] = "鼠标悬停时打开任务栏信息流",
        ["ShellFeedsTaskbarViewMode"] = "任务栏信息流的显示模式",
        ["DisableWindowsSpotlightFeatures"] = "Windows 聚焦锁屏和推荐内容",
        ["SpotlightExperiencesAndRecommendationsEnabled"] = "聚焦体验与系统推荐",
        ["ContentDeliveryAllowed"] = "Windows 内容推荐和静默内容投放",
        ["SubscribedContentEnabled"] = "订阅式推荐内容",
        ["SilentInstalledAppsEnabled"] = "Windows 静默安装推荐应用",
        ["PreInstalledAppsEnabled"] = "系统预装推荐应用",
        ["OemPreInstalledAppsEnabled"] = "OEM 推荐应用自动安装",
        ["DisableWindowsConsumerFeatures"] = "Windows 消费者体验和推荐应用",
        ["DisableSoftLanding"] = "新功能推广和使用建议",
        ["DisableCloudOptimizedContent"] = "微软云端优化和推荐内容",
        ["SystemPaneSuggestionsEnabled"] = "设置界面中的系统建议",
        ["ShowSyncProviderNotifications"] = "资源管理器中的同步提供商推广通知",
        ["AllowLocation"] = "Windows 定位服务",
        ["DisableLocation"] = "应用和系统的位置访问",
        ["DisableLocationScripting"] = "脚本调用定位服务",
        ["DisableWindowsLocationProvider"] = "Windows 内置位置提供程序",
        ["AllowFindMyDevice"] = "查找我的设备功能",
        ["LocationSyncEnabled"] = "跨设备同步位置数据",
        ["AllowWiFiHotSpotReporting"] = "Wi-Fi 热点信息上报",
        ["AllowAutoConnectToWiFiSenseHotspots"] = "自动连接 Wi-Fi Sense 热点",
        ["AutoConnectAllowedOEM"] = "OEM 推荐热点自动连接",
        ["Hotspot2SignUp"] = "Hotspot 2.0 在线注册",
        ["AllowCrossDeviceClipboard"] = "跨设备云剪贴板",
        ["AllowClipboardHistory"] = "剪贴板历史策略",
        ["EnableClipboardHistory"] = "当前用户剪贴板历史",
        ["EnableCdp"] = "Windows 跨设备体验平台",
        ["EnableActivityFeed"] = "Windows 活动历史时间线",
        ["PublishUserActivities"] = "向微软或其他设备发布用户活动",
        ["UploadUserActivities"] = "上传活动历史到云端",
        ["AllowProjectionToPC"] = "投影到此电脑功能",
        ["LetAppsSyncWithDevices"] = "应用与未配对设备通信和同步",
        ["LetAppsActivateWithVoice"] = "应用通过语音在后台激活",
        ["RemoteStartupDisabled"] = "远程设备唤醒或启动应用",
        ["AllowInputPersonalization"] = "输入、手写和键入个性化",
        ["AllowLinguisticDataCollection"] = "键入和语言数据收集",
        ["PreventHandwritingDataSharing"] = "手写识别数据共享",
        ["RestrictImplicitInkCollection"] = "隐式收集手写墨迹数据",
        ["RestrictImplicitTextCollection"] = "隐式收集键入文本数据",
        ["AllowWindowsInkWorkspace"] = "Windows Ink 工作区",
        ["AllowSuggestedAppsInWindowsInkWorkspace"] = "Ink 工作区中的推荐应用",
        ["EnableAutocorrection"] = "触摸键盘自动更正",
        ["EnableSpellchecking"] = "拼写检查",
        ["EnableTextPrediction"] = "键入文字预测",
        ["EnablePredictionSpaceInsertion"] = "文字预测后的自动空格",
        ["EnableDoubleTapSpace"] = "双击空格输入句号",
        ["EnableInkingWithTouch"] = "使用触摸进行墨迹书写",
        ["AllowGameDVR"] = "Xbox Game DVR 录屏策略",
        ["GameDVR_Enabled"] = "当前用户的游戏录制功能",
        ["AppCaptureEnabled"] = "应用和游戏画面捕获",
        ["AudioCaptureEnabled"] = "游戏录制中的音频捕获",
        ["CursorCaptureEnabled"] = "游戏录制中的鼠标指针捕获",
        ["UseNexusForGameBarEnabled"] = "Xbox Game Bar 新版界面",
        ["AutoGameModeEnabled"] = "自动游戏模式",
        ["AllowAutoGameMode"] = "系统自动启用游戏模式",
        ["NoAutoUpdate"] = "Windows Update 是否允许自动检查和安装",
        ["AUOptions"] = "Windows Update 下载与安装通知方式",
        ["NoAutoRebootWithLoggedOnUsers"] = "用户登录时是否允许更新自动重启",
        ["DODownloadMode"] = "传递优化使用局域网或互联网对等下载的方式",
        ["SystemSettingsDownloadMode"] = "设置应用中的传递优化下载来源",
        ["AutoDownload"] = "Microsoft Store 应用自动更新",
        ["AllowSpeechModelUpdate"] = "语音识别模型自动更新",
        ["ExcludeWUDriversInQualityUpdate"] = "质量更新中是否包含硬件驱动",
        ["PreventDeviceMetadataFromNetwork"] = "从互联网下载设备图标和元数据",
        ["SmartScreenEnabled"] = "浏览器或应用的 SmartScreen 检查",
        ["EnableSmartScreen"] = "Windows SmartScreen 安全检查",
        ["SmartScreenPuaEnabled"] = "SmartScreen 阻止潜在有害应用",
        ["ShellSmartScreenLevel"] = "资源管理器运行未知文件时的 SmartScreen 处置级别",
        ["SaveZoneInformation"] = "下载文件的互联网来源标记",
        ["ScanWithAntiVirus"] = "打开附件前调用杀毒软件扫描",
        ["EnableVirtualizationBasedSecurity"] = "基于虚拟化的安全性 VBS",
        ["PlatformAoAcOverride"] = "现代待机（S0 低功耗空闲）平台覆盖开关",
        ["AllowUpgradesWithUnsupportedTPMOrCPU"] = "不受支持 TPM 或 CPU 设备的 Windows 升级",
        ["BypassTPMCheck"] = "Windows 安装程序 TPM 检查",
        ["BypassSecureBootCheck"] = "Windows 安装程序安全启动检查",
        ["BypassRAMCheck"] = "Windows 安装程序内存容量检查",
        ["BypassStorageCheck"] = "Windows 安装程序磁盘容量检查",
        ["BypassCPUCheck"] = "Windows 安装程序 CPU 兼容性检查",
        ["EnablePrefetcher"] = "Windows 应用和启动预读取",
        ["EnableSuperfetch"] = "SysMain/Superfetch 预读取策略",
        ["NetworkThrottlingIndex"] = "多媒体负载下的网络限速",
        ["SystemResponsiveness"] = "多媒体任务为后台服务保留的 CPU 百分比",
        ["GPU Priority"] = "多媒体或游戏任务的 GPU 调度优先级",
        ["Priority"] = "多媒体任务的 CPU 调度优先级",
        ["Scheduling Category"] = "多媒体任务的调度类别",
        ["SFIO Priority"] = "多媒体任务的存储 I/O 优先级",
        ["HwSchMode"] = "硬件加速 GPU 调度",
        ["EnableFrameServerMode"] = "摄像头 Media Foundation 帧服务器模式",
        ["EnableTransparency"] = "Windows 透明效果",
        ["ColorPrevalence"] = "开始菜单、任务栏和标题栏是否使用系统强调色",
        ["MenuShowDelay"] = "菜单弹出前的等待时间",
        ["MouseHoverTime"] = "鼠标悬停触发提示前的等待时间",
        ["HungAppTimeout"] = "系统判断应用无响应前的等待时间",
        ["WaitToKillAppTimeout"] = "注销或关机时等待应用退出的时间",
        ["WaitToKillServiceTimeout"] = "关机时等待服务退出的时间",
        ["AutoEndTasks"] = "注销或关机时自动结束无响应应用",
        ["LowLevelHooksTimeout"] = "系统等待键盘、鼠标等低级钩子响应的最长时间",
        ["CrashDumpEnabled"] = "系统或服务崩溃时是否生成转储文件",
        ["DoReport"] = "Windows 错误报告是否向微软发送故障信息",
        ["MaintenanceDisabled"] = "Windows 自动维护任务",
        ["NonBestEffortLimit"] = "QoS 为非尽力而为流量保留的带宽比例",
        ["Append Completion"] = "命令输入时按路径补全文件名",
        ["AutoSuggest"] = "资源管理器和运行框的历史输入自动建议",
        ["fAllowToGetHelp"] = "Windows 远程协助连接",
        ["SbEnable"] = "应用程序兼容性 Shim 引擎",
        ["LongPathsEnabled"] = "Win32 长路径支持",
        ["HideFileExt"] = "资源管理器是否隐藏已知文件扩展名",
        ["ShowRecent"] = "开始菜单和跳转列表中的最近项目",
        ["ShowFrequent"] = "资源管理器中的常用文件夹",
        ["ShowTaskViewButton"] = "任务栏任务视图按钮",
        ["TaskbarAl"] = "Windows 11 任务栏图标对齐方式",
        ["TaskbarDa"] = "任务栏小组件按钮",
        ["TaskbarMn"] = "任务栏聊天按钮",
        ["HideSCAMeetNow"] = "任务栏 Meet Now 图标",
        ["PeopleBand"] = "任务栏人脉按钮",
        ["DisallowShaking"] = "Aero Shake 摇动窗口最小化功能",
        ["EnableSnapAssistFlyout"] = "窗口贴靠布局弹出面板",
        ["NoLowDiskSpaceChecks"] = "磁盘空间不足通知",
        ["NoResolveTrack"] = "快捷方式目标跟踪",
        ["NoResolveSearch"] = "快捷方式目标搜索",
        ["LinkResolveIgnoreLinkInfo"] = "快捷方式分布式链接跟踪信息",
        ["NoInternetOpenWith"] = "未知文件类型的联网查找应用",
        ["DefaultBrowserSettingsCampaignEnabled"] = "浏览器默认设置推广提示",
        ["HubsSidebarEnabled"] = "Edge 侧边栏",
        ["ExtensionManifestV2Availability"] = "Chrome/Edge Manifest V2 扩展兼容策略"
    };

    public static string Describe(RegistryDesired desired)
    {
        var function = DescribeFunction(desired);
        var result = DescribeResult(desired);
        return $"功能作用：{function}\n优化后效果：{result}";
    }

    private static string DescribeFunction(RegistryDesired desired)
    {
        if (ValueEffects.TryGetValue(desired.Name, out var effect)) return effect;
        var name = desired.Name;
        var path = desired.Path;
        var action = desired.ActionName;

        if (path.Contains(@"Office\16.0\OSM\PreventedApplications", StringComparison.OrdinalIgnoreCase))
            return $"阻止 Office OSM 监测 {OfficeApplication(name)} 的加载和使用情况";
        if (path.Contains(@"Office\16.0\OSM\PreventedSolutionTypes", StringComparison.OrdinalIgnoreCase))
            return $"阻止 Office OSM 监测 {OfficeSolution(name)} 类型内容";
        if (path.Contains(@"shell\OpenWithCMD", StringComparison.OrdinalIgnoreCase))
            return "资源管理器右键菜单中的“在此处打开命令提示符”入口、图标或执行命令";
        if (path.Contains(@"shell\runas", StringComparison.OrdinalIgnoreCase))
            return "文件或文件夹右键菜单中的“取得所有权”入口和管理员授权命令";
        if (path.Contains("ContentDeliveryManager", StringComparison.OrdinalIgnoreCase) || name.StartsWith("SubscribedContent-", StringComparison.OrdinalIgnoreCase))
            return "Windows 推荐内容、技巧、广告或静默应用投放";
        if (name.Contains("Telemetry", StringComparison.OrdinalIgnoreCase) || name.Contains("Logging", StringComparison.OrdinalIgnoreCase) || name.Contains("Upload", StringComparison.OrdinalIgnoreCase))
            return "对应组件的遥测、诊断日志或数据上传";
        if (name.Contains("Feedback", StringComparison.OrdinalIgnoreCase) || name.Contains("OptIn", StringComparison.OrdinalIgnoreCase) || name.Contains("OptedIn", StringComparison.OrdinalIgnoreCase))
            return "对应组件的体验改善计划、反馈提示或数据收集授权";
        if (name.StartsWith("Disable", StringComparison.OrdinalIgnoreCase)) return $"“{ReadableName(name[7..])}”功能的禁用策略";
        if (name.StartsWith("Enable", StringComparison.OrdinalIgnoreCase)) return $"“{ReadableName(name[6..])}”功能的启用状态";
        if (name.StartsWith("Allow", StringComparison.OrdinalIgnoreCase)) return $"是否允许“{ReadableName(name[5..])}”功能";
        if (name.StartsWith("Prevent", StringComparison.OrdinalIgnoreCase)) return $"阻止“{ReadableName(name[7..])}”的策略";
        if (name.StartsWith("Show", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Hide", StringComparison.OrdinalIgnoreCase)) return $"“{ReadableName(name)}”界面元素的显示状态";
        if (name.StartsWith("Bypass", StringComparison.OrdinalIgnoreCase)) return $"绕过“{ReadableName(name[6..])}”检查的安装策略";
        return DescribeAction(action, path);
    }

    private static string DescribeResult(RegistryDesired desired)
    {
        if (desired.Delete) return "删除该值，移除 YAML 指定的菜单、策略或旧配置。";
        var name = desired.Name;
        var value = desired.Value.Trim().Trim('"', '\'');
        if (name.Equals("AUOptions", StringComparison.OrdinalIgnoreCase) && value == "2") return "设为“下载前通知、安装前通知”，避免后台自动下载安装更新。";
        if (name.Equals("DODownloadMode", StringComparison.OrdinalIgnoreCase) && value == "0") return "只从微软更新服务器下载，不参与局域网或互联网对等分发。";
        if (name.Equals("ExtensionManifestV2Availability", StringComparison.OrdinalIgnoreCase) && value == "2") return "允许继续运行 Manifest V2 扩展，提高旧扩展兼容性。";
        if (name.Equals("NetworkThrottlingIndex", StringComparison.OrdinalIgnoreCase) && value.Equals("ffffffff", StringComparison.OrdinalIgnoreCase)) return "取消多媒体网络节流，可能提高吞吐量，也可能增加后台网络占用。";
        if (name.Equals("MenuShowDelay", StringComparison.OrdinalIgnoreCase)) return $"将菜单显示延迟设为 {value} 毫秒，使菜单更快弹出。";
        if (name.Equals("MouseHoverTime", StringComparison.OrdinalIgnoreCase)) return $"将悬停触发时间设为 {value} 毫秒。";
        if (name.StartsWith("Disable", StringComparison.OrdinalIgnoreCase) || name.StartsWith("No", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Prevent", StringComparison.OrdinalIgnoreCase) || name.StartsWith("TurnOff", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Hide", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Exclude", StringComparison.OrdinalIgnoreCase))
            return value == "1" ? "启用禁止、隐藏或排除策略，使上述功能停止或不再显示。" : $"写入 {desired.Kind} 值 {value}，按该策略定义调整上述限制。";
        if (name.StartsWith("Enable", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Allow", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Enabled", StringComparison.OrdinalIgnoreCase))
            return value == "0" ? "关闭或禁止上述功能。" : value == "1" ? "启用上述功能或策略。" : $"写入 {desired.Kind} 值 {value}，设置上述功能的工作模式。";
        return $"写入 {desired.Kind} 值 {value}，按 YAML 设定调整上述功能。";
    }

    private static string DescribeAction(string action, string path)
    {
        var text = action.ToLowerInvariant();
        if (text.Contains("chrome")) return "Google Chrome 遥测、清理报告、反馈和扩展兼容策略";
        if (text.Contains("edge")) return "Microsoft Edge 遥测、个性化、侧边栏和 SmartScreen 策略";
        if (text.Contains("firefox")) return "Mozilla Firefox 遥测、研究功能和默认浏览器代理策略";
        if (text.Contains("office")) return "Microsoft Office 遥测、日志、反馈和 OSM 监测策略";
        if (text.Contains("visual studio")) return "Visual Studio 遥测、反馈、日志和扩展数据收集策略";
        if (text.Contains("nvidia")) return "NVIDIA 遥测服务和数据收集策略";
        if (text.Contains("copilot") || text.Contains("ai service")) return "Windows Copilot、Cortana、联网搜索及 AI 数据分析功能";
        if (text.Contains("telemetry") || text.Contains("privacy") || text.Contains("ceip")) return "Windows 诊断、遥测、广告标识、反馈和隐私相关功能";
        if (text.Contains("update") || text.Contains("delivery optimization")) return "Windows Update、传递优化、商店更新和推荐应用下载行为";
        if (text.Contains("smart") || text.Contains("security")) return "SmartScreen、附件来源标记、杀毒扫描和安全提示行为";
        if (text.Contains("game") || text.Contains("xbox")) return "Xbox Game Bar、游戏录制、游戏模式和多媒体调度";
        if (text.Contains("ink") || text.Contains("touch") || text.Contains("spell") || text.Contains("sticky")) return "Windows Ink、触摸键盘、拼写预测和辅助键盘功能";
        if (text.Contains("spotlight") || text.Contains("content") || text.Contains("suggested")) return "Windows 聚焦、推荐内容、广告和建议应用投放";
        if (text.Contains("search") || text.Contains("feeds") || text.Contains("cortana")) return "Windows 搜索、联网结果、新闻信息流和 Cortana";
        if (text.Contains("clipboard") || text.Contains("activity") || text.Contains("cross device")) return "剪贴板历史、活动记录和跨设备同步";
        if (text.Contains("wifi") || text.Contains("location")) return "位置服务、Wi-Fi Sense 和热点信息共享";
        if (text.Contains("explorer") || text.Contains("taskbar") || text.Contains("meet now")) return "资源管理器、任务栏和 Windows Shell 界面行为";
        if (text.Contains("context menu") || path.Contains(@"\shell\", StringComparison.OrdinalIgnoreCase)) return "资源管理器右键菜单项目及其执行命令";
        if (text.Contains("performance") || text.Contains("multimedia") || text.Contains("throttling")) return "系统响应、网络节流、存储与多媒体任务调度";
        if (text.Contains("prefetch") || text.Contains("superfetch")) return "Windows 预读取和 SysMain 缓存策略";
        if (text.Contains("standby") || text.Contains("aoac")) return "现代待机和低功耗空闲模式";
        if (text.Contains("hardware") || text.Contains("labconfig") || text.Contains("mosetup")) return "Windows 安装与升级的 TPM、CPU、内存和安全启动检查";
        if (text.Contains("virtualization")) return "基于虚拟化的安全性和 Device Guard";
        if (text.Contains("service wait") || text.Contains("timeout")) return "应用、服务退出和界面响应等待时间";
        if (text.Contains("transparency")) return "Windows 透明效果和系统强调色显示";
        if (text.Contains("reporting service")) return "Windows 错误报告和自动维护任务";
        if (text.Contains("autocomplete")) return "运行框、资源管理器和命令输入的自动补全建议";
        if (text.Contains("remote assistance")) return "Windows 远程协助连接权限";
        if (text.Contains("desktop tweak")) return "桌面交互速度、无响应应用等待时间和自动结束行为";
        if (text.Contains("crash control")) return "系统或服务崩溃时的转储记录";
        if (text.Contains("cast to device")) return "资源管理器“播放到设备/投射到设备”右键菜单处理程序";
        if (text.Contains("show more options") || text.Contains("sticker")) return "Windows 11 经典右键菜单和桌面贴纸功能";
        if (text.Contains("appcompat")) return "Windows 应用程序兼容性检测和 Shim 引擎";
        if (text.Contains("sqm")) return "Windows 客户体验改善计划 SQM 日志记录器";
        if (text.Contains("accessibility")) return "粘滞键、筛选键、切换键等辅助键盘快捷方式提示";
        return "该优化项目对应的 Windows 或软件策略参数";
    }

    private static string OfficeApplication(string name) => name.ToLowerInvariant() switch
    {
        "accesssolution" => "Access", "olksolution" => "Outlook", "onenotesolution" => "OneNote", "pptsolution" => "PowerPoint",
        "projectsolution" => "Project", "publishersolution" => "Publisher", "visiosolution" => "Visio", "wdsolution" => "Word", "xlsolution" => "Excel", _ => name
    };

    private static string OfficeSolution(string name) => name.ToLowerInvariant() switch
    {
        "agave" => "Office Web 加载项", "appaddins" => "应用加载项", "comaddins" => "COM 加载项", "documentfiles" => "文档文件", "templatefiles" => "模板文件", _ => name
    };

    private static string ReadableName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "默认值";
        return System.Text.RegularExpressions.Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " ");
    }
}
