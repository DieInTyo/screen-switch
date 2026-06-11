using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class MoveWindowPickerForm : Form
{
    private readonly WindowMover _windowMover;
    private readonly LocalizedStrings _text;
    private readonly Action<int> _onMoved;
    private readonly Action<Exception> _onError;
    private readonly ListView _windowList;
    private readonly Button _moveSelectedButton;
    private readonly Label _hintLabel;

    public MoveWindowPickerForm(
        WindowMover windowMover,
        LocalizedStrings text,
        Action<int> onMoved,
        Action<Exception> onError)
    {
        _windowMover = windowMover;
        _text = text;
        _onMoved = onMoved;
        _onError = onError;

        Text = _text.MoveWindowPickerTitle;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(620, 420);
        MinimumSize = new Size(480, 320);
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;

        _windowList = new ListView
        {
            CheckBoxes = true,
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details
        };
        _windowList.Columns.Add("Monitor", 80);
        _windowList.Columns.Add("Application", 150);
        _windowList.Columns.Add("Window", 340);
        _windowList.ItemChecked += (_, _) => UpdateMoveSelectedState();
        _windowList.DoubleClick += (_, _) => MoveFocusedWindow();

        _moveSelectedButton = new Button
        {
            Text = _text.MoveSelectedWindows,
            AutoSize = true,
            Enabled = false
        };
        _moveSelectedButton.Click += (_, _) => MoveSelectedWindows();

        _hintLabel = new Label
        {
            Text = _text.MoveWindowDoubleClickHint,
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        var bottomPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        bottomPanel.Controls.Add(_moveSelectedButton);
        bottomPanel.Controls.Add(_hintLabel);

        Controls.Add(_windowList);
        Controls.Add(bottomPanel);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        RefreshWindowList();
    }

    private void RefreshWindowList()
    {
        DiagnosticLog.Info("action move-window-picker refresh");
        _windowList.BeginUpdate();
        _windowList.Items.Clear();

        var windows = _windowMover
            .GetMovableWindows(includeMinimizedWindows: true)
            .OrderBy(window => window.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        foreach (var window in windows)
        {
            var item = new ListViewItem(FormatMonitorBadge(window.ScreenDeviceName))
            {
                Tag = window,
                ToolTipText = window.Title
            };
            item.SubItems.Add(window.AppName);
            item.SubItems.Add(window.Title);
            _windowList.Items.Add(item);
        }

        if (windows.Length == 0)
        {
            var item = new ListViewItem(string.Empty)
            {
                ForeColor = SystemColors.GrayText
            };
            item.SubItems.Add(string.Empty);
            item.SubItems.Add(_text.NoWindowsFound);
            _windowList.Items.Add(item);
        }

        _windowList.EndUpdate();
        UpdateMoveSelectedState();
        DiagnosticLog.Info($"action move-window-picker count={windows.Length}");
    }

    private void MoveSelectedWindows()
    {
        var handles = _windowList.CheckedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<MovableWindowInfo>()
            .Select(window => window.Handle)
            .ToArray();

        if (handles.Length == 0)
        {
            return;
        }

        try
        {
            var movedCount = 0;
            foreach (var handle in handles)
            {
                DiagnosticLog.Info($"action move-window-picker selected-batch hwnd={DiagnosticLog.FormatHandle(handle)}");
                if (_windowMover.MoveWindowToOtherMonitor(handle))
                {
                    movedCount++;
                }
            }

            _onMoved(movedCount);
            RefreshWindowList();
            BringPickerToFront();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Info($"action move-window-picker batch failed {ex.GetType().Name}: {ex.Message}");
            _onError(ex);
        }
    }

    private void MoveFocusedWindow()
    {
        if (_windowList.FocusedItem?.Tag is not MovableWindowInfo window)
        {
            return;
        }

        try
        {
            DiagnosticLog.Info(
                $"action move-window-picker selected hwnd={DiagnosticLog.FormatHandle(window.Handle)} title=\"{window.Title}\" app=\"{window.AppName}\" pid={window.ProcessId}");
            var moved = _windowMover.MoveWindowToOtherMonitor(window.Handle);
            _onMoved(moved ? 1 : 0);
            RefreshWindowList();
            BringPickerToFront();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Info($"action move-window-picker failed {ex.GetType().Name}: {ex.Message}");
            _onError(ex);
        }
    }

    private void UpdateMoveSelectedState()
    {
        _moveSelectedButton.Enabled = _windowList.CheckedItems
            .Cast<ListViewItem>()
            .Any(item => item.Tag is MovableWindowInfo);
    }

    private void BringPickerToFront()
    {
        TopMost = false;
        TopMost = true;
        Activate();
    }

    private static string FormatMonitorBadge(string screenDeviceName)
    {
        return $"[{GetMonitorNumber(screenDeviceName)}]";
    }

    private static int GetMonitorNumber(string screenDeviceName)
    {
        var screens = Screen.AllScreens
            .OrderBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();

        var index = Array.FindIndex(screens, screen => screen.DeviceName == screenDeviceName);
        return index >= 0 ? index + 1 : 0;
    }
}
