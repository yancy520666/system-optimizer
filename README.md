# 系统优化工具 Lite

一款面向 Windows 用户的轻量化系统维护工具，提供系统优化、系统清理、驱动辅助管理、常用工具箱和基础设置管理。软件本体保持轻量，OptimizerNXT、BleachBit 和工具箱组件按需从固定版本远端获取，降低安装包体积并提升部署灵活性。

## 功能特性

- 系统优化：集中展示 OptimizerNXT 优化项，支持说明提示、应用优化、回退优化和恢复默认。
- 系统清理：基于 BleachBit 提供清理预览、执行清理、分类折叠和清理结果统计。
- 驱动管理：识别电脑厂商、型号和序列号，辅助打开对应官方驱动中心。
- 工具箱：整合常用 Windows 维护工具，支持按需下载和本地工具识别。
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

发布后的程序位于：

```text
publish/SystemOptimizerLite.exe
```

该发布方式为 framework-dependent，需要目标电脑已安装 .NET 8 Desktop Runtime。如果希望做成完全独立运行包，可以改用 self-contained 发布，但体积会明显增大。

## 数据与组件目录

软件运行时会使用以下本地目录保存配置、日志、组件和回退数据：

```text
%LocalAppData%\SystemOptimizerLite
```

主要子目录包括：

- `runtime`：远端组件和工具箱文件。
- `logs`：软件运行和执行日志。
- `backups`：系统优化前的回退快照。
- `cache`：驱动检测等临时缓存。

## 帮助文档

[系统优化工具帮助文档](https://yangg-app.notion.site/system-optimizer-help?source=copy_link)

如果 GitHub 下载不稳定，可参考备用下载页面手动获取组件：

[组件与工具备用下载页](https://yangg-app.notion.site/system-optimizer-tools-download?source=copy_link)

## 注意事项

- 系统优化功能建议按需启用，不建议一次性开启不了解作用的高风险项目。
- 执行优化前，建议关闭正在运行的大型软件，并确认当前系统状态正常。
- 如果优化后出现异常，可以通过回退或恢复默认功能还原相关配置。
- 不同电脑配置、系统版本、驱动状态和第三方环境可能导致不同结果。

## 免责声明

本软件仅作为 Windows 系统功能管理、运行环境修复和系统维护辅助工具使用。使用高风险优化项前，请确认已了解相关功能作用。因误操作、系统环境异常或第三方组件导致的问题，开发者不承担直接责任。

## License

许可证待补充。若你计划公开发布，建议在仓库中补充明确的开源许可证文件，例如 MIT、Apache-2.0 或 GPL 系列许可证。
