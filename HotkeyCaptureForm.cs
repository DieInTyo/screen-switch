using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class HotkeyCaptureForm : Form
{
    private const int VirtualKeyLeftWin = 0x5B;
    private const int VirtualKeyRightWin = 0x5C;
    private const int VirtualKeyLeftShift = 0xA0;
    private const int VirtualKeyRightShift = 0xA1;
    private const int VirtualKeyLeftControl = 0xA2;
    private const int VirtualKeyRightControl = 0xA3;
    private const int VirtualKeyLeftAlt = 0xA4;
    private const int VirtualKeyRightAlt = 0xA5;
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
            Control = IsKeyDown(VirtualKeyLeftControl) || IsKeyDown(VirtualKeyRightControl),
            Alt = IsKeyDown(VirtualKeyLeftAlt) || IsKeyDown(VirtualKeyRightAlt),
            Shift = IsKeyDown(VirtualKeyLeftShift) || IsKeyDown(VirtualKeyRightShift),
            Win = IsKeyDown(VirtualKeyLeftWin) || IsKeyDown(VirtualKeyRightWin),
            ControlSide = GetModifierSide(VirtualKeyLeftControl, VirtualKeyRightControl),
            AltSide = GetModifierSide(VirtualKeyLeftAlt, VirtualKeyRightAlt),
            ShiftSide = GetModifierSide(VirtualKeyLeftShift, VirtualKeyRightShift),
            WinSide = GetModifierSide(VirtualKeyLeftWin, VirtualKeyRightWin),
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
        return IsKeyDown(VirtualKeyLeftWin) || IsKeyDown(VirtualKeyRightWin);
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (NativeMethods.GetKeyState(virtualKey) & 0x8000) != 0;
    }

    private static ModifierKeySide GetModifierSide(int leftVirtualKey, int rightVirtualKey)
    {
        var leftDown = IsKeyDown(leftVirtualKey);
        var rightDown = IsKeyDown(rightVirtualKey);
        return (leftDown, rightDown) switch
        {
            (true, false) => ModifierKeySide.Left,
            (false, true) => ModifierKeySide.Right,
            _ => ModifierKeySide.Any
        };
    }

    private static string FormatGesture(HotkeyGesture? gesture, LocalizedStrings text)
    {
        return gesture is not null && gesture.IsValid()
            ? gesture.ToDisplayString()
            : text.NotAssigned;
    }
}
