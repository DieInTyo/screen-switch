using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private readonly Dictionary<int, HotkeyAction> _registeredActions = new();
    private bool _disposed;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams());
    }

    public event Action<HotkeyAction>? HotkeyPressed;

    public IReadOnlyList<HotkeyRegistrationFailure> Configure(AppSettings settings)
    {
        UnregisterAll();

        if (!settings.HotkeysEnabled)
        {
            DiagnosticLog.Info("hotkeys disabled");
            return Array.Empty<HotkeyRegistrationFailure>();
        }

        var failures = new List<HotkeyRegistrationFailure>();
        var usedGestures = new HashSet<string>(StringComparer.Ordinal);
        RegisterIfValid(HotkeyAction.SelectedMode, settings.SelectedModeHotkey, usedGestures, failures);
        RegisterIfValid(HotkeyAction.ActiveWindow, settings.ActiveWindowHotkey, usedGestures, failures);
        RegisterIfValid(HotkeyAction.AllWindows, settings.AllWindowsHotkey, usedGestures, failures);
        return failures;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmHotKey && _registeredActions.TryGetValue(m.WParam.ToInt32(), out var action))
        {
            DiagnosticLog.Info($"hotkey pressed action={action}");
            HotkeyPressed?.Invoke(action);
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        DestroyHandle();
    }

    private void RegisterIfValid(
        HotkeyAction action,
        HotkeyGesture? gesture,
        HashSet<string> usedGestures,
        List<HotkeyRegistrationFailure> failures)
    {
        if (gesture is null || !gesture.IsValid())
        {
            return;
        }

        var displayGesture = gesture.ToDisplayString();
        var gestureKey = $"{gesture.ToNativeModifiers()}:{(uint)gesture.Key}";
        if (!usedGestures.Add(gestureKey))
        {
            failures.Add(new HotkeyRegistrationFailure(action, HotkeyRegistrationFailureKind.Duplicate, 0));
            DiagnosticLog.Info($"hotkey duplicate action={action} gesture={displayGesture}");
            return;
        }

        var id = GetHotkeyId(action);
        if (!NativeMethods.RegisterHotKey(Handle, id, gesture.ToNativeModifiers(), (uint)gesture.Key))
        {
            var error = Marshal.GetLastWin32Error();
            var kind = error == 1409
                ? HotkeyRegistrationFailureKind.SystemConflict
                : HotkeyRegistrationFailureKind.RegistrationError;
            failures.Add(new HotkeyRegistrationFailure(action, kind, error));
            DiagnosticLog.Info($"hotkey register failed action={action} gesture={displayGesture} error={error}");
            return;
        }

        _registeredActions[id] = action;
        DiagnosticLog.Info($"hotkey registered action={action} gesture={displayGesture}");
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredActions.Keys.ToArray())
        {
            if (!NativeMethods.UnregisterHotKey(Handle, id))
            {
                DiagnosticLog.Info($"hotkey unregister failed id={id} error={Marshal.GetLastWin32Error()}");
            }
        }

        _registeredActions.Clear();
    }

    private static int GetHotkeyId(HotkeyAction action)
    {
        return 0x5100 + (int)action;
    }
}

internal enum HotkeyRegistrationFailureKind
{
    Duplicate = 0,
    SystemConflict = 1,
    RegistrationError = 2
}

internal readonly record struct HotkeyRegistrationFailure(
    HotkeyAction Action,
    HotkeyRegistrationFailureKind Kind,
    int ErrorCode);
