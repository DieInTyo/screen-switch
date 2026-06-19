using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class HotkeyCaptureForm : Form
{
    private const int VirtualKeyLeftWin = 0x5B;
    private const int VirtualKeyRightWin = 0x5C;
    private readonly LocalizedStrings _text;
    private readonly UiTheme _theme;
    private readonly Label _hintLabel;
    private readonly Label _currentLabel;
    private readonly Label _noteLabel;

    public HotkeyCaptureForm(
        string actionName,
        HotkeyGesture? currentGesture,
        LocalizedStrings text,
        UiTheme theme,
        Point? preferredLocation)
    {
        _text = text;
        _theme = theme;
        Text = _text.HotkeyCaptureTitle(actionName);
        StartPosition = preferredLocation.HasValue ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        ClientSize = new Size(360, 118);

        _hintLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(10, 6, 10, 2),
            Text = _text.HotkeyCapturePrompt
        };

        _currentLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(10, 2, 10, 2),
            Text = _text.HotkeyCaptureCurrent(FormatGesture(currentGesture, _text))
        };

        _noteLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 2, 10, 8),
            Text = _text.HotkeyCaptureClearNote
        };

        Controls.Add(_noteLabel);
        Controls.Add(_currentLabel);
        Controls.Add(_hintLabel);
        ApplyTheme();

        if (preferredLocation.HasValue)
        {
            Location = ClampLocation(preferredLocation.Value, Size);
        }
    }

    public HotkeyGesture? SelectedGesture { get; private set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode is Keys.Escape or Keys.Space or Keys.Back or Keys.Delete)
        {
            SelectedGesture = null;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (HotkeyGesture.IsModifierKey(e.KeyCode))
        {
            _hintLabel.Text = _text.HotkeyCaptureModifierOnly;
            return;
        }

        var gesture = new HotkeyGesture
        {
            Control = e.Control,
            Alt = e.Alt,
            Shift = e.Shift,
            Win = IsWinKeyDown(),
            Key = e.KeyCode
        };

        if (!gesture.IsValid())
        {
            _hintLabel.Text = _text.HotkeyCaptureNeedsModifier;
            return;
        }

        SelectedGesture = gesture;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ApplyTheme()
    {
        BackColor = _theme.Background;
        ForeColor = _theme.Foreground;
        foreach (var label in Controls.OfType<Label>())
        {
            label.BackColor = _theme.Background;
            label.ForeColor = label == _noteLabel ? _theme.MutedForeground : _theme.Foreground;
        }
    }

    private static Point ClampLocation(Point preferredLocation, Size size)
    {
        var area = Screen.FromPoint(preferredLocation).WorkingArea;
        var x = Math.Clamp(preferredLocation.X + 8, area.Left, Math.Max(area.Left, area.Right - size.Width));
        var y = Math.Clamp(preferredLocation.Y + 8, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Point(x, y);
    }

    private static bool IsWinKeyDown()
    {
        return (NativeMethods.GetKeyState(VirtualKeyLeftWin) & 0x8000) != 0
            || (NativeMethods.GetKeyState(VirtualKeyRightWin) & 0x8000) != 0;
    }

    private static string FormatGesture(HotkeyGesture? gesture, LocalizedStrings text)
    {
        return gesture is not null && gesture.IsValid()
            ? gesture.ToDisplayString()
            : text.NotAssigned;
    }
}
