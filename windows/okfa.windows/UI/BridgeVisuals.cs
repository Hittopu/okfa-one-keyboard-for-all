using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace KeyboardBridge.Windows.UI;

internal enum BridgeBadgeKind
{
    Waves,
    Link,
    Check,
    Warning,
    Question,
    Off,
}

internal enum BridgeButtonKind
{
    Primary,
    Secondary,
}

internal static class BridgeTheme
{
    public static readonly Color WindowTop = Color.FromArgb(245, 247, 251);
    public static readonly Color WindowBottom = Color.FromArgb(232, 237, 246);
    public static readonly Color WindowGlow = Color.FromArgb(70, 117, 149, 255);
    public static readonly Color CardCanvas = Color.FromArgb(239, 243, 249);
    public static readonly Color CardFill = Color.FromArgb(247, 249, 252);
    public static readonly Color CardFillHighlight = Color.FromArgb(249, 251, 254);
    public static readonly Color CardBorder = Color.FromArgb(214, 220, 229);
    public static readonly Color CardShadow = Color.FromArgb(10, 22, 37, 64);
    public static readonly Color TextPrimary = Color.FromArgb(28, 34, 49);
    public static readonly Color TextSecondary = Color.FromArgb(103, 111, 130);
    public static readonly Color AccentBlue = Color.FromArgb(15, 108, 189);
    public static readonly Color AccentBlueSoft = Color.FromArgb(26, 15, 108, 189);
    public static readonly Color SuccessGreen = Color.FromArgb(16, 124, 16);
    public static readonly Color SuccessGreenSoft = Color.FromArgb(26, 16, 124, 16);
    public static readonly Color WarningOrange = Color.FromArgb(202, 80, 16);
    public static readonly Color WarningOrangeSoft = Color.FromArgb(26, 202, 80, 16);
    public static readonly Color DangerRed = Color.FromArgb(216, 92, 80);
    public static readonly Color SecondaryButtonFill = Color.FromArgb(250, 251, 253);
    public static readonly Color SecondaryButtonBorder = Color.FromArgb(205, 212, 224);

    public static Font CreateDisplayFont(float size, FontStyle style = FontStyle.Bold)
    {
        return new Font(PreferredDisplayFamily, size, style, GraphicsUnit.Point);
    }

    public static Font CreateTextFont(float size, FontStyle style = FontStyle.Regular)
    {
        return new Font(PreferredTextFamily, size, style, GraphicsUnit.Point);
    }

    public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void PaintWindowBackground(Graphics graphics, Rectangle bounds)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var brush = new LinearGradientBrush(bounds, WindowTop, WindowBottom, 90f))
        {
            graphics.FillRectangle(brush, bounds);
        }

        PaintGlow(graphics, new Rectangle(bounds.Right - 160, bounds.Top - 18, 176, 176), WindowGlow);
        PaintGlow(graphics, new Rectangle(bounds.Left - 40, bounds.Top + 136, 144, 144), Color.FromArgb(32, 255, 255, 255));
    }

    public static void TryApplyWindowBackdrop(Form form)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }

        try
        {
            var cornerPreference = 2;
            _ = DwmSetWindowAttribute(form.Handle, 33, ref cornerPreference, Marshal.SizeOf<int>());

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                var backdrop = 3;
                _ = DwmSetWindowAttribute(form.Handle, 38, ref backdrop, Marshal.SizeOf<int>());
            }
        }
        catch
        {
        }
    }

    private static string PreferredDisplayFamily => IsFontInstalled("Segoe UI Variable Display")
        ? "Segoe UI Variable Display"
        : "Segoe UI";

    private static string PreferredTextFamily => IsFontInstalled("Segoe UI Variable Text")
        ? "Segoe UI Variable Text"
        : "Segoe UI";

    private static bool IsFontInstalled(string familyName)
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(family => family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));
    }

    private static void PaintGlow(Graphics graphics, Rectangle bounds, Color color)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = color,
            SurroundColors = [Color.FromArgb(0, color)],
        };

        graphics.FillEllipse(brush, bounds);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}

internal sealed class GlassCardPanel : Panel
{
    public GlassCardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true
        );
        BackColor = BridgeTheme.CardCanvas;
        Padding = new Padding(30);
        Margin = Padding.Empty;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var backgroundBrush = new SolidBrush(BridgeTheme.CardCanvas);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var cardRect = Rectangle.FromLTRB(8, 8, Width - 8, Height - 16);
        using var cardPath = BridgeTheme.CreateRoundedPath(cardRect, 10);
        using var fillBrush = new LinearGradientBrush(cardRect, BridgeTheme.CardFillHighlight, BridgeTheme.CardFill, 90f);
        using var borderPen = new Pen(BridgeTheme.CardBorder, 1f);

        e.Graphics.FillPath(fillBrush, cardPath);
        e.Graphics.DrawPath(borderPen, cardPath);
    }
}

internal sealed class BridgeTableLayoutPanel : TableLayoutPanel
{
    public BridgeTableLayoutPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
}

internal sealed class BridgeFlowLayoutPanel : FlowLayoutPanel
{
    public BridgeFlowLayoutPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
}

internal sealed class BridgeStatusBadge : Control
{
    private BridgeBadgeKind _badgeKind = BridgeBadgeKind.Waves;
    private Color _accentColor = BridgeTheme.AccentBlue;

