using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class WindowMover
{
    private const int DwmaCloaked = 14;
    private readonly int _currentProcessId = Environment.ProcessId;

    public int MoveAllWindowsBetweenMonitors()
    {
        var screens = Screen.AllScreens
            .OrderBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();

        if (screens.Length != 2)
        {
            throw new InvalidOperationException("Приложение работает только когда подключено ровно два монитора.");
        }

        var moved = 0;

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!ShouldMoveWindow(handle))
            {
                return true;
            }

            if (!NativeMethods.GetWindowPlacement(handle, out var placement))
            {
                return true;
            }

            var normalBounds = placement.rcNormalPosition.ToRectangle();
            if (normalBounds.Width <= 0 || normalBounds.Height <= 0)
            {
                return true;
            }

            var currentScreen = Screen.FromRectangle(normalBounds);
            if (screens.All(screen => screen.DeviceName != currentScreen.DeviceName))
            {
                return true;
            }

            var targetScreen = screens[0].DeviceName == currentScreen.DeviceName ? screens[1] : screens[0];
            var targetBounds = MapBounds(normalBounds, currentScreen.WorkingArea, targetScreen.WorkingArea);

            placement.length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();
            placement.flags = 0;
            placement.rcNormalPosition = NativeMethods.RECT.FromRectangle(targetBounds);

            if (!NativeMethods.SetWindowPlacement(handle, ref placement))
            {
                return true;
            }

            if (placement.showCmd == NativeMethods.ShowWindowCommand.Maximize)
            {
                NativeMethods.ShowWindow(handle, NativeMethods.ShowWindowCommand.Restore);
                NativeMethods.SetWindowPlacement(handle, ref placement);
                NativeMethods.ShowWindow(handle, NativeMethods.ShowWindowCommand.Maximize);
            }

            moved++;
            return true;
        }, IntPtr.Zero);

        return moved;
    }

    private bool ShouldMoveWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == NativeMethods.GetShellWindow())
        {
            return false;
        }

        if (!NativeMethods.IsWindowVisible(handle))
        {
            return false;
        }

        if (NativeMethods.GetWindow(handle, NativeMethods.GetWindowCommand.Owner) != IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == _currentProcessId)
        {
            return false;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.WindowLongIndex.ExStyle).ToInt64();
        if ((extendedStyle & NativeMethods.WsExToolWindow) != 0)
        {
            return false;
        }

        if (IsCloaked(handle))
        {
            return false;
        }

        var className = new StringBuilder(256);
        _ = NativeMethods.GetClassName(handle, className, className.Capacity);
        if (className.ToString() is "Shell_TrayWnd" or "Progman" or "WorkerW")
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(handle, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        return true;
    }

    private bool IsCloaked(IntPtr handle)
    {
        return NativeMethods.DwmGetWindowAttribute(
            handle,
            DwmaCloaked,
            out int cloaked,
            Marshal.SizeOf<int>()) == 0 && cloaked != 0;
    }

    private static Rectangle MapBounds(Rectangle windowBounds, Rectangle sourceArea, Rectangle targetArea)
    {
        var safeSourceWidth = Math.Max(1, sourceArea.Width);
        var safeSourceHeight = Math.Max(1, sourceArea.Height);

        var leftRatio = (windowBounds.Left - sourceArea.Left) / (double)safeSourceWidth;
        var topRatio = (windowBounds.Top - sourceArea.Top) / (double)safeSourceHeight;
        var widthRatio = windowBounds.Width / (double)safeSourceWidth;
        var heightRatio = windowBounds.Height / (double)safeSourceHeight;

        var minWidth = Math.Min(120, targetArea.Width);
        var minHeight = Math.Min(80, targetArea.Height);
        var targetWidth = Math.Clamp((int)Math.Round(widthRatio * targetArea.Width), minWidth, targetArea.Width);
        var targetHeight = Math.Clamp((int)Math.Round(heightRatio * targetArea.Height), minHeight, targetArea.Height);

        var targetLeft = targetArea.Left + (int)Math.Round(leftRatio * targetArea.Width);
        var targetTop = targetArea.Top + (int)Math.Round(topRatio * targetArea.Height);

        targetLeft = Math.Clamp(targetLeft, targetArea.Left, targetArea.Right - targetWidth);
        targetTop = Math.Clamp(targetTop, targetArea.Top, targetArea.Bottom - targetHeight);

        return new Rectangle(targetLeft, targetTop, targetWidth, targetHeight);
    }
}
