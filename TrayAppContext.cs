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
    private readonly AppSettings _settings;
    private readonly ToolStripMenuItem _leftClickActiveItem;
    private readonly ToolStripMenuItem _leftClickAllItem;
    private readonly ToolStripMenuItem _notificationsItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Dictionary<string, IntPtr> _lastWindowByMonitor = new(StringComparer.Ordinal);
    private TrackedWindow _lastTrackedWindow;

    public TrayAppContext()
    {
        DiagnosticLog.Start();
        _settingsStore = new SettingsStore();
        _startupManager = new StartupManager();
        _settings = _settingsStore.Load();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Переместить активное окно", null, (_, _) => MoveActiveWindow());
        menu.Items.Add("Переместить все окна", null, (_, _) => MoveAllWindows());
        menu.Items.Add(new ToolStripSeparator());

        var leftClickMenu = new ToolStripMenuItem("Левый клик");
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
        menu.Items.Add("Выход", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Screen Switch",
            Visible = true,
            ContextMenuStrip = menu
        };

        ApplyLeftClickChecks();

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

        if (_settings.LeftClickAction == LeftClickAction.AllWindows)
        {
            MoveAllWindows();
            return;
        }

        MoveActiveWindow();
    }

    private void MoveActiveWindow()
    {
        var otherMonitorWindow = GetLastWindowOnOtherMonitor();

        try
        {
            if (otherMonitorWindow.Handle == IntPtr.Zero)
            {
                ShowStatus("На другом мониторе нет запомненного открытого окна для обмена.");
                return;
            }

            var movedCount = _windowMover.MoveActiveWindowBetweenMonitors(_lastTrackedWindow.Handle, otherMonitorWindow.Handle);
            UpdateTrackerInterval();

            if (movedCount >= 2)
            {
                RememberSwap(otherMonitorWindow);
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
            () => _windowMover.MoveAllWindowsBetweenMonitors(),
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
