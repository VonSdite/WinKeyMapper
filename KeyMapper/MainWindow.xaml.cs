using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeyMapper.Core;

namespace KeyMapper;

public partial class MainWindow : Window
{
    private readonly KeyboardHook _hook = new();
    private readonly TrayIcon _tray;
    private bool _reallyExit;
    private bool _initialized;
    private bool _suspendMappingChanges;
    private bool _active;

    public ObservableCollection<MappingVm> Mappings { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Icon = AppIcon.ImageSource;

        _tray = new TrayIcon();
        _tray.ShowRequested += () => Dispatcher.BeginInvoke(new Action(ShowFromTray));
        _tray.QuitRequested += () => Dispatcher.BeginInvoke(new Action(Quit));
        _tray.Show();

        var cfg = ConfigStore.Load();
        foreach (var m in cfg.Mappings)
            Mappings.Add(MakeVm(m));
        Mappings.CollectionChanged += (_, _) => OnMappingsChanged();

        _hook.Install();
        UpdateEmptyState();

        StartupManager.EnsureTrayArgument();
        StartupSwitch.IsChecked = StartupManager.IsEnabled();
        MasterSwitch.IsChecked = cfg.Active;
        if (cfg.Active) StartRemap(); else UpdateStatus();

        _initialized = true;
    }

    // —— 配置 ↔ 界面 ——
    private MappingVm MakeVm(Mapping m)
    {
        var vm = new MappingVm
        {
            SourceId = m.SourceId,
            TargetId = m.TargetId,
            Enabled = m.Enabled,
        };
        vm.PropertyChanged += MappingVm_PropertyChanged;
        return vm;
    }

