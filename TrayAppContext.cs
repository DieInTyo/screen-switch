using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class TrayAppContext : ApplicationContext
{
    private const int NormalTrackerInterval = 250;
    private const int PendingTrackerInterval = 40;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly SettingsStore _settingsStore;
    private readonly StartupManager _startupManager;
    private readonly System.Windows.Forms.Timer _foregroundTracker;
    private readonly WindowMover _windowMover = new();
    private readonly HotkeyManager _hotkeyManager;
    private readonly AppSettings _settings;
    private readonly ToolStripMenuItem _moveActiveItem;
    private readonly ToolStripMenuItem _moveAllItem;
    private readonly ToolStripMenuItem _minimizeAllItem;
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
    private readonly ToolStripMenuItem _hotkeyMoveWindowItem;
    private readonly ToolStripMenuItem _hotkeyMinimizeAllWindowsItem;
    private readonly ToolStripMenuItem _hotkeyToggleOverlayItem;
    private readonly ToolStripMenuItem _resetHotkeysItem;
    private readonly ToolStripMenuItem _overlayMenu;
    private readonly ToolStripMenuItem _overlayEnabledItem;
    private readonly ToolStripMenuItem _overlayDraggableItem;
    private readonly ToolStripMenuItem _overlayOpacityItem;
    private readonly OpacitySliderControl _overlayOpacityTrackBar;
    private readonly ToolStripControlHost _overlayOpacityTrackBarHost;
    private readonly ToolStripMenuItem _overlayPositionMenu;
    private readonly ToolStripMenuItem _overlayTopLeftItem;
    private readonly ToolStripMenuItem _overlayTopRightItem;
    private readonly ToolStripMenuItem _overlayBottomLeftItem;
    private readonly ToolStripMenuItem _overlayBottomRightItem;
    private readonly ToolStripMenuItem _notificationsItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _themeMenu;
    private readonly ToolStripMenuItem _lightThemeItem;
    private readonly ToolStripMenuItem _darkThemeItem;
    private readonly ToolStripMenuItem _languageMenu;
    private readonly ToolStripMenuItem _englishLanguageItem;
    private readonly ToolStripMenuItem _russianLanguageItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly Dictionary<string, IntPtr> _lastWindowByMonitor = new(StringComparer.Ordinal);
    private readonly Dictionary<HotkeyAction, HotkeyRegistrationFailure> _hotkeyRegistrationErrors = new();
    private readonly HashSet<IntPtr> _selectedMoveWindowHandles = new();
    private LocalizedStrings _text;
    private TrackedWindow _lastTrackedWindow;
    private MoveWindowPickerForm? _moveWindowPicker;
    private OverlayForm? _overlayForm;
    private ContextMenuStrip? _overlaySettingsMenu;
    private bool _allowMenuCloseOnce;

    public TrayAppContext()
    {
        DiagnosticLog.Start();
        _settingsStore = new SettingsStore();
        _startupManager = new StartupManager();
        _settings = _settingsStore.Load();
        _text = LocalizedStrings.For(_settings.Language);

        _menu = new ContextMenuStrip
        {
            ShowItemToolTips = true
        };
        _menu.Closing += MenuOnClosing;
        _menu.Closed += (_, _) => _allowMenuCloseOnce = false;
        _moveActiveItem = new ToolStripMenuItem(string.Empty, null, (_, _) =>
        {
            AllowMenuClose();
            MoveActiveWindow();
        });
        _menu.Items.Add(_moveActiveItem);
        _moveAllItem = new ToolStripMenuItem(string.Empty, null, (_, _) =>
        {
            AllowMenuClose();
            MoveAllWindows();
        });
        _menu.Items.Add(_moveAllItem);
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
        _menu.Items.Add(_moveWindowMenu);
        _minimizeAllItem = new ToolStripMenuItem(string.Empty, null, (_, _) =>
        {
            AllowMenuClose();
            MinimizeAllWindows();
        });
        _menu.Items.Add(_minimizeAllItem);
        _menu.Items.Add(new ToolStripSeparator());

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
        _menu.Items.Add(_leftClickMenu);

        _hotkeysMenu = new ToolStripMenuItem();
        _hotkeysMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(_hotkeysMenu);
        _hotkeysMenu.DropDown.ShowItemToolTips = true;
        _hotkeysMenu.DropDown.Closing += MenuOnClosing;
        _hotkeysEnabledItem = new ToolStripMenuItem(string.Empty, null, ToggleHotkeys)
        {
            CheckOnClick = true,
            Checked = _settings.HotkeysEnabled
        };
        _hotkeySelectedModeItem = CreateHotkeyRootItem(HotkeyAction.SelectedMode);
        _hotkeyActiveWindowItem = CreateHotkeyRootItem(HotkeyAction.ActiveWindow);
        _hotkeyAllWindowsItem = CreateHotkeyRootItem(HotkeyAction.AllWindows);
        _hotkeyMoveWindowItem = CreateHotkeyRootItem(HotkeyAction.MoveWindow);
        _hotkeyMinimizeAllWindowsItem = CreateHotkeyRootItem(HotkeyAction.MinimizeAllWindows);
        _hotkeyToggleOverlayItem = CreateHotkeyRootItem(HotkeyAction.ToggleOverlay);
        _resetHotkeysItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ResetHotkeys());
        _hotkeysMenu.DropDownItems.Add(_hotkeysEnabledItem);
        _hotkeysMenu.DropDownItems.Add(new ToolStripSeparator());
        _hotkeysMenu.DropDownItems.Add(_hotkeySelectedModeItem);
        _hotkeysMenu.DropDownItems.Add(_hotkeyActiveWindowItem);
        _hotkeysMenu.DropDownItems.Add(_hotkeyAllWindowsItem);
        _hotkeysMenu.DropDownItems.Add(_hotkeyMoveWindowItem);
        _hotkeysMenu.DropDownItems.Add(_hotkeyMinimizeAllWindowsItem);
        _hotkeysMenu.DropDownItems.Add(_hotkeyToggleOverlayItem);
        _hotkeysMenu.DropDownItems.Add(new ToolStripSeparator());
        _hotkeysMenu.DropDownItems.Add(_resetHotkeysItem);
        _menu.Items.Add(_hotkeysMenu);

        _overlayMenu = new ToolStripMenuItem();
        _overlayMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(_overlayMenu);
        _overlayMenu.DropDown.Closing += MenuOnClosing;
        _overlayEnabledItem = new ToolStripMenuItem(string.Empty)
        {
            CheckOnClick = true,
            Checked = _settings.OverlayEnabled
        };
        _overlayEnabledItem.Click += (_, _) => SetOverlayEnabled(_overlayEnabledItem.Checked, showStatus: true);
        _overlayDraggableItem = new ToolStripMenuItem(string.Empty)
        {
            CheckOnClick = true,
            Checked = _settings.OverlayDraggable
        };
        _overlayDraggableItem.Click += (_, _) => SetOverlayDraggable(_overlayDraggableItem.Checked);
        _overlayOpacityItem = new ToolStripMenuItem(string.Empty)
        {
            Enabled = false
        };
        _overlayOpacityTrackBar = CreateOpacityTrackBar();
        _overlayOpacityTrackBarHost = CreateOpacitySliderHost(_overlayOpacityTrackBar);
        _overlayPositionMenu = new ToolStripMenuItem();
        _overlayPositionMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(_overlayPositionMenu);
        _overlayPositionMenu.DropDown.Closing += MenuOnClosing;
        _overlayTopLeftItem = CreateOverlayPositionItem(OverlayPosition.TopLeft);
        _overlayTopRightItem = CreateOverlayPositionItem(OverlayPosition.TopRight);
        _overlayBottomLeftItem = CreateOverlayPositionItem(OverlayPosition.BottomLeft);
        _overlayBottomRightItem = CreateOverlayPositionItem(OverlayPosition.BottomRight);
        _overlayPositionMenu.DropDownItems.Add(_overlayTopLeftItem);
        _overlayPositionMenu.DropDownItems.Add(_overlayTopRightItem);
        _overlayPositionMenu.DropDownItems.Add(_overlayBottomLeftItem);
        _overlayPositionMenu.DropDownItems.Add(_overlayBottomRightItem);
        _overlayMenu.DropDownItems.Add(_overlayEnabledItem);
        _overlayMenu.DropDownItems.Add(_overlayDraggableItem);
        _overlayMenu.DropDownItems.Add(_overlayOpacityItem);
        _overlayMenu.DropDownItems.Add(_overlayOpacityTrackBarHost);
        _overlayMenu.DropDownItems.Add(_overlayPositionMenu);
        _menu.Items.Add(_overlayMenu);

        _moveMinimizedItem = new ToolStripMenuItem(string.Empty, null, ToggleMoveMinimizedWindows)
        {
            CheckOnClick = true,
            Checked = _settings.MoveMinimizedWindows
        };
        _menu.Items.Add(_moveMinimizedItem);

        _notificationsItem = new ToolStripMenuItem(string.Empty, null, ToggleNotifications)
        {
            CheckOnClick = true,
            Checked = _settings.ShowNotifications
        };
        _menu.Items.Add(_notificationsItem);

        _startupItem = new ToolStripMenuItem(string.Empty, null, ToggleStartup)
        {
            CheckOnClick = true,
            Checked = _startupManager.IsEnabled()
        };
        _menu.Items.Add(_startupItem);

        _themeMenu = new ToolStripMenuItem();
        _themeMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(_themeMenu);
        _themeMenu.DropDown.Closing += MenuOnClosing;
        _lightThemeItem = new ToolStripMenuItem(string.Empty, null, (_, _) => SetTheme(AppTheme.Light));
        _darkThemeItem = new ToolStripMenuItem(string.Empty, null, (_, _) => SetTheme(AppTheme.Dark));
        _themeMenu.DropDownItems.Add(_lightThemeItem);
        _themeMenu.DropDownItems.Add(_darkThemeItem);
        _menu.Items.Add(_themeMenu);

        _languageMenu = new ToolStripMenuItem();
        _languageMenu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(_languageMenu);
        _languageMenu.DropDown.Closing += MenuOnClosing;
        _englishLanguageItem = new ToolStripMenuItem("English", null, (_, _) => SetLanguage(AppLanguage.English));
        _russianLanguageItem = new ToolStripMenuItem("Русский", null, (_, _) => SetLanguage(AppLanguage.Russian));
        _languageMenu.DropDownItems.Add(_englishLanguageItem);
        _languageMenu.DropDownItems.Add(_russianLanguageItem);
        _menu.Items.Add(_languageMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _exitItem = new ToolStripMenuItem(string.Empty, null, (_, _) =>
        {
            AllowMenuClose();
            ExitThread();
        });
        _menu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Screen Switch",
            Visible = true,
            ContextMenuStrip = _menu
        };

        ApplyLeftClickChecks();
        ApplyLanguageChecks();
        ApplyOverlayChecks();
        ApplyUiText();
        ApplyTheme();

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
        if (_settings.OverlayEnabled)
        {
            ShowOverlay(showStatus: false);
        }
    }

    protected override void ExitThreadCore()
    {
        _foregroundTracker.Stop();
        _foregroundTracker.Dispose();
        _hotkeyManager.Dispose();
        _overlayForm?.Close();
        _overlayForm?.Dispose();
        _moveWindowPicker?.Close();
        _moveWindowPicker?.Dispose();
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

    private void MinimizeAllWindows()
    {
        ExecuteMove(
            () => _windowMover.MinimizeAllWindows(),
            count => $"{_text.MinimizedWindowsPrefix}: {count}.");
    }

    private void ShowMoveWindowPicker()
    {
        if (_moveWindowPicker is { IsDisposed: false })
        {
            _moveWindowPicker.Activate();
            return;
        }

        DiagnosticLog.Info("action move-window-picker open");
        _moveWindowPicker = new MoveWindowPickerForm(
            _windowMover,
            _text,
            UiTheme.For(_settings.Theme),
            _settings.MoveWindowPickerView,
            view =>
            {
                _settings.MoveWindowPickerView = view;
                _settingsStore.Save(_settings);
            },
            MoveActiveWindow,
            MoveAllWindows,
            MinimizeAllWindows,
            movedCount =>
            {
                UpdateTrackerInterval();
                ShowStatus($"{_text.MovedWindowsPrefix}: {movedCount}.");
            },
            ex =>
            {
                UpdateTrackerInterval();
                ShowStatus(LocalizeError(ex), ToolTipIcon.Warning);
            });
        _moveWindowPicker.FormClosed += (_, _) => _moveWindowPicker = null;
        _moveWindowPicker.Show();
        _moveWindowPicker.Activate();
    }

    private void ShowOverlay(bool showStatus)
    {
        if (_overlayForm is { IsDisposed: false })
        {
            _overlayForm.Place(_settings.OverlayPosition, _settings.OverlayCustomLocation, _settings.OverlayUseCustomLocation);
            _overlayForm.Activate();
            return;
        }

        DiagnosticLog.Info("overlay shown");
        _overlayForm = new OverlayForm(
            _windowMover,
            _text,
            UiTheme.For(_settings.Theme),
            _settings.OverlayOpacity,
            _settings.OverlayDraggable,
            movedCount =>
            {
                UpdateTrackerInterval();
                ShowStatus($"{_text.MovedWindowsPrefix}: {movedCount}.");
            },
            ex =>
            {
                UpdateTrackerInterval();
                ShowStatus(LocalizeError(ex), ToolTipIcon.Warning);
            },
            ShowMoveWindowPicker,
            MoveActiveWindow,
            MoveAllWindows,
            MinimizeAllWindows,
            () => SetOverlayEnabled(false, showStatus: true),
            location =>
            {
                _settings.OverlayCustomLocation = new OverlayLocation
                {
                    X = location.X,
                    Y = location.Y
                };
                _settings.OverlayUseCustomLocation = true;
                _settingsStore.Save(_settings);
            },
            ShowOverlaySettingsMenu);
        _overlayForm.FormClosed += (_, _) => _overlayForm = null;
        _overlayForm.Show();
        _overlayForm.Place(_settings.OverlayPosition, _settings.OverlayCustomLocation, _settings.OverlayUseCustomLocation);

        if (showStatus)
        {
            ShowStatus(_text.OverlayShown);
        }
    }

    private void HideOverlay(bool showStatus)
    {
        DiagnosticLog.Info("overlay hidden");
        if (_overlayForm is { IsDisposed: false })
        {
            _overlayForm.Close();
        }

        if (showStatus)
        {
            ShowStatus(_text.OverlayHidden);
        }
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
            UiTheme.For(_settings.Theme).ApplyToMenu(_moveWindowMenu.DropDown);
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
        UiTheme.For(_settings.Theme).ApplyToMenu(_moveWindowMenu.DropDown);
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

    private ToolStripMenuItem CreateOverlayPositionItem(OverlayPosition position)
    {
        return new ToolStripMenuItem(string.Empty, null, (_, _) => SetOverlayPosition(position));
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

    private void ApplyOverlayChecks()
    {
        _overlayEnabledItem.Checked = _settings.OverlayEnabled;
        _overlayDraggableItem.Checked = _settings.OverlayDraggable;
        _overlayTopLeftItem.Checked = _settings.OverlayPosition == OverlayPosition.TopLeft;
        _overlayTopRightItem.Checked = _settings.OverlayPosition == OverlayPosition.TopRight;
        _overlayBottomLeftItem.Checked = _settings.OverlayPosition == OverlayPosition.BottomLeft;
        _overlayBottomRightItem.Checked = _settings.OverlayPosition == OverlayPosition.BottomRight;
    }

    private void ApplyThemeChecks()
    {
        _lightThemeItem.Checked = _settings.Theme == AppTheme.Light;
        _darkThemeItem.Checked = _settings.Theme == AppTheme.Dark;
    }

    private void ApplyUiText()
    {
        _moveActiveItem.Text = _text.MoveActiveWindow;
        _moveAllItem.Text = _text.MoveAllWindows;
        _minimizeAllItem.Text = _text.MinimizeAllWindows;
        _moveWindowMenu.Text = _text.MoveWindow;
        _leftClickMenu.Text = _text.LeftClick;
        _leftClickActiveItem.Text = _text.LeftClickActive;
        _leftClickAllItem.Text = _text.LeftClickAll;
        _hotkeysMenu.Text = _text.Hotkeys;
        _hotkeysEnabledItem.Text = _text.EnableHotkeys;
        _resetHotkeysItem.Text = _text.ResetHotkeys;
        _overlayMenu.Text = _text.Overlay;
        _overlayEnabledItem.Text = _text.ShowOverlay;
        _overlayDraggableItem.Text = _text.OverlayDraggable;
        _overlayOpacityItem.Text = _text.OverlayOpacityValue(_settings.OverlayOpacity);
        _overlayPositionMenu.Text = _text.OverlayPosition;
        _overlayTopLeftItem.Text = _text.OverlayTopLeft;
        _overlayTopRightItem.Text = _text.OverlayTopRight;
        _overlayBottomLeftItem.Text = _text.OverlayBottomLeft;
        _overlayBottomRightItem.Text = _text.OverlayBottomRight;
        _moveMinimizedItem.Text = _text.MoveMinimizedWindows;
        _notificationsItem.Text = _text.ShowNotifications;
        _startupItem.Text = _text.StartWithWindows;
        _themeMenu.Text = _text.Theme;
        _lightThemeItem.Text = _text.ThemeLight;
        _darkThemeItem.Text = _text.ThemeDark;
        _languageMenu.Text = _text.LanguageMenu;
        _exitItem.Text = _text.Exit;
        _overlayForm?.SetText(_text);
        ApplyOverlayChecks();
        ApplyThemeChecks();
        UpdateHotkeyMenuText();
    }

    private void ApplyTheme()
    {
        var theme = UiTheme.For(_settings.Theme);
        theme.ApplyToMenu(_menu);
        if (_overlaySettingsMenu is { IsDisposed: false } settingsMenu)
        {
            theme.ApplyToMenu(settingsMenu);
        }

        _overlayForm?.ApplyTheme(theme);
        StyleOpacityTrackBar(_overlayOpacityTrackBar, theme);

        ApplyThemeChecks();
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
        ApplyTheme();
        UpdateHotkeyMenuText();
        _moveWindowPicker?.Close();
        UpdateOpenOverlaySettingsMenuTextSoon();
        ShowStatus(_text.LanguageChanged);
    }

    private void UpdateOpenOverlaySettingsMenuTextSoon()
    {
        if (_overlaySettingsMenu is not { IsDisposed: false } settingsMenu)
        {
            return;
        }

        _menu.BeginInvoke(new Action(() =>
        {
            if (settingsMenu.IsDisposed)
            {
                return;
            }

            UpdateOpenOverlaySettingsMenuText(settingsMenu);
        }));
    }

    private void UpdateOpenOverlaySettingsMenuText(ContextMenuStrip settingsMenu)
    {
        if (TryGetMenuItem(settingsMenu.Items, 0, out var overlayEnabledItem))
        {
            overlayEnabledItem.Text = _text.ShowOverlay;
            overlayEnabledItem.Checked = _settings.OverlayEnabled;
        }

        if (TryGetMenuItem(settingsMenu.Items, 1, out var overlayDraggableItem))
        {
            overlayDraggableItem.Text = _text.OverlayDraggable;
            overlayDraggableItem.Checked = _settings.OverlayDraggable;
        }

        if (TryGetMenuItem(settingsMenu.Items, 2, out var opacityItem))
        {
            opacityItem.Text = _text.OverlayOpacityValue(_settings.OverlayOpacity);
        }

        if (TryGetMenuItem(settingsMenu.Items, 4, out var positionMenu))
        {
            positionMenu.Text = _text.OverlayPosition;
            UpdateRadioItem(positionMenu.DropDownItems, 0, _text.OverlayTopLeft, _settings.OverlayPosition == OverlayPosition.TopLeft);
            UpdateRadioItem(positionMenu.DropDownItems, 1, _text.OverlayTopRight, _settings.OverlayPosition == OverlayPosition.TopRight);
            UpdateRadioItem(positionMenu.DropDownItems, 2, _text.OverlayBottomLeft, _settings.OverlayPosition == OverlayPosition.BottomLeft);
            UpdateRadioItem(positionMenu.DropDownItems, 3, _text.OverlayBottomRight, _settings.OverlayPosition == OverlayPosition.BottomRight);
        }

        if (TryGetMenuItem(settingsMenu.Items, 5, out var themeMenu))
        {
            themeMenu.Text = _text.Theme;
            UpdateRadioItem(themeMenu.DropDownItems, 0, _text.ThemeLight, _settings.Theme == AppTheme.Light);
            UpdateRadioItem(themeMenu.DropDownItems, 1, _text.ThemeDark, _settings.Theme == AppTheme.Dark);
        }

        if (TryGetMenuItem(settingsMenu.Items, 7, out var hotkeysMenu))
        {
            hotkeysMenu.Text = _text.Hotkeys;
            if (TryGetMenuItem(hotkeysMenu.DropDownItems, 0, out var enableHotkeysItem))
            {
                enableHotkeysItem.Text = _text.EnableHotkeys;
                enableHotkeysItem.Checked = _settings.HotkeysEnabled;
            }

            UpdateNestedHotkeyItem(hotkeysMenu, 2, _text.SelectedMode, _settings.SelectedModeHotkey, HotkeyAction.SelectedMode);
            UpdateNestedHotkeyItem(hotkeysMenu, 3, _text.ActiveWindow, _settings.ActiveWindowHotkey, HotkeyAction.ActiveWindow);
            UpdateNestedHotkeyItem(hotkeysMenu, 4, _text.AllWindows, _settings.AllWindowsHotkey, HotkeyAction.AllWindows);
            UpdateNestedHotkeyItem(hotkeysMenu, 5, _text.MoveWindow, _settings.MoveWindowHotkey, HotkeyAction.MoveWindow);
            UpdateNestedHotkeyItem(hotkeysMenu, 6, _text.MinimizeAllWindows, _settings.MinimizeAllWindowsHotkey, HotkeyAction.MinimizeAllWindows);
            UpdateNestedHotkeyItem(hotkeysMenu, 7, _text.Overlay, _settings.ToggleOverlayHotkey, HotkeyAction.ToggleOverlay);
            if (TryGetMenuItem(hotkeysMenu.DropDownItems, 9, out var resetHotkeysItem))
            {
                resetHotkeysItem.Text = _text.ResetHotkeys;
            }
        }

        if (TryGetMenuItem(settingsMenu.Items, 8, out var moveMinimizedItem))
        {
            moveMinimizedItem.Text = _text.MoveMinimizedWindows;
            moveMinimizedItem.Checked = _settings.MoveMinimizedWindows;
        }

        if (TryGetMenuItem(settingsMenu.Items, 9, out var notificationsItem))
        {
            notificationsItem.Text = _text.ShowNotifications;
            notificationsItem.Checked = _settings.ShowNotifications;
        }

        if (TryGetMenuItem(settingsMenu.Items, 10, out var startupItem))
        {
            startupItem.Text = _text.StartWithWindows;
            startupItem.Checked = _startupManager.IsEnabled();
        }

        if (TryGetMenuItem(settingsMenu.Items, 11, out var languageMenu))
        {
            languageMenu.Text = _text.LanguageMenu;
            UpdateRadioItem(languageMenu.DropDownItems, 0, "English", _settings.Language == AppLanguage.English);
            UpdateRadioItem(languageMenu.DropDownItems, 1, "Русский", _settings.Language == AppLanguage.Russian);
        }

        if (TryGetMenuItem(settingsMenu.Items, 13, out var exitItem))
        {
            exitItem.Text = _text.Exit;
        }

        UiTheme.For(_settings.Theme).ApplyToMenu(settingsMenu);
    }

    private void UpdateNestedHotkeyItem(
        ToolStripMenuItem parent,
        int index,
        string label,
        HotkeyGesture? gesture,
        HotkeyAction action)
    {
        if (TryGetMenuItem(parent.DropDownItems, index, out var item))
        {
            UpdateHotkeyMenuItem(item, label, gesture, action);
        }
    }

    private static void UpdateRadioItem(ToolStripItemCollection items, int index, string text, bool isChecked)
    {
        if (TryGetMenuItem(items, index, out var item))
        {
            item.Text = text;
            item.Checked = isChecked;
        }
    }

    private static bool TryGetMenuItem(ToolStripItemCollection items, int index, out ToolStripMenuItem item)
    {
        if (index >= 0 && index < items.Count && items[index] is ToolStripMenuItem menuItem)
        {
            item = menuItem;
            return true;
        }

        item = null!;
        return false;
    }

    private void SetOverlayEnabled(bool enabled, bool showStatus)
    {
        _settings.OverlayEnabled = enabled;
        _settingsStore.Save(_settings);
        ApplyOverlayChecks();

        if (enabled)
        {
            ShowOverlay(showStatus);
        }
        else
        {
            HideOverlay(showStatus);
        }

        DiagnosticLog.Info($"overlay toggled enabled={enabled}");
    }

    private void ToggleOverlay()
    {
        SetOverlayEnabled(!_settings.OverlayEnabled, showStatus: true);
    }

    private void SetOverlayDraggable(bool draggable)
    {
        _settings.OverlayDraggable = draggable;
        _settingsStore.Save(_settings);
        ApplyOverlayChecks();
        _overlayForm?.SetDraggable(draggable);
        DiagnosticLog.Info($"overlay draggable changed enabled={draggable}");
    }

    private void SetOverlayPosition(OverlayPosition position)
    {
        _settings.OverlayPosition = position;
        _settings.OverlayUseCustomLocation = false;
        _settingsStore.Save(_settings);
        ApplyOverlayChecks();
        _overlayForm?.Place(_settings.OverlayPosition, _settings.OverlayCustomLocation, useCustomLocation: false);
        DiagnosticLog.Info($"overlay position changed position={position}");
    }

    private void SetOverlayOpacity(int opacity)
    {
        opacity = NormalizeOverlayOpacity(opacity);
        _settings.OverlayOpacity = opacity;
        _settingsStore.Save(_settings);
        _overlayOpacityItem.Text = _text.OverlayOpacityValue(opacity);
        if (_overlayOpacityTrackBar.Value != opacity)
        {
            _overlayOpacityTrackBar.Value = opacity;
        }

        _overlayForm?.SetOpacityPercent(opacity);
        DiagnosticLog.Info($"overlay opacity changed value={opacity}");
    }

    private OpacitySliderControl CreateOpacityTrackBar()
    {
        var slider = new OpacitySliderControl
        {
            Value = NormalizeOverlayOpacity(_settings.OverlayOpacity),
            Width = 126,
            Height = 14
        };
        slider.ValueChanged += (_, _) =>
        {
            var value = NormalizeOverlayOpacity(slider.Value);
            if (slider.Value != value)
            {
                slider.Value = value;
                return;
            }

            SetOverlayOpacity(value);
        };
        slider.ApplyTheme(UiTheme.For(_settings.Theme));
        return slider;
    }

    private static void StyleOpacityTrackBar(OpacitySliderControl slider, UiTheme theme)
    {
        slider.ApplyTheme(theme);
    }

    private ToolStripControlHost CreateOpacitySliderHost(OpacitySliderControl? slider = null)
    {
        slider ??= CreateOpacityTrackBar();
        return new ToolStripControlHost(slider)
        {
            AutoSize = false,
            Margin = new Padding(8, 0, 8, 0),
            Size = new Size(132, 14)
        };
    }

    private void ShowOverlaySettingsMenu(Control anchor)
    {
        var settingsMenu = new ContextMenuStrip
        {
            ShowItemToolTips = true
        };
        settingsMenu.Closing += MenuOnClosing;
        settingsMenu.Closed += (_, _) =>
        {
            _allowMenuCloseOnce = false;
            if (ReferenceEquals(_overlaySettingsMenu, settingsMenu))
            {
                _overlaySettingsMenu = null;
            }
        };

        _overlaySettingsMenu = settingsMenu;
        PopulateOverlaySettingsMenu(settingsMenu);
        ShowMenuNearControl(settingsMenu, anchor);
    }

    private void PopulateOverlaySettingsMenu(ContextMenuStrip settingsMenu)
    {
        settingsMenu.Items.Clear();
        var overlayEnabledItem = CreateCheckItem(_text.ShowOverlay, _settings.OverlayEnabled, item => SetOverlayEnabled(item.Checked, showStatus: true));
        var overlayDraggableItem = CreateCheckItem(_text.OverlayDraggable, _settings.OverlayDraggable, item => SetOverlayDraggable(item.Checked));
        var opacityItem = new ToolStripMenuItem(_text.OverlayOpacityValue(_settings.OverlayOpacity))
        {
            Enabled = false
        };
        var opacityTrackBar = CreateOpacityTrackBar();
        opacityTrackBar.ValueChanged += (_, _) =>
        {
            opacityItem.Text = _text.OverlayOpacityValue(NormalizeOverlayOpacity(opacityTrackBar.Value));
        };
        var positionMenu = CreateOverlayPositionMenu();
        var themeMenu = CreateThemeMenu(settingsMenu);

        settingsMenu.Items.Add(overlayEnabledItem);
        settingsMenu.Items.Add(overlayDraggableItem);
        settingsMenu.Items.Add(opacityItem);
        settingsMenu.Items.Add(CreateOpacitySliderHost(opacityTrackBar));
        settingsMenu.Items.Add(positionMenu);
        settingsMenu.Items.Add(themeMenu);
        settingsMenu.Items.Add(new ToolStripSeparator());
        settingsMenu.Items.Add(CreateHotkeysMenu());
        settingsMenu.Items.Add(CreateCheckItem(_text.MoveMinimizedWindows, _settings.MoveMinimizedWindows, item =>
        {
            _moveMinimizedItem.Checked = item.Checked;
            ToggleMoveMinimizedWindows(null, EventArgs.Empty);
        }));
        settingsMenu.Items.Add(CreateCheckItem(_text.ShowNotifications, _settings.ShowNotifications, item =>
        {
            _notificationsItem.Checked = item.Checked;
            ToggleNotifications(null, EventArgs.Empty);
        }));
        settingsMenu.Items.Add(CreateCheckItem(_text.StartWithWindows, _startupManager.IsEnabled(), item =>
        {
            _startupItem.Checked = item.Checked;
            ToggleStartup(null, EventArgs.Empty);
        }));
        settingsMenu.Items.Add(CreateLanguageMenu());
        settingsMenu.Items.Add(new ToolStripSeparator());
        settingsMenu.Items.Add(new ToolStripMenuItem(_text.Exit, null, (_, _) =>
        {
            AllowMenuClose();
            ExitThread();
        }));

        UiTheme.For(_settings.Theme).ApplyToMenu(settingsMenu);
    }

    private static void ShowMenuNearControl(ContextMenuStrip menu, Control anchor)
    {
        var area = Screen.FromControl(anchor).WorkingArea;
        var preferred = menu.GetPreferredSize(Size.Empty);
        const int gap = 5;
        var below = anchor.PointToScreen(new Point(0, anchor.Height + gap));
        var x = below.X;
        var y = below.Y;

        if (x + preferred.Width > area.Right)
        {
            x = anchor.PointToScreen(new Point(anchor.Width, 0)).X - preferred.Width - gap;
        }

        if (y + preferred.Height > area.Bottom)
        {
            y = anchor.PointToScreen(Point.Empty).Y - preferred.Height - gap;
        }

        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - preferred.Width));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - preferred.Height));
        menu.Show(new Point(x, y));
    }

    private ToolStripMenuItem CreateCheckItem(string text, bool isChecked, Action<ToolStripMenuItem> onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            CheckOnClick = true,
            Checked = isChecked
        };
        item.Click += (_, _) => onClick(item);
        return item;
    }

    private static ToolStripMenuItem CreateRadioItem(string text, bool isChecked, Action<ToolStripMenuItem> onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Checked = isChecked
        };
        item.Click += (_, _) =>
        {
            CheckOnlySiblingItems(item);
            onClick(item);
        };
        return item;
    }

    private static void CheckOnlySiblingItems(ToolStripMenuItem selectedItem)
    {
        if (selectedItem.Owner is null)
        {
            selectedItem.Checked = true;
            return;
        }

        foreach (var item in selectedItem.Owner.Items.OfType<ToolStripMenuItem>())
        {
            item.Checked = ReferenceEquals(item, selectedItem);
        }
    }

    private ToolStripMenuItem CreateOverlayPositionMenu()
    {
        var menu = new ToolStripMenuItem(_text.OverlayPosition);
        menu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(menu);
        menu.DropDown.Closing += MenuOnClosing;
        menu.DropDownItems.Add(CreateOverlaySettingsPositionItem(_text.OverlayTopLeft, OverlayPosition.TopLeft));
        menu.DropDownItems.Add(CreateOverlaySettingsPositionItem(_text.OverlayTopRight, OverlayPosition.TopRight));
        menu.DropDownItems.Add(CreateOverlaySettingsPositionItem(_text.OverlayBottomLeft, OverlayPosition.BottomLeft));
        menu.DropDownItems.Add(CreateOverlaySettingsPositionItem(_text.OverlayBottomRight, OverlayPosition.BottomRight));
        return menu;
    }

    private ToolStripMenuItem CreateOverlaySettingsPositionItem(string text, OverlayPosition position)
    {
        return CreateRadioItem(text, _settings.OverlayPosition == position, _ => SetOverlayPosition(position));
    }

    private ToolStripMenuItem CreateThemeMenu(ContextMenuStrip parentMenu)
    {
        var menu = new ToolStripMenuItem(_text.Theme);
        menu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(menu);
        menu.DropDown.Closing += MenuOnClosing;
        var lightItem = CreateRadioItem(_text.ThemeLight, _settings.Theme == AppTheme.Light, _ =>
        {
            SetTheme(AppTheme.Light);
            UiTheme.For(_settings.Theme).ApplyToMenu(parentMenu);
        });
        var darkItem = CreateRadioItem(_text.ThemeDark, _settings.Theme == AppTheme.Dark, _ =>
        {
            SetTheme(AppTheme.Dark);
            UiTheme.For(_settings.Theme).ApplyToMenu(parentMenu);
        });

        menu.DropDownItems.Add(lightItem);
        menu.DropDownItems.Add(darkItem);
        return menu;
    }

    private ToolStripMenuItem CreateHotkeysMenu()
    {
        var menu = new ToolStripMenuItem(_text.Hotkeys);
        menu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(menu);
        menu.DropDown.ShowItemToolTips = true;
        menu.DropDown.Closing += MenuOnClosing;
        menu.DropDownItems.Add(CreateCheckItem(_text.EnableHotkeys, _settings.HotkeysEnabled, item => SetHotkeysEnabled(item.Checked, showStatus: true)));
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(CreateHotkeyMenuItem(_text.SelectedMode, _settings.SelectedModeHotkey, HotkeyAction.SelectedMode));
        menu.DropDownItems.Add(CreateHotkeyMenuItem(_text.ActiveWindow, _settings.ActiveWindowHotkey, HotkeyAction.ActiveWindow));
        menu.DropDownItems.Add(CreateHotkeyMenuItem(_text.AllWindows, _settings.AllWindowsHotkey, HotkeyAction.AllWindows));
        menu.DropDownItems.Add(CreateHotkeyMenuItem(_text.MoveWindow, _settings.MoveWindowHotkey, HotkeyAction.MoveWindow));
        menu.DropDownItems.Add(CreateHotkeyMenuItem(_text.MinimizeAllWindows, _settings.MinimizeAllWindowsHotkey, HotkeyAction.MinimizeAllWindows));
        menu.DropDownItems.Add(CreateHotkeyMenuItem(_text.Overlay, _settings.ToggleOverlayHotkey, HotkeyAction.ToggleOverlay));
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(new ToolStripMenuItem(_text.ResetHotkeys, null, (_, _) => ResetHotkeys()));
        return menu;
    }

    private ToolStripMenuItem CreateHotkeyRootItem(HotkeyAction action)
    {
        return new ToolStripMenuItem(string.Empty, null, (_, _) => CaptureHotkey(action, Cursor.Position));
    }

    private ToolStripMenuItem CreateHotkeyMenuItem(string label, HotkeyGesture? gesture, HotkeyAction action)
    {
        var item = new ToolStripMenuItem(label, null, (_, _) => CaptureHotkey(action, Cursor.Position))
        {
            ShowShortcutKeys = true,
            ShortcutKeyDisplayString = FormatHotkey(gesture),
            ToolTipText = _text.AssignHotkeyTooltip
        };
        if (_hotkeyRegistrationErrors.TryGetValue(action, out var failure))
        {
            item.ForeColor = _settings.Theme == AppTheme.Dark ? Color.IndianRed : Color.Firebrick;
            item.ToolTipText = GetHotkeyFailureMessage(failure);
        }

        return item;
    }

    private ToolStripMenuItem CreateLanguageMenu()
    {
        var menu = new ToolStripMenuItem(_text.LanguageMenu);
        menu.DropDownOpening += (_, _) => ApplyMonitorAwareDropDownDirection(menu);
        menu.DropDown.Closing += MenuOnClosing;
        menu.DropDownItems.Add(CreateRadioItem("English", _settings.Language == AppLanguage.English, _ => SetLanguage(AppLanguage.English)));
        menu.DropDownItems.Add(CreateRadioItem("Русский", _settings.Language == AppLanguage.Russian, _ => SetLanguage(AppLanguage.Russian)));
        return menu;
    }

    private static int NormalizeOverlayOpacity(int opacity)
    {
        var clamped = Math.Clamp(opacity, 50, 100);
        return (int)(Math.Round(clamped / 5.0, MidpointRounding.AwayFromZero) * 5);
    }

    private void SetTheme(AppTheme theme)
    {
        if (_settings.Theme == theme)
        {
            ApplyThemeChecks();
            return;
        }

        _settings.Theme = theme;
        _settingsStore.Save(_settings);
        _moveWindowPicker?.Close();
        ApplyTheme();
        UpdateHotkeyMenuText();
        DiagnosticLog.Info($"theme changed theme={theme}");
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
            case HotkeyAction.MoveWindow:
                ShowMoveWindowPicker();
                break;
            case HotkeyAction.MinimizeAllWindows:
                MinimizeAllWindows();
                break;
            case HotkeyAction.ToggleOverlay:
                ToggleOverlay();
                break;
        }
    }

    private void ToggleHotkeys(object? sender, EventArgs e)
    {
        SetHotkeysEnabled(_hotkeysEnabledItem.Checked, showStatus: true);
    }

    private void SetHotkeysEnabled(bool enabled, bool showStatus)
    {
        _settings.HotkeysEnabled = enabled;
        _hotkeysEnabledItem.Checked = enabled;
        _settingsStore.Save(_settings);
        ApplyHotkeyRegistrations(showFailures: true);
        if (showStatus)
        {
            ShowStatus(_settings.HotkeysEnabled
                ? _text.HotkeysEnabled
                : _text.HotkeysDisabled);
        }
    }

    private void CaptureHotkey(HotkeyAction action, Point? preferredLocation = null)
    {
        _hotkeyManager.UnregisterAll();
        try
        {
            using var dialog = new HotkeyCaptureForm(
                GetHotkeyActionName(action),
                GetHotkeyGesture(action),
                _text,
                UiTheme.For(_settings.Theme),
                preferredLocation);
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
        _settings.MoveWindowHotkey = null;
        _settings.MinimizeAllWindowsHotkey = null;
        _settings.ToggleOverlayHotkey = null;
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
        UpdateHotkeyMenuItem(_hotkeyMoveWindowItem, _text.MoveWindow, _settings.MoveWindowHotkey, HotkeyAction.MoveWindow);
        UpdateHotkeyMenuItem(_hotkeyMinimizeAllWindowsItem, _text.MinimizeAllWindows, _settings.MinimizeAllWindowsHotkey, HotkeyAction.MinimizeAllWindows);
        UpdateHotkeyMenuItem(_hotkeyToggleOverlayItem, _text.Overlay, _settings.ToggleOverlayHotkey, HotkeyAction.ToggleOverlay);
    }

    private void UpdateHotkeyMenuItem(
        ToolStripMenuItem item,
        string label,
        HotkeyGesture? gesture,
        HotkeyAction action)
    {
        item.Text = label;
        item.ShowShortcutKeys = true;
        item.ShortcutKeyDisplayString = FormatHotkey(gesture);
        if (_hotkeyRegistrationErrors.TryGetValue(action, out var failure))
        {
            item.ForeColor = _settings.Theme == AppTheme.Dark ? Color.IndianRed : Color.Firebrick;
            item.ToolTipText = GetHotkeyFailureMessage(failure);
            return;
        }

        item.ForeColor = UiTheme.For(_settings.Theme).Foreground;
        item.ToolTipText = _text.AssignHotkeyTooltip;
    }

    private HotkeyGesture? GetHotkeyGesture(HotkeyAction action)
    {
        return action switch
        {
            HotkeyAction.SelectedMode => _settings.SelectedModeHotkey?.Clone(),
            HotkeyAction.ActiveWindow => _settings.ActiveWindowHotkey?.Clone(),
            HotkeyAction.AllWindows => _settings.AllWindowsHotkey?.Clone(),
            HotkeyAction.MoveWindow => _settings.MoveWindowHotkey?.Clone(),
            HotkeyAction.MinimizeAllWindows => _settings.MinimizeAllWindowsHotkey?.Clone(),
            HotkeyAction.ToggleOverlay => _settings.ToggleOverlayHotkey?.Clone(),
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
            case HotkeyAction.MoveWindow:
                _settings.MoveWindowHotkey = gesture;
                break;
            case HotkeyAction.MinimizeAllWindows:
                _settings.MinimizeAllWindowsHotkey = gesture;
                break;
            case HotkeyAction.ToggleOverlay:
                _settings.ToggleOverlayHotkey = gesture;
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
            HotkeyAction.MoveWindow => _text.MoveWindow,
            HotkeyAction.MinimizeAllWindows => _text.MinimizeAllWindows,
            HotkeyAction.ToggleOverlay => _text.Overlay,
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
