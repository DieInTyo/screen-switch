using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly WindowMover _windowMover = new();

    public TrayAppContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Переместить окна", null, (_, _) => MoveWindows());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Screen Switch",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.MouseClick += NotifyIconOnMouseClick;
        _notifyIcon.ShowBalloonTip(
            2500,
            "Screen Switch",
            "Левый клик переносит окна между двумя мониторами.",
            ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }

    private void NotifyIconOnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            MoveWindows();
        }
    }

    private void MoveWindows()
    {
        try
        {
            var movedCount = _windowMover.MoveAllWindowsBetweenMonitors();
            ShowStatus($"Перемещено окон: {movedCount}.");
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, ToolTipIcon.Warning);
        }
    }

    private void ShowStatus(string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.BalloonTipTitle = "Screen Switch";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(2500);
    }
}
