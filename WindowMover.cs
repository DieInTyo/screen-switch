using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class WindowMover
{
    private const int DwmaCloaked = 14;
    private readonly int _currentProcessId = Environment.ProcessId;

    public int MoveActiveWindowBetweenMonitors(IntPtr preferredHandle = default, IntPtr swapHandle = default)
    {
        var screens = GetTwoScreens();
        var activeHandle = GetPreferredWindowHandle(preferredHandle, allowEnumerationFallback: false, requireNonMinimized: true);
        if (activeHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Не удалось определить открытое активное окно для обмена.");
        }

        if (TrySwapWindows(activeHandle, swapHandle, screens))
        {
            return 2;
        }

        throw new InvalidOperationException("На другом мониторе нет подходящего открытого окна для обмена.");
    }

    public bool TryGetMovableForegroundWindow(out TrackedWindow trackedWindow)
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (TryGetWindowSnapshot(handle, out var snapshot))
        {
            trackedWindow = new TrackedWindow(snapshot.Handle, snapshot.Screen.DeviceName);
            return true;
        }

        trackedWindow = default;
        return false;
    }

    public int MoveAllWindowsBetweenMonitors()
    {
        var screens = GetTwoScreens();
        var moved = 0;

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (MoveSingleWindow(handle, screens, restoreFocus: false))
            {
                moved++;
            }

            return true;
        }, IntPtr.Zero);

        return moved;
    }

    private Screen[] GetTwoScreens()
    {
        var screens = Screen.AllScreens
            .OrderBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();

        if (screens.Length != 2)
        {
            throw new InvalidOperationException("Приложение работает только когда подключено ровно два монитора.");
        }

        return screens;
    }

    private IntPtr GetPreferredWindowHandle(IntPtr preferredHandle, bool allowEnumerationFallback, bool requireNonMinimized)
    {
        if (ShouldMoveWindow(preferredHandle) && (!requireNonMinimized || !IsMinimized(preferredHandle)))
        {
            return preferredHandle;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (ShouldMoveWindow(foregroundWindow) && (!requireNonMinimized || !IsMinimized(foregroundWindow)))
        {
            return foregroundWindow;
        }

        if (!allowEnumerationFallback)
        {
            return IntPtr.Zero;
        }

        var fallback = IntPtr.Zero;
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!ShouldMoveWindow(handle) || (requireNonMinimized && IsMinimized(handle)))
            {
                return true;
            }

            fallback = handle;
            return false;
        }, IntPtr.Zero);

        return fallback;
    }

    private bool MoveSingleWindow(IntPtr handle, Screen[] screens, bool restoreFocus)
    {
        if (!TryGetWindowSnapshot(handle, out var snapshot))
        {
            return false;
        }

        if (screens.All(screen => screen.DeviceName != snapshot.Screen.DeviceName))
        {
            return false;
        }

        var targetScreen = screens[0].DeviceName == snapshot.Screen.DeviceName ? screens[1] : screens[0];
        var targetBounds = MapBounds(snapshot.NormalBounds, snapshot.Screen.WorkingArea, targetScreen.WorkingArea);

        if (!ApplyPlacement(snapshot.Handle, snapshot.Placement, targetBounds))
        {
            return false;
        }

        if (restoreFocus)
        {
            NativeMethods.SetForegroundWindow(snapshot.Handle);
        }

        return true;
    }

    private bool TrySwapWindows(IntPtr activeHandle, IntPtr swapHandle, Screen[] screens)
    {
        if (!TryGetWindowSnapshot(activeHandle, out var activeSnapshot, requireNonMinimized: true))
        {
            return false;
        }

        if (!TryGetWindowSnapshot(swapHandle, out var swapSnapshot, requireNonMinimized: true))
        {
            return false;
        }

        if (activeSnapshot.Handle == swapSnapshot.Handle)
        {
            return false;
        }

        if (activeSnapshot.Screen.DeviceName == swapSnapshot.Screen.DeviceName)
        {
            return false;
        }

        if (screens.All(screen => screen.DeviceName != activeSnapshot.Screen.DeviceName)
            || screens.All(screen => screen.DeviceName != swapSnapshot.Screen.DeviceName))
        {
            return false;
        }

        var activeTargetBounds = MapBounds(
            activeSnapshot.NormalBounds,
            activeSnapshot.Screen.WorkingArea,
            swapSnapshot.Screen.WorkingArea);
        var swapTargetBounds = MapBounds(
            swapSnapshot.NormalBounds,
            swapSnapshot.Screen.WorkingArea,
            activeSnapshot.Screen.WorkingArea);

        if (!ApplyPlacement(swapSnapshot.Handle, swapSnapshot.Placement, swapTargetBounds))
        {
            return false;
        }

        if (!ApplyPlacement(activeSnapshot.Handle, activeSnapshot.Placement, activeTargetBounds))
        {
            return false;
        }

        NativeMethods.SetForegroundWindow(activeSnapshot.Handle);
        return true;
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

    private bool TryGetWindowSnapshot(IntPtr handle, out WindowSnapshot snapshot, bool requireNonMinimized = false)
    {
        snapshot = default;

        if (!ShouldMoveWindow(handle))
        {
            return false;
        }

        if (!NativeMethods.GetWindowPlacement(handle, out var placement))
        {
            return false;
        }

        if (requireNonMinimized && placement.showCmd == NativeMethods.ShowWindowCommand.Minimize)
        {
            return false;
        }

        var normalBounds = placement.rcNormalPosition.ToRectangle();
        if (normalBounds.Width <= 0 || normalBounds.Height <= 0)
        {
            return false;
        }

        var screen = Screen.FromRectangle(normalBounds);
        snapshot = new WindowSnapshot(handle, placement, normalBounds, screen);
        return true;
    }

    private static bool IsMinimized(IntPtr handle)
    {
        return NativeMethods.IsIconic(handle);
    }

    private bool ApplyPlacement(
        IntPtr handle,
        NativeMethods.WINDOWPLACEMENT placement,
        Rectangle targetBounds)
    {
        placement.length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();
        placement.rcNormalPosition = NativeMethods.RECT.FromRectangle(targetBounds);

        if (!NativeMethods.SetWindowPlacement(handle, ref placement))
        {
            return false;
        }

        if (placement.showCmd == NativeMethods.ShowWindowCommand.Maximize)
        {
            NativeMethods.ShowWindow(handle, NativeMethods.ShowWindowCommand.Restore);
            NativeMethods.SetWindowPlacement(handle, ref placement);
            NativeMethods.ShowWindow(handle, NativeMethods.ShowWindowCommand.Maximize);
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

    private readonly record struct WindowSnapshot(
        IntPtr Handle,
        NativeMethods.WINDOWPLACEMENT Placement,
        Rectangle NormalBounds,
        Screen Screen);
}

internal readonly record struct TrackedWindow(IntPtr Handle, string ScreenDeviceName);
