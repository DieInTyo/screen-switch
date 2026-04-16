using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SettingsStore _settingsStore;
    private readonly StartupManager _startupManager;
    private readonly WindowMover _windowMover = new();
    private readonly AppSettings _settings;
    private readonly ToolStripMenuItem _leftClickActiveItem;
    private readonly ToolStripMenuItem _leftClickAllItem;
    private readonly ToolStripMenuItem _notificationsItem;
    private readonly ToolStripMenuItem _startupItem;

    public TrayAppContext()
    {
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
        ShowStatus("Левый клик выполняет выбранное действие из меню.");
    }

    protected override void ExitThreadCore()
    {
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
        ExecuteMove(
            () => _windowMover.MoveActiveWindowBetweenMonitors(),
            count => count == 1 ? "Активное окно перенесено." : $"Перемещено окон: {count}.");
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
            ShowStatus(successMessageFactory(movedCount));
        }
        catch (Exception ex)
        {
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
