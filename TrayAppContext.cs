using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class TrayAppContext : ApplicationContext
{
    private const int NormalTrackerInterval = 250;
    private const int PendingTrackerInterval = 40;
    private readonly NotifyIcon _notifyIcon;
    private readonly SettingsStore _settingsStore;
    private readonly StartupManager _startupManager;
    private readonly System.Windows.Forms.Timer _foregroundTracker;
    private readonly WindowMover _windowMover = new();
    private readonly HotkeyManager _hotkeyManager;
    private readonly AppSettings _settings;
    private readonly ToolStripMenuItem _leftClickActiveItem;
    private readonly ToolStripMenuItem _leftClickAllItem;
    private readonly ToolStripMenuItem _moveMinimizedItem;
    private readonly ToolStripMenuItem _hotkeysEnabledItem;
    private readonly ToolStripMenuItem _hotkeySelectedModeItem;
    private readonly ToolStripMenuItem _hotkeyActiveWindowItem;
    private readonly ToolStripMenuItem _hotkeyAllWindowsItem;
    private readonly ToolStripMenuItem _notificationsItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Dictionary<string, IntPtr> _lastWindowByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<HotkeyAction, string> _hotkeyRegistrationErrors = new();
    private TrackedWindow _lastTrackedWindow;
    private bool _allowMenuCloseOnce;

    public TrayAppContext()
    {
        DiagnosticLog.Start();
        _settingsStore = new SettingsStore();
        _startupManager = new StartupManager();
        _settings = _settingsStore.Load();

        var menu = new ContextMenuStrip
        {
            ShowItemToolTips = true
        };
        menu.Closing += MenuOnClosing;
        menu.Closed += (_, _) => _allowMenuCloseOnce = false;
        menu.Items.Add("Переместить активное окно", null, (_, _) =>
        {
            AllowMenuClose();
            MoveActiveWindow();
        });
        menu.Items.Add("Переместить все окна", null, (_, _) =>
        {
            AllowMenuClose();
            MoveAllWindows();
        });
        menu.Items.Add(new ToolStripSeparator());

        var leftClickMenu = new ToolStripMenuItem("Левый клик");
        leftClickMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(leftClickMenu);
        leftClickMenu.DropDown.Closing += MenuOnClosing;
        _leftClickActiveItem = new ToolStripMenuItem("Перемещать активное окно", null, (_, _) => SetLeftClickAction(LeftClickAction.ActiveWindow))
        {
            CheckOnClick = true
        };
        _leftClickAllItem = new ToolStripMenuItem("Перемещать все окна", null, (_, _) => SetLeftClickAction(LeftClickAction.AllWindows))
        {
            CheckOnClick = true
        };
        leftClickMenu.DropDownItems.Add(_leftClickActiveItem);
        leftClickMenu.DropDownItems.Add(_leftClickAllItem);
        menu.Items.Add(leftClickMenu);

        var hotkeysMenu = new ToolStripMenuItem("Горячие клавиши");
        hotkeysMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(hotkeysMenu);
        hotkeysMenu.DropDown.ShowItemToolTips = true;
        hotkeysMenu.DropDown.Closing += MenuOnClosing;
        _hotkeysEnabledItem = new ToolStripMenuItem("Включить горячие клавиши", null, ToggleHotkeys)
        {
            CheckOnClick = true,
            Checked = _settings.HotkeysEnabled
        };
        _hotkeySelectedModeItem = new ToolStripMenuItem(string.Empty, null, (_, _) => CaptureHotkey(HotkeyAction.SelectedMode));
        _hotkeyActiveWindowItem = new ToolStripMenuItem(string.Empty, null, (_, _) => CaptureHotkey(HotkeyAction.ActiveWindow));
        _hotkeyAllWindowsItem = new ToolStripMenuItem(string.Empty, null, (_, _) => CaptureHotkey(HotkeyAction.AllWindows));
        hotkeysMenu.DropDownItems.Add(_hotkeysEnabledItem);
        hotkeysMenu.DropDownItems.Add(new ToolStripSeparator());
        hotkeysMenu.DropDownItems.Add(_hotkeySelectedModeItem);
        hotkeysMenu.DropDownItems.Add(_hotkeyActiveWindowItem);
        hotkeysMenu.DropDownItems.Add(_hotkeyAllWindowsItem);
        hotkeysMenu.DropDownItems.Add(new ToolStripSeparator());
        hotkeysMenu.DropDownItems.Add("Сбросить горячие клавиши", null, (_, _) => ResetHotkeys());
        menu.Items.Add(hotkeysMenu);

        _moveMinimizedItem = new ToolStripMenuItem("Переносить свернутые окна", null, ToggleMoveMinimizedWindows)
        {
            CheckOnClick = true,
            Checked = _settings.MoveMinimizedWindows
        };
        menu.Items.Add(_moveMinimizedItem);

        _notificationsItem = new ToolStripMenuItem("Показывать уведомления", null, ToggleNotifications)
        {
            CheckOnClick = true,
            Checked = _settings.ShowNotifications
        };
        menu.Items.Add(_notificationsItem);

        _startupItem = new ToolStripMenuItem("Запускать вместе с Windows", null, ToggleStartup)
        {
            CheckOnClick = true,
            Checked = _startupManager.IsEnabled()
        };
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) =>
        {
            AllowMenuClose();
            ExitThread();
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Screen Switch",
            Visible = true,
            ContextMenuStrip = menu
        };

        ApplyLeftClickChecks();
        UpdateHotkeyMenuText();

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        ApplyHotkeyRegistrations(showFailures: true);

        _notifyIcon.MouseClick += NotifyIconOnMouseClick;
        _foregroundTracker = new System.Windows.Forms.Timer
        {
            Interval = NormalTrackerInterval
        };
        _foregroundTracker.Tick += TrackForegroundWindow;
        _foregroundTracker.Start();
        ShowStatus("Левый клик выполняет выбранное действие из меню.");
    }

    protected override void ExitThreadCore()
    {
        _foregroundTracker.Stop();
        _foregroundTracker.Dispose();
        _hotkeyManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }

    private void NotifyIconOnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        ExecuteSelectedMode();
    }

    private void MoveActiveWindow()
    {
        var fallbackOtherMonitorWindow = GetLastWindowOnOtherMonitor();

        try
        {
            var movedCount = _windowMover.MoveActiveWindowBetweenMonitors(_lastTrackedWindow.Handle, fallbackOtherMonitorWindow.Handle);
            UpdateTrackerInterval();

            if (movedCount >= 2)
            {
                ShowStatus("Активное окно и окно на другом мониторе поменялись местами.");
                return;
            }

            ShowStatus($"Перемещено окон: {movedCount}.");
        }
        catch (Exception ex)
        {
            UpdateTrackerInterval();
            ShowStatus(ex.Message, ToolTipIcon.Warning);
        }
    }

    private void MoveAllWindows()
    {
        ExecuteMove(
            () => _windowMover.MoveAllWindowsBetweenMonitors(_settings.MoveMinimizedWindows),
            count => $"Перемещено окон: {count}.");
    }

    private void ExecuteMove(Func<int> action, Func<int, string> successMessageFactory)
    {
        try
        {
            var movedCount = action();
            UpdateTrackerInterval();
            ShowStatus(successMessageFactory(movedCount));
        }
        catch (Exception ex)
        {
            UpdateTrackerInterval();
            ShowStatus(ex.Message, ToolTipIcon.Warning);
        }
    }

    private void SetLeftClickAction(LeftClickAction action)
    {
        _settings.LeftClickAction = action;
        _settingsStore.Save(_settings);
        ApplyLeftClickChecks();

        var description = action == LeftClickAction.ActiveWindow
            ? "Теперь левый клик переносит активное окно."
            : "Теперь левый клик переносит все окна.";
        ShowStatus(description);
    }

    private void ApplyLeftClickChecks()
    {
        _leftClickActiveItem.Checked = _settings.LeftClickAction == LeftClickAction.ActiveWindow;
        _leftClickAllItem.Checked = _settings.LeftClickAction == LeftClickAction.AllWindows;
    }

    private void MenuOnClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && !_allowMenuCloseOnce)
        {
            e.Cancel = true;
        }
    }

    private void AllowMenuClose()
    {
        _allowMenuCloseOnce = true;
    }

    private static void ApplyMonitorAwareDropDownDirection(ToolStripMenuItem item)
    {
        if (item.Owner is null)
        {
            return;
        }

        var itemBounds = item.Owner.RectangleToScreen(item.Bounds);
        var ownerScreen = Screen.FromRectangle(itemBounds);
        var dropDownSize = item.DropDown.GetPreferredSize(Size.Empty);
        var rightAvailable = ownerScreen.WorkingArea.Right - itemBounds.Right;
        var leftAvailable = itemBounds.Left - ownerScreen.WorkingArea.Left;

        item.DropDownDirection = rightAvailable >= dropDownSize.Width || rightAvailable >= leftAvailable
            ? ToolStripDropDownDirection.Right
            : ToolStripDropDownDirection.Left;
    }

    private void ExecuteSelectedMode()
    {
        if (_settings.LeftClickAction == LeftClickAction.AllWindows)
        {
            MoveAllWindows();
            return;
        }

        MoveActiveWindow();
    }

    private void OnHotkeyPressed(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.SelectedMode:
                ExecuteSelectedMode();
                break;
            case HotkeyAction.ActiveWindow:
                MoveActiveWindow();
                break;
            case HotkeyAction.AllWindows:
                MoveAllWindows();
                break;
        }
    }

    private void ToggleHotkeys(object? sender, EventArgs e)
    {
        _settings.HotkeysEnabled = _hotkeysEnabledItem.Checked;
        _settingsStore.Save(_settings);
        ApplyHotkeyRegistrations(showFailures: true);
        ShowStatus(_settings.HotkeysEnabled
            ? "Горячие клавиши включены."
            : "Горячие клавиши выключены.");
    }

    private void CaptureHotkey(HotkeyAction action)
    {
        _hotkeyManager.UnregisterAll();
        try
        {
            using var dialog = new HotkeyCaptureForm(GetHotkeyActionName(action), GetHotkeyGesture(action));
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            SetHotkeyGesture(action, dialog.SelectedGesture);
            _settingsStore.Save(_settings);
            UpdateHotkeyMenuText();
            ShowStatus(dialog.SelectedGesture is null
                ? $"{GetHotkeyActionName(action)}: горячая клавиша очищена."
                : $"{GetHotkeyActionName(action)}: {dialog.SelectedGesture.ToDisplayString()}.");
        }
        finally
        {
            ApplyHotkeyRegistrations(showFailures: true);
        }
    }

    private void ResetHotkeys()
    {
        _settings.SelectedModeHotkey = null;
        _settings.ActiveWindowHotkey = null;
        _settings.AllWindowsHotkey = null;
        _settingsStore.Save(_settings);
        UpdateHotkeyMenuText();
        ApplyHotkeyRegistrations(showFailures: true);
        ShowStatus("Горячие клавиши сброшены.");
    }

    private void ApplyHotkeyRegistrations(bool showFailures)
    {
        var failures = _hotkeyManager.Configure(_settings);
        _hotkeyRegistrationErrors.Clear();
        foreach (var failure in failures)
        {
            _hotkeyRegistrationErrors[failure.Action] = failure.Message;
        }

        UpdateHotkeyMenuText();
        if (showFailures && failures.Count > 0)
        {
            ShowStatus($"{GetHotkeyActionName(failures[0].Action)}: {failures[0].Message}", ToolTipIcon.Warning);
        }
    }

    private void UpdateHotkeyMenuText()
    {
        _hotkeysEnabledItem.Checked = _settings.HotkeysEnabled;
        UpdateHotkeyMenuItem(_hotkeySelectedModeItem, "Выбранный режим", _settings.SelectedModeHotkey, HotkeyAction.SelectedMode);
        UpdateHotkeyMenuItem(_hotkeyActiveWindowItem, "Активное окно", _settings.ActiveWindowHotkey, HotkeyAction.ActiveWindow);
        UpdateHotkeyMenuItem(_hotkeyAllWindowsItem, "Все окна", _settings.AllWindowsHotkey, HotkeyAction.AllWindows);
    }

    private void UpdateHotkeyMenuItem(
        ToolStripMenuItem item,
        string label,
        HotkeyGesture? gesture,
        HotkeyAction action)
    {
        item.Text = $"{label}: {FormatHotkey(gesture)}";
        if (_hotkeyRegistrationErrors.TryGetValue(action, out var error))
        {
            item.ForeColor = Color.Firebrick;
            item.ToolTipText = error;
            return;
        }

        item.ForeColor = SystemColors.MenuText;
        item.ToolTipText = "Нажмите, чтобы назначить горячую клавишу.";
    }

    private HotkeyGesture? GetHotkeyGesture(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.SelectedMode => _settings.SelectedModeHotkey?.Clone(),
            HotkeyAction.ActiveWindow => _settings.ActiveWindowHotkey?.Clone(),
            HotkeyAction.AllWindows => _settings.AllWindowsHotkey?.Clone(),
            _ => null
        };
    }

    private void SetHotkeyGesture(HotkeyAction action, HotkeyGesture? gesture)
    {
        switch (action)
        {
            case HotkeyAction.SelectedMode:
                _settings.SelectedModeHotkey = gesture;
                break;
            case HotkeyAction.ActiveWindow:
                _settings.ActiveWindowHotkey = gesture;
                break;
            case HotkeyAction.AllWindows:
                _settings.AllWindowsHotkey = gesture;
                break;
        }
    }

    private static string FormatHotkey(HotkeyGesture? gesture)
    {
        return gesture?.ToDisplayString() ?? "Не назначено";
    }

    private static string GetHotkeyActionName(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.SelectedMode => "Выбранный режим",
            HotkeyAction.ActiveWindow => "Активное окно",
            HotkeyAction.AllWindows => "Все окна",
            _ => "Действие"
        };
    }

    private void ToggleStartup(object? sender, EventArgs e)
    {
        try
        {
            var enabled = _startupItem.Checked;
            _startupManager.SetEnabled(enabled);
            ShowStatus(enabled
                ? "Автозапуск включен."
                : "Автозапуск выключен.");
        }
        catch (Exception ex)
        {
            _startupItem.Checked = _startupManager.IsEnabled();
            ShowStatus($"Не удалось изменить автозапуск: {ex.Message}", ToolTipIcon.Warning);
        }
    }

    private void ToggleNotifications(object? sender, EventArgs e)
    {
        _settings.ShowNotifications = _notificationsItem.Checked;
        _settingsStore.Save(_settings);

        if (_settings.ShowNotifications)
        {
            ShowStatus("Уведомления включены.");
        }
    }

    private void ToggleMoveMinimizedWindows(object? sender, EventArgs e)
    {
        _settings.MoveMinimizedWindows = _moveMinimizedItem.Checked;
        _settingsStore.Save(_settings);
        ShowStatus(_settings.MoveMinimizedWindows
            ? "Свернутые окна будут переноситься."
            : "Свернутые окна будут пропускаться.");
    }

    private void TrackForegroundWindow(object? sender, EventArgs e)
    {
        try
        {
            _windowMover.ProcessPendingFullscreenTransfers();
            UpdateTrackerInterval();
        }
        catch (Exception ex)
        {
            UpdateTrackerInterval();
            DiagnosticLog.Info($"pending check failed {ex.GetType().Name}: {ex.Message}");
        }

        if (_windowMover.TryGetMovableForegroundWindow(out var trackedWindow))
        {
            RemoveHandleFromMonitorCache(trackedWindow.Handle);
            _lastTrackedWindow = trackedWindow;
            _lastWindowByMonitor[trackedWindow.ScreenDeviceName] = trackedWindow.Handle;
        }
    }

    private void UpdateTrackerInterval()
    {
        var targetInterval = _windowMover.HasPendingFullscreenTransfers
            ? PendingTrackerInterval
            : NormalTrackerInterval;

        if (_foregroundTracker.Interval != targetInterval)
        {
            _foregroundTracker.Interval = targetInterval;
        }
    }

    private TrackedWindow GetLastWindowOnOtherMonitor()
    {
        if (_lastTrackedWindow.Handle == IntPtr.Zero)
        {
            return default;
        }

        foreach (var entry in _lastWindowByMonitor)
        {
            if (!string.Equals(entry.Key, _lastTrackedWindow.ScreenDeviceName, StringComparison.Ordinal))
            {
                return new TrackedWindow(entry.Value, entry.Key);
            }
        }

        return default;
    }

    private void RememberSwap(TrackedWindow otherMonitorWindow)
    {
        if (_lastTrackedWindow.Handle == IntPtr.Zero || otherMonitorWindow.Handle == IntPtr.Zero)
        {
            return;
        }

        var previousActive = _lastTrackedWindow;
        RemoveHandleFromMonitorCache(previousActive.Handle);
        RemoveHandleFromMonitorCache(otherMonitorWindow.Handle);

        _lastWindowByMonitor[previousActive.ScreenDeviceName] = otherMonitorWindow.Handle;
        _lastWindowByMonitor[otherMonitorWindow.ScreenDeviceName] = previousActive.Handle;
        _lastTrackedWindow = new TrackedWindow(previousActive.Handle, otherMonitorWindow.ScreenDeviceName);
    }

    private void RemoveHandleFromMonitorCache(IntPtr handle)
    {
        if (handle == IntPtr.Zero || _lastWindowByMonitor.Count == 0)
        {
            return;
        }

        var keysToRemove = _lastWindowByMonitor
            .Where(entry => entry.Value == handle)
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var key in keysToRemove)
        {
            _lastWindowByMonitor.Remove(key);
        }
    }

    private void ShowStatus(string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (!_settings.ShowNotifications)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = "Screen Switch";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(2500);
    }
}
