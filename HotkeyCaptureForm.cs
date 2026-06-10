using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class HotkeyCaptureForm : Form
{
    private const int VirtualKeyLeftWin = 0x5B;
    private const int VirtualKeyRightWin = 0x5C;
    private readonly Label _hintLabel;
    private readonly Label _currentLabel;

    public HotkeyCaptureForm(string actionName, HotkeyGesture? currentGesture)
    {
        Text = $"Горячая клавиша: {actionName}";
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
            Text = "Нажми сочетание с Ctrl, Alt, Shift или Win"
        };

        _currentLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 36,
            Text = $"Сейчас: {FormatGesture(currentGesture)}"
        };

        var noteLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Text = "Esc, Space, Backspace или Delete оставят поле пустым"
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
            _hintLabel.Text = "Добавь обычную клавишу к модификатору";
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
            _hintLabel.Text = "Нужно сочетание с Ctrl, Alt, Shift или Win";
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

    private static string FormatGesture(HotkeyGesture? gesture)
    {
        return gesture?.ToDisplayString() ?? "Не назначено";
    }
}
