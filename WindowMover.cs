using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class WindowMover
{
    private const int DwmaCloaked = 14;
    private const int DwmaExtendedFrameBounds = 9;
    private const int DefaultPlacementPoint = -1;
    private const int OffscreenCoordinateThreshold = -30000;
    private const int MinimumVisibleIntersectionSize = 20;
    private const int PollIntervalMilliseconds = 30;
    private const ushort VirtualKeyEscape = 0x1B;
    private const ushort VirtualKeyF = 0x46;
    private static readonly TimeSpan PendingFullscreenTransferTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FullScreenInputTimeout = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan MinimizedRestoreTimeout = TimeSpan.FromMilliseconds(700);
    private readonly int _currentProcessId = Environment.ProcessId;
    private readonly List<PendingFullscreenTransfer> _pendingFullscreenTransfers = new();

    public bool HasPendingFullscreenTransfers => _pendingFullscreenTransfers.Count > 0;

    public IReadOnlyList<MovableWindowInfo> GetMovableWindows(bool includeMinimizedWindows = true)
    {
        var windows = new List<MovableWindowInfo>();
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var topWindowScreens = new HashSet<string>(StringComparer.Ordinal);
        NativeMethods.EnumWindows((handle, lParam) =>
        {
            if (!TryGetWindowSnapshot(handle, out var snapshot))
            {
                return true;
            }

            if (!includeMinimizedWindows && IsMinimizedOrOffscreen(snapshot))
            {
                return true;
            }

            var title = GetWindowText(handle);
            var processName = GetProcessName(snapshot.ProcessId);
            var processPath = GetProcessPath(snapshot.ProcessId);
            var displayTitle = string.IsNullOrWhiteSpace(title) ? processName : title;
            if (string.IsNullOrWhiteSpace(displayTitle))
            {
                return true;
            }

            var appName = string.IsNullOrWhiteSpace(processName) ? displayTitle : processName;
            var isMinimizedOrOffscreen = IsMinimizedOrOffscreen(snapshot);
            var isTopOnMonitor = !isMinimizedOrOffscreen && topWindowScreens.Add(snapshot.Screen.DeviceName);
            windows.Add(new MovableWindowInfo(
                snapshot.Handle,
                appName,
                displayTitle,
                snapshot.ProcessId,
                processPath,
                snapshot.Screen.DeviceName,
                isMinimizedOrOffscreen,
                snapshot.Handle == foregroundWindow,
                isTopOnMonitor));
            return true;
        }, IntPtr.Zero);

        DiagnosticLog.Info($"action menu-window-list count={windows.Count} includeMinimized={includeMinimizedWindows}");
        return windows;
    }

    public bool MoveWindowToOtherMonitor(IntPtr handle)
    {
        DiagnosticLog.Info($"action menu-window hwnd={DiagnosticLog.FormatHandle(handle)}");
        var screens = GetTwoScreens();
        if (!TryGetWindowSnapshot(handle, out var snapshot))
        {
            DiagnosticLog.Info($"action menu-window failed=snapshot hwnd={DiagnosticLog.FormatHandle(handle)}");
            return false;
        }

        if (screens.All(screen => screen.DeviceName != snapshot.Screen.DeviceName))
        {
            DiagnosticLog.Info($"action menu-window failed=screen hwnd={DiagnosticLog.FormatHandle(handle)} screen={snapshot.Screen.DeviceName}");
            return false;
        }

        var targetScreen = screens[0].DeviceName == snapshot.Screen.DeviceName ? screens[1] : screens[0];
        return MoveSnapshotToScreen(snapshot, targetScreen);
    }

    public int MoveActiveWindowBetweenMonitors(IntPtr preferredHandle = default, IntPtr swapHandle = default)
    {
        DiagnosticLog.Info("action active");
        var screens = GetTwoScreens();
        var activeHandle = GetPreferredWindowHandle(preferredHandle, allowEnumerationFallback: false, requireNonMinimized: true);
        if (activeHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Не удалось определить открытое активное окно для обмена.");
        }

        if (TryGetWindowSnapshot(activeHandle, out var activeSnapshot, requireNonMinimized: true)
            && TryGetTargetScreen(activeSnapshot, screens, out var targetScreen)
            && TryGetTopWindowOnOtherMonitor(activeSnapshot, targetScreen, out var liveSwapSnapshot, out var liveVisibleBounds)
            && TrySwapSnapshots(activeSnapshot, liveSwapSnapshot, screens))
        {
            DiagnosticLog.Info(
                $"active swap candidate live-zorder hwnd={DiagnosticLog.FormatHandle(liveSwapSnapshot.Handle)} class={liveSwapSnapshot.ClassName} screen={liveSwapSnapshot.Screen.DeviceName} targetScreen={targetScreen.DeviceName} showCmd={liveSwapSnapshot.Placement.showCmd} current={FormatRectangle(liveSwapSnapshot.CurrentBounds)} visible={FormatRectangle(liveVisibleBounds)}");
            DiagnosticLog.Info($"action active completed moved=2 pending={_pendingFullscreenTransfers.Count}");
            return 2;
        }

        if (swapHandle != IntPtr.Zero
            && TryGetWindowSnapshot(activeHandle, out activeSnapshot, requireNonMinimized: true)
            && TryGetWindowSnapshot(swapHandle, out var cachedSwapSnapshot, requireNonMinimized: true)
            && TryGetTargetScreen(activeSnapshot, screens, out targetScreen)
            && IsActiveSwapCandidate(cachedSwapSnapshot, activeSnapshot.Handle, targetScreen, out var cachedVisibleBounds)
            && TrySwapSnapshots(activeSnapshot, cachedSwapSnapshot, screens))
        {
            DiagnosticLog.Info(
                $"active swap candidate fallback-cache hwnd={DiagnosticLog.FormatHandle(cachedSwapSnapshot.Handle)} class={cachedSwapSnapshot.ClassName} screen={cachedSwapSnapshot.Screen.DeviceName} targetScreen={targetScreen.DeviceName} showCmd={cachedSwapSnapshot.Placement.showCmd} current={FormatRectangle(cachedSwapSnapshot.CurrentBounds)} visible={FormatRectangle(cachedVisibleBounds)}");
            DiagnosticLog.Info($"action active completed moved=2 pending={_pendingFullscreenTransfers.Count}");
            return 2;
        }

        DiagnosticLog.Info(
            $"active swap candidate missing active={DiagnosticLog.FormatHandle(activeHandle)} fallback={DiagnosticLog.FormatHandle(swapHandle)} reason=no-visible-window-on-target");
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

    public int MoveAllWindowsBetweenMonitors(bool includeMinimizedWindows = true)
    {
        DiagnosticLog.Info($"action all includeMinimized={includeMinimizedWindows}");
        var screens = GetTwoScreens();
        var originalForegroundWindow = NativeMethods.GetForegroundWindow();
        var moved = 0;

        NativeMethods.EnumWindows((handle, lParam) =>
        {
            if (MoveSingleWindow(handle, screens, restoreFocus: false, includeMinimizedWindows))
            {
                moved++;
            }

            return true;
        }, IntPtr.Zero);

        RestoreForegroundWindow(originalForegroundWindow);
        DiagnosticLog.Info($"action all completed moved={moved} pending={_pendingFullscreenTransfers.Count}");
        return moved;
    }

    public int MinimizeAllWindows()
    {
        DiagnosticLog.Info("action minimize-all");
        var minimized = 0;

        NativeMethods.EnumWindows((handle, lParam) =>
        {
            if (!TryGetWindowSnapshot(handle, out var snapshot, requireNonMinimized: true))
            {
                return true;
            }

            if (NativeMethods.ShowWindow(snapshot.Handle, NativeMethods.ShowWindowCommand.Minimize))
            {
                minimized++;
                DiagnosticLog.Info(
                    $"action minimize-all window hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} title=\"{GetWindowText(snapshot.Handle)}\" pid={snapshot.ProcessId}");
            }
            else
            {
                DiagnosticLog.Win32Failure("ShowWindow Minimize", snapshot.Handle);
            }

            return true;
        }, IntPtr.Zero);

        DiagnosticLog.Info($"action minimize-all completed minimized={minimized}");
        return minimized;
    }

    public void ProcessPendingFullscreenTransfers()
    {
        if (_pendingFullscreenTransfers.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var screens = Screen.AllScreens;

        for (var index = _pendingFullscreenTransfers.Count - 1; index >= 0; index--)
        {
            var pending = _pendingFullscreenTransfers[index];
            if (now - pending.CreatedAt > PendingFullscreenTransferTtl)
            {
                DiagnosticLog.Info($"pending expired hwnd={DiagnosticLog.FormatHandle(pending.Handle)} target={pending.TargetScreenDeviceName}");
                _pendingFullscreenTransfers.RemoveAt(index);
                continue;
            }

            if (!NativeMethods.IsWindow(pending.Handle))
            {
                DiagnosticLog.Info($"pending removed missing-window hwnd={DiagnosticLog.FormatHandle(pending.Handle)}");
                _pendingFullscreenTransfers.RemoveAt(index);
                continue;
            }

            NativeMethods.GetWindowThreadProcessId(pending.Handle, out var processId);
            if (processId != pending.ProcessId)
            {
                DiagnosticLog.Info($"pending removed process-changed hwnd={DiagnosticLog.FormatHandle(pending.Handle)} oldPid={pending.ProcessId} newPid={processId}");
                _pendingFullscreenTransfers.RemoveAt(index);
                continue;
            }

            if (!TryGetWindowSnapshot(pending.Handle, out var snapshot))
            {
                continue;
            }

            if (snapshot.IsFullScreenLike)
            {
                continue;
            }

            var targetScreen = screens.FirstOrDefault(screen => screen.DeviceName == pending.TargetScreenDeviceName);
            if (targetScreen is null)
            {
                DiagnosticLog.Info($"pending removed missing-target hwnd={DiagnosticLog.FormatHandle(pending.Handle)} target={pending.TargetScreenDeviceName}");
                _pendingFullscreenTransfers.RemoveAt(index);
                continue;
            }

            if (ApplyMovement(snapshot, pending.TargetRestoreBounds, targetScreen, resetPlacementPoints: true, isPendingCorrection: true))
            {
                var elapsedMs = (long)(DateTimeOffset.UtcNow - pending.CreatedAt).TotalMilliseconds;
                DiagnosticLog.Info(
                    $"pending corrected hwnd={DiagnosticLog.FormatHandle(pending.Handle)} pid={pending.ProcessId} from={snapshot.Screen.DeviceName} target={targetScreen.DeviceName} showCmd={snapshot.Placement.showCmd} current={FormatRectangle(snapshot.CurrentBounds)} normal={FormatRectangle(snapshot.NormalBounds)} targetBounds={FormatRectangle(pending.TargetRestoreBounds)} elapsedMs={elapsedMs}");
                _pendingFullscreenTransfers.RemoveAt(index);
            }
        }
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
        if (ShouldMoveWindow(preferredHandle) && (!requireNonMinimized || !IsMinimizedOrOffscreen(preferredHandle)))
        {
            return preferredHandle;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (ShouldMoveWindow(foregroundWindow) && (!requireNonMinimized || !IsMinimizedOrOffscreen(foregroundWindow)))
        {
            return foregroundWindow;
        }

        if (!allowEnumerationFallback)
        {
            return IntPtr.Zero;
        }

        var fallback = IntPtr.Zero;
        NativeMethods.EnumWindows((handle, lParam) =>
        {
            if (!ShouldMoveWindow(handle) || (requireNonMinimized && IsMinimizedOrOffscreen(handle)))
            {
                return true;
            }

            fallback = handle;
            return false;
        }, IntPtr.Zero);

        return fallback;
    }

    private bool MoveSingleWindow(IntPtr handle, Screen[] screens, bool restoreFocus, bool includeMinimizedWindows = true)
    {
        if (!TryGetWindowSnapshot(handle, out var snapshot))
        {
            return false;
        }

        if (!includeMinimizedWindows && IsMinimizedOrOffscreen(snapshot))
        {
            DiagnosticLog.Info(
                $"skip minimized hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} class={snapshot.ClassName} current={FormatRectangle(snapshot.CurrentBounds)} normal={FormatRectangle(snapshot.NormalBounds)}");
            return false;
        }

        if (screens.All(screen => screen.DeviceName != snapshot.Screen.DeviceName))
        {
            return false;
        }

        var targetScreen = screens[0].DeviceName == snapshot.Screen.DeviceName ? screens[1] : screens[0];
        if (!MoveSnapshotToScreen(snapshot, targetScreen))
        {
            return false;
        }

        if (restoreFocus)
        {
            NativeMethods.SetForegroundWindow(snapshot.Handle);
        }

        return true;
    }

    private bool TryGetTopWindowOnOtherMonitor(
        WindowSnapshot activeSnapshot,
        Screen targetScreen,
        out WindowSnapshot swapSnapshot,
        out Rectangle visibleBounds)
    {
        swapSnapshot = default;
        visibleBounds = Rectangle.Empty;
        var foundSnapshot = default(WindowSnapshot);
        var foundVisibleBounds = Rectangle.Empty;

        NativeMethods.EnumWindows((handle, lParam) =>
        {
            if (!TryGetWindowSnapshot(handle, out var candidate, requireNonMinimized: true))
            {
                return true;
            }

            if (!IsActiveSwapCandidate(candidate, activeSnapshot.Handle, targetScreen, out var candidateVisibleBounds))
            {
                return true;
            }

            foundSnapshot = candidate;
            foundVisibleBounds = candidateVisibleBounds;
            return false;
        }, IntPtr.Zero);

        swapSnapshot = foundSnapshot;
        visibleBounds = foundVisibleBounds;
        return swapSnapshot.Handle != IntPtr.Zero;
    }

    private static bool TryGetTargetScreen(WindowSnapshot activeSnapshot, Screen[] screens, out Screen targetScreen)
    {
        targetScreen = null!;
        if (screens.All(screen => screen.DeviceName != activeSnapshot.Screen.DeviceName))
        {
            return false;
        }

        targetScreen = screens.First(screen => screen.DeviceName != activeSnapshot.Screen.DeviceName);
        return true;
    }

    private bool IsActiveSwapCandidate(
        WindowSnapshot candidate,
        IntPtr activeHandle,
        Screen targetScreen,
        out Rectangle visibleBounds)
    {
        visibleBounds = Rectangle.Empty;
        if (candidate.Handle == activeHandle || IsMinimizedOrOffscreen(candidate) || IsMinimized(candidate.Handle))
        {
            return false;
        }

        if (!TryGetVisibleWindowBounds(candidate.Handle, out visibleBounds))
        {
            return false;
        }

        if (IsOffscreenLike(visibleBounds))
        {
            return false;
        }

        return HasMeaningfulIntersection(visibleBounds, targetScreen.Bounds);
    }

    private bool TrySwapSnapshots(WindowSnapshot activeSnapshot, WindowSnapshot swapSnapshot, Screen[] screens)
    {
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

        if (!MoveSnapshotToScreen(swapSnapshot, activeSnapshot.Screen))
        {
            return false;
        }

        if (!MoveSnapshotToScreen(activeSnapshot, swapSnapshot.Screen))
        {
            return false;
        }

        NativeMethods.SetForegroundWindow(activeSnapshot.Handle);
        return true;
    }

    private bool MoveSnapshotToScreen(WindowSnapshot snapshot, Screen targetScreen)
    {
        var targetBounds = MapBounds(snapshot.NormalBounds, snapshot.RestoreScreen.WorkingArea, targetScreen.WorkingArea);
        DiagnosticLog.Info(
            $"move hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} pid={snapshot.ProcessId} class={snapshot.ClassName} current={FormatRectangle(snapshot.CurrentBounds)} normal={FormatRectangle(snapshot.NormalBounds)} source={snapshot.Screen.DeviceName} restore={snapshot.RestoreScreen.DeviceName} target={targetScreen.DeviceName} fullscreen={snapshot.IsFullScreenLike} offscreen={snapshot.IsOffscreenLike}");

        if (TryControlledBrowserFullScreenTransfer(snapshot, targetScreen, targetBounds))
        {
            return true;
        }

        if (!ApplyMovement(snapshot, targetBounds, targetScreen))
        {
            return false;
        }

        if (snapshot.IsFullScreenLike)
        {
            DiagnosticLog.Info(
                $"fullscreen transfer fallback=pending hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} class={snapshot.ClassName}");
            RememberPendingFullscreenTransfer(snapshot, targetScreen, targetBounds);
        }

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

        var className = GetClassName(handle);
        if (className is "Shell_TrayWnd" or "Progman" or "WorkerW"
            || IsTransientPopupClass(className))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(handle, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        return true;
    }

    private bool TryGetWindowSnapshot(
        IntPtr handle,
        out WindowSnapshot snapshot,
        bool requireNonMinimized = false)
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

        if (!NativeMethods.GetWindowRect(handle, out var currentRect))
        {
            return false;
        }

        var normalBounds = placement.rcNormalPosition.ToRectangle();
        if (normalBounds.Width <= 0 || normalBounds.Height <= 0)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        var currentBounds = currentRect.ToRectangle();
        var isOffscreenLike = IsOffscreenLike(currentBounds);
        if (requireNonMinimized
            && (placement.showCmd == NativeMethods.ShowWindowCommand.Minimize
                || isOffscreenLike
                || NativeMethods.IsIconic(handle)))
        {
            return false;
        }

        var currentScreen = Screen.FromRectangle(currentBounds);
        var restoreScreen = Screen.FromRectangle(normalBounds);
        var isFullScreenLike = !isOffscreenLike && CoversScreen(currentBounds, currentScreen.Bounds);
        var screen = isFullScreenLike || !isOffscreenLike ? currentScreen : restoreScreen;
        var className = GetClassName(handle);
        var title = GetWindowText(handle);
        if (IsTransientPopupWindow(handle, className, title, currentBounds, normalBounds, isFullScreenLike))
        {
            DiagnosticLog.Info(
                $"skip transient-popup hwnd={DiagnosticLog.FormatHandle(handle)} class={className} title=\"{title}\" rect={FormatRectangle(currentBounds)}");
            return false;
        }

        snapshot = new WindowSnapshot(handle, placement, normalBounds, currentBounds, screen, restoreScreen, isFullScreenLike, isOffscreenLike, processId, className);
        return true;
    }

    private static bool IsMinimized(IntPtr handle)
    {
        return NativeMethods.IsIconic(handle);
    }

    private static bool IsMinimizedOrOffscreen(IntPtr handle)
    {
        if (NativeMethods.IsIconic(handle))
        {
            return true;
        }

        if (NativeMethods.GetWindowPlacement(handle, out var placement)
            && placement.showCmd == NativeMethods.ShowWindowCommand.Minimize)
        {
            return true;
        }

        return NativeMethods.GetWindowRect(handle, out var rect)
            && IsOffscreenLike(rect.ToRectangle());
    }

    private bool ApplyMovement(
        WindowSnapshot snapshot,
        Rectangle targetBounds,
        Screen targetScreen,
        bool resetPlacementPoints = false,
        bool isPendingCorrection = false,
        bool isControlledFullScreenTransfer = false)
    {
        var placement = snapshot.Placement;
        placement.length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();
        placement.rcNormalPosition = NativeMethods.RECT.FromRectangle(targetBounds);
        if (resetPlacementPoints)
        {
            ResetPlacementPoints(ref placement);
        }

        var mode = GetMovementMode(snapshot, isPendingCorrection);
        var applied = mode switch
        {
            MovementMode.Offscreen => ApplyMinimizedOrOffscreenMovement(snapshot, placement, targetBounds, targetScreen),
            MovementMode.Minimized => ApplyMinimizedOrOffscreenMovement(snapshot, placement, targetBounds, targetScreen),
            MovementMode.Maximized => ApplyMaximizedMovement(snapshot.Handle, placement, targetBounds),
            MovementMode.FullScreenLike => ApplyFullScreenMovement(snapshot.Handle, placement, targetScreen.Bounds),
            _ => ApplyNormalMovement(snapshot.Handle, placement, targetBounds)
        };

        DiagnosticLog.Info(
            $"move applied hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} mode={mode.ToString().ToLowerInvariant()} ok={applied} targetBounds={FormatRectangle(targetBounds)} controlledFullscreen={isControlledFullScreenTransfer}");

        return applied;
    }

    private bool TryControlledBrowserFullScreenTransfer(WindowSnapshot snapshot, Screen targetScreen, Rectangle targetBounds)
    {
        if (!snapshot.IsFullScreenLike || !IsChromiumBrowserWindow(snapshot.ClassName))
        {
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        DiagnosticLog.Info(
            $"fullscreen input begin hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} class={snapshot.ClassName} target={targetScreen.DeviceName}");

        NativeMethods.SetForegroundWindow(snapshot.Handle);
        if (!WaitForForegroundWindow(snapshot.Handle, TimeSpan.FromMilliseconds(350)))
        {
            DiagnosticLog.Info(
                $"fullscreen input foreground-timeout hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }

        if (!SendVirtualKey(VirtualKeyEscape))
        {
            DiagnosticLog.Info(
                $"fullscreen input failed=esc-send hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return false;
        }

        if (!WaitForSnapshot(
            snapshot.Handle,
            current => !current.IsFullScreenLike && !current.IsOffscreenLike,
            FullScreenInputTimeout,
            out var restoredSnapshot))
        {
            DiagnosticLog.Info(
                $"fullscreen input failed=esc-timeout hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return false;
        }

        DiagnosticLog.Info(
            $"fullscreen input exited hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} showCmd={restoredSnapshot.Placement.showCmd} current={FormatRectangle(restoredSnapshot.CurrentBounds)} normal={FormatRectangle(restoredSnapshot.NormalBounds)} elapsedMs={stopwatch.ElapsedMilliseconds}");

        if (!ApplyMovement(restoredSnapshot, targetBounds, targetScreen, isControlledFullScreenTransfer: true))
        {
            DiagnosticLog.Info(
                $"fullscreen input failed=move hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return false;
        }

        NativeMethods.SetForegroundWindow(snapshot.Handle);
        if (!WaitForForegroundWindow(snapshot.Handle, TimeSpan.FromMilliseconds(350)))
        {
            DiagnosticLog.Info(
                $"fullscreen input foreground-timeout-after-move hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }

        if (!SendVirtualKey(VirtualKeyF))
        {
            DiagnosticLog.Info(
                $"fullscreen input failed=f-send hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return true;
        }

        if (WaitForSnapshot(
            snapshot.Handle,
            current => current.IsFullScreenLike && current.Screen.DeviceName == targetScreen.DeviceName,
            FullScreenInputTimeout,
            out var fullscreenSnapshot))
        {
            DiagnosticLog.Info(
                $"fullscreen input reentered hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} current={FormatRectangle(fullscreenSnapshot.CurrentBounds)} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        else
        {
            DiagnosticLog.Info(
                $"fullscreen input failed=f-timeout hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }

        return true;
    }

    private bool ApplyMinimizedOrOffscreenMovement(
        WindowSnapshot snapshot,
        NativeMethods.WINDOWPLACEMENT placement,
        Rectangle targetBounds,
        Screen targetScreen)
    {
        var stopwatch = Stopwatch.StartNew();
        DiagnosticLog.Info(
            $"minimized invisible begin hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} current={FormatRectangle(snapshot.CurrentBounds)} normal={FormatRectangle(snapshot.NormalBounds)} targetBounds={FormatRectangle(targetBounds)}");

        var invisible = TryMakeWindowInvisible(snapshot.Handle);
        if (!invisible.Enabled)
        {
            DiagnosticLog.Info(
                $"minimized invisible failed=enable hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)}");
        }

        NativeMethods.ShowWindow(snapshot.Handle, NativeMethods.ShowWindowCommand.Restore);

        if (!WaitForSnapshot(
            snapshot.Handle,
            current => !current.IsOffscreenLike && !current.IsFullScreenLike,
            MinimizedRestoreTimeout,
            out var restoredSnapshot))
        {
            NativeMethods.ShowWindow(snapshot.Handle, NativeMethods.ShowWindowCommand.Minimize);
            RestoreWindowVisibility(snapshot.Handle, invisible);
            DiagnosticLog.Info(
                $"minimized invisible failed=restore-timeout hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return false;
        }

        DiagnosticLog.Info(
            $"minimized invisible restored hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} showCmd={restoredSnapshot.Placement.showCmd} current={FormatRectangle(restoredSnapshot.CurrentBounds)} elapsedMs={stopwatch.ElapsedMilliseconds}");

        var restoredPlacement = restoredSnapshot.Placement;
        restoredPlacement.length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();
        restoredPlacement.rcNormalPosition = NativeMethods.RECT.FromRectangle(targetBounds);

        var moved = restoredSnapshot.Placement.showCmd == NativeMethods.ShowWindowCommand.Maximize
            ? ApplyMaximizedMovement(restoredSnapshot.Handle, restoredPlacement, targetBounds)
            : ApplyNormalMovement(restoredSnapshot.Handle, restoredPlacement, targetBounds);

        NativeMethods.ShowWindow(snapshot.Handle, NativeMethods.ShowWindowCommand.Minimize);
        RestoreWindowVisibility(snapshot.Handle, invisible);
        DiagnosticLog.Info(
            $"minimized invisible minimized hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} ok={moved} elapsedMs={stopwatch.ElapsedMilliseconds}");

        return moved;
    }

    private bool ApplyNormalMovement(
        IntPtr handle,
        NativeMethods.WINDOWPLACEMENT placement,
        Rectangle targetBounds)
    {
        if (!SetWindowPosition(handle, targetBounds))
        {
            return false;
        }

        return SetRestorePlacement(handle, placement);
    }

    private bool ApplyMaximizedMovement(
        IntPtr handle,
        NativeMethods.WINDOWPLACEMENT placement,
        Rectangle targetBounds)
    {
        NativeMethods.ShowWindow(handle, NativeMethods.ShowWindowCommand.Restore);

        if (!SetWindowPosition(handle, targetBounds))
        {
            return false;
        }

        placement.showCmd = NativeMethods.ShowWindowCommand.Normal;
        if (!SetRestorePlacement(handle, placement))
        {
            return false;
        }

        NativeMethods.ShowWindow(handle, NativeMethods.ShowWindowCommand.Maximize);
        return true;
    }

    private bool ApplyFullScreenMovement(
        IntPtr handle,
        NativeMethods.WINDOWPLACEMENT placement,
        Rectangle targetScreenBounds)
    {
        if (!SetRestorePlacement(handle, placement))
        {
            return false;
        }

        return SetWindowPosition(handle, targetScreenBounds);
    }

    private bool SetWindowPosition(IntPtr handle, Rectangle bounds)
    {
        if (NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SetWindowPosFlags.NoZOrder
            | NativeMethods.SetWindowPosFlags.NoActivate
            | NativeMethods.SetWindowPosFlags.ShowWindow))
        {
            return true;
        }

        DiagnosticLog.Win32Failure("SetWindowPos", handle);
        return false;
    }

    private bool SetRestorePlacement(IntPtr handle, NativeMethods.WINDOWPLACEMENT placement)
    {
        placement.length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>();
        if (NativeMethods.SetWindowPlacement(handle, ref placement))
        {
            return true;
        }

        DiagnosticLog.Win32Failure("SetWindowPlacement", handle);
        return false;
    }

    private bool WaitForSnapshot(
        IntPtr handle,
        Func<WindowSnapshot, bool> predicate,
        TimeSpan timeout,
        out WindowSnapshot snapshot)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            if (TryGetWindowSnapshot(handle, out snapshot) && predicate(snapshot))
            {
                return true;
            }

            Thread.Sleep(PollIntervalMilliseconds);
        }
        while (DateTimeOffset.UtcNow < deadline);

        snapshot = default;
        return false;
    }

    private static bool WaitForForegroundWindow(IntPtr handle, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            if (NativeMethods.GetForegroundWindow() == handle)
            {
                return true;
            }

            Thread.Sleep(PollIntervalMilliseconds);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return NativeMethods.GetForegroundWindow() == handle;
    }

    private static bool SendVirtualKey(ushort virtualKey)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(virtualKey, 0),
            CreateKeyboardInput(virtualKey, NativeMethods.KeyboardEventFlags.KeyUp)
        };

        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent == inputs.Length)
        {
            return true;
        }

        DiagnosticLog.Info(
            $"SendInput failed vk=0x{virtualKey:X2} size={Marshal.SizeOf<NativeMethods.INPUT>()} sent={sent} error={Marshal.GetLastWin32Error()}");
        return false;
    }

    private static NativeMethods.INPUT CreateKeyboardInput(
        ushort virtualKey,
        NativeMethods.KeyboardEventFlags flags)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.InputType.Keyboard,
            union = new NativeMethods.INPUTUNION
            {
                keyboardInput = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags
                }
            }
        };
    }

    private static void RestoreForegroundWindow(IntPtr handle)
    {
        if (handle != IntPtr.Zero && NativeMethods.IsWindow(handle))
        {
            NativeMethods.SetForegroundWindow(handle);
        }
    }

    private static InvisibleWindowState TryMakeWindowInvisible(IntPtr handle)
    {
        var originalStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.WindowLongIndex.ExStyle);
        var originalStyleValue = originalStyle.ToInt64();
        var wasLayered = (originalStyleValue & NativeMethods.WsExLayered) != 0;
        var hadLayeredAttributes = false;
        uint originalColorKey = 0;
        byte originalAlpha = 255;
        uint originalLayeredFlags = NativeMethods.LwaAlpha;

        if (wasLayered)
        {
            hadLayeredAttributes = NativeMethods.GetLayeredWindowAttributes(
                handle,
                out originalColorKey,
                out originalAlpha,
                out originalLayeredFlags);
        }

        if (!wasLayered)
        {
            var newStyle = new IntPtr(originalStyleValue | NativeMethods.WsExLayered);
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.WindowLongIndex.ExStyle, newStyle);
            var styleAfterSet = NativeMethods.GetWindowLongPtr(handle, NativeMethods.WindowLongIndex.ExStyle).ToInt64();
            if ((styleAfterSet & NativeMethods.WsExLayered) == 0)
            {
                DiagnosticLog.Win32Failure("SetWindowLongPtr WS_EX_LAYERED", handle);
                return new InvisibleWindowState(originalStyle, wasLayered, hadLayeredAttributes, originalColorKey, originalAlpha, originalLayeredFlags, false);
            }
        }

        if (!NativeMethods.SetLayeredWindowAttributes(handle, 0, 0, NativeMethods.LwaAlpha))
        {
            DiagnosticLog.Win32Failure("SetLayeredWindowAttributes alpha=0", handle);
            RestoreWindowVisibility(handle, new InvisibleWindowState(originalStyle, wasLayered, hadLayeredAttributes, originalColorKey, originalAlpha, originalLayeredFlags, true));
            return new InvisibleWindowState(originalStyle, wasLayered, hadLayeredAttributes, originalColorKey, originalAlpha, originalLayeredFlags, false);
        }

        return new InvisibleWindowState(originalStyle, wasLayered, hadLayeredAttributes, originalColorKey, originalAlpha, originalLayeredFlags, true);
    }

    private static void RestoreWindowVisibility(IntPtr handle, InvisibleWindowState state)
    {
        if (!state.Enabled)
        {
            return;
        }

        if (state.WasLayered)
        {
            if (state.HadLayeredAttributes)
            {
                NativeMethods.SetLayeredWindowAttributes(
                    handle,
                    state.OriginalColorKey,
                    state.OriginalAlpha,
                    state.OriginalLayeredFlags);
            }
            else
            {
                NativeMethods.SetLayeredWindowAttributes(handle, 0, 255, NativeMethods.LwaAlpha);
            }
        }
        else
        {
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.WindowLongIndex.ExStyle, state.OriginalExtendedStyle);
        }

        DiagnosticLog.Info($"minimized invisible restored-style hwnd={DiagnosticLog.FormatHandle(handle)}");
    }

    private static MovementMode GetMovementMode(WindowSnapshot snapshot, bool isPendingCorrection)
    {
        if (snapshot.IsFullScreenLike && !isPendingCorrection)
        {
            return MovementMode.FullScreenLike;
        }

        if (snapshot.IsOffscreenLike)
        {
            return MovementMode.Offscreen;
        }

        return snapshot.Placement.showCmd switch
        {
            NativeMethods.ShowWindowCommand.Minimize => MovementMode.Minimized,
            NativeMethods.ShowWindowCommand.Maximize => MovementMode.Maximized,
            _ => MovementMode.Normal
        };
    }

    private static bool IsMinimizedOrOffscreen(WindowSnapshot snapshot)
    {
        return snapshot.IsOffscreenLike
            || snapshot.Placement.showCmd == NativeMethods.ShowWindowCommand.Minimize;
    }

    private void RememberPendingFullscreenTransfer(WindowSnapshot snapshot, Screen targetScreen, Rectangle targetBounds)
    {
        _pendingFullscreenTransfers.RemoveAll(pending => pending.Handle == snapshot.Handle);
        _pendingFullscreenTransfers.Add(new PendingFullscreenTransfer(
            snapshot.Handle,
            snapshot.ProcessId,
            targetScreen.DeviceName,
            targetBounds,
            DateTimeOffset.UtcNow));

        DiagnosticLog.Info(
            $"pending created hwnd={DiagnosticLog.FormatHandle(snapshot.Handle)} pid={snapshot.ProcessId} target={targetScreen.DeviceName} bounds={FormatRectangle(targetBounds)}");
    }

    private static void ResetPlacementPoints(ref NativeMethods.WINDOWPLACEMENT placement)
    {
        placement.ptMinPosition = new NativeMethods.POINT
        {
            X = DefaultPlacementPoint,
            Y = DefaultPlacementPoint
        };
        placement.ptMaxPosition = new NativeMethods.POINT
        {
            X = DefaultPlacementPoint,
            Y = DefaultPlacementPoint
        };
    }

    private bool IsCloaked(IntPtr handle)
    {
        return NativeMethods.DwmGetWindowAttribute(
            handle,
            DwmaCloaked,
            out int cloaked,
            Marshal.SizeOf<int>()) == 0 && cloaked != 0;
    }

    private static bool TryGetVisibleWindowBounds(IntPtr handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (NativeMethods.DwmGetWindowAttribute(
                handle,
                DwmaExtendedFrameBounds,
                out NativeMethods.RECT frameBounds,
                Marshal.SizeOf<NativeMethods.RECT>()) == 0
            && frameBounds.Width > 0
            && frameBounds.Height > 0)
        {
            bounds = frameBounds.ToRectangle();
            return true;
        }

        if (!NativeMethods.GetWindowRect(handle, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        bounds = rect.ToRectangle();
        return true;
    }

    private static bool HasMeaningfulIntersection(Rectangle bounds, Rectangle screenBounds)
    {
        var intersection = Rectangle.Intersect(bounds, screenBounds);
        return intersection.Width >= MinimumVisibleIntersectionSize
            && intersection.Height >= MinimumVisibleIntersectionSize;
    }

    private static string GetClassName(IntPtr handle)
    {
        var className = new StringBuilder(256);
        _ = NativeMethods.GetClassName(handle, className, className.Capacity);
        return className.ToString();
    }

    private static string GetWindowText(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder(length + 1);
        _ = NativeMethods.GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetProcessPath(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatRectangle(Rectangle rectangle)
    {
        return $"{rectangle.Left},{rectangle.Top},{rectangle.Width}x{rectangle.Height}";
    }

    private static bool IsChromiumBrowserWindow(string className)
    {
        return className.Contains("Chrome_", StringComparison.Ordinal)
            && className.Contains("WidgetWin", StringComparison.Ordinal);
    }

    private static bool IsTransientPopupClass(string className)
    {
        return className.Equals("tooltips_class32", StringComparison.OrdinalIgnoreCase)
            || className.Equals("SysShadow", StringComparison.OrdinalIgnoreCase)
            || className.Equals("DropDown", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Tooltip", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientPopupWindow(
        IntPtr handle,
        string className,
        string title,
        Rectangle currentBounds,
        Rectangle normalBounds,
        bool isFullScreenLike)
    {
        if (isFullScreenLike)
        {
            return false;
        }

        if (IsTransientPopupClass(className))
        {
            return true;
        }

        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.WindowLongIndex.Style).ToInt64();
        var isPopup = (style & NativeMethods.WsPopup) != 0;
        var hasCaption = (style & NativeMethods.WsCaption) != 0;
        var smallCurrent = currentBounds.Width <= 460 || currentBounds.Height <= 180;
        var smallNormal = normalBounds.Width <= 460 || normalBounds.Height <= 180;
        if (isPopup && !hasCaption && smallCurrent && smallNormal)
        {
            return true;
        }

        if (isPopup
            && smallCurrent
            && title.Contains(':', StringComparison.CurrentCulture)
            && !title.Contains(" - ", StringComparison.CurrentCulture)
            && !title.Contains(" — ", StringComparison.CurrentCulture))
        {
            return true;
        }

        return false;
    }

    private static bool IsOffscreenLike(Rectangle rectangle)
    {
        return rectangle.Left <= OffscreenCoordinateThreshold
            || rectangle.Top <= OffscreenCoordinateThreshold;
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

    private static bool CoversScreen(Rectangle windowBounds, Rectangle screenBounds)
    {
        const int Tolerance = 2;

        return windowBounds.Left <= screenBounds.Left + Tolerance
            && windowBounds.Top <= screenBounds.Top + Tolerance
            && windowBounds.Right >= screenBounds.Right - Tolerance
            && windowBounds.Bottom >= screenBounds.Bottom - Tolerance;
    }

    private readonly record struct WindowSnapshot(
        IntPtr Handle,
        NativeMethods.WINDOWPLACEMENT Placement,
        Rectangle NormalBounds,
        Rectangle CurrentBounds,
        Screen Screen,
        Screen RestoreScreen,
        bool IsFullScreenLike,
        bool IsOffscreenLike,
        int ProcessId,
        string ClassName);

    private readonly record struct PendingFullscreenTransfer(
        IntPtr Handle,
        int ProcessId,
        string TargetScreenDeviceName,
        Rectangle TargetRestoreBounds,
        DateTimeOffset CreatedAt);

    private readonly record struct InvisibleWindowState(
        IntPtr OriginalExtendedStyle,
        bool WasLayered,
        bool HadLayeredAttributes,
        uint OriginalColorKey,
        byte OriginalAlpha,
        uint OriginalLayeredFlags,
        bool Enabled);

    private enum MovementMode
    {
        Normal,
        Maximized,
        Minimized,
        Offscreen,
        FullScreenLike
    }
}

internal readonly record struct TrackedWindow(IntPtr Handle, string ScreenDeviceName);

internal readonly record struct MovableWindowInfo(
    IntPtr Handle,
    string AppName,
    string Title,
    int ProcessId,
    string ProcessPath,
    string ScreenDeviceName,
    bool IsMinimizedOrOffscreen,
    bool IsForeground,
    bool IsTopOnMonitor);