    public BridgeStatusBadge()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true
        );
        Size = new Size(38, 38);
        BackColor = Color.Transparent;
    }

    public void Apply(BridgeBadgeKind badgeKind, Color accentColor)
    {
        _badgeKind = badgeKind;
        _accentColor = accentColor;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var badgeRect = Rectangle.FromLTRB(0, 0, Width - 1, Height - 1);
        using var badgePath = BridgeTheme.CreateRoundedPath(badgeRect, 10);
        using var fillBrush = new SolidBrush(_accentColor);
        using var borderPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1f);

        e.Graphics.FillPath(fillBrush, badgePath);
        e.Graphics.DrawPath(borderPen, badgePath);

        DrawBrandMark(e.Graphics, badgeRect);
    }

    private static void DrawBrandMark(Graphics graphics, Rectangle bounds)
    {
        using var fillBrush = new SolidBrush(Color.White);

        FillRoundedRect(graphics, fillBrush, new RectangleF(bounds.Left + 8, bounds.Top + 10, 9, 9), 3f);
        FillRoundedRect(graphics, fillBrush, new RectangleF(bounds.Left + 8, bounds.Top + 23, 9, 9), 3f);
        FillRoundedRect(graphics, fillBrush, new RectangleF(bounds.Left + 21, bounds.Top + 16.5f, 13, 13), 4f);
        graphics.FillEllipse(fillBrush, bounds.Left + 33.5f, bounds.Top + 10.5f, 4.5f, 4.5f);
    }

    private static void FillRoundedRect(Graphics graphics, Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.StartFigure();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}

internal sealed class BridgePillLabel : Control
{
    private Color _fillColor = BridgeTheme.AccentBlueSoft;
    private Color _textColor = BridgeTheme.AccentBlue;
    public int MaximumTextWidth { get; set; } = 300;

    public BridgePillLabel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true
        );
        BackColor = Color.Transparent;
        Font = BridgeTheme.CreateTextFont(10.5f, FontStyle.Bold);
        Size = new Size(120, 32);
    }

    public void Apply(string title, Color fillColor, Color textColor)
    {
        Text = title;
        _fillColor = fillColor;
        _textColor = textColor;
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var measured = TextRenderer.MeasureText(Text, Font);
        var width = Math.Min(MaximumTextWidth, Math.Max(96, measured.Width + 28));
        return new Size(width, 32);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var pillRect = Rectangle.FromLTRB(0, 0, Width - 1, Height - 1);
        using var pillPath = BridgeTheme.CreateRoundedPath(pillRect, 8);
        using var fillBrush = new SolidBrush(_fillColor);
        using var borderPen = new Pen(Color.FromArgb(28, _textColor), 1f);

        e.Graphics.FillPath(fillBrush, pillPath);
        e.Graphics.DrawPath(borderPen, pillPath);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            Rectangle.FromLTRB(pillRect.Left + 12, pillRect.Top, pillRect.Right - 12, pillRect.Bottom),
            _textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        );
    }
}

internal sealed class BridgeButton : Button
{
    private bool _isHovered;
    private bool _isPressed;

    public BridgeButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true
        );
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = BridgeTheme.TextPrimary;
        Font = BridgeTheme.CreateTextFont(12.5f, FontStyle.Bold);
        Size = new Size(120, 44);
        MinimumSize = new Size(110, 44);
        Padding = new Padding(14, 0, 14, 0);
    }

    public BridgeButtonKind BridgeKind { get; set; } = BridgeButtonKind.Primary;

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        _isPressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = Rectangle.FromLTRB(0, 0, Width - 1, Height - 1);
        using var path = BridgeTheme.CreateRoundedPath(rect, 10);

        var palette = ResolvePalette();
        using var fillBrush = new SolidBrush(palette.Fill);
        using var borderPen = new Pen(palette.Border, 1f);

        e.Graphics.FillPath(fillBrush, path);
        e.Graphics.DrawPath(borderPen, path);

        if (Focused && Enabled)
        {
            var focusRect = Rectangle.Inflate(rect, -3, -3);
            using var focusPath = BridgeTheme.CreateRoundedPath(focusRect, 8);
            using var focusPen = new Pen(Color.FromArgb(66, BridgeTheme.AccentBlue), 1.25f);
            e.Graphics.DrawPath(focusPen, focusPath);
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            rect,
            palette.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        );
    }

    private (Color Fill, Color Border, Color Text) ResolvePalette()
    {
        if (!Enabled)
        {
            return (
                Color.FromArgb(116, 255, 255, 255),
                Color.FromArgb(36, 91, 103, 132),
                Color.FromArgb(126, BridgeTheme.TextSecondary)
            );
        }

        if (BridgeKind == BridgeButtonKind.Primary)
        {
            var fill = _isPressed
                ? ControlPaint.Dark(BridgeTheme.AccentBlue, 0.10f)
                : _isHovered
                    ? ControlPaint.Light(BridgeTheme.AccentBlue, 0.04f)
                    : BridgeTheme.AccentBlue;

            return (fill, fill, Color.White);
        }

        var secondaryFill = _isPressed
            ? Color.FromArgb(236, 241, 248)
            : _isHovered
                ? Color.FromArgb(246, 248, 252)
                : BridgeTheme.SecondaryButtonFill;

        return (secondaryFill, BridgeTheme.SecondaryButtonBorder, BridgeTheme.TextPrimary);
    }
}
