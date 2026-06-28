using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyMapper.Core;

/// <summary>应用图标：在代码中按像素绘制键盘样式图标，无需图片资源文件。</summary>
public static class AppIcon
{
    private static Icon? _gdiIcon;       // 持有以保活底层 HICON
    private static ImageSource? _imageSource;

    /// <summary>生成 32×32 键盘样式 GDI 图标（用于托盘）。</summary>
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using (var shadow = new SolidBrush(System.Drawing.Color.FromArgb(42, 15, 23, 42)))
        using (var shadowPath = RoundedRect(3, 4, 26, 26, 7))
            g.FillPath(shadow, shadowPath);

        using (var tilePath = RoundedRect(2, 2, 28, 28, 7))
        using (var tile = new System.Drawing.Drawing2D.LinearGradientBrush(
                   new Rectangle(2, 2, 28, 28),
                   System.Drawing.Color.FromArgb(255, 59, 130, 246),
                   System.Drawing.Color.FromArgb(255, 29, 78, 216),
                   LinearGradientMode.ForwardDiagonal))
        using (var stroke = new System.Drawing.Pen(System.Drawing.Color.FromArgb(120, 255, 255, 255), 1))
        {
            g.FillPath(tile, tilePath);
            g.DrawPath(stroke, tilePath);
        }

        // 白色键盘主体，在小尺寸托盘里也能保持清楚轮廓。
        using (var keyboard = new SolidBrush(System.Drawing.Color.FromArgb(246, 248, 250, 252)))
        using (var outline = new System.Drawing.Pen(System.Drawing.Color.FromArgb(210, 191, 219, 254), 1))
        using (var path = RoundedRect(5, 10, 22, 13, 3))
        {
            g.FillPath(keyboard, path);
            g.DrawPath(outline, path);
        }

        using var key = new SolidBrush(System.Drawing.Color.FromArgb(255, 37, 99, 235));
        using var accentKey = new SolidBrush(System.Drawing.Color.FromArgb(255, 34, 197, 94));
        for (int i = 0; i < 4; i++)
        {
            FillRounded(g, key, 8 + i * 4, 13, 3, 2, 1);
            FillRounded(g, i == 3 ? accentKey : key, 8 + i * 4, 16, 3, 2, 1);
        }
        FillRounded(g, key, 10, 20, 12, 2, 1);

        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>WPF 窗口图标（缓存，所有窗口共用同一实例）。</summary>
    public static ImageSource ImageSource
    {
        get
        {
            if (_imageSource is null)
            {
                _gdiIcon = Create(); // 保活，使 HICON 在程序生命周期内有效
                _imageSource = Imaging.CreateBitmapSourceFromHIcon(
                    _gdiIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            return _imageSource;
        }
    }

    private static GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
    {
        var path = new GraphicsPath();
        int d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void FillRounded(Graphics g, System.Drawing.Brush brush, int x, int y, int w, int h, int r)
    {
        using var path = RoundedRect(x, y, w, h, r);
        g.FillPath(brush, path);
    }
}
