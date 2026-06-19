using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class UiTheme
{
    private UiTheme(
        AppTheme theme,
        Color background,
        Color surface,
        Color surfaceAlt,
        Color foreground,
        Color mutedForeground,
        Color border,
        Color accent,
        Color selected)
    {
        Theme = theme;
        Background = background;
        Surface = surface;
        SurfaceAlt = surfaceAlt;
        Foreground = foreground;
        MutedForeground = mutedForeground;
        Border = border;
        Accent = accent;
        Selected = selected;
    }

    public AppTheme Theme { get; }
    public Color Background { get; }
    public Color Surface { get; }
    public Color SurfaceAlt { get; }
    public Color Foreground { get; }
    public Color MutedForeground { get; }
    public Color Border { get; }
    public Color Accent { get; }
    public Color Selected { get; }

    public static UiTheme For(AppTheme theme)
    {
        return theme == AppTheme.Dark
            ? new UiTheme(
                theme,
                Color.FromArgb(30, 30, 30),
                Color.FromArgb(38, 38, 38),
                Color.FromArgb(58, 70, 92),
                Color.FromArgb(245, 245, 245),
                Color.FromArgb(170, 170, 170),
                Color.FromArgb(78, 78, 78),
                Color.FromArgb(82, 145, 255),
                Color.FromArgb(48, 76, 120))
            : new UiTheme(
                theme,
                Color.FromArgb(245, 245, 245),
                Color.White,
                Color.FromArgb(238, 238, 238),
                Color.Black,
                Color.FromArgb(90, 90, 90),
                Color.FromArgb(190, 190, 190),
                Color.DodgerBlue,
                Color.FromArgb(220, 235, 255));
    }

    public ToolStripRenderer CreateRenderer()
    {
        return new ThemedToolStripRenderer(this);
    }

    public void ApplyToMenu(ToolStrip menu)
    {
        menu.BackColor = Surface;
        menu.ForeColor = Foreground;
        menu.Renderer = CreateRenderer();

        foreach (ToolStripItem item in menu.Items)
        {
            ApplyToMenuItem(item);
        }
    }

    private void ApplyToMenuItem(ToolStripItem item)
    {
        item.BackColor = Surface;
        if (item.ForeColor != Color.Firebrick && item.ForeColor != Color.IndianRed)
        {
            item.ForeColor = Foreground;
        }

        if (item is ToolStripControlHost { Control: OpacitySliderControl slider })
        {
            slider.ApplyTheme(this);
        }

        if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
        {
            menuItem.DropDown.BackColor = Surface;
            menuItem.DropDown.ForeColor = Foreground;
            menuItem.DropDown.Renderer = CreateRenderer();
            foreach (ToolStripItem child in menuItem.DropDownItems)
            {
                ApplyToMenuItem(child);
            }
        }
    }

    private sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
    {
        private readonly UiTheme _theme;

        public ThemedToolStripRenderer(UiTheme theme)
            : base(new MenuColorTable(theme))
        {
            _theme = theme;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var selected = e.Item is not null && (e.Item.Selected || e.Item.Pressed);
            using var brush = new SolidBrush(selected ? _theme.SurfaceAlt : _theme.Surface);
            var size = e.Item?.Size ?? e.ToolStrip?.Size ?? Size.Empty;
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, size));
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var rect = e.ImageRectangle;
            rect.Inflate(2, 2);
            using var background = new SolidBrush(_theme.Selected);
            using var border = new Pen(_theme.Accent);
            e.Graphics.FillRectangle(background, rect);
            e.Graphics.DrawRectangle(border, rect);

            var check = new[]
            {
                new Point(rect.Left + 4, rect.Top + rect.Height / 2),
                new Point(rect.Left + rect.Width / 2 - 1, rect.Bottom - 5),
                new Point(rect.Right - 4, rect.Top + 4)
            };
            using var pen = new Pen(_theme.Foreground, 2);
            e.Graphics.DrawLines(pen, check);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item?.Enabled != false ? _theme.Foreground : _theme.MutedForeground;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item?.Enabled != false ? e.Item?.ForeColor ?? _theme.Foreground : _theme.MutedForeground;
            base.OnRenderItemText(e);
        }
    }

    private sealed class MenuColorTable : ProfessionalColorTable
    {
        private readonly UiTheme _theme;

        public MenuColorTable(UiTheme theme)
        {
            _theme = theme;
            UseSystemColors = false;
        }

        public override Color MenuItemSelected => _theme.SurfaceAlt;
        public override Color MenuItemBorder => _theme.Border;
        public override Color MenuBorder => _theme.Border;
        public override Color ToolStripDropDownBackground => _theme.Surface;
        public override Color ImageMarginGradientBegin => _theme.Surface;
        public override Color ImageMarginGradientMiddle => _theme.Surface;
        public override Color ImageMarginGradientEnd => _theme.Surface;
        public override Color CheckBackground => _theme.SurfaceAlt;
        public override Color CheckSelectedBackground => _theme.SurfaceAlt;
        public override Color CheckPressedBackground => _theme.SurfaceAlt;
        public override Color SeparatorDark => _theme.Border;
        public override Color SeparatorLight => _theme.Border;
    }
}
