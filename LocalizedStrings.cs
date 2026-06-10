namespace ScreenSwitch;

internal sealed class LocalizedStrings
{
    private LocalizedStrings(AppLanguage language)
    {
        Language = language;
    }

    public AppLanguage Language { get; }

    public static LocalizedStrings For(AppLanguage language)
    {
        return new LocalizedStrings(language);
    }

    public string MoveActiveWindow => IsRussian ? "Переместить активное окно" : "Move active window";
    public string MoveAllWindows => IsRussian ? "Переместить все окна" : "Move all windows";
    public string MoveWindow => IsRussian ? "Перенести окно" : "Move window";
    public string MoveSelectedWindows => IsRussian ? "Перенести выбранные" : "Move selected";
    public string MoveWindowDoubleClickHint => IsRussian ? "Двойной клик переносит одно окно" : "Double-click moves one window";
    public string NoWindowsFound => IsRussian ? "Окна не найдены" : "No windows found";
    public string WindowMoved => IsRussian ? "Окно перенесено." : "Window moved.";
    public string WindowMoveFailed => IsRussian ? "Не удалось перенести выбранное окно." : "Could not move the selected window.";
    public string LeftClick => IsRussian ? "Левый клик" : "Left click";
    public string LeftClickActive => IsRussian ? "Перемещать активное окно" : "Move active window";
    public string LeftClickAll => IsRussian ? "Перемещать все окна" : "Move all windows";
    public string Hotkeys => IsRussian ? "Горячие клавиши" : "Hotkeys";
    public string EnableHotkeys => IsRussian ? "Включить горячие клавиши" : "Enable hotkeys";
    public string SelectedMode => IsRussian ? "Выбранный режим" : "Selected mode";
    public string ActiveWindow => IsRussian ? "Активное окно" : "Active window";
    public string AllWindows => IsRussian ? "Все окна" : "All windows";
    public string ResetHotkeys => IsRussian ? "Сбросить горячие клавиши" : "Reset hotkeys";
    public string MoveMinimizedWindows => IsRussian ? "Переносить свернутые окна" : "Move minimized windows";
    public string ShowNotifications => IsRussian ? "Показывать уведомления" : "Show notifications";
    public string StartWithWindows => IsRussian ? "Запускать вместе с Windows" : "Start with Windows";
    public string LanguageMenu => IsRussian ? "Язык" : "Language";
    public string Exit => IsRussian ? "Выход" : "Exit";
    public string NotAssigned => IsRussian ? "Не назначено" : "Not assigned";
    public string AssignHotkeyTooltip => IsRussian ? "Нажмите, чтобы назначить горячую клавишу." : "Click to assign a hotkey.";
    public string AppName => "Screen Switch";

    public string StartupEnabled => IsRussian ? "Автозапуск включен." : "Startup enabled.";
    public string StartupDisabled => IsRussian ? "Автозапуск выключен." : "Startup disabled.";
    public string NotificationsEnabled => IsRussian ? "Уведомления включены." : "Notifications enabled.";
    public string HotkeysEnabled => IsRussian ? "Горячие клавиши включены." : "Hotkeys enabled.";
    public string HotkeysDisabled => IsRussian ? "Горячие клавиши выключены." : "Hotkeys disabled.";
    public string HotkeysReset => IsRussian ? "Горячие клавиши сброшены." : "Hotkeys reset.";
    public string HotkeyCleared => IsRussian ? "горячая клавиша очищена." : "hotkey cleared.";
    public string InitialStatus => IsRussian ? "Левый клик выполняет выбранное действие из меню." : "Left click runs the selected menu action.";
    public string LanguageChanged => IsRussian ? "Язык изменен." : "Language changed.";
    public string LeftClickNowActive => IsRussian ? "Теперь левый клик переносит активное окно." : "Left click now moves the active window.";
    public string LeftClickNowAll => IsRussian ? "Теперь левый клик переносит все окна." : "Left click now moves all windows.";
    public string ActiveSwapCompleted => IsRussian ? "Активное окно и окно на другом мониторе поменялись местами." : "Active window and the window on the other monitor were swapped.";
    public string MovedWindowsPrefix => IsRussian ? "Перемещено окон" : "Moved windows";
    public string MinimizedWillMove => IsRussian ? "Свернутые окна будут переноситься." : "Minimized windows will be moved.";
    public string MinimizedWillSkip => IsRussian ? "Свернутые окна будут пропускаться." : "Minimized windows will be skipped.";
    public string CouldNotFindActiveWindow => IsRussian ? "Не удалось определить открытое активное окно для обмена." : "Could not detect an open active window to swap.";
    public string NoWindowOnOtherMonitor => IsRussian ? "На другом мониторе нет подходящего открытого окна для обмена." : "There is no suitable open window on the other monitor to swap with.";
    public string TwoMonitorsRequired => IsRussian ? "Приложение работает только когда подключено ровно два монитора." : "Screen Switch works only when exactly two monitors are connected.";
    public string StartupRegistryOpenFailed => IsRussian ? "Не удалось открыть раздел автозапуска." : "Could not open the startup registry key.";

    public string ChangeStartupFailed(string message)
    {
        return IsRussian
            ? $"Не удалось изменить автозапуск: {message}"
            : $"Could not change startup setting: {message}";
    }

    public string HotkeyCaptureTitle(string actionName)
    {
        return IsRussian ? $"Горячая клавиша: {actionName}" : $"Hotkey: {actionName}";
    }

    public string HotkeyCapturePrompt => IsRussian
        ? "Нажми сочетание с Ctrl, Alt, Shift или Win"
        : "Press a shortcut with Ctrl, Alt, Shift, or Win";

    public string HotkeyCaptureCurrent(string current)
    {
        return IsRussian ? $"Сейчас: {current}" : $"Current: {current}";
    }

    public string HotkeyCaptureClearNote => IsRussian
        ? "Esc, Space, Backspace или Delete оставят поле пустым"
        : "Esc, Space, Backspace, or Delete leaves this empty";

    public string HotkeyCaptureModifierOnly => IsRussian
        ? "Добавь обычную клавишу к модификатору"
        : "Add a regular key to the modifier";

    public string HotkeyCaptureNeedsModifier => IsRussian
        ? "Нужно сочетание с Ctrl, Alt, Shift или Win"
        : "Use Ctrl, Alt, Shift, or Win with the key";

    public string HotkeyDuplicate => IsRussian
        ? "Это сочетание уже назначено другому действию Screen Switch."
        : "This shortcut is already assigned to another Screen Switch action.";

    public string HotkeySystemConflict => IsRussian
        ? "Конфликт с системным или другим глобальным сочетанием клавиш."
        : "Conflict with a system or another global keyboard shortcut.";

    public string HotkeyRegisterFailed(int errorCode)
    {
        return IsRussian
            ? $"Не удалось зарегистрировать сочетание клавиш. Код ошибки: {errorCode}."
            : $"Could not register this keyboard shortcut. Error code: {errorCode}.";
    }

    private bool IsRussian => Language == AppLanguage.Russian;
}
