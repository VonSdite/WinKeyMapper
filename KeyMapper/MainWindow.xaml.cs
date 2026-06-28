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
        if (_hook.IsRemapping) ApplyRemap();
        UpdateEmptyState();
        UpdateStatus();
    }

    private void SaveConfig()
    {
        var cfg = new AppConfig { Active = MasterSwitch.IsChecked == true };
        foreach (var vm in Mappings)
            cfg.Mappings.Add(new Mapping
            {
                SourceId = vm.SourceId,
                TargetId = vm.TargetId,
                Enabled = vm.Enabled,
            });
        ConfigStore.Save(cfg);
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
        ApplyRemap();
        UpdateStatus();
    }

    private void StopRemap()
    {
        _hook.Stop();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        bool running = _hook.IsRemapping;
        int enabled = Mappings.Count(m => m.Enabled);
        int total = Mappings.Count;
        StatusDot.Fill = running
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("MutedBrush");
        StatusText.Text = running
            ? $"运行中 · {enabled} 个映射生效"
            : enabled == 0 ? "已暂停 · 没有启用的映射" : $"已暂停 · {enabled} 个映射待生效";
        SummaryText.Text = total == 0
            ? "还没有映射"
            : enabled == total ? $"{total} 个映射" : $"{enabled}/{total} 个已启用";
        MappingList.Opacity = running ? 1.0 : 0.42;
        MappingList.IsHitTestVisible = running;
        ListDisabledOverlay.Visibility = !running && total > 0 ? Visibility.Visible : Visibility.Collapsed;
        MasterSwitch.IsChecked = running;
        _tray.SetTooltip(running
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
            if (!Mappings.Any(m => m.Enabled))
            {
                AppDialog.Alert(this, "无法开启改键", "没有已启用的映射，请先添加。");
                MasterSwitch.IsChecked = false;
                return;
            }
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
