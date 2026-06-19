using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class OpacitySliderControl : Control
{
    private const int MinimumValue = 50;
    private const int MaximumValue = 100;
    private const int Step = 5;
    private UiTheme _theme = UiTheme.For(AppTheme.Light);
    private bool _dragging;
    private bool _hovered;
    private int _value = 100;

    public OpacitySliderControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable
            | ControlStyles.UserPaint,
            true);
        Cursor = Cursors.Hand;
        Size = new Size(126, 14);
    }

    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            var normalized = Normalize(value);
            if (_value == normalized)
            {
                return;
            }

            _value = normalized;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ApplyTheme(UiTheme theme)
    {
        _theme = theme;
        BackColor = theme.Surface;
        ForeColor = theme.Foreground;
        Invalidate();
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
        if (!_dragging)
        {
            Invalidate();
        }

        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            base.OnMouseDown(e);
            return;
        }

        Focus();
        _dragging = true;
        SetValueFromX(e.X);
        Capture = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            SetValueFromX(e.X);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
        }

        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Left or Keys.Down)
        {
            Value -= Step;
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Right or Keys.Up)
        {
            Value += Step;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var trackRect = GetTrackRect();
        using var trackBrush = new SolidBrush(_theme.Theme == AppTheme.Dark ? Color.FromArgb(86, 86, 86) : Color.FromArgb(205, 205, 205));
        using var fillBrush = new SolidBrush(_theme.Accent);
        using var thumbBrush = new SolidBrush(_hovered || _dragging ? _theme.Accent : _theme.Foreground);
        using var borderPen = new Pen(_theme.Border);

        e.Graphics.FillRectangle(trackBrush, trackRect);
        var fillWidth = Math.Max(0, GetThumbCenterX() - trackRect.Left);
        e.Graphics.FillRectangle(fillBrush, trackRect.Left, trackRect.Top, fillWidth, trackRect.Height);

        var thumb = GetThumbRect();
        e.Graphics.FillEllipse(thumbBrush, thumb);
        e.Graphics.DrawEllipse(borderPen, thumb);
    }

    private void SetValueFromX(int x)
    {
        var track = GetTrackRect();
        var clamped = Math.Clamp(x, track.Left, track.Right);
        var ratio = track.Width <= 0 ? 0 : (clamped - track.Left) / (double)track.Width;
        Value = MinimumValue + (int)Math.Round((MaximumValue - MinimumValue) * ratio / Step) * Step;
    }

    private Rectangle GetTrackRect()
    {
        const int thumbRadius = 5;
        var left = thumbRadius;
        var width = Math.Max(1, Width - thumbRadius * 2);
        return new Rectangle(left, Height / 2 - 1, width, 3);
    }

    private Rectangle GetThumbRect()
    {
        const int size = 10;
        return new Rectangle(GetThumbCenterX() - size / 2, Height / 2 - size / 2, size, size);
    }

    private int GetThumbCenterX()
    {
        var track = GetTrackRect();
        var ratio = (_value - MinimumValue) / (double)(MaximumValue - MinimumValue);
        return track.Left + (int)Math.Round(track.Width * ratio);
    }

    private static int Normalize(int value)
    {
        value = Math.Clamp(value, MinimumValue, MaximumValue);
        return (int)Math.Round(value / (double)Step) * Step;
    }
}
