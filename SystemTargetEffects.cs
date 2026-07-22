namespace SystemOptimizerLite;

public static class SystemTargetEffectCatalog
{
    public static string DescribeService(string name, string mode)
    {
        var purpose = name.ToLowerInvariant() switch
        {
            "diagtrack" => "连接用户体验与遥测服务，用于收集诊断和使用数据。",
            "dmwappushservice" => "WAP 推送消息路由服务，部分 Windows 遥测流程会使用它。",
            "diagsvc" => "诊断执行服务，用于 Windows 故障检测和修复。",
            "wersvc" or "wercplsupport" => "Windows 错误报告服务，用于生成故障报告并支持诊断修复。",
            "pcasvc" => "程序兼容性助手，用于识别旧软件兼容问题并应用建议修复。",
            "wsearch" => "Windows 搜索索引服务，为开始菜单、资源管理器和 Outlook 提供快速搜索。",
            "sysmain" => "SysMain 预读取服务，根据使用习惯预加载常用数据。",
            "spooler" => "打印后台处理服务，负责打印队列及部分 PDF/虚拟打印功能。",
            "remoteregistry" => "远程注册表服务，允许获授权的远程管理工具读取注册表。",
            "xblauthmanager" => "Xbox 身份验证服务，支持 Xbox 与 Game Pass 登录。",
            "xblgamesave" => "Xbox 云存档同步服务。",
            "xboxgipsvc" => "Xbox 配件服务，支持部分手柄和外设。",
            "xboxnetapisvc" => "Xbox 网络服务，支持多人游戏和 Xbox 网络连接。",
            "xbgm" => "Xbox 游戏监控服务，部分系统版本并不安装。",
            "nvtelemetrycontainer" => "NVIDIA 遥测容器，负责 NVIDIA 诊断数据组件。",
            "wuauserv" => "Windows Update 服务，负责检测、下载和安装系统更新。",
            "bits" => "后台智能传输服务，Windows Update 和其他组件依赖它传输文件。",
            _ => $"Windows 或软件注册的 {name} 服务。"
        };
        var effect = mode.ToLowerInvariant() switch
        {
            "disable" => "将启动类型设为禁用，并在契约要求时停止当前实例。",
            "demand" => "将启动类型设为手动，仅在系统需要时启动。",
            "enable" => "确保服务没有被禁用。",
            "start" => "启动服务或恢复其可运行状态。",
            _ => $"按契约执行 {mode} 操作。"
        };
        return $"功能作用：{purpose}\n优化后效果：{effect}\n潜在影响：禁用系统服务可能影响依赖它的功能；缺失服务只按不适用处理，查询失败不会忽略。";
    }

    public static string DescribeTask(string task)
    {
        var lower = task.ToLowerInvariant();
        var purpose = lower switch
        {
            var x when x.Contains("office") && x.Contains("telemetry") => "Office 遥测代理任务，用于汇总 Office 兼容和使用数据。",
            var x when x.Contains("firefox default browser agent") => "Firefox 默认浏览器代理任务，用于周期性检查默认浏览器及相关遥测。",
            var x when x.Contains("customer experience improvement program") => "Windows 客户体验改善计划任务，用于收集 CEIP 诊断数据。",
            var x when x.Contains("feedback\\siuf") => "Windows 反馈任务，用于触发反馈和场景数据收集。",
            var x when x.Contains("sq m") || x.Contains("sqm") => "软件质量指标任务，用于收集可靠性和使用统计。",
            var x when x.Contains("xblgamesave") => "Xbox 游戏存档任务，用于登录或云存档相关处理。",
            var x when x.Contains("windowsupdate") => "Windows Update 维护任务。",
            var x when x.Contains("time synchronization") => "Windows 时间同步任务。",
            var x when x.Contains("diskdiagnostic") || x.Contains("autochk") => "Windows 磁盘诊断或启动检查任务。",
            var x when x.Contains("smartscreen") => "Windows SmartScreen 安全检查任务。",
            _ => "Windows 或应用注册的计划任务。"
        };
        return $"功能作用：{purpose}\n优化后效果：任务存在时将其停止并禁用；任务不存在时不会创建占位任务。\n潜在影响：只有契约明确允许的遥测任务可被禁用，更新、安全、诊断和存档任务默认安全跳过。";
    }

    public static string DescribeCommand(string command)
    {
        var lower = command.ToLowerInvariant();
        if (lower.Contains("fsutil") && lower.Contains("disablelastaccess"))
            return "功能作用：控制 NTFS 最后访问时间戳更新。\n优化后效果：减少目录访问时的元数据更新。\n潜在影响：可能影响依赖最后访问时间的备份或归档软件，因此不属于推荐默认。";
        if (lower.Contains("schtasks")) return DescribeTask(ExtractTask(command));
        if (lower.Contains("icacls"))
            return "功能作用：修改文件或目录访问控制列表。\n优化后效果：按契约阻止指定主体访问目标。\n潜在影响：错误 ACL 可能阻断 Windows 诊断、更新或修复，默认安全契约不执行 SYSTEM 全拒绝。";
        return "功能作用：执行契约声明的系统命令。\n优化后效果：仅在具备状态读取器和明确逆操作时执行。\n潜在影响：未知命令会被阻止，不会因退出码为零而直接判定成功。";
    }

    public static string DescribeAcl(string path)
        => $"功能作用：控制 {path} 的文件系统访问权限。\n优化后效果：仅应用契约明确声明且可精确恢复的 ACL。\n潜在影响：权限配置错误可能使系统组件无法写入或修复，因此读取失败或没有快照时禁止执行。";

    private static string ExtractTask(string command)
    {
        var marker = command.IndexOf("/tn", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? command : command[(marker + 3)..].Trim().Trim('"', '\'');
    }
}
