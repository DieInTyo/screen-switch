using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal enum MoveDirection
{
    Both,
    Left,
    Right
}

internal sealed class MoveDirectionButton : Control
{
    private UiTheme _theme = UiTheme.For(AppTheme.Light);
    private MoveDirection _direction = MoveDirection.Both;
    private bool _hovered;
    private bool _pressed;

    public MoveDirectionButton()
    {
        Cursor = Cursors.Hand;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.UserPaint,
            true);
    }

    public MoveDirection Direction
    {
        get => _direction;
        set
        {
            if (_direction == value)
            {
                return;
            }

            _direction = value;
            Invalidate();
        }
    }

    public void ApplyTheme(UiTheme theme)
    {
        _theme = theme;
        BackColor = theme.Background;
        ForeColor = Enabled ? theme.Foreground : theme.MutedForeground;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (_hovered && Enabled)
        {
            using var hoverBrush = new SolidBrush(_pressed ? _theme.Selected : _theme.SurfaceAlt);
            e.Graphics.FillRectangle(hoverBrush, 1, 1, Width - 2, Height - 2);
        }

        using (var dividerPen = new Pen(_theme.Border))
        {
            var dividerX = Width / 2;
            e.Graphics.DrawLine(dividerPen, dividerX, 0, dividerX, Height);
        }

        var color = Enabled ? _theme.Foreground : _theme.MutedForeground;
        using var pen = new Pen(color, 1.8f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        var outlineColor = _hovered && Enabled
            ? _pressed ? _theme.Selected : _theme.SurfaceAlt
            : _theme.Background;
        using var outlinePen = new Pen(outlineColor, 4.2f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };

        if (_direction == MoveDirection.Left)
        {
            var start = new PointF(Width / 2f + 5, Height / 2f);
            var end = new PointF(Width / 2f - 5, Height / 2f);
            DrawArrow(e.Graphics, outlinePen, start, end, left: true);
            DrawArrow(e.Graphics, pen, start, end, left: true);
            return;
        }

        if (_direction == MoveDirection.Right)
        {
            var start = new PointF(Width / 2f - 5, Height / 2f);
            var end = new PointF(Width / 2f + 5, Height / 2f);
            DrawArrow(e.Graphics, outlinePen, start, end, left: false);
            DrawArrow(e.Graphics, pen, start, end, left: false);
            return;
        }

        var leftStart = new PointF(Width / 2f + 1, Height / 2f - 3);
        var leftEnd = new PointF(Width / 2f - 6, Height / 2f - 3);
        var rightStart = new PointF(Width / 2f - 1, Height / 2f + 3);
        var rightEnd = new PointF(Width / 2f + 6, Height / 2f + 3);
        DrawArrow(e.Graphics, outlinePen, leftStart, leftEnd, left: true);
        DrawArrow(e.Graphics, outlinePen, rightStart, rightEnd, left: false);
        DrawArrow(e.Graphics, pen, leftStart, leftEnd, left: true);
        DrawArrow(e.Graphics, pen, rightStart, rightEnd, left: false);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    private static void DrawArrow(Graphics graphics, Pen pen, PointF start, PointF end, bool left)
    {
        graphics.DrawLine(pen, start, end);
        var headX = end.X;
        var headY = end.Y;
        var sign = left ? 1 : -1;
        graphics.DrawLine(pen, headX, headY, headX + sign * 4, headY - 3);
        graphics.DrawLine(pen, headX, headY, headX + sign * 4, headY + 3);
    }
}