    private void MappingVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MappingVm.Enabled))
            OnMappingsChanged();
    }

    /// <summary>任意映射变化：保存、若运行中则重新应用、刷新状态。构造期跳过。</summary>
    private void OnMappingsChanged()
    {
        if (!_initialized || _suspendMappingChanges) return;
        SaveConfig();
        if (_active) ApplyRemap();
        UpdateEmptyState();
        UpdateStatus();
    }

    private void SaveConfig()
    {
        ConfigStore.Save(BuildConfig());
    }

    private AppConfig BuildConfig()
    {
        var cfg = new AppConfig { Active = _active };
        foreach (var vm in Mappings)
            cfg.Mappings.Add(new Mapping
            {
                SourceId = vm.SourceId,
                TargetId = vm.TargetId,
                Enabled = vm.Enabled,
            });
        return cfg;
    }

    private void ApplyRemap()
    {
        var map = new Dictionary<int, int>();
        foreach (var vm in Mappings)
        {
            if (!vm.Enabled) continue;
            var s = Keys.ById(vm.SourceId);
            var t = Keys.ById(vm.TargetId);
            if (s != null && t != null && s.Vk != t.Vk)
                map[s.Vk] = t.Vk;
        }
        _hook.SetRemap(map);
    }

    // —— 改键启停 ——
    private void StartRemap()
    {
        _active = true;
        ApplyRemap();
        UpdateStatus();
    }

    private void StopRemap()
    {
        _active = false;
        _hook.Stop();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        bool active = _active;
        int enabled = Mappings.Count(m => m.Enabled);
        int total = Mappings.Count;
        StatusDot.Fill = active
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("MutedBrush");
        StatusText.Text = active
            ? enabled == 0 ? "已开启 · 暂无映射" : $"运行中 · {enabled} 个映射生效"
            : enabled == 0 ? "已暂停 · 没有启用的映射" : $"已暂停 · {enabled} 个映射待生效";
        SummaryText.Text = total == 0
            ? "还没有映射"
            : enabled == total ? $"{total} 个映射" : $"{enabled}/{total} 个已启用";
        MappingList.Opacity = active ? 1.0 : 0.42;
        MappingList.IsHitTestVisible = active;
        ListDisabledOverlay.Visibility = !active && total > 0 ? Visibility.Visible : Visibility.Collapsed;
        MasterSwitch.IsChecked = active;
        _tray.SetTooltip(active
            ? $"键盘按键映射 · 运行中（{enabled} 项）"
            : "键盘按键映射 · 已暂停");
    }

    private void UpdateEmptyState() =>
        EmptyState.Visibility = Mappings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // —— 事件 ——
    private void MasterSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (MasterSwitch.IsChecked == true)
        {
            StartRemap();
        }
        else
        {
            StopRemap();
        }
        SaveConfig();
    }

    private void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = StartupSwitch.IsChecked == true;
        try
        {
            StartupManager.SetEnabled(enabled);
        }
        catch (Exception ex)
        {
            StartupSwitch.IsChecked = StartupManager.IsEnabled();
            AppDialog.Alert(
                this,
                "开机启动设置失败",
                "无法写入当前用户的开机启动项。",
                ex.Message);
        }
    }

    private void ApplySystem_Click(object sender, RoutedEventArgs e)
    {
        var cfg = BuildConfig();
        int count;
        try
        {
            count = SystemKeyMapper.CountConfigMappings(cfg);
        }
        catch (Exception ex)
        {
            AppDialog.Alert(
                this,
                "系统级映射不可用",
                "当前规则里有 Windows Scancode Map 不支持的按键。",
                ex.Message);
            return;
        }

        if (count == 0)
        {
            AppDialog.Alert(
                this,
                "没有可写入的映射",
                "请先启用至少一条有效映射。",
                "系统级映射只会写入已启用、且源键和目标键不同的规则。");
            return;
        }

        string detail = $"将写入 {count} 条启用映射。写入后需要重启 Windows 才会在 Git Bash、终端和提权窗口中生效。";
        if (SystemKeyMapper.HasSystemMap())
            detail += $"{Environment.NewLine}{Environment.NewLine}检测到系统里已有 Scancode Map，本次写入会先备份它；以后点“清除系统”会恢复备份。";

        bool confirmed = AppDialog.Confirm(
            this,
            "写入系统级映射",
            "把当前启用规则写入 Windows Scancode Map？",
            detail,
            "写入",
            "取消");
        if (!confirmed)
            return;

        ConfigStore.Save(cfg);
        if (RunSystemCommand(SystemKeyMapper.ApplyArgumentsForConfig(ConfigStore.FilePath)))
        {
            AppDialog.Alert(
                this,
                "系统级映射已写入",
                "请重启 Windows 让系统级映射生效。",
                "重启后，这些映射会在更底层生效，不依赖本程序的键盘钩子。");
        }
    }

    private void ClearSystem_Click(object sender, RoutedEventArgs e)
    {
        bool confirmed = AppDialog.Confirm(
            this,
            "清除系统级映射",
            "清除 Windows Scancode Map？",
            "这只会移除本工具写入的系统级映射，不会删除当前列表里的规则。清除后需要重启 Windows 才会恢复。",
            "清除",
            "取消");
        if (!confirmed)
            return;

        if (RunSystemCommand(SystemKeyMapper.ClearArgument))
        {
            AppDialog.Alert(
                this,
                "系统级映射已清除",
                "请重启 Windows 让清除操作生效。",
                "当前列表里的规则仍会保留，可继续用于软件层实时映射。");
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new MappingDialog(_hook) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            string sourceId = dlg.SourceId!;
            string targetId = dlg.TargetId!;
            var conflicts = FindSourceConflicts(sourceId, null);
            if (!ConfirmOverwrite(sourceId, targetId, conflicts)) return;

            ChangeMappings(() =>
            {
                RemoveMappings(conflicts);
                Mappings.Add(MakeVm(new Mapping
                {
                    SourceId = sourceId,
                    TargetId = targetId,
                    Enabled = true,
                }));
            });
        }
    }

    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is MappingVm vm)
            OpenEdit(vm);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is MappingVm vm)
        {
            bool confirmed = AppDialog.Confirm(
                this,
                "删除映射",
                "确定要删除这条映射吗？",
                FormatMapping(vm.SourceId, vm.TargetId),
                "删除",
                "取消");
            if (!confirmed) return;

            ChangeMappings(() =>
            {
                vm.PropertyChanged -= MappingVm_PropertyChanged;
                Mappings.Remove(vm);
            });
        }
    }

    private void MappingList_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        if (MappingList.SelectedIndex >= 0 && SelectedVm() is MappingVm vm)
            OpenEdit(vm);
    }

    private void OpenEdit(MappingVm vm)
    {
        var dlg = new MappingDialog(_hook, vm) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            string sourceId = dlg.SourceId!;
            string targetId = dlg.TargetId!;
            var conflicts = FindSourceConflicts(sourceId, vm);
            if (!ConfirmOverwrite(sourceId, targetId, conflicts)) return;

            ChangeMappings(() =>
            {
                RemoveMappings(conflicts);
                vm.SourceId = sourceId;
                vm.TargetId = targetId;
                vm.RefreshDisplays();
            });
        }
    }

    private MappingVm? SelectedVm() =>
        MappingList.SelectedIndex >= 0 ? Mappings[MappingList.SelectedIndex] : null;

    private List<MappingVm> FindSourceConflicts(string sourceId, MappingVm? owner) =>
        Mappings.Where(m => !ReferenceEquals(m, owner) && m.SourceId == sourceId).ToList();

    private bool ConfirmOverwrite(string sourceId, string targetId, IReadOnlyList<MappingVm> conflicts)
    {
        if (conflicts.Count == 0) return true;

        string existing = string.Join(Environment.NewLine,
            conflicts.Select(m => FormatMapping(m.SourceId, m.TargetId) + (m.Enabled ? "" : "（已停用）")));
        string next = FormatMapping(sourceId, targetId);
        return AppDialog.Confirm(
            this,
            "覆盖已有映射？",
            $"源键「{DisplayOf(sourceId)}」已经有映射，继续后会替换旧映射。",
            $"已有映射：{Environment.NewLine}{existing}{Environment.NewLine}{Environment.NewLine}将改为：{Environment.NewLine}{next}",
            "覆盖",
            "取消");
    }

    private void ChangeMappings(Action change)
    {
        _suspendMappingChanges = true;
        try
        {
            change();
        }
        finally
        {
            _suspendMappingChanges = false;
        }
        OnMappingsChanged();
    }

    private void RemoveMappings(IEnumerable<MappingVm> mappings)
    {
        foreach (var vm in mappings.ToList())
        {
            vm.PropertyChanged -= MappingVm_PropertyChanged;
            Mappings.Remove(vm);
        }
    }

    private static string FormatMapping(string sourceId, string targetId) =>
        $"{DisplayOf(sourceId)} → {DisplayOf(targetId)}";

    private static string DisplayOf(string id) =>
        Keys.ById(id)?.Display ?? id;

    private bool RunSystemCommand(string arguments)
    {
        try
        {
            int exitCode;
            if (SystemKeyMapper.IsAdministrator())
            {
                exitCode = RunSystemCommandAsAdmin(arguments);
            }
            else
            {
                var result = SystemKeyMapper.RunElevatedAndWait(arguments);
                if (result.Cancelled)
                {
                    AppDialog.Alert(
                        this,
                        "需要管理员权限",
                        "系统级映射需要管理员权限，刚才的 UAC 请求已取消。");
                    return false;
                }

                if (!result.Started)
                {
                    AppDialog.Alert(
                        this,
                        "无法请求管理员权限",
                        "没有成功启动提权写入进程。");
                    return false;
                }

                exitCode = result.ExitCode;
            }

            if (exitCode == SystemKeyMapper.SuccessExitCode)
                return true;

            string detail = exitCode switch
            {
                SystemKeyMapper.NoMappingsExitCode => "提权进程没有读到可写入的启用映射。",
                SystemKeyMapper.NoOwnedMapExitCode => "没有找到本工具写入的系统级映射标记。为避免误删，不会清除当前系统映射。",
                SystemKeyMapper.CurrentMapChangedExitCode => "当前 Scancode Map 已被其他工具或手动修改。为避免覆盖别人的改动，不会清除或恢复。",
                _ => "注册表写入失败。",
            };
            AppDialog.Alert(this, "系统级映射失败", "没有完成系统级映射操作。", detail);
            return false;
        }
        catch (Exception ex)
        {
            AppDialog.Alert(this, "系统级映射失败", "没有完成系统级映射操作。", ex.Message);
            return false;
        }
    }

    private int RunSystemCommandAsAdmin(string arguments)
    {
        if (arguments.StartsWith(SystemKeyMapper.ApplyArgument, StringComparison.OrdinalIgnoreCase))
        {
            var cfg = BuildConfig();
            return SystemKeyMapper.ApplyConfig(cfg) > 0
                ? SystemKeyMapper.SuccessExitCode
                : SystemKeyMapper.NoMappingsExitCode;
        }

        if (string.Equals(arguments, SystemKeyMapper.ClearArgument, StringComparison.OrdinalIgnoreCase))
        {
            return SystemKeyMapper.Clear() switch
            {
                SystemMapClearResult.Cleared => SystemKeyMapper.SuccessExitCode,
                SystemMapClearResult.NoOwnedMap => SystemKeyMapper.NoOwnedMapExitCode,
                SystemMapClearResult.CurrentMapChanged => SystemKeyMapper.CurrentMapChangedExitCode,
                _ => SystemKeyMapper.ErrorExitCode,
            };
        }

        return SystemKeyMapper.ErrorExitCode;
    }

    // —— 托盘 / 关闭 ——
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void ShowFromTray()
    {
        Show();
        Activate();
    }

    private void Quit()
    {
        _reallyExit = true;
        SaveConfig();
        _tray.Hide();
        _hook.Dispose();
        Application.Current.Shutdown();
    }
}
