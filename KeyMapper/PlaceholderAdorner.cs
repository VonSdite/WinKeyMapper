using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KeyMapper;

/// <summary>
/// TextBox 上的 placeholder 装饰器。
/// 位置取自 GetRectFromCharacterIndex(0)（文本系统给出的光标矩形）并转换到 TextBox
/// 坐标系，使 placeholder 与光标严格同起点——不依赖手算 padding/border，从根本上对齐。
/// 仅当文本为空且未聚焦时显示（聚焦时让位给光标，二者不并存）。
/// </summary>
public sealed class PlaceholderAdorner : Adorner
{
    private readonly TextBox _box;
    private readonly TextBlock _text;

    public PlaceholderAdorner(TextBox box, string placeholder, Brush foreground) : base(box)
    {
        _box = box;
        _text = new TextBlock
        {
            Text = placeholder,
            Foreground = foreground,
            IsHitTestVisible = false,
            Focusable = false,
        };
        AddVisualChild(_text);
        IsHitTestVisible = false;
    }

    public void Refresh()
    {
        bool show = string.IsNullOrEmpty(_box.Text);
        var v = show ? Visibility.Visible : Visibility.Collapsed;
        if (_text.Visibility == v) return;
        _text.Visibility = v;
        InvalidateArrange();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _text;

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // 默认按 border+padding 估算内容起点（回退方案）
        double x = _box.BorderThickness.Left + _box.Padding.Left;
        double y = _box.BorderThickness.Top + _box.Padding.Top;
        double h = finalSize.Height - y - _box.BorderThickness.Bottom - _box.Padding.Bottom;

        // 优先用文本系统给出的光标矩形，与光标严格同起点
        if (_box.Template.FindName("PART_ContentHost", _box) is ScrollViewer sv)
        {
            try
            {
                Rect rect = _box.GetRectFromCharacterIndex(0);
                if (!rect.IsEmpty && rect.Height > 0)
                {
                    Rect r = sv.TransformToVisual(_box).TransformBounds(rect);
                    x = r.X;
                    y = r.Y;
                    h = r.Height;
                }
            }
            catch
            {
                // 变换不可用时维持上面的 padding 估算
            }
        }

        _text.Arrange(new Rect(x, y, Math.Max(0, finalSize.Width - x), h));
        return finalSize;
    }
}
