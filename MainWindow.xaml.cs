using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SystemOptimizerLite;

public partial class MainWindow : Window
{
    private readonly List<CategoryInfo> _categories = new()
    {
        new("系统优化", "轻量、安全、可回退的系统优化工具"),
        new("系统清理", "基于 BleachBit 的系统垃圾清理与空间释放服务"),
        new("驱动管理", "检查驱动状态及安装官方驱动中心"),
        new("工具箱", "实用 Windows 系统专属工具"),
        new("设置", "主题、本地组件与运行目录")
    };

    private readonly OptimizerService _optimizer = new();
    private readonly CleanerService _cleaner = new();
    private readonly DriverService _drivers = new();
    private readonly ToolboxService _toolbox = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HashSet<string> _selectedCleanerIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _busyOptimizerIds = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;
    private string _active = "系统优化";
    private int _renderVersion;
    private CleanerRunSummary? _lastCleanerSummary;
    private bool _forceDriverRefresh;
    private bool _optimizerComponentBusy;
    private bool _cleanerComponentBusy;
    private bool _cleanerRunBusy;
    private DownloadFailure? _lastDownloadFailure;
    private const string HelpDocumentUrl = "https://yangg-app.notion.site/system-optimizer-help?source=copy_link";
    private const string ManualDownloadUrl = "https://yangg-app.notion.site/system-optimizer-tools-download?source=copy_link";

