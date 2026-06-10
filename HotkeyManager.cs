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
        RegisterIfValid(HotkeyAction.SelectedMode, settings.SelectedModeHotkey, "Выбранный режим", usedGestures, failures);
        RegisterIfValid(HotkeyAction.ActiveWindow, settings.ActiveWindowHotkey, "Активное окно", usedGestures, failures);
        RegisterIfValid(HotkeyAction.AllWindows, settings.AllWindowsHotkey, "Все окна", usedGestures, failures);
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
        string displayName,
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
            var message = "Это сочетание уже назначено другому действию Screen Switch.";
            failures.Add(new HotkeyRegistrationFailure(action, message));
            DiagnosticLog.Info($"hotkey duplicate action={action} gesture={displayGesture}");
            return;
        }

        var id = GetHotkeyId(action);
        if (!NativeMethods.RegisterHotKey(Handle, id, gesture.ToNativeModifiers(), (uint)gesture.Key))
        {
            var error = Marshal.GetLastWin32Error();
            var message = error == 1409
                ? "Конфликт с системным или другим глобальным сочетанием клавиш."
                : $"Не удалось зарегистрировать сочетание клавиш. Код ошибки: {error}.";
            failures.Add(new HotkeyRegistrationFailure(action, message));
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

internal readonly record struct HotkeyRegistrationFailure(HotkeyAction Action, string Message);
