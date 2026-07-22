# 系统优化工具 Lite

一款面向 Windows 用户的轻量化系统维护工具，提供系统优化、系统清理、驱动辅助管理和常用工具箱服务。软件本体保持轻量，OptimizerNXT、BleachBit 和工具箱组件按需从远端获取，降低安装包体积并提升部署灵活性。

## 适用场景

- 重装 Windows 系统后出现驱动缺失、环境缺失、Windows 激活异常等情况
- 驱动出现异常需修复的情况
- 软件运行时产生大量垃圾缓存文件，需要清理对应空间
- 需要关闭系统或浏览器某些无用项目

## 功能特性

- 系统优化：提供 24 个固定哈希的 OptimizerNXT 优化契约，使用真实注册表、服务与计划任务状态区分默认、已优化、已优化但跳过不适用项、混合和查询失败；任务按逻辑目标去重，执行后逐项验证，失败时自动回退本次修改。
- 系统清理：合并 BleachBit XML 与内置系统项目，使用经过审核的均衡默认策略；敏感数据、备份、聊天记录、Defender 日志和未知项目默认不选，清理前必须先预览。
- 驱动管理：分别识别厂商、型号、OEM 序列号、产品 UUID、主板与机箱信息，并按置信度引导到对应官方驱动中心；同一次运行复用内存缓存，重新启动时强制刷新，关闭时自动删除驱动状态缓存。
- 工具箱：整合常用 Windows 系统维护工具，支持按需下载和本地工具识别使用，包括可测速、切换并恢复 IPv4 DNS 的智能选择 DNS 工具。
- 设置管理：支持深色、浅色和跟随系统主题，以及本地组件保留策略。

## 运行环境

- Windows 10 / Windows 11
- .NET 8 Desktop Runtime
- 建议使用管理员权限运行，以便执行系统优化、驱动检测和清理操作。

## 开源组件

- [OptimizerNXT](https://github.com/hellzerg/optimizerNXT)：用于提供部分 Windows 优化策略与 YAML 配置支持。
- [BleachBit](https://www.bleachbit.org/)：用于提供系统清理服务。

本项目对相关功能进行了桌面端整合与轻量化封装。远端组件采用固定版本下载策略，避免上游更新导致 Lite 版行为发生不可控变化。

## 源码构建

```powershell
dotnet build
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

启动程序位于：

```text
SystemOptimizerLite-v3.1-lite-win-x64.zip/SystemOptimizerLite.exe
```

需要目标电脑已安装 .NET 8 Desktop Runtime。

轻量版发布包内提供 `启动环境自检并修复.bat`。建议普通用户双击该启动器运行：它会先检测 `.NET 8 Desktop Runtime`，缺失时自动请求管理员权限，下载并静默安装 Microsoft 官方运行时，然后启动主程序。

自包含版发布包已内置 .NET 8 运行环境，可直接双击 `SystemOptimizerLite.exe`，无需安装额外运行时。

## 数据与组件目录

软件运行时会使用以下本地目录保存配置、日志、组件和回退数据：

```text
%LocalAppData%\SystemOptimizerLite
```

主要子目录包括：

- `runtime`：存储远端组件和工具箱文件。
- `logs`：软件运行和执行日志。
- `rollback-backups`：带版本与校验信息的操作前精确回退快照。
- `rollback-reports`：默认恢复与精确回退的逐项验证报告。
- `cache`：不含原始 SN/UUID 的驱动状态与组件下载临时缓存；正常关闭时自动清除，异常退出的残留会在下次启动时清除。

设置、正式日志、优化状态、回退备份、回退报告和已验证的驱动安装包不属于临时缓存，不会被启动/关闭清理流程删除。已安装的 OptimizerNXT、BleachBit 与工具箱组件仍遵循设置页的本地组件保留选项。

## 帮助文档

[系统优化工具帮助文档](https://yangg-app.notion.site/system-optimizer-help?source=copy_link)

如果 GitHub 下载不稳定，可参考备用下载页面手动获取组件：

[组件与工具备用下载页](https://yangg-app.notion.site/system-optimizer-tools-download?source=copy_link)

## 注意事项

- 系统优化功能建议按需启用，不建议一次性开启不了解作用的高风险项目。
- 执行优化前，建议关闭正在运行的大型软件，并确认当前系统状态正常。
- “精确回退”恢复到本工具执行前的状态；“扫描并恢复”不依赖 Applied 记录，按当前 Windows 的安全基线逐项恢复并验证。
- 禁用 Defender 已从 24 项优化中永久移除，旧版本遗留修改只提供一次性恢复。工具箱中的“Windows 更新与安全设置工具”是独立高级程序，可能同时修改 Defender 与更新服务，不使用 YAML 且不属于全局恢复范围。
- 系统还原与 OneDrive 管理属于工具箱高级操作，不参与全局优化恢复；已删除的还原点和应用数据无法由本软件找回。
- 不同电脑配置、系统版本、驱动状态和第三方环境可能导致不同结果。

## 免责声明

本软件仅作为 Windows 系统功能管理、运行环境修复和系统维护辅助工具使用。使用高风险优化项前，请确认已了解相关功能作用。因误操作、系统环境异常或第三方组件导致的问题，开发者不承担直接责任。
