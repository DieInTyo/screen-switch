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
    public bool OverlayEnabled { get; set; }
    public bool OverlayDraggable { get; set; }
    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.BottomRight;
    public OverlayLocation? OverlayCustomLocation { get; set; }
    public bool OverlayUseCustomLocation { get; set; }
    public int OverlayOpacity { get; set; } = 100;
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public MoveWindowPickerView MoveWindowPickerView { get; set; } = MoveWindowPickerView.Table;
    public bool HotkeysEnabled { get; set; }
    public HotkeyGesture? SelectedModeHotkey { get; set; }
    public HotkeyGesture? ActiveWindowHotkey { get; set; }
    public HotkeyGesture? AllWindowsHotkey { get; set; }
    public HotkeyGesture? MoveWindowHotkey { get; set; }
    public HotkeyGesture? MinimizeAllWindowsHotkey { get; set; }
    public HotkeyGesture? ToggleOverlayHotkey { get; set; }
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
    MinimizeAllWindows = 4,
    ToggleOverlay = 5
}

internal enum OverlayPosition
{
    TopLeft = 0,
    TopRight = 1,
    BottomLeft = 2,
    BottomRight = 3
}

internal sealed class OverlayLocation
{
    public int X { get; set; }
    public int Y { get; set; }
}

internal enum AppTheme
{
    Light = 0,
    Dark = 1
}

internal enum MoveWindowPickerView
{
    Table = 0,
    Tiles = 1
}

internal enum AppLanguage
{
    English = 0,
    Russian = 1
}

internal enum ModifierKeySide
{
    Any = 0,
    Left = 1,
    Right = 2
}

internal sealed class HotkeyGesture
{
    public bool Control { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public ModifierKeySide ControlSide { get; set; } = ModifierKeySide.Any;
    public ModifierKeySide AltSide { get; set; } = ModifierKeySide.Any;
    public ModifierKeySide ShiftSide { get; set; } = ModifierKeySide.Any;
    public ModifierKeySide WinSide { get; set; } = ModifierKeySide.Any;
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
            parts.Add(FormatModifier("Ctrl", ControlSide));
        }

        if (Alt)
        {
            parts.Add(FormatModifier("Alt", AltSide));
        }

        if (Shift)
        {
            parts.Add(FormatModifier("Shift", ShiftSide));
        }

        if (Win)
        {
            parts.Add(FormatModifier("Win", WinSide));
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
            ControlSide = ControlSide,
            AltSide = AltSide,
            ShiftSide = ShiftSide,
            WinSide = WinSide,
            Key = Key
        };
    }

    public bool ConflictsWith(HotkeyGesture other)
    {
        return Key == other.Key
            && ModifierConflicts(Control, ControlSide, other.Control, other.ControlSide)
            && ModifierConflicts(Alt, AltSide, other.Alt, other.AltSide)
            && ModifierConflicts(Shift, ShiftSide, other.Shift, other.ShiftSide)
            && ModifierConflicts(Win, WinSide, other.Win, other.WinSide);
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

    private static string FormatModifier(string name, ModifierKeySide side)
    {
        return side switch
        {
            ModifierKeySide.Left => $"Left {name}",
            ModifierKeySide.Right => $"Right {name}",
            _ => name
        };
    }

    private static bool ModifierConflicts(
        bool firstEnabled,
        ModifierKeySide firstSide,
        bool secondEnabled,
        ModifierKeySide secondSide)
    {
        if (firstEnabled != secondEnabled)
        {
            return false;
        }

        if (!firstEnabled)
        {
            return true;
        }

        return firstSide == ModifierKeySide.Any
            || secondSide == ModifierKeySide.Any
            || firstSide == secondSide;
    }
}
