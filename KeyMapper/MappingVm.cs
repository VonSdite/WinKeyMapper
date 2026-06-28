using System.ComponentModel;
using System.Runtime.CompilerServices;
using KeyMapper.Core;

namespace KeyMapper;

/// <summary>映射的界面绑定模型。</summary>
public sealed class MappingVm : INotifyPropertyChanged
{
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                OnPC();
            }
        }
    }

    public string SourceDisplay => Keys.ById(SourceId)?.Display ?? SourceId;
    public string TargetDisplay => Keys.ById(TargetId)?.Display ?? TargetId;

    public void RefreshDisplays()
    {
        OnPC(nameof(SourceDisplay));
        OnPC(nameof(TargetDisplay));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPC([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
