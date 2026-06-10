using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class TrayAppContext : ApplicationContext
{
    private const int NormalTrackerInterval = 250;
    private const int PendingTrackerInterval = 40;
    private readonly NotifyIcon _notifyIcon;
    private readonly SettingsStore _settingsStore;
    private readonly StartupManager _startupManager;
    private readonly System.Windows.Forms.Timer _foregroundTracker;
    private readonly WindowMover _windowMover = new();
    private readonly HotkeyManager _hotkeyManager;
    private readonly AppSettings _settings;
    private readonly ToolStripMenuItem _moveActiveItem;
    private readonly ToolStripMenuItem _moveAllItem;
    private readonly ToolStripMenuItem _moveWindowMenu;
    private readonly ToolStripMenuItem _moveSelectedWindowsItem;
    private readonly ToolStripMenuItem _leftClickMenu;
    private readonly ToolStripMenuItem _leftClickActiveItem;
    private readonly ToolStripMenuItem _leftClickAllItem;
    private readonly ToolStripMenuItem _moveMinimizedItem;
    private readonly ToolStripMenuItem _hotkeysMenu;
    private readonly ToolStripMenuItem _hotkeysEnabledItem;
    private readonly ToolStripMenuItem _hotkeySelectedModeItem;
    private readonly ToolStripMenuItem _hotkeyActiveWindowItem;
    private readonly ToolStripMenuItem _hotkeyAllWindowsItem;
    private readonly ToolStripMenuItem _resetHotkeysItem;
    private readonly ToolStripMenuItem _notificationsItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _languageMenu;
    private readonly ToolStripMenuItem _englishLanguageItem;
    private readonly ToolStripMenuItem _russianLanguageItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly Dictionary<string, IntPtr> _lastWindowByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<HotkeyAction, HotkeyRegistrationFailure> _hotkeyRegistrationErrors = new();
    private readonly HashSet<IntPtr> _selectedMoveWindowHandles = new();
    private LocalizedStrings _text;
    private TrackedWindow _lastTrackedWindow;
    private bool _allowMenuCloseOnce;

    public TrayAppContext()
    {
        DiagnosticLog.Start();
        _settingsStore = new SettingsStore();
        _startupManager = new StartupManager();
        _settings = _settingsStore.Load();
        _text = LocalizedStrings.For(_settings.Language);

        var menu = new ContextMenuStrip
        {
            ShowItemToolTips = true
        };
        menu.Closing += MenuOnClosing;
        menu.Closed += (_, _) => _allowMenuCloseOnce = false;
        _moveActiveItem = new ToolStripMenuItem(string.Empty, null, (_, _) =>
        {
            AllowMenuClose();
            MoveActiveWindow();
        });
        menu.Items.Add(_moveActiveItem);
        _moveAllItem = new ToolStripMenuItem(string.Empty, null, (_, _) =>
        {
            AllowMenuClose();
            MoveAllWindows();
        });
        menu.Items.Add(_moveAllItem);
        _moveWindowMenu = new ToolStripMenuItem();
        _moveWindowMenu.DropDownItems.Add(new ToolStripMenuItem(string.Empty)
        {
            Enabled = false
        });
        _moveWindowMenu.DropDownOpening += (_, _) =>
        {
            _selectedMoveWindowHandles.Clear();
            RebuildMoveWindowMenu();
            ApplyMonitorAwareDropDownDirection(_moveWindowMenu);
        };
        _moveWindowMenu.DropDown.Closing += MenuOnClosing;
        _moveSelectedWindowsItem = new ToolStripMenuItem(string.Empty, null, (_, _) => MoveSelectedWindows());
        menu.Items.Add(_moveWindowMenu);
        menu.Items.Add(new ToolStripSeparator());

        _leftClickMenu = new ToolStripMenuItem();
        _leftClickMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(_leftClickMenu);
        _leftClickMenu.DropDown.Closing += MenuOnClosing;
        _leftClickActiveItem = new ToolStripMenuItem(string.Empty, null, (_, _) => SetLeftClickAction(LeftClickAction.ActiveWindow))
        {
            CheckOnClick = true
        };
        _leftClickAllItem = new ToolStripMenuItem(string.Empty, null, (_, _) => SetLeftClickAction(LeftClickAction.AllWindows))
        {
            CheckOnClick = true
        };
        _leftClickMenu.DropDownItems.Add(_leftClickActiveItem);
        _leftClickMenu.DropDownItems.Add(_leftClickAllItem);
        menu.Items.Add(_leftClickMenu);

        _hotkeysMenu = new ToolStripMenuItem();
        _hotkeysMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(_hotkeysMenu);
        _hotkeysMenu.DropDown.ShowItemToolTips = true;
        _hotkeysMenu.DropDown.Closing += MenuOnClosing;
        _hotkeysEnabledItem = new ToolStripMenuItem(string.Empty, null, ToggleHotkeys)
        {
            CheckOnClick = true,
            Checked = _settings.HotkeysEnabled
        };
        _hotkeySelectedModeItem = new ToolStripMenuItem(string.Empty, null, (_, _) => CaptureHotkey(HotkeyAction.SelectedMode));
        _hotkeyActiveWindowItem = new ToolStripMenuItem(string.Empty, null, (_, _) => CaptureHotkey(HotkeyAction.ActiveWindow));
        _hotkeyAllWindowsItem = new ToolStripMenuItem(string.Empty, null, (_, _) => CaptureHotkey(HotkeyAction.AllWindows));
        _resetHotkeysItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ResetHotkeys());
        _hotkeysMenu.DropDownItems.Add(_hotkeysEnabledItem);
        _hotkeysMenu.DropDownItems.Add(new ToolStripSeparator());
        _hotkeysMenu.DropDownItems.Add(_hotkeySelectedModeItem);
        _hotkeysMenu.DropDownItems.Add(_hotkeyActiveWindowItem);
        _hotkeysMenu.DropDownItems.Add(_hotkeyAllWindowsItem);
        _hotkeysMenu.DropDownItems.Add(new ToolStripSeparator());
        _hotkeysMenu.DropDownItems.Add(_resetHotkeysItem);
        menu.Items.Add(_hotkeysMenu);

        _moveMinimizedItem = new ToolStripMenuItem(string.Empty, null, ToggleMoveMinimizedWindows)
        {
            CheckOnClick = true,
            Checked = _settings.MoveMinimizedWindows
        };
        menu.Items.Add(_moveMinimizedItem);

        _notificationsItem = new ToolStripMenuItem(string.Empty, null, ToggleNotifications)
        {
            CheckOnClick = true,
            Checked = _settings.ShowNotifications
        };
        menu.Items.Add(_notificationsItem);

        _startupItem = new ToolStripMenuItem(string.Empty, null, ToggleStartup)
        {
            CheckOnClick = true,
            Checked = _startupManager.IsEnabled()
        };
        menu.Items.Add(_startupItem);

        _languageMenu = new ToolStripMenuItem();
        _languageMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(_languageMenu);
        _languageMenu.DropDown.Closing += MenuOnClosing;
        _englishLanguageItem = new ToolStripMenuItem("English", null, (_, _) => SetLanguage(AppLanguage.English))
        {
            CheckOnClick = true
        };
        _russianLanguageItem = new ToolStripMenuItem("Русский", null, (_, _) => SetLanguage(AppLanguage.Russian))
        {
            CheckOnClick = true
        };
        _languageMenu.DropDownItems.Add(_englishLanguageItem);
        _languageMenu.DropDownItems.Add(_russianLanguageItem);
        menu.Items.Add(_languageMenu);
        menu.Items.Add(new ToolStripSeparator());
        _exitItem = new ToolStripMenuItem(string.Empty, null, (_, _) =>
        {
            AllowMenuClose();
            ExitThread();
        });
        menu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Screen Switch",
            Visible = true,
            ContextMenuStrip = menu
        };

        ApplyLeftClickChecks();
        ApplyLanguageChecks();
        ApplyUiText();

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        ApplyHotkeyRegistrations(showFailures: true);

        _notifyIcon.MouseClick += NotifyIconOnMouseClick;
        _foregroundTracker = new System.Windows.Forms.Timer
        {
            Interval = NormalTrackerInterval
        };
        _foregroundTracker.Tick += TrackForegroundWindow;
        _foregroundTracker.Start();
        ShowStatus(_text.InitialStatus);
    }

    protected override void ExitThreadCore()
    {
        _foregroundTracker.Stop();
        _foregroundTracker.Dispose();
        _hotkeyManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }

    private void NotifyIconOnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        ExecuteSelectedMode();
    }

    private void MoveActiveWindow()
    {
        var fallbackOtherMonitorWindow = GetLastWindowOnOtherMonitor();

        try
        {
            var movedCount = _windowMover.MoveActiveWindowBetweenMonitors(_lastTrackedWindow.Handle, fallbackOtherMonitorWindow.Handle);
            UpdateTrackerInterval();

            if (movedCount >= 2)
            {
                ShowStatus(_text.ActiveSwapCompleted);
                return;
            }

            ShowStatus($"{_text.MovedWindowsPrefix}: {movedCount}.");
        }
        catch (Exception ex)
        {
            UpdateTrackerInterval();
            ShowStatus(LocalizeError(ex), ToolTipIcon.Warning);
        }
    }

    private void MoveAllWindows()
    {
        ExecuteMove(
            () => _windowMover.MoveAllWindowsBetweenMonitors(_settings.MoveMinimizedWindows),
            count => $"{_text.MovedWindowsPrefix}: {count}.");
    }

    private void MoveSpecificWindow(MovableWindowInfo window)
    {
        AllowMenuClose();
        try
        {
            DiagnosticLog.Info(
                $"action menu-window selected hwnd={DiagnosticLog.FormatHandle(window.Handle)} title=\"{window.Title}\" app=\"{window.AppName}\" pid={window.ProcessId}");
            if (!_windowMover.MoveWindowToOtherMonitor(window.Handle))
            {
                ShowStatus(_text.WindowMoveFailed, ToolTipIcon.Warning);
                return;
            }

            UpdateTrackerInterval();
            ShowStatus(_text.WindowMoved);
        }
        catch (Exception ex)
        {
            UpdateTrackerInterval();
            DiagnosticLog.Info($"action menu-window failed {ex.GetType().Name}: {ex.Message}");
            ShowStatus(LocalizeError(ex), ToolTipIcon.Warning);
        }
    }

    private void MoveSelectedWindows()
    {
        var handles = _selectedMoveWindowHandles.ToArray();
        if (handles.Length == 0)
        {
            return;
        }

        AllowMenuClose();
        try
        {
            var movedCount = 0;
            foreach (var handle in handles)
            {
                DiagnosticLog.Info($"action menu-window selected-batch hwnd={DiagnosticLog.FormatHandle(handle)}");
                if (_windowMover.MoveWindowToOtherMonitor(handle))
                {
                    movedCount++;
                }
            }

            _selectedMoveWindowHandles.Clear();
            UpdateTrackerInterval();
            ShowStatus($"{_text.MovedWindowsPrefix}: {movedCount}.");
        }
        catch (Exception ex)
        {
            UpdateTrackerInterval();
            DiagnosticLog.Info($"action menu-window batch failed {ex.GetType().Name}: {ex.Message}");
            ShowStatus(LocalizeError(ex), ToolTipIcon.Warning);
        }
    }

    private void RebuildMoveWindowMenu()
    {
        _moveWindowMenu.DropDownItems.Clear();
        var windows = _windowMover
            .GetMovableWindows(includeMinimizedWindows: true)
            .OrderBy(window => window.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        PruneSelectedMoveWindows(windows);
        _moveSelectedWindowsItem.Text = _text.MoveSelectedWindows;
        _moveSelectedWindowsItem.Enabled = _selectedMoveWindowHandles.Count > 0;
        _moveWindowMenu.DropDownItems.Add(_moveSelectedWindowsItem);
        _moveWindowMenu.DropDownItems.Add(new ToolStripSeparator());

        if (windows.Length == 0)
        {
            _moveWindowMenu.DropDownItems.Add(new ToolStripMenuItem(_text.NoWindowsFound)
            {
                Enabled = false
            });
            return;
        }

        foreach (var appGroup in windows.GroupBy(window => window.AppName, StringComparer.CurrentCultureIgnoreCase))
        {
            var appWindows = appGroup.ToArray();
            if (appWindows.Length == 1)
            {
                _moveWindowMenu.DropDownItems.Add(CreateWindowMenuItem(FormatWindowMenuLabel(appGroup.Key, appWindows[0]), appWindows[0]));
                continue;
            }

            var appItem = new ToolStripMenuItem(FormatAppGroupLabel(appGroup.Key, appWindows))
            {
                AutoToolTip = true,
                ToolTipText = appGroup.Key
            };
            appItem.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(appItem);
            appItem.DropDown.Closing += MenuOnClosing;
            foreach (var window in appWindows)
            {
                appItem.DropDownItems.Add(CreateWindowMenuItem(FormatWindowMenuLabel(window.Title, window), window));
            }

            _moveWindowMenu.DropDownItems.Add(appItem);
        }

        _moveWindowMenu.DropDownItems.Add(new ToolStripSeparator());
        _moveWindowMenu.DropDownItems.Add(new ToolStripMenuItem(_text.MoveWindowDoubleClickHint)
        {
            Enabled = false
        });
    }

    private ToolStripMenuItem CreateWindowMenuItem(string label, MovableWindowInfo window)
    {
        var item = new ToolStripMenuItem(label)
        {
            AutoToolTip = true,
            CheckOnClick = true,
            Checked = _selectedMoveWindowHandles.Contains(window.Handle),
            DoubleClickEnabled = true,
            ToolTipText = window.Title
        };
        item.CheckedChanged += (_, _) => ToggleMoveWindowSelection(window.Handle, item.Checked);
        item.DoubleClick += (_, _) => MoveSpecificWindow(window);

        return item;
    }

    private void ToggleMoveWindowSelection(IntPtr handle, bool selected)
    {
        if (selected)
        {
            _selectedMoveWindowHandles.Add(handle);
        }
        else
        {
            _selectedMoveWindowHandles.Remove(handle);
        }

        _moveSelectedWindowsItem.Enabled = _selectedMoveWindowHandles.Count > 0;
    }

    private void PruneSelectedMoveWindows(IReadOnlyCollection<MovableWindowInfo> windows)
    {
        var availableHandles = windows.Select(window => window.Handle).ToHashSet();
        _selectedMoveWindowHandles.RemoveWhere(handle => !availableHandles.Contains(handle));
    }

    private static string FormatAppGroupLabel(string appName, IReadOnlyCollection<MovableWindowInfo> windows)
    {
        var monitors = windows
            .Select(window => GetMonitorNumber(window.ScreenDeviceName))
            .Distinct()
            .OrderBy(monitor => monitor)
            .ToArray();

        return monitors.Length == 0
            ? appName
            : $"{FormatMonitorBadge(monitors)} {appName}";
    }

    private static string FormatWindowMenuLabel(string label, MovableWindowInfo window)
    {
        return $"{FormatMonitorBadge(new[] { GetMonitorNumber(window.ScreenDeviceName) })} {label}";
    }

    private static string FormatMonitorBadge(IEnumerable<int> monitors)
    {
        return $"[{string.Join(",", monitors)}]";
    }

    private static int GetMonitorNumber(string screenDeviceName)
    {
        var screens = Screen.AllScreens
            .OrderBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();

        var index = Array.FindIndex(screens, screen => screen.DeviceName == screenDeviceName);
        return index >= 0 ? index + 1 : 0;
    }

    private void ExecuteMove(Func<int> action, Func<int, string> successMessageFactory)
    {
        try
        {
            var movedCount = action();
            UpdateTrackerInterval();
            ShowStatus(successMessageFactory(movedCount));
        }
        catch (Exception ex)
        {
            UpdateTrackerInterval();
            ShowStatus(LocalizeError(ex), ToolTipIcon.Warning);
        }
    }

    private void SetLeftClickAction(LeftClickAction action)
    {
        _settings.LeftClickAction = action;
        _settingsStore.Save(_settings);
        ApplyLeftClickChecks();

        var description = action == LeftClickAction.ActiveWindow
            ? _text.LeftClickNowActive
            : _text.LeftClickNowAll;
        ShowStatus(description);
    }

    private void ApplyLeftClickChecks()
    {
        _leftClickActiveItem.Checked = _settings.LeftClickAction == LeftClickAction.ActiveWindow;
        _leftClickAllItem.Checked = _settings.LeftClickAction == LeftClickAction.AllWindows;
    }

    private void ApplyLanguageChecks()
    {
        _englishLanguageItem.Checked = _settings.Language == AppLanguage.English;
        _russianLanguageItem.Checked = _settings.Language == AppLanguage.Russian;
    }

    private void ApplyUiText()
    {
        _moveActiveItem.Text = _text.MoveActiveWindow;
        _moveAllItem.Text = _text.MoveAllWindows;
        _moveWindowMenu.Text = _text.MoveWindow;
        _leftClickMenu.Text = _text.LeftClick;
        _leftClickActiveItem.Text = _text.LeftClickActive;
        _leftClickAllItem.Text = _text.LeftClickAll;
        _hotkeysMenu.Text = _text.Hotkeys;
        _hotkeysEnabledItem.Text = _text.EnableHotkeys;
        _resetHotkeysItem.Text = _text.ResetHotkeys;
        _moveMinimizedItem.Text = _text.MoveMinimizedWindows;
        _notificationsItem.Text = _text.ShowNotifications;
        _startupItem.Text = _text.StartWithWindows;
        _languageMenu.Text = _text.LanguageMenu;
        _exitItem.Text = _text.Exit;
        UpdateHotkeyMenuText();
    }

    private void MenuOnClosing(object? sender, ToolStripDropDownClosingEventArgs e)
    {
        if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && !_allowMenuCloseOnce)
        {
            e.Cancel = true;
        }
    }

    private void AllowMenuClose()
    {
        _allowMenuCloseOnce = true;
    }

    private static void ApplyMonitorAwareDropDownDirection(ToolStripMenuItem item)
    {
        if (item.Owner is null)
        {
            return;
        }

        var itemBounds = item.Owner.RectangleToScreen(item.Bounds);
        var ownerScreen = Screen.FromRectangle(itemBounds);
        var dropDownSize = item.DropDown.GetPreferredSize(Size.Empty);
        var rightAvailable = ownerScreen.WorkingArea.Right - itemBounds.Right;
        var leftAvailable = itemBounds.Left - ownerScreen.WorkingArea.Left;

        item.DropDownDirection = rightAvailable >= dropDownSize.Width || rightAvailable >= leftAvailable
            ? ToolStripDropDownDirection.Right
            : ToolStripDropDownDirection.Left;
    }

    private void ExecuteSelectedMode()
    {
        if (_settings.LeftClickAction == LeftClickAction.AllWindows)
        {
            MoveAllWindows();
            return;
        }

        MoveActiveWindow();
    }

    private void SetLanguage(AppLanguage language)
    {
        _settings.Language = language;
        _settingsStore.Save(_settings);
        _text = LocalizedStrings.For(language);
        ApplyLanguageChecks();
        ApplyUiText();
        ShowStatus(_text.LanguageChanged);
    }

    private void OnHotkeyPressed(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.SelectedMode:
                ExecuteSelectedMode();
                break;
            case HotkeyAction.ActiveWindow:
                MoveActiveWindow();
                break;
            case HotkeyAction.AllWindows:
                MoveAllWindows();
                break;
        }
    }

    private void ToggleHotkeys(object? sender, EventArgs e)
    {
        _settings.HotkeysEnabled = _hotkeysEnabledItem.Checked;
        _settingsStore.Save(_settings);
        ApplyHotkeyRegistrations(showFailures: true);
        ShowStatus(_settings.HotkeysEnabled
            ? _text.HotkeysEnabled
            : _text.HotkeysDisabled);
    }

    private void CaptureHotkey(HotkeyAction action)
    {
        _hotkeyManager.UnregisterAll();
        try
        {
            using var dialog = new HotkeyCaptureForm(GetHotkeyActionName(action), GetHotkeyGesture(action), _text);
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            SetHotkeyGesture(action, dialog.SelectedGesture);
            _settingsStore.Save(_settings);
            UpdateHotkeyMenuText();
            ShowStatus(dialog.SelectedGesture is null
                ? $"{GetHotkeyActionName(action)}: {_text.HotkeyCleared}"
                : $"{GetHotkeyActionName(action)}: {dialog.SelectedGesture.ToDisplayString()}.");
        }
        finally
        {
            ApplyHotkeyRegistrations(showFailures: true);
        }
    }

    private void ResetHotkeys()
    {
        _settings.SelectedModeHotkey = null;
        _settings.ActiveWindowHotkey = null;
        _settings.AllWindowsHotkey = null;
        _settingsStore.Save(_settings);
        UpdateHotkeyMenuText();
        ApplyHotkeyRegistrations(showFailures: true);
        ShowStatus(_text.HotkeysReset);
    }

    private void ApplyHotkeyRegistrations(bool showFailures)
    {
        var failures = _hotkeyManager.Configure(_settings);
        _hotkeyRegistrationErrors.Clear();
        foreach (var failure in failures)
        {
            _hotkeyRegistrationErrors[failure.Action] = failure;
        }

        UpdateHotkeyMenuText();
        if (showFailures && failures.Count > 0)
        {
            ShowStatus($"{GetHotkeyActionName(failures[0].Action)}: {GetHotkeyFailureMessage(failures[0])}", ToolTipIcon.Warning);
        }
    }

    private void UpdateHotkeyMenuText()
    {
        _hotkeysEnabledItem.Checked = _settings.HotkeysEnabled;
        UpdateHotkeyMenuItem(_hotkeySelectedModeItem, _text.SelectedMode, _settings.SelectedModeHotkey, HotkeyAction.SelectedMode);
        UpdateHotkeyMenuItem(_hotkeyActiveWindowItem, _text.ActiveWindow, _settings.ActiveWindowHotkey, HotkeyAction.ActiveWindow);
        UpdateHotkeyMenuItem(_hotkeyAllWindowsItem, _text.AllWindows, _settings.AllWindowsHotkey, HotkeyAction.AllWindows);
    }

    private void UpdateHotkeyMenuItem(
        ToolStripMenuItem item,
        string label,
        HotkeyGesture? gesture,
        HotkeyAction action)
    {
        item.Text = $"{label}: {FormatHotkey(gesture)}";
        if (_hotkeyRegistrationErrors.TryGetValue(action, out var failure))
        {
            item.ForeColor = Color.Firebrick;
            item.ToolTipText = GetHotkeyFailureMessage(failure);
            return;
        }

        item.ForeColor = SystemColors.MenuText;
        item.ToolTipText = _text.AssignHotkeyTooltip;
    }

    private HotkeyGesture? GetHotkeyGesture(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.SelectedMode => _settings.SelectedModeHotkey?.Clone(),
            HotkeyAction.ActiveWindow => _settings.ActiveWindowHotkey?.Clone(),
            HotkeyAction.AllWindows => _settings.AllWindowsHotkey?.Clone(),
            _ => null
        };
    }

    private void SetHotkeyGesture(HotkeyAction action, HotkeyGesture? gesture)
    {
        switch (action)
        {
            case HotkeyAction.SelectedMode:
                _settings.SelectedModeHotkey = gesture;
                break;
            case HotkeyAction.ActiveWindow:
                _settings.ActiveWindowHotkey = gesture;
                break;
            case HotkeyAction.AllWindows:
                _settings.AllWindowsHotkey = gesture;
                break;
        }
    }

    private string FormatHotkey(HotkeyGesture? gesture)
    {
        return gesture is not null && gesture.IsValid()
            ? gesture.ToDisplayString()
            : _text.NotAssigned;
    }

    private string GetHotkeyActionName(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.SelectedMode => _text.SelectedMode,
            HotkeyAction.ActiveWindow => _text.ActiveWindow,
            HotkeyAction.AllWindows => _text.AllWindows,
            _ => "Action"
        };
    }

    private string GetHotkeyFailureMessage(HotkeyRegistrationFailure failure)
    {
        return failure.Kind switch
        {
            HotkeyRegistrationFailureKind.Duplicate => _text.HotkeyDuplicate,
            HotkeyRegistrationFailureKind.SystemConflict => _text.HotkeySystemConflict,
            _ => _text.HotkeyRegisterFailed(failure.ErrorCode)
        };
    }

    private void ToggleStartup(object? sender, EventArgs e)
    {
        try
        {
            var enabled = _startupItem.Checked;
            _startupManager.SetEnabled(enabled);
            ShowStatus(enabled
                ? _text.StartupEnabled
                : _text.StartupDisabled);
        }
        catch (Exception ex)
        {
            _startupItem.Checked = _startupManager.IsEnabled();
            ShowStatus(_text.ChangeStartupFailed(LocalizeError(ex)), ToolTipIcon.Warning);
        }
    }

    private void ToggleNotifications(object? sender, EventArgs e)
    {
        _settings.ShowNotifications = _notificationsItem.Checked;
        _settingsStore.Save(_settings);

        if (_settings.ShowNotifications)
        {
            ShowStatus(_text.NotificationsEnabled);
        }
    }

    private void ToggleMoveMinimizedWindows(object? sender, EventArgs e)
    {
        _settings.MoveMinimizedWindows = _moveMinimizedItem.Checked;
        _settingsStore.Save(_settings);
        ShowStatus(_settings.MoveMinimizedWindows
            ? _text.MinimizedWillMove
            : _text.MinimizedWillSkip);
    }

    private void TrackForegroundWindow(object? sender, EventArgs e)
    {
        try
        {
            _windowMover.ProcessPendingFullscreenTransfers();
            UpdateTrackerInterval();
        }
        catch (Exception ex)
        {
            UpdateTrackerInterval();
            DiagnosticLog.Info($"pending check failed {ex.GetType().Name}: {ex.Message}");
        }

        if (_windowMover.TryGetMovableForegroundWindow(out var trackedWindow))
        {
            RemoveHandleFromMonitorCache(trackedWindow.Handle);
            _lastTrackedWindow = trackedWindow;
            _lastWindowByMonitor[trackedWindow.ScreenDeviceName] = trackedWindow.Handle;
        }
    }

    private void UpdateTrackerInterval()
    {
        var targetInterval = _windowMover.HasPendingFullscreenTransfers
            ? PendingTrackerInterval
            : NormalTrackerInterval;

        if (_foregroundTracker.Interval != targetInterval)
        {
            _foregroundTracker.Interval = targetInterval;
        }
    }

    private TrackedWindow GetLastWindowOnOtherMonitor()
    {
        if (_lastTrackedWindow.Handle == IntPtr.Zero)
        {
            return default;
        }

        foreach (var entry in _lastWindowByMonitor)
        {
            if (!string.Equals(entry.Key, _lastTrackedWindow.ScreenDeviceName, StringComparison.Ordinal))
            {
                return new TrackedWindow(entry.Value, entry.Key);
            }
        }

        return default;
    }

    private void RememberSwap(TrackedWindow otherMonitorWindow)
    {
        if (_lastTrackedWindow.Handle == IntPtr.Zero || otherMonitorWindow.Handle == IntPtr.Zero)
        {
            return;
        }

        var previousActive = _lastTrackedWindow;
        RemoveHandleFromMonitorCache(previousActive.Handle);
        RemoveHandleFromMonitorCache(otherMonitorWindow.Handle);

        _lastWindowByMonitor[previousActive.ScreenDeviceName] = otherMonitorWindow.Handle;
        _lastWindowByMonitor[otherMonitorWindow.ScreenDeviceName] = previousActive.Handle;
        _lastTrackedWindow = new TrackedWindow(previousActive.Handle, otherMonitorWindow.ScreenDeviceName);
    }

    private void RemoveHandleFromMonitorCache(IntPtr handle)
    {
        if (handle == IntPtr.Zero || _lastWindowByMonitor.Count == 0)
        {
            return;
        }

        var keysToRemove = _lastWindowByMonitor
            .Where(entry => entry.Value == handle)
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var key in keysToRemove)
        {
            _lastWindowByMonitor.Remove(key);
        }
    }

    private void ShowStatus(string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (!_settings.ShowNotifications)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = _text.AppName;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(2500);
    }

    private string LocalizeError(Exception ex)
    {
        return ex.Message switch
        {
            "Не удалось определить открытое активное окно для обмена." => _text.CouldNotFindActiveWindow,
            "На другом мониторе нет подходящего открытого окна для обмена." => _text.NoWindowOnOtherMonitor,
            "Приложение работает только когда подключено ровно два монитора." => _text.TwoMonitorsRequired,
            "Не удалось открыть раздел автозапуска." => _text.StartupRegistryOpenFailed,
            _ => ex.Message
        };
    }
}
