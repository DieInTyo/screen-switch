using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class HotkeyManager : IDisposable
{
    private const uint LlkhfExtended = 0x01;
    private readonly Dictionary<HotkeyAction, HotkeyGesture> _registeredGestures = new();
    private readonly HashSet<Keys> _pressedKeys = new();
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
    private IntPtr _hookHandle;
    private string? _lastFiredSignature;
    private bool _disposed;

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
        RegisterIfValid(HotkeyAction.SelectedMode, settings.SelectedModeHotkey, failures);
        RegisterIfValid(HotkeyAction.ActiveWindow, settings.ActiveWindowHotkey, failures);
        RegisterIfValid(HotkeyAction.AllWindows, settings.AllWindowsHotkey, failures);
        RegisterIfValid(HotkeyAction.MoveWindow, settings.MoveWindowHotkey, failures);
        RegisterIfValid(HotkeyAction.MinimizeAllWindows, settings.MinimizeAllWindowsHotkey, failures);
        RegisterIfValid(HotkeyAction.ToggleOverlay, settings.ToggleOverlayHotkey, failures);

        if (_registeredGestures.Count == 0)
        {
            DiagnosticLog.Info("hotkeys enabled with no valid gestures");
            return failures;
        }

        if (!InstallHook())
        {
            var error = Marshal.GetLastWin32Error();
            DiagnosticLog.Info($"hotkey hook install failed error={error}");
            foreach (var action in _registeredGestures.Keys.ToArray())
            {
                failures.Add(new HotkeyRegistrationFailure(action, HotkeyRegistrationFailureKind.RegistrationError, error));
            }

            _registeredGestures.Clear();
        }

        return failures;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
    }

    public void UnregisterAll()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            if (!NativeMethods.UnhookWindowsHookEx(_hookHandle))
            {
                DiagnosticLog.Info($"hotkey hook uninstall failed error={Marshal.GetLastWin32Error()}");
            }
        }

        _hookHandle = IntPtr.Zero;
        _keyboardProc = null;
        _registeredGestures.Clear();
        _pressedKeys.Clear();
        _lastFiredSignature = null;
    }

    private void RegisterIfValid(
        HotkeyAction action,
        HotkeyGesture? gesture,
        List<HotkeyRegistrationFailure> failures)
    {
        if (gesture is null || !gesture.IsValid())
        {
            return;
        }

        var conflict = _registeredGestures
            .Any(registered => registered.Value.ConflictsWith(gesture));
        if (conflict)
        {
            failures.Add(new HotkeyRegistrationFailure(action, HotkeyRegistrationFailureKind.Duplicate, 0));
            DiagnosticLog.Info($"hotkey duplicate action={action} gesture={gesture.ToDisplayString()}");
            return;
        }

        _registeredGestures[action] = gesture.Clone();
        DiagnosticLog.Info($"hotkey registered action={action} gesture={gesture.ToDisplayString()}");
    }

    private bool InstallHook()
    {
        _keyboardProc = KeyboardHookCallback;
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule is null
            ? IntPtr.Zero
            : NativeMethods.GetModuleHandle(currentModule.ModuleName);
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _keyboardProc,
            moduleHandle,
            0);
        return _hookHandle != IntPtr.Zero;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        try
        {
            var message = wParam.ToInt32();
            if (message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown or NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp)
            {
                var hook = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var key = NormalizeKey((Keys)hook.vkCode, hook.scanCode, hook.flags);
                if (message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp)
                {
                    _pressedKeys.Remove(key);
                    _lastFiredSignature = null;
                }
                else
                {
                    _pressedKeys.Add(key);
                    TryFireHotkey(key);
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Info($"hotkey hook callback failed error=\"{ex.Message}\"");
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void TryFireHotkey(Keys key)
    {
        if (HotkeyGesture.IsModifierKey(key))
        {
            return;
        }

        foreach (var pair in _registeredGestures)
        {
            var action = pair.Key;
            var gesture = pair.Value;
            if (!Matches(gesture, key))
            {
                continue;
            }

            var signature = $"{action}:{gesture.ToDisplayString()}";
            if (_lastFiredSignature == signature)
            {
                return;
            }

            _lastFiredSignature = signature;
            DiagnosticLog.Info($"hotkey pressed action={action} gesture={gesture.ToDisplayString()}");
            HotkeyPressed?.Invoke(action);
            return;
        }
    }

    private bool Matches(HotkeyGesture gesture, Keys key)
    {
        return gesture.Key == key
            && ModifierMatches(gesture.Control, gesture.ControlSide, Keys.LControlKey, Keys.RControlKey)
            && ModifierMatches(gesture.Alt, gesture.AltSide, Keys.LMenu, Keys.RMenu)
            && ModifierMatches(gesture.Shift, gesture.ShiftSide, Keys.LShiftKey, Keys.RShiftKey)
            && ModifierMatches(gesture.Win, gesture.WinSide, Keys.LWin, Keys.RWin);
    }

    private bool ModifierMatches(bool required, ModifierKeySide side, Keys leftKey, Keys rightKey)
    {
        var leftDown = _pressedKeys.Contains(leftKey);
        var rightDown = _pressedKeys.Contains(rightKey);
        if (!required)
        {
            return !leftDown && !rightDown;
        }

        return side switch
        {
            ModifierKeySide.Left => leftDown,
            ModifierKeySide.Right => rightDown,
            _ => leftDown || rightDown
        };
    }

    private static Keys NormalizeKey(Keys key, uint scanCode, uint flags)
    {
        return key switch
        {
            Keys.ControlKey => (flags & LlkhfExtended) != 0 ? Keys.RControlKey : Keys.LControlKey,
            Keys.Menu => (flags & LlkhfExtended) != 0 ? Keys.RMenu : Keys.LMenu,
            Keys.ShiftKey => scanCode == 0x36 ? Keys.RShiftKey : Keys.LShiftKey,
            _ => key
        };
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
