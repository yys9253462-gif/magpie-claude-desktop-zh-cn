using System.Drawing.Drawing2D;

namespace ClaudeZhStandalone;

public static class AppIcon
{
    public static Icon Create(int size = 256)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var background = new LinearGradientBrush(
                new Rectangle(0, 0, size, size),
                Color.FromArgb(218, 126, 85), Color.FromArgb(139, 65, 72), 35F);
            using var path = RoundedRectangle(new Rectangle(0, 0, size, size), size / 5);
            graphics.FillPath(background, path);
            using var glow = new SolidBrush(Color.FromArgb(25, 255, 255, 255));
            graphics.FillEllipse(glow, -size / 5, -size / 3, size, size);
            using var ink = new SolidBrush(Color.FromArgb(255, 250, 243));
            using var font = new Font("Microsoft YaHei UI", size * 0.40F, FontStyle.Bold, GraphicsUnit.Pixel);
            var text = "文";
            var measure = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, ink, (size - measure.Width) / 2, size * 0.14F);
            using var small = new Font("Segoe UI", size * 0.13F, FontStyle.Bold, GraphicsUnit.Pixel);
            var label = "A  /  中";
            var labelSize = graphics.MeasureString(label, small);
            graphics.DrawString(label, small, ink, (size - labelSize.Width) / 2, size * 0.73F);
        }
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
