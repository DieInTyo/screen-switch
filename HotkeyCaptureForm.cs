using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class HotkeyCaptureForm : Form
{
    private const int VirtualKeyLeftWin = 0x5B;
    private const int VirtualKeyRightWin = 0x5C;
    private readonly LocalizedStrings _text;
    private readonly Label _hintLabel;
    private readonly Label _currentLabel;

    public HotkeyCaptureForm(string actionName, HotkeyGesture? currentGesture, LocalizedStrings text)
    {
        _text = text;
        Text = _text.HotkeyCaptureTitle(actionName);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(420, 150);

        _hintLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 64,
            Text = _text.HotkeyCapturePrompt
        };

        _currentLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 36,
            Text = _text.HotkeyCaptureCurrent(FormatGesture(currentGesture, _text))
        };

        var noteLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Text = _text.HotkeyCaptureClearNote
        };

        Controls.Add(noteLabel);
        Controls.Add(_currentLabel);
        Controls.Add(_hintLabel);
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
