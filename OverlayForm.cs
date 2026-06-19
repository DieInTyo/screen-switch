using System.Drawing;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class OverlayForm : Form
{
    private const int MaxRowsPerZone = 2;
    private const int TileSize = 30;
    private const int IconSize = 22;
    private const int TileGap = 4;
    private const int TilesPerZone = 4;
    private const int ZoneWidth = TilesPerZone * (TileSize + TileGap);
    private const int OuterPadding = 4;
    private const int DividerWidth = 24;
    private const int OverlayWidth = ZoneWidth * 2 + DividerWidth + OuterPadding * 2;
    private const int TaskbarSafetyGap = 8;
    private const int TopSafetyGap = 34;

    private readonly WindowMover _windowMover;
    private readonly Action<int> _onMoved;
    private readonly Action<Exception> _onError;
    private readonly Action _openWindowPicker;
    private readonly Action _moveActiveWindow;
    private readonly Action _moveAllWindows;
    private readonly Action _minimizeAllWindows;
    private readonly Action _hideOverlay;
    private readonly Action<Point> _customLocationChanged;
    private readonly Action<Control> _showSettingsMenu;
    private readonly TableLayoutPanel _zonePanel;
    private readonly MonitorZone _leftZone;
    private readonly MonitorZone _rightZone;
    private readonly Panel _dividerPanel;
    private readonly FlowLayoutPanel _actionPanel;
    private readonly Panel _headerPanel;
    private readonly Panel _headerDividerLine;
    private readonly Button _settingsButton;
    private readonly Button _hideButton;
    private readonly MoveDirectionButton _moveSelectedButton;
    private readonly MonitorBadge _leftBadge;
    private readonly MonitorBadge _rightBadge;
    private readonly Button _activeButton;
    private readonly Button _allButton;
    private readonly Button _minimizeButton;
    private readonly ToolTip _toolTip = new()
    {
        Active = true,
        AutoPopDelay = 7000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true
    };
    private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<IntPtr, TileState> _tiles = new();
    private readonly Dictionary<string, Control> _moreTiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _appWindowCounts = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<IntPtr> _selectedHandles = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private Label? _emptyLabel;
    private LocalizedStrings _text;
    private UiTheme _theme;
    private bool _draggable;
    private bool _dragging;
    private bool _hasPlacement;
    private bool _useCustomLocation;
    private OverlayPosition _position = OverlayPosition.BottomRight;
    private OverlayLocation? _customLocation;
    private Point _dragStartCursor;
    private Point _dragStartLocation;
    private TileState? _pendingTileDrag;
    private Point _pendingTileDragStart;

    public OverlayForm(
        WindowMover windowMover,
        LocalizedStrings text,
        UiTheme theme,
        int opacity,
        bool draggable,
        Action<int> onMoved,
        Action<Exception> onError,
        Action openWindowPicker,
        Action moveActiveWindow,
        Action moveAllWindows,
        Action minimizeAllWindows,
        Action hideOverlay,
        Action<Point> customLocationChanged,
        Action<Control> showSettingsMenu)
    {
        _windowMover = windowMover;
        _text = text;
        _theme = theme;
        _draggable = draggable;
        _onMoved = onMoved;
        _onError = onError;
        _openWindowPicker = openWindowPicker;
        _moveActiveWindow = moveActiveWindow;
        _moveAllWindows = moveAllWindows;
        _minimizeAllWindows = minimizeAllWindows;
        _hideOverlay = hideOverlay;
        _customLocationChanged = customLocationChanged;
        _showSettingsMenu = showSettingsMenu;

        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        Opacity = NormalizeOpacity(opacity);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;

        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(OuterPadding, 1, 2, 1)
        };
        AttachDragHandlers(_headerPanel);

        _headerDividerLine = new Panel
        {
            Width = 1
        };
        _headerPanel.Controls.Add(_headerDividerLine);

        _leftBadge = new MonitorBadge
        {
            Size = new Size(15, 15),
            Text = "1"
        };
        _rightBadge = new MonitorBadge
        {
            Size = new Size(15, 15),
            Text = "2"
        };
        _headerPanel.Controls.Add(_leftBadge);
        _headerPanel.Controls.Add(_rightBadge);
        _leftBadge.BringToFront();
        _rightBadge.BringToFront();

        _settingsButton = new Button
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Bold),
            Size = new Size(28, 20),
            Text = "\u22EE",
            TextAlign = ContentAlignment.MiddleCenter
        };
        _settingsButton.FlatAppearance.BorderSize = 0;
        _settingsButton.Click += (_, _) => _showSettingsMenu(_settingsButton);
        _headerPanel.Controls.Add(_settingsButton);

        _hideButton = new Button
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold),
            Size = new Size(28, 20),
            Text = "-",
            TextAlign = ContentAlignment.MiddleCenter
        };
        _hideButton.FlatAppearance.BorderSize = 0;
        _hideButton.Click += (_, _) => _hideOverlay();
        _headerPanel.Controls.Add(_hideButton);

        _leftZone = new MonitorZone();
        _rightZone = new MonitorZone();
        _leftZone.ClearRequested = ClearSelection;
        _rightZone.ClearRequested = ClearSelection;
        ConfigureZoneDrop(_leftZone);
        ConfigureZoneDrop(_rightZone);

        _dividerPanel = new DoubleBufferedPanel
        {
            Margin = Padding.Empty,
            Width = DividerWidth
        };
        _dividerPanel.Paint += (_, e) =>
        {
            var x = _dividerPanel.Width / 2;
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, x, 0, x, Math.Max(1, _dividerPanel.Height));
        };
        _moveSelectedButton = CreateCenterMoveButton();
        _dividerPanel.Controls.Add(_moveSelectedButton);

        _zonePanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = new Padding(OuterPadding, 0, OuterPadding, 1),
            RowCount = 1
        };
        _zonePanel.MouseDown += (_, e) =>
        {
            if (_zonePanel.GetChildAtPoint(e.Location) is null)
            {
                ClearSelection();
            }
        };
        _zonePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _zonePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DividerWidth));
        _zonePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _zonePanel.Controls.Add(_leftZone.Container, 0, 0);
        _zonePanel.Controls.Add(_dividerPanel, 1, 0);
        _zonePanel.Controls.Add(_rightZone.Container, 2, 0);

        _actionPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(OuterPadding, 0, OuterPadding, 4),
            WrapContents = false
        };

        _activeButton = CreateActionButton(string.Empty, () => RunOverlayAction(_moveActiveWindow), width: 82);
        _allButton = CreateActionButton(string.Empty, () => RunOverlayAction(_moveAllWindows), width: 50);
        _minimizeButton = CreateActionButton(string.Empty, () => RunOverlayAction(_minimizeAllWindows), width: 94);
        _actionPanel.Controls.Add(_activeButton);
        _actionPanel.Controls.Add(_allButton);
        _actionPanel.Controls.Add(_minimizeButton);

        Controls.Add(_actionPanel);
        Controls.Add(_zonePanel);
        Controls.Add(_headerPanel);

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 500
        };
        _refreshTimer.Tick += (_, _) => RefreshTiles(forceRebuild: false);

        ApplyText();
        ApplyTheme(theme);
        RefreshTiles(forceRebuild: true);
        UpdateDraggableCursor();
        ResizeToContent();
        _refreshTimer.Start();
    }

    public void RefreshTiles(bool forceRebuild = false)
    {
        var screens = GetOrderedScreens();
        var leftScreen = screens.ElementAtOrDefault(0);
        var rightScreen = screens.ElementAtOrDefault(1);
        ConfigureZone(_leftZone, leftScreen, 1);
        ConfigureZone(_rightZone, rightScreen, 2);

        var windows = _windowMover
            .GetMovableWindows(includeMinimizedWindows: true)
            .OrderBy(window => window.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _selectedHandles.RemoveWhere(handle => !windows.Any(window => window.Handle == handle));
        var visibleWindows = GetVisibleOverlayWindows(windows, leftScreen?.DeviceName, rightScreen?.DeviceName);
        var visibleHandles = visibleWindows.Select(window => window.Handle).ToHashSet();

        _zonePanel.SuspendLayout();
        _leftZone.TilePanel.SuspendLayout();
        _rightZone.TilePanel.SuspendLayout();
        if (forceRebuild)
        {
            ClearZone(_leftZone);
            ClearZone(_rightZone);
            _tiles.Clear();
            _moreTiles.Clear();
            _appWindowCounts.Clear();
            _emptyLabel = null;
        }

        _appWindowCounts.Clear();
        foreach (var group in windows.GroupBy(window => window.AppName, StringComparer.CurrentCultureIgnoreCase))
        {
            _appWindowCounts[group.Key] = group.Count();
        }

        foreach (var staleHandle in _tiles.Keys.Where(handle => !visibleHandles.Contains(handle)).ToArray())
        {
            var tile = _tiles[staleHandle].Tile;
            tile.Parent?.Controls.Remove(tile);
            tile.Dispose();
            _tiles.Remove(staleHandle);
        }

        RebuildZoneTiles(_leftZone, visibleWindows.Where(window => window.ScreenDeviceName == leftScreen?.DeviceName).ToArray());
        RebuildZoneTiles(_rightZone, visibleWindows.Where(window => window.ScreenDeviceName == rightScreen?.DeviceName).ToArray());
        UpdateMoreTiles(windows, visibleWindows, leftScreen?.DeviceName, rightScreen?.DeviceName);
        UpdateEmptyLabel(windows.Length == 0);
        ResizeZones();

        _rightZone.TilePanel.ResumeLayout();
        _leftZone.TilePanel.ResumeLayout();
        _zonePanel.ResumeLayout();

        UpdateMoveSelectedState();
        ResizeToContent();
    }

    public void SetText(LocalizedStrings text)
    {
        _text = text;
        ApplyText();
        RefreshTiles(forceRebuild: true);
    }

    public void ApplyTheme(UiTheme theme)
    {
        _theme = theme;
        BackColor = theme.Background;
        ForeColor = theme.Foreground;
        _headerPanel.BackColor = theme.Background;
        _headerDividerLine.BackColor = theme.Border;
        _zonePanel.BackColor = theme.Background;
        _dividerPanel.BackColor = theme.Background;
        _actionPanel.BackColor = theme.Background;
        StyleZone(_leftZone);
        StyleZone(_rightZone);
        _leftBadge.ApplyTheme(theme);
        _rightBadge.ApplyTheme(theme);
        StyleHeaderButton(_settingsButton);
        StyleHeaderButton(_hideButton);
        _moveSelectedButton.ApplyTheme(theme);
        _dividerPanel.Invalidate();
        foreach (var button in _actionPanel.Controls.OfType<Button>())
        {
            StyleActionButton(button);
        }

        foreach (var state in _tiles.Values)
        {
            UpdateTileVisual(state);
        }

        foreach (var moreTile in _moreTiles.Values.OfType<Button>())
        {
            StyleActionButton(moreTile);
        }

        if (_emptyLabel is not null)
        {
            _emptyLabel.ForeColor = theme.MutedForeground;
            _emptyLabel.BackColor = theme.Background;
        }
    }

    public void SetOpacityPercent(int opacity)
    {
        Opacity = NormalizeOpacity(opacity);
    }

    public void SetDraggable(bool draggable)
    {
        _draggable = draggable;
        UpdateDraggableCursor();
        DiagnosticLog.Info($"overlay draggable={draggable}");
    }

    public void Place(OverlayPosition position, OverlayLocation? customLocation, bool useCustomLocation)
    {
        _position = position;
        _customLocation = customLocation;
        _useCustomLocation = useCustomLocation;
        _hasPlacement = true;

        var desiredSize = Size == Size.Empty ? new Size(OverlayWidth, 120) : Size;
        var location = useCustomLocation && customLocation is not null
            ? new Point(customLocation.X, customLocation.Y)
            : GetCornerLocation(position, desiredSize, GetPlacementArea());

        Location = ClampLocation(location, desiredSize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            foreach (var image in _iconCache.Values)
            {
                image.Dispose();
            }

            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ApplyText()
    {
        Text = _text.Overlay;
        _moveSelectedButton.Direction = MoveDirection.Both;
        _activeButton.Text = _text.OverlayActiveButton;
        _allButton.Text = _text.OverlayAllButton;
        _minimizeButton.Text = _text.OverlayMinimizeButton;
        _toolTip.SetToolTip(_moveSelectedButton, _text.MoveSelectedWindows);
        _toolTip.SetToolTip(_activeButton, _text.MoveActiveWindow);
        _toolTip.SetToolTip(_allButton, _text.MoveAllWindows);
        _toolTip.SetToolTip(_minimizeButton, _text.MinimizeAllWindows);
        _toolTip.SetToolTip(_settingsButton, _text.OverlaySettings);
        _toolTip.SetToolTip(_hideButton, _text.OverlayHide);
        _toolTip.SetToolTip(_leftBadge, "1");
        _toolTip.SetToolTip(_rightBadge, "2");
    }

    private MovableWindowInfo[] GetVisibleOverlayWindows(
        MovableWindowInfo[] windows,
        string? leftDeviceName,
        string? rightDeviceName)
    {
        return windows
            .GroupBy(window => window.ScreenDeviceName)
            .SelectMany(group =>
            {
                var capacity = GetZoneCapacity(group.Key == leftDeviceName ? _leftZone : _rightZone);
                return group.Count() > capacity ? group.Take(Math.Max(0, capacity - 1)) : group;
            })
            .ToArray();
    }

    private void RebuildZoneTiles(MonitorZone zone, MovableWindowInfo[] windows)
    {
        foreach (var window in windows)
        {
            if (!_tiles.TryGetValue(window.Handle, out var state))
            {
                state = CreateWindowTile(window);
                _tiles[window.Handle] = state;
            }

            if (!zone.TilePanel.Controls.Contains(state.Tile))
            {
                state.Tile.Parent?.Controls.Remove(state.Tile);
                zone.TilePanel.Controls.Add(state.Tile);
            }

            UpdateWindowTile(state, window);
            zone.TilePanel.Controls.SetChildIndex(state.Tile, Array.IndexOf(windows, window));
        }
    }

    private void UpdateMoreTiles(
        MovableWindowInfo[] allWindows,
        MovableWindowInfo[] visibleWindows,
        string? leftDeviceName,
        string? rightDeviceName)
    {
        UpdateMoreTile(_leftZone, allWindows, visibleWindows, leftDeviceName);
        UpdateMoreTile(_rightZone, allWindows, visibleWindows, rightDeviceName);
    }

    private void UpdateMoreTile(
        MonitorZone zone,
        MovableWindowInfo[] allWindows,
        MovableWindowInfo[] visibleWindows,
        string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            RemoveMoreTile(deviceName);
            return;
        }

        var total = allWindows.Count(window => window.ScreenDeviceName == deviceName);
        var visible = visibleWindows.Count(window => window.ScreenDeviceName == deviceName);
        if (total <= visible)
        {
            RemoveMoreTile(deviceName);
            return;
        }

        if (!_moreTiles.TryGetValue(deviceName, out var moreTile))
        {
            moreTile = CreateMoreTile();
            _moreTiles[deviceName] = moreTile;
        }

        if (!zone.TilePanel.Controls.Contains(moreTile))
        {
            moreTile.Parent?.Controls.Remove(moreTile);
            zone.TilePanel.Controls.Add(moreTile);
        }

        zone.TilePanel.Controls.SetChildIndex(moreTile, zone.TilePanel.Controls.Count - 1);
    }

    private void RemoveMoreTile(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName) || !_moreTiles.Remove(deviceName, out var moreTile))
        {
            return;
        }

        moreTile.Parent?.Controls.Remove(moreTile);
        moreTile.Dispose();
    }

    private void UpdateEmptyLabel(bool shouldShow)
    {
        if (!shouldShow)
        {
            if (_emptyLabel is not null)
            {
                _emptyLabel.Parent?.Controls.Remove(_emptyLabel);
                _emptyLabel.Dispose();
                _emptyLabel = null;
            }

            return;
        }

        _emptyLabel ??= new Label
        {
            AutoSize = true,
            Margin = new Padding(4),
            Text = _text.OverlayNoWindows
        };
        _emptyLabel.Text = _text.OverlayNoWindows;
        _emptyLabel.ForeColor = _theme.MutedForeground;
        _emptyLabel.BackColor = _theme.Background;
        if (!_leftZone.TilePanel.Controls.Contains(_emptyLabel))
        {
            _emptyLabel.Parent?.Controls.Remove(_emptyLabel);
            _leftZone.TilePanel.Controls.Add(_emptyLabel);
        }
    }

    private TileState CreateWindowTile(MovableWindowInfo window)
    {
        var tile = new Panel
        {
            Cursor = Cursors.Hand,
            Margin = new Padding(2),
            Size = new Size(TileSize, TileSize),
            Tag = window.Handle
        };
        tile.Paint += (_, e) => PaintTileBorder(tile, e);

        var icon = new PictureBox
        {
            Location = new Point((TileSize - IconSize) / 2, (TileSize - IconSize) / 2),
            Size = new Size(IconSize, IconSize),
            SizeMode = PictureBoxSizeMode.StretchImage
        };
        tile.Controls.Add(icon);

        var state = new TileState(tile, icon);
        AttachTileHandlers(tile, state);
        AttachTileHandlers(icon, state);
        return state;
    }

    private void AttachTileHandlers(Control control, TileState state)
    {
        control.Click += (_, _) => ToggleTileSelection(state);
        control.DoubleClick += (_, _) => MoveSingleWindow(state.Handle);
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _pendingTileDrag = state;
            _pendingTileDragStart = Cursor.Position;
        };
        control.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || _pendingTileDrag != state)
            {
                return;
            }

            if (Math.Abs(Cursor.Position.X - _pendingTileDragStart.X) < SystemInformation.DragSize.Width / 2
                && Math.Abs(Cursor.Position.Y - _pendingTileDragStart.Y) < SystemInformation.DragSize.Height / 2)
            {
                return;
            }

            var data = new TileDragData(state.Handle, state.ScreenDeviceName);
            _pendingTileDrag = null;
            control.DoDragDrop(data, DragDropEffects.Move);
        };
        control.MouseUp += (_, _) => _pendingTileDrag = null;
        control.DragEnter += (_, e) =>
        {
            if (FindZoneForControl(control) is { } zone)
            {
                OnZoneDragEnter(zone, e);
            }
        };
        control.DragDrop += (_, e) =>
        {
            if (FindZoneForControl(control) is { } zone)
            {
                OnZoneDragDrop(zone, e);
            }
        };
        control.AllowDrop = true;
    }

    private void UpdateWindowTile(TileState state, MovableWindowInfo window)
    {
        var tooltip = _appWindowCounts.TryGetValue(window.AppName, out var appCount) && appCount <= 1
            ? window.AppName
            : _text.OverlayWindowTooltip(window.AppName, window.Title);
        var iconKey = string.IsNullOrWhiteSpace(window.ProcessPath) ? window.AppName : window.ProcessPath;
        var changed = false;

        state.Window = window;
        if (state.Handle != window.Handle)
        {
            state.Handle = window.Handle;
            state.Tile.Tag = window.Handle;
            changed = true;
        }

        if (!string.Equals(state.IconKey, iconKey, StringComparison.OrdinalIgnoreCase))
        {
            state.IconKey = iconKey;
            state.Icon.Image = GetTileIcon(window);
        }

        if (state.IsMinimizedOrOffscreen != window.IsMinimizedOrOffscreen)
        {
            state.IsMinimizedOrOffscreen = window.IsMinimizedOrOffscreen;
            state.Icon.Image = GetTileIcon(window);
            changed = true;
        }

        state.Icon.Enabled = true;

        if (state.IsForeground != window.IsForeground)
        {
            state.IsForeground = window.IsForeground;
            changed = true;
        }

        if (state.IsTopOnMonitor != window.IsTopOnMonitor)
        {
            state.IsTopOnMonitor = window.IsTopOnMonitor;
            changed = true;
        }

        if (!string.Equals(state.ScreenDeviceName, window.ScreenDeviceName, StringComparison.Ordinal))
        {
            state.ScreenDeviceName = window.ScreenDeviceName;
            changed = true;
        }

        if (!ReferenceEquals(state.Theme, _theme))
        {
            state.Theme = _theme;
            changed = true;
        }

        if (!string.Equals(state.Tooltip, tooltip, StringComparison.CurrentCulture))
        {
            state.Tooltip = tooltip;
            _toolTip.SetToolTip(state.Tile, tooltip);
            _toolTip.SetToolTip(state.Icon, tooltip);
        }

        UpdateTileVisual(state, force: changed);
    }

    private void UpdateTileVisual(TileState state, bool force = false)
    {
        var selected = _selectedHandles.Contains(state.Handle);
        var backColor = selected ? _theme.Selected : _theme.Surface;
        if (state.Tile.BackColor != backColor)
        {
            state.Tile.BackColor = backColor;
            force = true;
        }

        if (state.Selected != selected)
        {
            state.Selected = selected;
            force = true;
        }

        if (force)
        {
            state.Tile.Invalidate();
        }
    }

    private void PaintTileBorder(Control tile, PaintEventArgs e)
    {
        if (tile.Tag is not IntPtr handle)
        {
            return;
        }

        var selected = _selectedHandles.Contains(handle);
        var state = _tiles.TryGetValue(handle, out var tileState) ? tileState : null;
        var highlighted = state?.Window.IsForeground == true || state?.Window.IsTopOnMonitor == true;
        var minimized = state?.Window.IsMinimizedOrOffscreen == true;
        using var pen = new Pen(selected ? _theme.Accent : minimized ? _theme.Border : _theme.MutedForeground, selected ? 2 : 1);
        e.Graphics.DrawRectangle(pen, 0, 0, tile.Width - 1, tile.Height - 1);
        if (highlighted)
        {
            using var brush = new SolidBrush(_theme.Accent);
            e.Graphics.FillRectangle(brush, 3, tile.Height - 4, tile.Width - 6, 2);
        }
    }

    private Control CreateMoreTile()
    {
        var tile = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2),
            Size = new Size(TileSize, TileSize),
            Text = "..."
        };
        StyleActionButton(tile);
        _toolTip.SetToolTip(tile, _text.OverlayMore);
        tile.Click += (_, _) => _openWindowPicker();
        return tile;
    }

    private MoveDirectionButton CreateCenterMoveButton()
    {
        var button = new MoveDirectionButton
        {
            Size = new Size(20, 20)
        };
        button.Click += (_, _) => MoveSelectedWindows();
        return button;
    }

    private Button CreateActionButton(string text, Action action, int width)
    {
        var button = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8.4f, FontStyle.Regular),
            Margin = new Padding(1),
            Padding = new Padding(1, 0, 1, 0),
            Size = new Size(width, 24),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = false
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void StyleHeaderButton(Button button)
    {
        button.BackColor = _theme.Background;
        button.ForeColor = _theme.Foreground;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = _theme.SurfaceAlt;
        button.FlatAppearance.MouseDownBackColor = _theme.Selected;
    }

    private void StyleActionButton(Button button)
    {
        button.BackColor = _theme.Surface;
        button.ForeColor = _theme.Foreground;
        button.FlatAppearance.BorderColor = _theme.Border;
        button.FlatAppearance.MouseOverBackColor = _theme.SurfaceAlt;
        button.FlatAppearance.MouseDownBackColor = _theme.Selected;
    }

    private void StyleZone(MonitorZone zone)
    {
        zone.Container.BackColor = _theme.Background;
        zone.TilePanel.BackColor = _theme.Background;
    }

    private void ConfigureZone(MonitorZone zone, Screen? screen, int monitorNumber)
    {
        zone.DeviceName = screen?.DeviceName ?? string.Empty;
        if (ReferenceEquals(zone, _leftZone))
        {
            _leftBadge.Text = monitorNumber.ToString();
        }
        else if (ReferenceEquals(zone, _rightZone))
        {
            _rightBadge.Text = monitorNumber.ToString();
        }

        zone.Container.AllowDrop = !string.IsNullOrWhiteSpace(zone.DeviceName);
        zone.TilePanel.AllowDrop = zone.Container.AllowDrop;
    }

    private void ConfigureZoneDrop(MonitorZone zone)
    {
        zone.Container.DragEnter += (_, e) => OnZoneDragEnter(zone, e);
        zone.TilePanel.DragEnter += (_, e) => OnZoneDragEnter(zone, e);
        zone.Container.DragDrop += (_, e) => OnZoneDragDrop(zone, e);
        zone.TilePanel.DragDrop += (_, e) => OnZoneDragDrop(zone, e);
    }

    private MonitorZone? FindZoneForControl(Control control)
    {
        for (var current = control; current is not null; current = current.Parent)
        {
            if (current is Panel panel && FindZone(panel) is { } zone)
            {
                return zone;
            }
        }

        return null;
    }

    private MonitorZone? FindZone(Panel container)
    {
        if (ReferenceEquals(container, _leftZone.Container))
        {
            return _leftZone;
        }

        if (ReferenceEquals(container, _rightZone.Container))
        {
            return _rightZone;
        }

        return null;
    }

    private static void OnZoneDragEnter(MonitorZone zone, DragEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(zone.DeviceName)
            && e.Data?.GetDataPresent(typeof(TileDragData)) == true)
        {
            e.Effect = DragDropEffects.Move;
        }
    }

    private void OnZoneDragDrop(MonitorZone zone, DragEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(zone.DeviceName)
            || e.Data?.GetData(typeof(TileDragData)) is not TileDragData dragData
            || string.Equals(dragData.SourceDeviceName, zone.DeviceName, StringComparison.Ordinal))
        {
            return;
        }

        MoveSingleWindow(dragData.Handle);
    }

    private void ClearZone(MonitorZone zone)
    {
        foreach (Control control in zone.TilePanel.Controls)
        {
            control.Dispose();
        }

        zone.TilePanel.Controls.Clear();
    }

    private void ResizeZones()
    {
        var zoneWidth = ZoneWidth;
        _leftZone.Container.Margin = Padding.Empty;
        _rightZone.Container.Margin = Padding.Empty;
        ResizeZone(_leftZone, zoneWidth);
        ResizeZone(_rightZone, zoneWidth);
        _dividerPanel.Height = Math.Max(_leftZone.Container.Height, _rightZone.Container.Height);
        _moveSelectedButton.Location = new Point(
            Math.Max(0, (_dividerPanel.Width - _moveSelectedButton.Width) / 2),
            Math.Max(1, (_dividerPanel.Height - _moveSelectedButton.Height) / 2));
    }

    private void ResizeZone(MonitorZone zone, int zoneWidth)
    {
        var tileCount = zone.TilePanel.Controls
            .Cast<Control>()
            .Count(control => control is Panel || control is Button);
        var rows = Math.Max(1, Math.Min(MaxRowsPerZone, (int)Math.Ceiling(tileCount / (double)GetTilesPerRow(zoneWidth))));
        zone.TilePanel.Width = zoneWidth;
        zone.TilePanel.Location = Point.Empty;
        zone.TilePanel.Height = rows * (TileSize + 4);
        zone.Container.Width = zoneWidth;
        zone.Container.Height = zone.TilePanel.Bottom;
    }

    private static int GetZoneCapacity(MonitorZone zone)
    {
        return GetTilesPerRow(zone.Container.Width <= 0 ? ZoneWidth : zone.Container.Width)
            * MaxRowsPerZone;
    }

    private static int GetTilesPerRow(int zoneWidth)
    {
        return Math.Max(1, zoneWidth / (TileSize + TileGap));
    }

    private void ToggleTileSelection(TileState state)
    {
        if (!_selectedHandles.Add(state.Handle))
        {
            _selectedHandles.Remove(state.Handle);
        }

        DiagnosticLog.Info($"overlay tile selected hwnd={DiagnosticLog.FormatHandle(state.Handle)} selected={_selectedHandles.Contains(state.Handle)}");
        UpdateTileVisual(state);
        UpdateMoveSelectedState();
    }

    private void ClearSelection()
    {
        if (_selectedHandles.Count == 0)
        {
            return;
        }

        _selectedHandles.Clear();
        foreach (var state in _tiles.Values)
        {
            UpdateTileVisual(state, force: true);
        }

        UpdateMoveSelectedState();
    }

    private void MoveSelectedWindows()
    {
        var handles = _selectedHandles.ToArray();
        if (handles.Length == 0)
        {
            return;
        }

        try
        {
            var moved = 0;
            foreach (var handle in handles)
            {
                DiagnosticLog.Info($"overlay move selected hwnd={DiagnosticLog.FormatHandle(handle)}");
                if (_windowMover.MoveWindowToOtherMonitor(handle))
                {
                    moved++;
                }
            }

            _selectedHandles.Clear();
            _onMoved(moved);
            RefreshTiles(forceRebuild: false);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Info($"overlay move selected failed {ex.GetType().Name}: {ex.Message}");
            _onError(ex);
        }
    }

    private void MoveSingleWindow(IntPtr handle)
    {
        try
        {
            DiagnosticLog.Info($"overlay move single hwnd={DiagnosticLog.FormatHandle(handle)}");
            var moved = _windowMover.MoveWindowToOtherMonitor(handle) ? 1 : 0;
            _selectedHandles.Clear();
            _onMoved(moved);
            RefreshTiles(forceRebuild: false);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Info($"overlay move single failed {ex.GetType().Name}: {ex.Message}");
            _onError(ex);
        }
    }

    private void RunOverlayAction(Action action)
    {
        action();
        _selectedHandles.Clear();
        RefreshTiles(forceRebuild: false);
    }

    private void UpdateMoveSelectedState()
    {
        var selectedScreens = _tiles.Values
            .Where(state => _selectedHandles.Contains(state.Handle))
            .Select(state => state.ScreenDeviceName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _moveSelectedButton.Enabled = selectedScreens.Length > 0;
        _moveSelectedButton.Direction = selectedScreens.Length switch
        {
            1 when selectedScreens[0] == _leftZone.DeviceName => MoveDirection.Right,
            1 when selectedScreens[0] == _rightZone.DeviceName => MoveDirection.Left,
            _ => MoveDirection.Both
        };
        _moveSelectedButton.ApplyTheme(_theme);
    }

    private void ResizeToContent()
    {
        var previousSize = Size;
        ResizeZones();
        var height = _headerPanel.Height + _zonePanel.Height + _actionPanel.Height + 2;
        Size = new Size(OverlayWidth, Math.Max(82, height));
        _headerDividerLine.Location = new Point(ClientSize.Width / 2, 0);
        _headerDividerLine.Height = _headerPanel.Height;
        _leftBadge.Location = new Point(OuterPadding + 1, _headerPanel.Height - _leftBadge.Height - 1);
        _rightBadge.Location = new Point(OuterPadding + ZoneWidth + DividerWidth + 1, _headerPanel.Height - _rightBadge.Height - 1);
        _hideButton.Location = new Point(ClientSize.Width - _hideButton.Width - 2, 1);
        _settingsButton.Location = new Point(_hideButton.Left - _settingsButton.Width - 2, 1);
        CenterActionButtons();
        if (_hasPlacement && !_useCustomLocation && previousSize != Size)
        {
            Location = ClampLocation(GetCornerLocation(_position, Size, GetPlacementArea()), Size);
        }
    }

    private void CenterActionButtons()
    {
        var buttonWidth = _actionPanel.Controls
            .Cast<Control>()
            .Sum(control => control.Width + control.Margin.Left + control.Margin.Right);
        var sidePadding = Math.Max(OuterPadding, (ClientSize.Width - buttonWidth) / 2);
        _actionPanel.Padding = new Padding(sidePadding, 0, sidePadding, 4);
    }

    private Image GetAppIcon(MovableWindowInfo window)
    {
        return AppIconHelper.GetAppIcon(window, _iconCache);
    }

    private Image GetTileIcon(MovableWindowInfo window)
    {
        return AppIconHelper.GetTileIcon(window, _iconCache);
    }

    private void AttachDragHandlers(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (!_draggable || e.Button != MouseButtons.Left)
            {
                return;
            }

            _dragging = true;
            _dragStartCursor = Cursor.Position;
            _dragStartLocation = Location;
        };
        control.MouseMove += (_, _) =>
        {
            if (!_dragging)
            {
                return;
            }

            var delta = new Size(Cursor.Position.X - _dragStartCursor.X, Cursor.Position.Y - _dragStartCursor.Y);
            Location = ClampLocation(_dragStartLocation + delta, Size);
        };
        control.MouseUp += (_, _) =>
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            _customLocationChanged(Location);
            DiagnosticLog.Info($"overlay dragged location={Location.X},{Location.Y}");
        };
    }

    private void UpdateDraggableCursor()
    {
        _headerPanel.Cursor = _draggable ? Cursors.SizeAll : Cursors.Default;
    }

    private Rectangle GetPlacementArea()
    {
        return IsHandleCreated
            ? Screen.FromControl(this).WorkingArea
            : Screen.FromPoint(Location).WorkingArea;
    }

    private static Point GetCornerLocation(OverlayPosition position, Size size, Rectangle area)
    {
        const int margin = 8;
        var x = position is OverlayPosition.TopRight or OverlayPosition.BottomRight
            ? area.Right - size.Width - margin - TaskbarSafetyGap
            : area.Left + margin;
        var y = position is OverlayPosition.BottomLeft or OverlayPosition.BottomRight
            ? area.Bottom - size.Height - margin - TaskbarSafetyGap
            : area.Top + margin + TopSafetyGap;

        return new Point(x, y);
    }

    private static Point ClampLocation(Point location, Size size)
    {
        var area = Screen.FromPoint(location).WorkingArea;
        var x = Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - size.Width));
        var y = Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Point(x, y);
    }

    private static Screen[] GetOrderedScreens()
    {
        return Screen.AllScreens
            .OrderBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();
    }

    private static double NormalizeOpacity(int opacity)
    {
        return Math.Clamp(opacity, 50, 100) / 100.0;
    }

    private sealed class TileState
    {
        public TileState(Panel tile, PictureBox icon)
        {
            Tile = tile;
            Icon = icon;
        }

        public Panel Tile { get; }
        public PictureBox Icon { get; }
        public IntPtr Handle { get; set; }
        public MovableWindowInfo Window { get; set; }
        public string IconKey { get; set; } = string.Empty;
        public string ScreenDeviceName { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
        public UiTheme? Theme { get; set; }
        public bool Selected { get; set; }
        public bool IsForeground { get; set; }
        public bool IsTopOnMonitor { get; set; }
        public bool IsMinimizedOrOffscreen { get; set; }
    }

    private sealed class TileDragData
    {
        public TileDragData(IntPtr handle, string sourceDeviceName)
        {
            Handle = handle;
            SourceDeviceName = sourceDeviceName;
        }

        public IntPtr Handle { get; }
        public string SourceDeviceName { get; }
    }

    private sealed class MonitorZone
    {
        public MonitorZone()
        {
            Container = new Panel
            {
                Margin = Padding.Empty
            };
            TilePanel = new DoubleBufferedFlowLayoutPanel
            {
                Location = Point.Empty,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                WrapContents = true
            };
            Container.MouseDown += (_, e) =>
            {
                if (Container.GetChildAtPoint(e.Location) is null)
                {
                    ClearRequested?.Invoke();
                }
            };
            TilePanel.MouseDown += (_, e) =>
            {
                if (TilePanel.GetChildAtPoint(e.Location) is null)
                {
                    ClearRequested?.Invoke();
                }
            };
            Container.Controls.Add(TilePanel);
        }

        public Panel Container { get; }
        public DoubleBufferedFlowLayoutPanel TilePanel { get; }
        public string DeviceName { get; set; } = string.Empty;
        public Action? ClearRequested { get; set; }
    }

    private sealed class MonitorBadge : Label
    {
        private UiTheme _theme = UiTheme.For(AppTheme.Light);

        public MonitorBadge()
        {
            TextAlign = ContentAlignment.MiddleCenter;
            Font = new Font(FontFamily.GenericSansSerif, 7, FontStyle.Bold);
        }

        public void ApplyTheme(UiTheme theme)
        {
            _theme = theme;
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_theme.Accent);
            e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private sealed class DoubleBufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public DoubleBufferedFlowLayoutPanel()
        {
            DoubleBuffered = true;
        }
    }

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
        }
    }
}