    private sealed record DownloadFailure(string Page, string Module, string Reason, string TargetDirectory, string Instruction);

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnableRoundedWindowCorners();
        _settings = SettingsService.Load();
        _active = "系统优化";
        ApplyTheme();
        Loaded += async (_, _) =>
        {
            RenderNav();
            await RefreshTopStatusAsync();
            await SelectPageAsync(_active);
        };
        Closed += (_, _) =>
        {
            _shutdown.Cancel();
            _settings.LastSelectedPage = "系统优化";
            SettingsService.Save(_settings);
            if (!_settings.KeepLocalComponents) AppPaths.CleanupRuntimeComponents();
        };
    }

    private void RenderNav()
    {
        NavPanel.Children.Clear();
        foreach (var category in _categories)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = category.Name, FontSize = 15, FontWeight = FontWeights.SemiBold },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Height = 44,
                Margin = new Thickness(0, 0, 0, 8),
                Background = category.Name == _active ? BrushOf("BlueSoft") : Brushes.Transparent,
                BorderBrush = category.Name == _active ? BrushOf("Blue") : Brushes.Transparent
            };
            button.Click += async (_, _) => await SelectPageAsync(category.Name);
            NavPanel.Children.Add(button);
        }
    }

    private async Task SelectPageAsync(string name)
    {
        var renderVersion = ++_renderVersion;
        _active = name;
        _settings.LastSelectedPage = name;
        SettingsService.Save(_settings);
        RenderNav();
        var category = _categories.First(x => x.Name == name);
        TitleText.Text = category.Name;
        SubtitleText.Text = category.Description;
        ContentPanel.Children.Clear();
        PageActionPanel.Children.Clear();
        StatusText.Text = "正在加载...";
        try
        {
            if (name == "系统优化") await RenderOptimizerAsync();
            else if (name == "系统清理") await RenderCleanerAsync();
            else if (name == "驱动管理") await RenderDriversAsync(renderVersion);
            else if (name == "工具箱") await RenderToolsAsync();
            else RenderSettings();
            await RefreshTopStatusAsync();
            StatusText.Text = "就绪";
        }
        catch (Exception ex)
        {
            ContentPanel.Children.Clear();
            AddInfoCard("加载失败", ex.Message, true);
            StatusText.Text = ex.Message;
            LogService.Write($"页面加载失败：{name} - {ex}");
        }
    }

    private async Task RefreshTopStatusAsync()
    {
        TopStatusPanel.Children.Clear();
        AddStatusChip(AdminService.IsAdministrator ? "管理员权限" : "非管理员", AdminService.IsAdministrator ? "Green" : "Yellow");
        var runtime = EnvironmentService.RuntimeText();
        AddStatusChip(runtime, runtime.Contains("正常") ? "Green" : "Red");

        if (_active == "系统优化")
        {
            var optimizer = await _optimizer.GetComponentStatusAsync();
            if (_optimizerComponentBusy) optimizer = optimizer with { State = ComponentState.Downloading, Text = "获取远端服务中" };
            AddStatusChip(optimizer.State == ComponentState.Online ? "Optimizer 在线" : optimizer.State == ComponentState.Downloading ? "获取远端服务中" : "Optimizer 离线",
                optimizer.State == ComponentState.Online ? "Green" : optimizer.State == ComponentState.Downloading ? "Yellow" : "Red");
        }
        else if (_active == "系统清理")
        {
            var cleaner = await _cleaner.GetStatusAsync();
            if (_cleanerComponentBusy) cleaner = cleaner with { State = ComponentState.Downloading, Text = "获取远端服务中" };
            AddStatusChip(cleaner.State == ComponentState.Online ? "BleachBit 在线" : cleaner.State == ComponentState.Downloading ? "获取远端服务中" : "BleachBit 离线",
                cleaner.State == ComponentState.Online ? "Green" : cleaner.State == ComponentState.Downloading ? "Yellow" : "Red");
        }
    }

    private void AddStatusChip(string text, string colorKey)
    {
        var border = new Border
        {
            Background = BrushOf("Panel2"),
            BorderBrush = BrushOf("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = BrushOf(colorKey), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        border.Child = row;
        TopStatusPanel.Children.Add(border);
    }

    private async Task RenderOptimizerAsync()
    {
        var status = await _optimizer.GetComponentStatusAsync();
        if (_optimizerComponentBusy) status = status with { State = ComponentState.Downloading, Text = "获取远端服务中" };
        AddActionButton(status.State == ComponentState.Online ? "重新获取服务" : "获取远端服务", async () => await RunGuardedAsync(async () =>
        {
            ClearDownloadFailure("系统优化");
            _optimizerComponentBusy = true;
            await SelectPageAsync("系统优化");
            try
            {
                await _optimizer.InstallAsync(_shutdown.Token, msg => Dispatcher.Invoke(() => StatusText.Text = msg), force: true);
            }
            catch (Exception ex)
            {
                SetDownloadFailure("系统优化", "OptimizerNXT 组件", ex, AppPaths.OptimizerRuntime, "请在备用下载页下载 OptimizerNXT 组件压缩包，并将 zip 内容解压到下面的目标目录。");
                throw;
            }
            finally
            {
                _optimizerComponentBusy = false;
                await SelectPageAsync("系统优化");
            }
        }));
        AddActionButton("恢复默认", async () => await RestoreAllAsync());
        if (!AdminService.IsAdministrator) AddActionButton("管理员重启", () => AdminService.RestartAsAdministrator());

        AddOptimizerStatusCard(status);
        AddDownloadFailureCard("系统优化");
        var locked = status.State != ComponentState.Online;

        var items = await _optimizer.GetItemsAsync();
        var grid = CreateCardGrid();
        foreach (var item in items.OrderBy(OptimizerSortRisk).ThenBy(OptimizerSortRecommended).ThenBy(x => OptimizerGroupOrder(x.Group)).ThenBy(x => x.Order))
        {
            var applied = await _optimizer.IsAppliedAsync(item.Id);
            grid.Children.Add(CreateOptimizerCard(item, applied, !locked));
        }
        ContentPanel.Children.Add(grid);
    }

    private void AddOptimizerStatusCard(ComponentStatus status)
    {
        var description = status.State switch
        {
            ComponentState.Online => "组件已就绪，可执行并回退系统优化项目。",
            ComponentState.Downloading => "正在获取 OptimizerNXT 组件，请稍候。",
            _ => "OptimizerNXT 组件未就绪时无法执行系统优化，请先获取远端服务。"
        };
        var meta = status.State == ComponentState.Online ? $"{status.ReadyCount} 项可用" : ComponentMetaText(status);
        AddComponentHeader("OptimizerNXT", OptimizerService.PinnedVersion, description, meta, ComponentMetaColor(status), status.State != ComponentState.Online);
    }

    private Border CreateOptimizerCard(OptimizerItem item, bool applied, bool enabled)
    {
        var card = CardBorder();
        card.Padding = new Thickness(12, 10, 12, 10);
        card.Height = 84;
        card.ToolTip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = item.Description,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                LineHeight = 22
            }
        };
        var canToggle = enabled && !_busyOptimizerIds.Contains(item.Id);
        card.Cursor = canToggle ? Cursors.Hand : Cursors.Arrow;
        card.Opacity = canToggle ? 1 : 0.48;
        if (applied) card.BorderBrush = BrushOf("Green");

        var root = new Grid { VerticalAlignment = VerticalAlignment.Center };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = item.Risk == "high" ? BrushOf("Red") : BrushOf("Text"),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 34,
            LineHeight = 18
        });
        copy.Children.Add(new TextBlock
        {
            Text = item.Description,
            Foreground = item.Risk == "high" ? BrushOf("Red") : BrushOf("Muted"),
            FontSize = 11,
            Margin = new Thickness(0, 4, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Height = 17
        });
        var state = CreateOptimizerSwitch(item, applied, canToggle);
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(state, 1);
        root.Children.Add(copy);
        root.Children.Add(state);
        card.Child = root;
        if (canToggle)
        {
            card.MouseLeftButtonUp += async (_, _) => await ToggleOptimizerAsync(item, applied);
        }
        return card;
    }

    private static int OptimizerSortRisk(OptimizerItem item) => item.Risk == "high" ? 1 : 0;
    private static int OptimizerSortRecommended(OptimizerItem item) => item.EnabledByDefault || item.DefaultChecked ? 0 : 1;
    private static int OptimizerGroupOrder(string group) => group switch
    {
        "基础优化" => 10,
        "启动与后台" => 20,
        "性能增强" => 30,
        "浏览器与遥测" => 40,
        "应用遥测" => 50,
        "输入体验" => 60,
        "内存与预读取" => 70,
        "显卡与服务" => 80,
        "右键菜单" => 90,
        "系统更新" => 100,
        "安全相关" => 110,
        "存储与清理" => 120,
        "电源与硬件" => 130,
        _ => 999
    };

    private Border CreateOptimizerSwitch(OptimizerItem item, bool applied, bool enabled)
    {
        var pill = new Border
        {
            Width = 54,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = applied ? BrushOf("Blue") : BrushOf("Panel3"),
            BorderBrush = applied ? BrushOf("Blue") : BrushOf("Border"),
            BorderThickness = new Thickness(1),
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            Margin = new Thickness(12, 0, 0, 0),
            Opacity = enabled ? 1 : 0.75,
            VerticalAlignment = VerticalAlignment.Center
        };
        pill.Child = new Ellipse
        {
            Width = 20,
            Height = 20,
            Fill = BrushOf("Text"),
            Margin = applied ? new Thickness(29, 3, 3, 3) : new Thickness(4, 3, 28, 3)
        };
        if (enabled)
        {
            pill.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                await ToggleOptimizerAsync(item, applied);
            };
        }
        return pill;
    }

    private async Task ToggleOptimizerAsync(OptimizerItem item, bool applied)
    {
        if (!_busyOptimizerIds.Add(item.Id)) return;
        try
        {
            await SelectPageAsync("系统优化");
            await RunGuardedAsync(async () =>
            {
                EnsureAdminOrThrow();
                if (applied)
                {
                    StatusText.Text = $"正在回退：{item.Title}";
                    await _optimizer.RestoreAsync(item.Id);
                    StatusText.Text = $"已回退：{item.Title}";
                }
                else
                {
                    if (item.Risk == "high" && MessageBox.Show($"“{item.Title}”属于高风险项目，可能影响 Windows 默认功能。确认继续吗？", "高风险确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                    StatusText.Text = $"正在优化：{item.Title}";
                    await _optimizer.ApplyAsync(item, _shutdown.Token);
                    StatusText.Text = $"已优化：{item.Title}";
                }
            });
        }
        finally
        {
            _busyOptimizerIds.Remove(item.Id);
            if (_active == "系统优化") await SelectPageAsync("系统优化");
        }
    }

    private async Task RenderCleanerAsync()
    {
        var status = await _cleaner.GetStatusAsync();
        if (_cleanerComponentBusy) status = status with { State = ComponentState.Downloading, Text = "获取远端服务中" };
        AddActionButton(status.State == ComponentState.Online ? "重新获取组件" : "获取清理组件", async () => await RunGuardedAsync(async () =>
        {
            ClearDownloadFailure("系统清理");
            _cleanerComponentBusy = true;
            await SelectPageAsync("系统清理");
            try
            {
                await _cleaner.InstallAsync(_shutdown.Token, msg => Dispatcher.Invoke(() => StatusText.Text = msg), force: true);
            }
            catch (Exception ex)
            {
                SetDownloadFailure("系统清理", "BleachBit 组件", ex, AppPaths.CleanerRuntime, "请在备用下载页下载 BleachBit portable 压缩包，并将 zip 内容解压到下面的目标目录。");
                throw;
            }
            finally
            {
                _cleanerComponentBusy = false;
                await SelectPageAsync("系统清理");
            }
        }));
        AddActionButton("恢复默认", async () => await ResetCleanerDefaultsAsync());
        AddActionButton("预览清理", async () => await PreviewSelectedCleanerAsync());
        AddActionButton("执行清理", async () => await CleanSelectedCleanerAsync());
        if (!AdminService.IsAdministrator) AddActionButton("管理员重启", () => AdminService.RestartAsAdministrator());

        if (status.State != ComponentState.Online)
        {
            AddCleanerHeader(status, new List<CleanerItem>());
            AddDownloadFailureCard("系统清理");
            return;
        }

        var items = await _cleaner.GetItemsAsync();
        SyncCleanerSelection(items);
        AddCleanerHeader(status, items);
        AddDownloadFailureCard("系统清理");
        if (_lastCleanerSummary != null) AddCleanerSummaryCard(_lastCleanerSummary);

        foreach (var group in items.GroupBy(x => x.Group).OrderBy(x => x.Key))
        {
            var expander = new Expander
            {
                Header = $"{group.Key} · {group.Count(x => _selectedCleanerIds.Contains(x.Id))} 项已选择 / {group.Count()} 项",
                IsExpanded = group.Key is "Windows" or "浏览器",
                Foreground = BrushOf("Text"),
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 12)
            };
            var panel = new StackPanel();
            foreach (var item in group) panel.Children.Add(CreateCleanerRow(item));
            expander.Content = panel;
            ContentPanel.Children.Add(expander);
        }
    }

    private void AddCleanerHeader(ComponentStatus status, List<CleanerItem> items)
    {
        var description = status.State switch
        {
            ComponentState.Online => "清理组件已就绪，默认只选择适合定期清理的安全项目。",
            ComponentState.Downloading => "正在获取 BleachBit 组件，请稍候。",
            _ => "BleachBit 组件未就绪时无法预览或执行清理，请先获取清理组件。"
        };
        var meta = status.State == ComponentState.Online ? $"{items.Count} 项可用" : ComponentMetaText(status);
        AddComponentHeader("BleachBit", CleanerService.PinnedVersion, description, meta, ComponentMetaColor(status), status.State != ComponentState.Online);
    }

    private void SyncCleanerSelection(List<CleanerItem> items)
    {
        var available = items.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedCleanerIds.RemoveWhere(x => !available.Contains(x));
        if (_selectedCleanerIds.Count == 0)
        {
            foreach (var item in items.Where(x => x.DefaultSelected))
                _selectedCleanerIds.Add(item.Id);
        }
    }

    private Border CreateCleanerRow(CleanerItem item)
    {
        var card = CardBorder();
        card.Margin = new Thickness(0, 0, 0, 10);
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = item.Risk == "high" ? BrushOf("Red") : BrushOf("Text")
        });
        copy.Children.Add(new TextBlock { Text = item.Description, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 12, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(CreateCleanerSwitch(item));
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(buttons, 1);
        root.Children.Add(copy);
        root.Children.Add(buttons);
        card.Child = root;
        return card;
    }

    private Border CreateCleanerSwitch(CleanerItem item)
    {
        var selected = _selectedCleanerIds.Contains(item.Id);
        var pill = new Border
        {
            Width = 62,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = selected ? BrushOf("Blue") : BrushOf("Panel3"),
            BorderBrush = selected ? BrushOf("Blue") : BrushOf("Border"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Margin = new Thickness(10, 0, 0, 0)
        };
        var dot = new Ellipse
        {
            Width = 22,
            Height = 22,
            Fill = BrushOf("Text"),
            Margin = selected ? new Thickness(33, 4, 4, 4) : new Thickness(5, 4, 33, 4)
        };
        pill.Child = dot;
        pill.MouseLeftButtonUp += async (_, _) =>
        {
            if (selected) _selectedCleanerIds.Remove(item.Id);
            else _selectedCleanerIds.Add(item.Id);
            await SelectPageAsync("系统清理");
        };
        return pill;
    }

    private async Task ResetCleanerDefaultsAsync()
    {
        var items = await _cleaner.GetItemsAsync();
        _selectedCleanerIds.Clear();
        foreach (var item in items.Where(x => x.DefaultSelected))
            _selectedCleanerIds.Add(item.Id);
        _lastCleanerSummary = null;
        await SelectPageAsync("系统清理");
    }

    private async Task PreviewSelectedCleanerAsync()
    {
        await RunGuardedAsync(async () =>
        {
            var ids = _selectedCleanerIds.ToList();
            if (ids.Count == 0) throw new InvalidOperationException("请至少选择一个清理项目。");
            _cleanerRunBusy = true;
            _lastCleanerSummary = CleanerLoadingSummary("--preview", ids.Count);
            await SelectPageAsync("系统清理");
            try
            {
                var result = await _cleaner.RunAsync("--preview", ids, _shutdown.Token);
                _lastCleanerSummary = result.Summary;
            }
            finally
            {
                _cleanerRunBusy = false;
                if (_active == "系统清理") await SelectPageAsync("系统清理");
            }
        });
    }

    private async Task CleanSelectedCleanerAsync()
    {
        await RunGuardedAsync(async () =>
        {
            EnsureAdminOrThrow();
            var items = await _cleaner.GetItemsAsync();
            var selected = items.Where(x => _selectedCleanerIds.Contains(x.Id)).ToList();
            if (selected.Count == 0) throw new InvalidOperationException("请至少选择一个清理项目。");
            var highRisk = selected.Where(x => x.Risk == "high").ToList();
            if (highRisk.Count > 0 && MessageBox.Show($"已选择 {highRisk.Count} 个高风险清理项，可能清除登录状态、历史记录或敏感缓存。确认继续吗？", "高风险确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _cleanerRunBusy = true;
            _lastCleanerSummary = CleanerLoadingSummary("--clean", selected.Count);
            await SelectPageAsync("系统清理");
            StatusText.Text = $"正在清理 {selected.Count} 项...";
            try
            {
                var result = await _cleaner.RunAsync("--clean", selected.Select(x => x.Id), _shutdown.Token);
                _lastCleanerSummary = result.Summary;
            }
            finally
            {
                _cleanerRunBusy = false;
                if (_active == "系统清理") await SelectPageAsync("系统清理");
            }
        });
    }

    private static CleanerRunSummary CleanerLoadingSummary(string mode, int selectedItems)
    {
        var preview = mode.Contains("preview", StringComparison.OrdinalIgnoreCase);
        return new CleanerRunSummary(mode, selectedItems, 0, 0, preview ? "正在计算" : "正在清理", preview ? "正在分析预计清理内容..." : "正在执行清理任务...", true, new List<CleanerPathInfo>());
    }

    private void AddCleanerSummaryCard(CleanerRunSummary summary)
    {
        var preview = summary.Mode.Contains("preview", StringComparison.OrdinalIgnoreCase);
        var card = CardBorder();
        card.BorderBrush = BrushOf(preview ? "Blue" : "Green");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = preview ? "清理预设" : "清理结果",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf(preview ? "Blue" : "Green")
        });

        var row = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 10) };
        row.Children.Add(MetricBlock(preview ? "预计清理项目" : "已清理项目", $"{summary.SelectedItems} 项"));
        row.Children.Add(MetricBlock(preview ? "预计文件数量" : "处理文件数量", summary.FileCount > 0 ? $"{summary.FileCount} 个" : "未识别"));
        row.Children.Add(MetricBlock(preview ? "预计释放空间" : "释放磁盘空间", summary.SpaceText));
        panel.Children.Add(row);
        if (summary.IsLoading || _cleanerRunBusy)
        {
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 4, 0, 10) });
            panel.Children.Add(new TextBlock { Text = summary.OutputPreview, Foreground = BrushOf("Muted"), FontSize = 13 });
        }
        else
        {
            AddCleanerPathPreview(panel, summary);
        }
        card.Child = panel;
        ContentPanel.Children.Add(card);
    }

    private void AddCleanerPathPreview(StackPanel panel, CleanerRunSummary summary)
    {
        if (summary.TopPaths.Count == 0)
            return;

        foreach (var item in summary.TopPaths.Take(5))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = item.Path,
                Foreground = BrushOf("Muted"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var size = new TextBlock
            {
                Text = item.SizeText,
                Foreground = BrushOf("Text"),
                FontSize = 12,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Grid.SetColumn(size, 1);
            row.Children.Add(size);
            panel.Children.Add(row);
        }
    }

    private UIElement MetricBlock(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = BrushOf("Muted"), FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) });
        return panel;
    }

    private async Task RenderDriversAsync(int renderVersion)
    {
        AddActionButton("刷新", async () =>
        {
            _forceDriverRefresh = true;
            _drivers.ClearCache();
            await SelectPageAsync("驱动管理");
        });
        var loading = AddLoadingCard(_forceDriverRefresh ? "正在刷新驱动状态..." : "正在读取驱动状态...");
        try
        {
            var data = await _drivers.GetPageDataAsync(_forceDriverRefresh);
            _forceDriverRefresh = false;
            if (renderVersion != _renderVersion || _active != "驱动管理") return;
            ContentPanel.Children.Remove(loading);

            AddDriverHealthCard(data);
            var info = data.Device;
            AddInfoCard("当前设备", $"{info.Manufacturer} · {info.Model}\nSN：{info.SerialNumber}\n匹配结果：{info.MatchSummary}\n缓存时间：{data.CachedAt:yyyy-MM-dd HH:mm:ss}", !info.MatchSuccess);
            var actionCard = ActionCard("官方驱动中心", string.IsNullOrWhiteSpace(info.SupportPage) ? "未匹配到厂商官网入口。" : info.SupportPage, info.MatchSuccess ? "Green" : "Yellow");
            var actions = (StackPanel)((Grid)actionCard.Child).Children[1];
            if (!string.IsNullOrWhiteSpace(info.DownloadUrl))
                actions.Children.Add(SmallButton("一键下载", async () => await RunGuardedAsync(async () =>
                {
                    StatusText.Text = "正在下载官方驱动包...";
                    var path = await _drivers.DownloadDriverPackageAsync(info, _shutdown.Token);
                    MessageBox.Show($"驱动包已下载：\n{path}", "驱动下载", MessageBoxButton.OK, MessageBoxImage.Information);
                })));
            actions.Children.Add(SmallButton("打开官网", () =>
            {
                var url = string.IsNullOrWhiteSpace(info.SupportPage) ? "https://www.microsoft.com/windows" : info.SupportPage;
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }, 104));
            ContentPanel.Children.Add(actionCard);

            AddSectionTitle("驱动反馈", "主要设备");
            var grid = CreateCardGrid();
            foreach (var driver in data.Drivers)
                grid.Children.Add(SimpleMiniCard(driver.Name, driver.Detail, driver.Status));
            ContentPanel.Children.Add(grid);
        }
        catch (Exception ex)
        {
            _forceDriverRefresh = false;
            if (renderVersion != _renderVersion || _active != "驱动管理") return;
            ContentPanel.Children.Remove(loading);
            AddInfoCard("驱动状态加载失败", ex.Message, true);
            LogService.Write($"驱动状态加载失败：{ex}");
        }
    }

    private Border AddLoadingCard(string text)
    {
        var card = CardBorder();
        card.Child = new TextBlock { Text = text, Foreground = BrushOf("Muted"), FontSize = 15 };
        ContentPanel.Children.Add(card);
        return card;
    }

    private void AddDriverHealthCard(DriverPageData data)
    {
        var abnormal = data.Drivers.Where(x => !x.Status.Contains("正常", StringComparison.OrdinalIgnoreCase)).ToList();
        var title = abnormal.Count == 0 ? "驱动状态正常" : $"发现 {abnormal.Count} 个驱动异常";
        var detail = abnormal.Count == 0
            ? $"已检查 {data.Drivers.Count} 个主要设备，未发现明显异常。"
            : string.Join("\n", abnormal.Take(4).Select(x => $"{x.Name}：{x.Status}"));
        AddInfoCard(title, detail, abnormal.Count > 0, abnormal.Count == 0 ? "Green" : "Yellow");
    }

    private async Task RenderToolsAsync()
    {
        AddSectionTitle("工具箱", "固定 GitHub Release tools-v1");
        AddDownloadFailureCard("工具箱");
        var grid = CreateCardGrid();
        foreach (var tool in await _toolbox.GetToolsAsync())
        {
            var card = CreateToolCard(tool);
            var actions = (StackPanel)((Grid)card.Child).Children[1];
            actions.Children.Add(SmallButton("启动", async () => await RunGuardedAsync(async () =>
            {
                if (tool.Dangerous && MessageBox.Show($"“{tool.Name}”可能修改系统设置，确认启动吗？", "确认启动", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                ClearDownloadFailure("工具箱");
                StatusText.Text = $"正在准备工具：{tool.Name}";
                try
                {
                    StatusText.Text = await _toolbox.DownloadAndLaunchAsync(tool, _shutdown.Token);
                }
                catch (Exception ex)
                {
                    SetDownloadFailure("工具箱", tool.Name, ex, AppPaths.ToolRuntime, "请在备用下载页下载对应工具压缩包，并将 zip 内容解压到下面的目标目录。");
                    throw;
                }
                finally
                {
                    if (_active == "工具箱") await SelectPageAsync("工具箱");
                }
            })));
            grid.Children.Add(card);
        }
        ContentPanel.Children.Add(grid);
    }

    private Border CreateToolCard(ToolItem tool)
    {
        var card = CardBorder();
        card.Height = 112;
        card.BorderBrush = BrushOf("Green");
        card.ToolTip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = tool.Description,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                LineHeight = 22
            }
        };

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = tool.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 38
        });
        copy.Children.Add(new TextBlock
        {
            Text = tool.Description,
            Foreground = BrushOf("Muted"),
            FontSize = 12,
            Margin = new Thickness(0, 5, 12, 0),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 34
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(actions, 1);
        root.Children.Add(copy);
        root.Children.Add(actions);
        card.Child = root;
        return card;
    }

    private void RenderSettings()
    {
        AddSectionTitle("主题设置", "");
        var themeCard = CardBorder();
        var themePanel = new StackPanel();
        themePanel.Children.Add(new TextBlock { Text = "界面主题", FontSize = 17, FontWeight = FontWeights.SemiBold });
        var combo = new ComboBox { Margin = new Thickness(0, 12, 0, 0), Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        combo.Items.Add("Dark");
        combo.Items.Add("Light");
        combo.Items.Add("System");
        combo.SelectedItem = _settings.ThemeMode;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value)
            {
                _settings.ThemeMode = value;
                SettingsService.Save(_settings);
                ApplyTheme();
                _ = SelectPageAsync("设置");
            }
        };
        themePanel.Children.Add(combo);
        themeCard.Child = themePanel;
        ContentPanel.Children.Add(themeCard);

        AddSectionTitle("本地组件", "");
        var componentCard = CardBorder();
        var componentPanel = new StackPanel();
        var keep = new CheckBox { Content = "关闭软件后保留本地组件", IsChecked = _settings.KeepLocalComponents, FontSize = 16, Margin = new Thickness(0, 0, 0, 12) };
        keep.Checked += (_, _) => { _settings.KeepLocalComponents = true; SettingsService.Save(_settings); };
        keep.Unchecked += (_, _) => { _settings.KeepLocalComponents = false; SettingsService.Save(_settings); };
        componentPanel.Children.Add(keep);
        componentPanel.Children.Add(new TextBlock { Text = AppPaths.Runtime, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        componentPanel.Children.Add(SmallButton("打开本地组件位置", () =>
        {
            Directory.CreateDirectory(AppPaths.Runtime);
            Process.Start(new ProcessStartInfo(AppPaths.Runtime) { UseShellExecute = true });
        }));
        componentCard.Child = componentPanel;
        ContentPanel.Children.Add(componentCard);
    }

    private Border ActionCard(string title, string description, string colorKey)
    {
        var card = CardBorder();
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold });
        copy.Children.Add(new TextBlock { Text = description, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 16, 0), MaxWidth = 650 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(actions, 1);
        root.Children.Add(copy);
        root.Children.Add(actions);
        card.BorderBrush = BrushOf(colorKey);
        card.Child = root;
        return card;
    }

    private Border SimpleMiniCard(string title, string description, string status)
    {
        var card = CardBorder();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = description, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        panel.Children.Add(new TextBlock { Text = status, Foreground = BrushOf("Green"), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });
        card.Child = panel;
        return card;
    }

    private void AddInfoCard(string title, string description, bool warning, string? colorKey = null)
    {
        var card = CardBorder();
        card.BorderBrush = BrushOf(colorKey ?? (warning ? "Yellow" : "Border"));
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = warning ? BrushOf("Yellow") : BrushOf("Text") });
        panel.Children.Add(new TextBlock { Text = description, Foreground = BrushOf("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        card.Child = panel;
        ContentPanel.Children.Add(card);
    }

    private void AddDownloadFailureCard(string page)
    {
        if (_lastDownloadFailure == null || _lastDownloadFailure.Page != page) return;
        var failure = _lastDownloadFailure;
        var card = CardBorder();
        card.BorderBrush = BrushOf("Yellow");
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = "备用下载方法",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf("Yellow")
        });
        copy.Children.Add(new TextBlock
        {
            Text = $"{failure.Module} 自动下载未完成：{failure.Reason}",
            Foreground = BrushOf("Text"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 18, 0)
        });
        copy.Children.Add(new TextBlock
        {
            Text = $"{failure.Instruction}\n目标目录：{failure.TargetDirectory}\n完成后回到软件重新点击获取组件，或重新打开该页面检测状态。",
            Foreground = BrushOf("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 18, 0),
            LineHeight = 23
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(SmallButton("打开备用下载页", () =>
        {
            Process.Start(new ProcessStartInfo(ManualDownloadUrl) { UseShellExecute = true });
        }, 142));
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(actions, 1);
        root.Children.Add(copy);
        root.Children.Add(actions);
        card.Child = root;
        ContentPanel.Children.Add(card);
    }

    private void SetDownloadFailure(string page, string module, Exception ex, string targetDirectory, string instruction)
    {
        _lastDownloadFailure = new DownloadFailure(page, module, DownloadFailureReason(ex), targetDirectory, instruction);
    }

    private void ClearDownloadFailure(string page)
    {
        if (_lastDownloadFailure?.Page == page) _lastDownloadFailure = null;
    }

    private static string DownloadFailureReason(Exception ex)
    {
        var text = ex.ToString();
        if (text.Contains("429") || text.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase) || text.Contains("请求过于频繁"))
            return "远端请求过于频繁或被限流。";
        if (text.Contains("SHA", StringComparison.OrdinalIgnoreCase) || text.Contains("校验失败"))
            return "文件校验失败，已阻止使用该下载结果。";
        if (text.Contains("timed out", StringComparison.OrdinalIgnoreCase) || text.Contains("timeout", StringComparison.OrdinalIgnoreCase) || text.Contains("超时") || text.Contains("速度过慢"))
            return "下载超时或网络速度过慢。";
        if (text.Contains("NameResolution", StringComparison.OrdinalIgnoreCase) || text.Contains("No such host", StringComparison.OrdinalIgnoreCase) || text.Contains("无法解析"))
            return "无法连接远端服务器，可能是 DNS 或网络连接异常。";
        if (text.Contains("GitHub", StringComparison.OrdinalIgnoreCase) || text.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            return "无法稳定连接 GitHub 下载源。";
        if (ex is HttpRequestException)
            return "远端下载请求失败。";
        if (ex is UnauthorizedAccessException || text.Contains("占用") || text.Contains("权限"))
            return "本地文件被占用或没有写入权限。";
        return ex.Message;
    }

    private void AddComponentHeader(string name, string version, string description, string meta, string metaColorKey, bool warning)
    {
        var card = CardBorder();
        card.BorderBrush = BrushOf(metaColorKey);
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = BrushOf("Text"),
            VerticalAlignment = VerticalAlignment.Center
        });
        title.Children.Add(new TextBlock
        {
            Text = version,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf("Muted"),
            Margin = new Thickness(10, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        copy.Children.Add(title);
        copy.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = BrushOf("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 18, 0),
            MaxWidth = 720
        });

        var status = new TextBlock
        {
            Text = meta,
            Foreground = BrushOf(metaColorKey),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(status, 1);
        root.Children.Add(copy);
        root.Children.Add(status);
        card.Child = root;
        ContentPanel.Children.Add(card);
    }

    private static string ComponentMetaText(ComponentStatus status) => status.State switch
    {
        ComponentState.Online => "在线",
        ComponentState.Downloading => "获取中",
        _ => "离线"
    };

    private static string ComponentMetaColor(ComponentStatus status) => status.State switch
    {
        ComponentState.Online => "Green",
        ComponentState.Downloading => "Yellow",
        _ => "Red"
    };

    private void AddSectionTitle(string title, string meta)
    {
        var row = new DockPanel { Margin = new Thickness(2, 12, 2, 8) };
        row.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(meta))
        {
            var right = new TextBlock { Text = meta, Foreground = BrushOf("Muted"), HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
        }
        ContentPanel.Children.Add(row);
    }

    private UniformGrid CreateCardGrid() => new() { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };

    private Border CardBorder() => new()
    {
        Background = BrushOf("Panel2"),
        BorderBrush = BrushOf("Border"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
        Margin = new Thickness(0, 0, 10, 10)
    };

    private Button SmallButton(string text, Action action, double minWidth = 96)
    {
        var button = new Button { Content = text, MinWidth = minWidth, Height = 36, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
        button.Click += (_, _) => action();
        return button;
    }

    private Button SmallButton(string text, Func<Task> action, double minWidth = 96)
    {
        var button = new Button { Content = text, MinWidth = minWidth, Height = 36, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private void AddActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(6, 3, 0, 3), MinWidth = 104, Height = 38, Padding = new Thickness(10, 7, 10, 7), FontSize = 14, FontWeight = FontWeights.SemiBold };
        button.Click += (_, _) => action();
        PageActionPanel.Children.Add(button);
    }

    private void AddActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(6, 3, 0, 3), MinWidth = 104, Height = 38, Padding = new Thickness(10, 7, 10, 7), FontSize = 14, FontWeight = FontWeights.SemiBold };
        button.Click += async (_, _) => await action();
        PageActionPanel.Children.Add(button);
    }

    private async Task RunGuardedAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = ex.Message;
            LogService.Write($"操作失败：{ex}");
        }
    }

    private async Task RestoreAllAsync()
    {
        await RunGuardedAsync(async () =>
        {
            EnsureAdminOrThrow();
            if (MessageBox.Show("将恢复所有已记录快照的优化项目，确认继续吗？", "恢复默认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            StatusText.Text = "正在恢复默认...";
            MessageBox.Show(await _optimizer.RestoreAllAsync(), "恢复默认", MessageBoxButton.OK, MessageBoxImage.Information);
            await SelectPageAsync("系统优化");
        });
    }

    private static void EnsureAdminOrThrow()
    {
        if (!AdminService.IsAdministrator) throw new InvalidOperationException("该操作需要管理员权限，请点击“管理员重启”后重试。");
    }

    private void ApplyTheme()
    {
        var dark = _settings.ThemeMode.Equals("Dark", StringComparison.OrdinalIgnoreCase) ||
                   (_settings.ThemeMode.Equals("System", StringComparison.OrdinalIgnoreCase) && !SystemUsesLightTheme());
        if (dark)
        {
            SetBrush("Bg", "#050B12"); SetBrush("Sidebar", "#07111C"); SetBrush("Panel", "#0D1723"); SetBrush("Panel2", "#121F2D"); SetBrush("Panel3", "#17283A"); SetBrush("Border", "#223445"); SetBrush("Text", "#F1F6FF"); SetBrush("Muted", "#98A8BB"); SetBrush("BlueSoft", "#113566");
        }
        else
        {
            SetBrush("Bg", "#F5F7FB"); SetBrush("Sidebar", "#EEF3F8"); SetBrush("Panel", "#FFFFFF"); SetBrush("Panel2", "#FFFFFF"); SetBrush("Panel3", "#E7EEF7"); SetBrush("Border", "#CAD6E4"); SetBrush("Text", "#101722"); SetBrush("Muted", "#5F6F82"); SetBrush("BlueSoft", "#DCEBFF");
        }
        SetBrush("Blue", "#2D7DFF"); SetBrush("Green", "#31D07A"); SetBrush("Red", "#FF6470"); SetBrush("Yellow", "#F4C95D");
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 1;
        }
        catch { return false; }
    }

    private static void SetBrush(string key, string color) => Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static Brush BrushOf(string key) => (Brush)Application.Current.Resources[key];
    private static string RiskText(string risk) => risk == "high" ? "高风险" : "普通项目";
    private static string TrimOutput(string text) => text.Length > 5000 ? text[..5000] + "\n\n输出较长，已截断显示，完整内容请查看日志。" : text;

    private void EnableRoundedWindowCorners()
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = 2;
            _ = DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch
        {
            // Older Windows builds keep the inner rounded border fallback.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private void MoreButton_Click(object sender, RoutedEventArgs e) => MorePopup.IsOpen = true;

    private void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var path = LogService.ExportToDesktop();
        MessageBox.Show($"日志已导出到桌面：\n{path}", "导出日志", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var status = await _optimizer.GetComponentStatusAsync();
        var about = new Window
        {
            Title = "关于软件",
            Owner = this,
            Width = 720,
            Height = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = BrushOf("Bg"),
            Foreground = BrushOf("Text")
        };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = "系统优化工具", FontSize = 30, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Windows 本地优化、运行环境修复与驱动维护工具", Foreground = BrushOf("Muted"), FontSize = 16, Margin = new Thickness(0, 12, 0, 18) });
        panel.Children.Add(AboutLine("软件版本", "v3.0 · 本地轻量版"));
        panel.Children.Add(AboutLine("组件状态", status.State == ComponentState.Online ? $"OptimizerNXT {OptimizerService.PinnedVersion} · YAML {status.ReadyCount}/{status.TotalCount}" : "OptimizerNXT 未就绪"));
        panel.Children.Add(AboutLine("软件作者", "Yangg"));
        panel.Children.Add(AboutLine("适用系统", "Windows 10 / Windows 11"));

        panel.Children.Add(AboutSectionTitle("软件简介"));
        panel.Children.Add(AboutCard(AboutParagraph("本软件面向 Windows 用户，提供系统优化、环境修复、常用组件维护、驱动辅助管理和系统清理等功能。工具以本地桌面端形式运行，适合重装系统后初始化电脑、日常维护系统状态，以及快速修复部分软件或游戏运行环境缺失问题。")));

        panel.Children.Add(AboutSectionTitle("使用建议"));
        panel.Children.Add(AboutCard(AboutBullets(
            "重装系统后，建议优先检查驱动环境、运行库组件和 DirectX 组件是否完整。",
            "系统优化功能建议按需启用，不建议一次性开启不了解作用的高风险项目。",
            "执行优化前，建议关闭正在运行的大型软件，并确认当前系统状态正常。",
            "如果优化后出现异常，可以通过恢复默认或回退功能还原相关配置。",
            "遇到问题时，可优先查看帮助文档中的常见案例与处理方法。")));

        panel.Children.Add(AboutSectionTitle("开源组件"));
        panel.Children.Add(AboutCard(AboutBullets(
            "OptimizerNXT：用于提供部分 Windows 优化策略与 YAML 配置支持。",
            "BleachBit：用于提供系统清理服务。",
            "本工具对相关功能进行了桌面端整合与轻量化封装。")));

        panel.Children.Add(AboutSectionTitle("开源地址"));
        panel.Children.Add(AboutCard(AboutLink("https://github.com/yancy520666/system-optimizer")));

        panel.Children.Add(AboutSectionTitle("免责声明"));
        panel.Children.Add(AboutCard(AboutParagraph("本软件仅作为 Windows 系统功能管理、运行环境修复和系统维护辅助工具使用。不同电脑配置、系统版本、驱动状态和第三方环境可能导致不同结果。使用高风险优化项前，请确认已了解相关功能作用。因误操作、系统环境异常或第三方组件导致的问题，开发者不承担直接责任。")));

        scroll.Content = panel;
        about.Content = scroll;
        about.ShowDialog();
    }

    private UIElement AboutLine(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, Foreground = BrushOf("Muted"), FontSize = 16 });
        var v = new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        row.Children.Add(v);
        return row;
    }

    private UIElement AboutSectionTitle(string text) => new TextBlock
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 20, 0, 8)
    };

    private Border AboutCard(UIElement content)
    {
        var card = CardBorder();
        card.Margin = new Thickness(0, 0, 0, 4);
        card.Padding = new Thickness(14);
        card.Child = content;
        return card;
    }

    private TextBlock AboutParagraph(string text) => new()
    {
        Text = text,
        Foreground = BrushOf("Muted"),
        FontSize = 14.5,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 25
    };

    private StackPanel AboutBullets(params string[] items)
    {
        var panel = new StackPanel();
        foreach (var item in items)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "• " + item,
                Foreground = BrushOf("Muted"),
                FontSize = 14.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 25,
                Margin = new Thickness(0, 0, 0, 5)
            });
        }
        return panel;
    }

    private TextBlock AboutLink(string url)
    {
        var block = new TextBlock
        {
            FontSize = 14.5,
            Foreground = BrushOf("Muted"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 26
        };
        var link = new Hyperlink(new Run(url))
        {
            NavigateUri = new Uri(url),
            Foreground = BrushOf("Blue")
        };
        link.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };
        block.Inlines.Add(link);
        return block;
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(HelpDocumentUrl) { UseShellExecute = true });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
