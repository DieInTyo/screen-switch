using System.Text.Json;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsStore()
    {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenSwitch");
        _settingsPath = Path.Combine(appDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_settingsPath, json);
    }
}

internal sealed class AppSettings
{
    public LeftClickAction LeftClickAction { get; set; } = LeftClickAction.ActiveWindow;
    public bool ShowNotifications { get; set; } = true;
    public bool MoveMinimizedWindows { get; set; } = true;
    public bool HotkeysEnabled { get; set; }
    public HotkeyGesture? SelectedModeHotkey { get; set; }
    public HotkeyGesture? ActiveWindowHotkey { get; set; }
    public HotkeyGesture? AllWindowsHotkey { get; set; }
    public HotkeyGesture? MoveWindowHotkey { get; set; }
    public HotkeyGesture? MinimizeAllWindowsHotkey { get; set; }
    public AppLanguage Language { get; set; } = AppLanguage.English;
}

internal enum LeftClickAction
{
    ActiveWindow = 0,
    AllWindows = 1
}

internal enum HotkeyAction
{
    SelectedMode = 0,
    ActiveWindow = 1,
    AllWindows = 2,
    MoveWindow = 3,
    MinimizeAllWindows = 4
}

internal enum AppLanguage
{
    English = 0,
    Russian = 1
}

internal sealed class HotkeyGesture
{
    public bool Control { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public Keys Key { get; set; }

    public bool IsValid()
    {
        return Key != Keys.None
            && HasModifier()
            && !IsModifierKey(Key)
            && Key is not Keys.Escape and not Keys.Space and not Keys.Back and not Keys.Delete;
    }

    public bool HasModifier()
    {
        return Control || Alt || Shift || Win;
    }

    public NativeMethods.HotkeyModifiers ToNativeModifiers()
    {
        var modifiers = NativeMethods.HotkeyModifiers.NoRepeat;
        if (Control)
        {
            modifiers |= NativeMethods.HotkeyModifiers.Control;
        }

        if (Alt)
        {
            modifiers |= NativeMethods.HotkeyModifiers.Alt;
        }

        if (Shift)
        {
            modifiers |= NativeMethods.HotkeyModifiers.Shift;
        }

        if (Win)
        {
            modifiers |= NativeMethods.HotkeyModifiers.Win;
        }

        return modifiers;
    }

    public string ToDisplayString()
    {
        if (!IsValid())
        {
            return "Not assigned";
        }

        var parts = new List<string>();
        if (Control)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Win)
        {
            parts.Add("Win");
        }

        parts.Add(FormatKey(Key));
        return string.Join(" + ", parts);
    }

    public HotkeyGesture Clone()
    {
        return new HotkeyGesture
        {
            Control = Control,
            Alt = Alt,
            Shift = Shift,
            Win = Win,
            Key = Key
        };
    }

    internal static bool IsModifierKey(Keys key)
    {
        return key is Keys.ControlKey
            or Keys.ShiftKey
            or Keys.Menu
            or Keys.LWin
            or Keys.RWin
            or Keys.LControlKey
            or Keys.RControlKey
            or Keys.LShiftKey
            or Keys.RShiftKey
            or Keys.LMenu
            or Keys.RMenu;
    }

    private static string FormatKey(Keys key)
    {
        if (key >= Keys.A && key <= Keys.Z)
        {
            return key.ToString();
        }

        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return ((int)(key - Keys.D0)).ToString();
        }

        if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
        {
            return $"Num {(int)(key - Keys.NumPad0)}";
        }

        return key switch
        {
            Keys.Oemtilde => "`",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemPipe => "\\",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            _ => key.ToString()
        };
    }
}
