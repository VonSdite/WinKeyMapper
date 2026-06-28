using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using KeyMapper.Core;

namespace KeyMapper;

public partial class MappingDialog : Window
{
    private readonly KeyboardHook _hook;
    private readonly List<KeyDef> _allKeys;
    private readonly CollectionViewSource _cvs;
    private readonly bool _isNew;

    public string? SourceId { get; private set; }
    public string? TargetId { get; private set; }

    public MappingDialog(KeyboardHook hook, MappingVm? existing = null)
    {
        InitializeComponent();
        _hook = hook;
        _isNew = existing == null;
        _allKeys = Keys.All.ToList();

        Title = _isNew ? "新建映射" : "编辑映射";
        Icon = AppIcon.ImageSource;

        _cvs = new CollectionViewSource { Source = _allKeys };
        _cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(KeyDef.Category)));
        _cvs.SortDescriptions.Add(new SortDescription(nameof(KeyDef.Category), ListSortDirection.Ascending));
        _cvs.SortDescriptions.Add(new SortDescription(nameof(KeyDef.Display), ListSortDirection.Ascending));
        TargetList.ItemsSource = _cvs.View;

        if (existing != null)
        {
            SourceId = existing.SourceId;
            TargetId = existing.TargetId;
            SourceDisplay.Text = Keys.ById(SourceId)?.Display ?? "—";
            SourceHint.Text = "已捕获 · 点击按键可重新捕获";
            SelectTarget(TargetId);
        }

        // 对话框打开期间暂停改键，避免在搜索框里打出被改写的键
        _hook.Pause();
        _hook.KeyCaptured += OnCaptured;
        Loaded += MappingDialog_Loaded;
        UpdatePreview();
    }

    private void MappingDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isNew && string.IsNullOrEmpty(SourceId))
            BeginSourceCapture();
        else
            FilterBox.Focus();
    }

    private void CaptureArea_MouseDown(object sender, MouseButtonEventArgs e) => BeginSourceCapture();

    private void BeginSourceCapture()
    {
        SourceId = null;
        SourceHint.Text = "请按下要改写的源键…";
        SourceDisplay.Text = "...";
        SourceKeyCap.BorderBrush = (Brush)FindResource("AccentBrush");
        SourceKeyCap.Background = (Brush)FindResource("SelectedBrush");
        _hook.BeginCapture();
        UpdatePreview();
    }

    private void OnCaptured(object? sender, KeyDef? def)
    {
        SourceKeyCap.BorderBrush = (Brush)FindResource("BorderBrush");
        SourceKeyCap.Background = (Brush)FindResource("CardBrush");
        if (def == null)
        {
            SourceHint.Text = "未能识别该键，点击重试";
            SourceDisplay.Text = "—";
            UpdatePreview();
            return;
        }
        SourceId = def.Id;
        SourceDisplay.Text = def.Display;
        SourceHint.Text = "已捕获 · 点击键帽可重新捕获";
        UpdatePreview();
        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = FilterBox.Text.Trim().ToLower();
        _cvs.View.Filter = k =>
        {
            if (string.IsNullOrEmpty(q)) return true;
            var kd = (KeyDef)k;
            return kd.Display.ToLower().Contains(q) || kd.Id.Contains(q);
        };
        if (TargetId != null) SelectTarget(TargetId);
        UpdatePreview();
    }

    private void Target_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetList.SelectedItem is KeyDef def)
        {
            TargetId = def.Id;
            UpdatePreview();
        }
    }

    private void SelectTarget(string? id)
    {
        if (id == null) return;
        foreach (var item in TargetList.Items)
            if (item is KeyDef kd && kd.Id == id)
            {
                TargetList.SelectedItem = kd;
                TargetList.ScrollIntoView(kd);
                return;
            }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    /// <summary>双击目标键即确认；前提是源键已设置，否则不响应。</summary>
    private void TargetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(SourceId)) return;
        if (TargetList.SelectedItem is not KeyDef def) return;
        TargetId = def.Id;
        Confirm();
    }

    private void Confirm()
    {
        if (string.IsNullOrEmpty(SourceId)) { AppDialog.Alert(this, "无法保存映射", "请先捕获源键。"); return; }
        if (string.IsNullOrEmpty(TargetId)) { AppDialog.Alert(this, "无法保存映射", "请选择目标键。"); return; }
        if (SourceId == TargetId) { AppDialog.Alert(this, "无法保存映射", "源键与目标键不能相同。"); return; }
        DialogResult = true;
        Close();
    }

    private void UpdatePreview()
    {
        OkButton.IsEnabled =
            !string.IsNullOrEmpty(SourceId) &&
            !string.IsNullOrEmpty(TargetId) &&
            SourceId != TargetId;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        _hook.CancelCapture();
        _hook.KeyCaptured -= OnCaptured;
        _hook.Resume();
        base.OnClosing(e);
    }
}
