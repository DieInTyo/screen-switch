using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenSwitch;

internal sealed class MoveWindowPickerForm : Form
{
    private const int TileSize = 34;
    private const int IconSize = 23;

    private readonly WindowMover _windowMover;
    private readonly LocalizedStrings _text;
    private readonly UiTheme _theme;
    private readonly Action<int> _onMoved;
    private readonly Action<Exception> _onError;
    private readonly Action<MoveWindowPickerView> _onViewChanged;
    private readonly Action _moveActiveWindow;
    private readonly Action _moveAllWindows;
    private readonly Action _minimizeAllWindows;
    private readonly ListView _windowList;
    private readonly Panel _contentPanel;
    private readonly TableLayoutPanel _tileView;
    private readonly FlowLayoutPanel _viewPanel;
    private readonly Button _tableViewButton;
    private readonly Button _tilesViewButton;
    private readonly Button _moveSelectedButton;
    private readonly Button _tableActiveButton;
    private readonly Button _tableAllButton;
    private readonly Button _tableMinimizeButton;
    private readonly MoveDirectionButton _tileMoveSelectedButton;
    private readonly Button _tileActiveButton;
    private readonly Button _tileAllButton;
    private readonly Button _tileMinimizeButton;
    private readonly Label _hintLabel;
    private readonly Panel _bottomPanel;
    private readonly Panel _tileActionPanel;
    private readonly Label _tileHintLabel;
    private readonly Icon _formIcon;
    private readonly ToolTip _toolTip = new()
    {
        Active = true,
        AutoPopDelay = 7000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true
    };
    private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _appWindowCounts = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<IntPtr> _selectedTileHandles = new();
    private readonly List<TileState> _tileStates = new();
    private MoveWindowPickerView _view;
    private MovableWindowInfo[] _windows = Array.Empty<MovableWindowInfo>();
    private int _sortColumn;
    private SortOrder _sortOrder = SortOrder.Ascending;

    public MoveWindowPickerForm(
        WindowMover windowMover,
        LocalizedStrings text,
        UiTheme theme,
        MoveWindowPickerView view,
        Action<MoveWindowPickerView> onViewChanged,
        Action moveActiveWindow,
        Action moveAllWindows,
        Action minimizeAllWindows,
        Action<int> onMoved,
        Action<Exception> onError)
    {
        _windowMover = windowMover;
        _text = text;
        _theme = theme;
        _view = view;
        _onViewChanged = onViewChanged;
        _moveActiveWindow = moveActiveWindow;
        _moveAllWindows = moveAllWindows;
        _minimizeAllWindows = minimizeAllWindows;
        _onMoved = onMoved;
        _onError = onError;

        Text = _text.MoveWindowPickerTitle;
        _formIcon = TrayIconFactory.Create();
        Icon = _formIcon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(620, 420);
        MinimumSize = new Size(480, 320);
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;

        _viewPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(8, 6, 8, 4),
            WrapContents = false
        };
        _tableViewButton = CreateModeButton(_text.PickerTableView, MoveWindowPickerView.Table);
        _tilesViewButton = CreateModeButton(_text.PickerTilesView, MoveWindowPickerView.Tiles);
        _viewPanel.Controls.Add(_tableViewButton);
        _viewPanel.Controls.Add(_tilesViewButton);

        _windowList = new ListView
        {
            CheckBoxes = true,
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            MultiSelect = false,
            OwnerDraw = true,
            View = View.Details
        };
        _windowList.Columns.Add(_text.ColumnMonitor, 72);
        _windowList.Columns.Add(_text.ColumnApplication, 170);
        _windowList.Columns.Add(_text.ColumnWindow, 250);
        _windowList.Columns.Add(_text.ColumnState, 96);
        _windowList.ColumnClick += (_, e) => SortByColumn(e.Column);
        _windowList.DrawColumnHeader += DrawColumnHeader;
        _windowList.DrawItem += (_, _) => { };
        _windowList.DrawSubItem += DrawSubItem;
        _windowList.ItemChecked += (_, _) => UpdateMoveSelectedState();
        _windowList.DoubleClick += (_, _) => MoveFocusedWindow();
        _windowList.Resize += (_, _) => ResizeTableColumns();
        _windowList.MouseDown += (_, e) =>
        {
            if (_windowList.GetItemAt(e.X, e.Y) is null)
            {
                ClearSelection();
            }
        };

        _tileView = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            RowCount = 1,
            Visible = false
        };
        _tileView.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _tileView.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
        _tileView.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill
        };
        _contentPanel.Controls.Add(_tileView);
        _contentPanel.Controls.Add(_windowList);

        _moveSelectedButton = new Button
        {
            Text = _text.MoveSelectedWindows,
            AutoSize = false,
            Size = new Size(118, 24)
        };
        _moveSelectedButton.Click += (_, _) => MoveSelectedWindows();

        _tableActiveButton = CreateActionButton(_text.OverlayActiveButton, RunPickerAction(_moveActiveWindow), width: 82);
        _tableAllButton = CreateActionButton(_text.OverlayAllButton, RunPickerAction(_moveAllWindows), width: 58);
        _tableMinimizeButton = CreateActionButton(_text.OverlayMinimizeButton, RunPickerAction(_minimizeAllWindows), width: 98);

        _tileMoveSelectedButton = new MoveDirectionButton
        {
            Size = new Size(22, 22)
        };
        _tileMoveSelectedButton.Click += (_, _) => MoveSelectedWindows();

        _tileActiveButton = CreateActionButton(_text.OverlayActiveButton, RunPickerAction(_moveActiveWindow), width: 82);
        _tileAllButton = CreateActionButton(_text.OverlayAllButton, RunPickerAction(_moveAllWindows), width: 58);
        _tileMinimizeButton = CreateActionButton(_text.OverlayMinimizeButton, RunPickerAction(_minimizeAllWindows), width: 98);

        _hintLabel = new Label
        {
            Text = _text.MoveWindowDoubleClickHint,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleRight
        };
        _tileHintLabel = new Label
        {
            Text = _text.MoveWindowDoubleClickHint,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleRight
        };

        _bottomPanel = new Panel
        {
            Height = 42,
            Dock = DockStyle.Bottom,
            Padding = new Padding(8)
        };
        _bottomPanel.Resize += (_, _) => LayoutBottomPanel();
        _bottomPanel.Controls.Add(_tableActiveButton);
        _bottomPanel.Controls.Add(_tableAllButton);
        _bottomPanel.Controls.Add(_tableMinimizeButton);
        _bottomPanel.Controls.Add(_hintLabel);
        _bottomPanel.Controls.Add(_moveSelectedButton);

        _tileActionPanel = new Panel
        {
            Height = 42,
            Dock = DockStyle.Bottom,
            Padding = new Padding(8),
            Visible = false
        };
        _tileActionPanel.Resize += (_, _) => LayoutTileActionPanel();
        _tileView.MouseDown += (_, e) =>
        {
            if (_tileView.GetChildAtPoint(e.Location) is null)
            {
                ClearSelection();
            }
        };
        _tileActionPanel.Controls.Add(_tileActiveButton);
        _tileActionPanel.Controls.Add(_tileAllButton);
        _tileActionPanel.Controls.Add(_tileMinimizeButton);
        _tileActionPanel.Controls.Add(_tileHintLabel);

        Controls.Add(_contentPanel);
        Controls.Add(_viewPanel);
        Controls.Add(_tileActionPanel);
        Controls.Add(_bottomPanel);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        ApplyTheme();
        ApplyView();
        RefreshWindowList();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var image in _iconCache.Values)
            {
                image.Dispose();
            }

            _toolTip.Dispose();
            _formIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    public void ApplyTheme()
    {
        BackColor = _theme.Background;
        ForeColor = _theme.Foreground;
        _windowList.BackColor = _theme.Surface;
        _windowList.ForeColor = _theme.Foreground;
        _tileView.BackColor = _theme.Background;
        _viewPanel.BackColor = _theme.Background;
        _bottomPanel.BackColor = _theme.Background;
        _tileActionPanel.BackColor = _theme.Background;
        _hintLabel.BackColor = _theme.Background;
        _hintLabel.ForeColor = _theme.MutedForeground;
        _tileHintLabel.BackColor = _theme.Background;
        _tileHintLabel.ForeColor = _theme.MutedForeground;
        StyleButton(_tableViewButton, _view == MoveWindowPickerView.Table);
        StyleButton(_tilesViewButton, _view == MoveWindowPickerView.Tiles);
        StyleButton(_moveSelectedButton, selected: false);
        StyleButton(_tableActiveButton, selected: false);
        StyleButton(_tableAllButton, selected: false);
        StyleButton(_tableMinimizeButton, selected: false);
        StyleButton(_tileActiveButton, selected: false);
        StyleButton(_tileAllButton, selected: false);
        StyleButton(_tileMinimizeButton, selected: false);
        _tileMoveSelectedButton.ApplyTheme(_theme);
        _moveSelectedButton.ForeColor = _moveSelectedButton.Enabled ? _theme.Foreground : _theme.MutedForeground;
        ResizeTableColumns();
        LayoutBottomPanel();
        LayoutTileActionPanel();
        ApplyDarkTitleBar();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
    }

    private Button CreateModeButton(string text, MoveWindowPickerView view)
    {
        var button = new Button
        {
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 4, 0),
            Text = text
        };
        button.Click += (_, _) => SetView(view);
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
            AutoEllipsis = true,
            UseCompatibleTextRendering = false
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void SetView(MoveWindowPickerView view)
    {
        if (_view == view)
        {
            return;
        }

        _view = view;
        _onViewChanged(view);
        ApplyView();
        ApplyTheme();
        RefreshWindowList();
    }

    private void ApplyView()
    {
        _windowList.Visible = _view == MoveWindowPickerView.Table;
        _tileView.Visible = _view == MoveWindowPickerView.Tiles;
        _bottomPanel.Visible = _view == MoveWindowPickerView.Table;
        _tileActionPanel.Visible = _view == MoveWindowPickerView.Tiles;
        if (_view == MoveWindowPickerView.Table)
        {
            _windowList.BringToFront();
        }
        else
        {
            _tileView.BringToFront();
        }
    }

    private void RefreshWindowList()
    {
        DiagnosticLog.Info("action move-window-picker refresh");
        _windows = SortWindows(_windowMover.GetMovableWindows(includeMinimizedWindows: true).ToArray());
        _appWindowCounts.Clear();
        foreach (var group in _windows.GroupBy(window => window.AppName, StringComparer.CurrentCultureIgnoreCase))
        {
            _appWindowCounts[group.Key] = group.Count();
        }

        RefreshTable();
        RefreshTiles();
        UpdateMoveSelectedState();
        DiagnosticLog.Info($"action move-window-picker count={_windows.Length} view={_view}");
    }

    private void RefreshTable()
    {
        _windowList.Columns[0].Text = _text.ColumnMonitor;
        _windowList.Columns[1].Text = _text.ColumnApplication;
        _windowList.Columns[2].Text = _text.ColumnWindow;
        _windowList.Columns[3].Text = _text.ColumnState;

        _windowList.BeginUpdate();
        _windowList.Items.Clear();

        foreach (var window in _windows)
        {
            var item = new ListViewItem(FormatMonitorBadge(window.ScreenDeviceName))
            {
                BackColor = _theme.Surface,
                ForeColor = _theme.Foreground,
                Tag = window,
                ToolTipText = GetWindowTooltip(window)
            };
            item.SubItems.Add(window.AppName);
            item.SubItems.Add(window.Title);
            item.SubItems.Add(GetWindowStateText(window));
            _windowList.Items.Add(item);
        }

        if (_windows.Length == 0)
        {
            var item = new ListViewItem(string.Empty)
            {
                BackColor = _theme.Surface,
                ForeColor = _theme.MutedForeground
            };
            item.SubItems.Add(string.Empty);
            item.SubItems.Add(_text.NoWindowsFound);
            item.SubItems.Add(string.Empty);
            _windowList.Items.Add(item);
        }

        _windowList.EndUpdate();
        ResizeTableColumns();
    }

    private void RefreshTiles()
    {
        _tileView.SuspendLayout();
        foreach (Control control in _tileView.Controls)
        {
            control.Dispose();
        }

        _tileView.Controls.Clear();
        _tileStates.Clear();

        var screens = GetOrderedScreens();
        AddTileZone(screens.ElementAtOrDefault(0), 1, 0);
        var divider = CreateTileDivider();
        _tileView.Controls.Add(divider, 1, 0);
        AddTileZone(screens.ElementAtOrDefault(1), 2, 2);
        _tileView.ResumeLayout();
    }

    private TileZone AddTileZone(Screen? screen, int monitorNumber, int column)
    {
        var zone = new TileZone(screen?.DeviceName ?? string.Empty);
        var container = new Panel
        {
            BackColor = _theme.Background,
            Dock = DockStyle.Fill,
            Margin = new Padding(column == 0 ? 0 : 4, 0, column == 0 ? 4 : 0, 0)
        };
        zone.Container = container;

        var header = new Panel
        {
            BackColor = _theme.Background,
            Dock = DockStyle.Top,
            Height = 20
        };
        var badge = new MonitorBadge
        {
            Location = new Point(2, 3),
            Size = new Size(16, 16),
            Text = monitorNumber.ToString()
        };
        badge.ApplyTheme(_theme);
        header.Controls.Add(badge);
        var flow = new FlowLayoutPanel
        {
            AutoScroll = true,
            BackColor = _theme.Background,
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
            WrapContents = true
        };
        zone.Flow = flow;
        ConfigureTileZoneDrop(zone);
        flow.MouseDown += (_, e) =>
        {
            if (flow.GetChildAtPoint(e.Location) is null)
            {
                ClearSelection();
            }
        };
        container.MouseDown += (_, e) =>
        {
            if (container.GetChildAtPoint(e.Location) is null)
            {
                ClearSelection();
            }
        };
        container.Controls.Add(flow);
        container.Controls.Add(header);
        badge.BringToFront();
        _tileView.Controls.Add(container, column, 0);

        if (screen is null)
        {
            return zone;
        }

        foreach (var window in _windows.Where(window => window.ScreenDeviceName == screen.DeviceName))
        {
            var state = CreateTile(window);
            _tileStates.Add(state);
            flow.Controls.Add(state.Tile);
        }

        return zone;
    }

    private Control CreateTileDivider()
    {
        var panel = new DoubleBufferedPanel
        {
            BackColor = _theme.Background,
            Dock = DockStyle.Fill
        };
        panel.Paint += (_, e) =>
        {
            var x = panel.Width / 2;
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, x, 0, x, panel.Height);
        };
        panel.Controls.Add(_tileMoveSelectedButton);
        panel.Resize += (_, _) =>
        {
            _tileMoveSelectedButton.Location = new Point(
                Math.Max(0, (panel.Width - _tileMoveSelectedButton.Width) / 2),
                Math.Max(0, (panel.Height - _tileMoveSelectedButton.Height) / 2));
        };
        _tileMoveSelectedButton.Location = new Point(2, 2);
        return panel;
    }

    private TileState CreateTile(MovableWindowInfo window)
    {
        var tile = new Panel
        {
            BackColor = _selectedTileHandles.Contains(window.Handle) ? _theme.Selected : _theme.Surface,
            Cursor = Cursors.Hand,
            Margin = new Padding(2),
            Size = new Size(TileSize, TileSize),
            Tag = window.Handle
        };
        tile.Paint += (_, e) =>
        {
            var selected = _selectedTileHandles.Contains(window.Handle);
            var highlighted = window.IsForeground || window.IsTopOnMonitor;
            using var pen = new Pen(selected ? _theme.Accent : window.IsMinimizedOrOffscreen ? _theme.Border : _theme.MutedForeground, selected ? 2 : 1);
            e.Graphics.DrawRectangle(pen, 0, 0, tile.Width - 1, tile.Height - 1);
            if (highlighted)
            {
                using var brush = new SolidBrush(_theme.Accent);
                e.Graphics.FillRectangle(brush, 3, tile.Height - 4, tile.Width - 6, 2);
            }
        };

        var icon = new PictureBox
        {
            Image = GetTileIcon(window),
            Location = new Point((TileSize - IconSize) / 2, (TileSize - IconSize) / 2),
            Size = new Size(IconSize, IconSize),
            SizeMode = PictureBoxSizeMode.StretchImage
        };
        tile.Controls.Add(icon);
        _toolTip.SetToolTip(tile, GetWindowTooltip(window));
        _toolTip.SetToolTip(icon, GetWindowTooltip(window));

        var state = new TileState(tile, window);
        AttachTileHandlers(tile, state);
        AttachTileHandlers(icon, state);
        return state;
    }

    private void AttachTileHandlers(Control control, TileState state)
    {
        control.Click += (_, _) =>
        {
            if (!_selectedTileHandles.Add(state.Window.Handle))
            {
                _selectedTileHandles.Remove(state.Window.Handle);
            }

            state.Tile.BackColor = _selectedTileHandles.Contains(state.Window.Handle) ? _theme.Selected : _theme.Surface;
            state.Tile.Invalidate();
            UpdateMoveSelectedState();
        };
        control.DoubleClick += (_, _) => MoveWindow(state.Window);
        control.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                state.DragStart = Cursor.Position;
                state.PendingDrag = true;
            }
        };
        control.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || !state.PendingDrag)
            {
                return;
            }

            if (Math.Abs(Cursor.Position.X - state.DragStart.X) < SystemInformation.DragSize.Width / 2
                && Math.Abs(Cursor.Position.Y - state.DragStart.Y) < SystemInformation.DragSize.Height / 2)
            {
                return;
            }

            state.PendingDrag = false;
            control.DoDragDrop(new TileDragData(state.Window.Handle, state.Window.ScreenDeviceName), DragDropEffects.Move);
        };
        control.MouseUp += (_, _) => state.PendingDrag = false;
    }

    private void ConfigureTileZoneDrop(TileZone zone)
    {
        zone.Container.AllowDrop = true;
        zone.Flow.AllowDrop = true;
        zone.Container.DragEnter += (_, e) => OnTileZoneDragEnter(zone, e);
        zone.Flow.DragEnter += (_, e) => OnTileZoneDragEnter(zone, e);
        zone.Container.DragDrop += (_, e) => OnTileZoneDragDrop(zone, e);
        zone.Flow.DragDrop += (_, e) => OnTileZoneDragDrop(zone, e);
    }

    private static void OnTileZoneDragEnter(TileZone zone, DragEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(zone.DeviceName)
            && e.Data?.GetDataPresent(typeof(TileDragData)) == true)
        {
            e.Effect = DragDropEffects.Move;
        }
    }

    private void OnTileZoneDragDrop(TileZone zone, DragEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(zone.DeviceName)
            || e.Data?.GetData(typeof(TileDragData)) is not TileDragData dragData
            || string.Equals(dragData.SourceDeviceName, zone.DeviceName, StringComparison.Ordinal))
        {
            return;
        }

        MoveWindow(dragData.Handle);
    }

    private void SortByColumn(int column)
    {
        if (_sortColumn == column)
        {
            _sortOrder = _sortOrder == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
        }
        else
        {
            _sortColumn = column;
            _sortOrder = SortOrder.Ascending;
        }

        RefreshWindowList();
    }

    private MovableWindowInfo[] SortWindows(MovableWindowInfo[] windows)
    {
        Func<MovableWindowInfo, object> selector = _sortColumn switch
        {
            0 => window => GetMonitorNumber(window.ScreenDeviceName),
            1 => window => window.AppName,
            2 => window => window.Title,
            3 => GetWindowStateText,
            _ => window => window.AppName
        };
        var sorted = _sortOrder == SortOrder.Descending
            ? windows.OrderByDescending(selector).ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            : windows.OrderBy(selector).ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase);
        return sorted.ToArray();
    }

    private void MoveSelectedWindows()
    {
        var handles = _view == MoveWindowPickerView.Table
            ? _windowList.CheckedItems
                .Cast<ListViewItem>()
                .Select(item => item.Tag)
                .OfType<MovableWindowInfo>()
                .Select(window => window.Handle)
                .ToArray()
            : _selectedTileHandles.ToArray();

        if (handles.Length == 0)
        {
            return;
        }

        try
        {
            var movedCount = 0;
            foreach (var handle in handles)
            {
                DiagnosticLog.Info($"action move-window-picker selected-batch hwnd={DiagnosticLog.FormatHandle(handle)}");
                if (_windowMover.MoveWindowToOtherMonitor(handle))
                {
                    movedCount++;
                }
            }

            _selectedTileHandles.Clear();
            _onMoved(movedCount);
            RefreshWindowList();
            BringPickerToFront();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Info($"action move-window-picker batch failed {ex.GetType().Name}: {ex.Message}");
            _onError(ex);
        }
    }

    private void MoveFocusedWindow()
    {
        if (_windowList.FocusedItem?.Tag is not MovableWindowInfo window)
        {
            return;
        }

        MoveWindow(window);
    }

    private void MoveWindow(MovableWindowInfo window)
    {
        MoveWindow(window.Handle, window.Title, window.AppName, window.ProcessId);
    }

    private void MoveWindow(IntPtr handle)
    {
        MoveWindow(handle, string.Empty, string.Empty, 0);
    }

    private void MoveWindow(IntPtr handle, string title, string appName, int processId)
    {
        try
        {
            DiagnosticLog.Info(
                $"action move-window-picker selected hwnd={DiagnosticLog.FormatHandle(handle)} title=\"{title}\" app=\"{appName}\" pid={processId}");
            var moved = _windowMover.MoveWindowToOtherMonitor(handle);
            _selectedTileHandles.Clear();
            _onMoved(moved ? 1 : 0);
            RefreshWindowList();
            BringPickerToFront();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Info($"action move-window-picker failed {ex.GetType().Name}: {ex.Message}");
            _onError(ex);
        }
    }

    private Action RunPickerAction(Action action)
    {
        return () =>
        {
            action();
            _selectedTileHandles.Clear();
            RefreshWindowList();
            BringPickerToFront();
        };
    }

    private void UpdateMoveSelectedState()
    {
        var hasSelection = _view == MoveWindowPickerView.Table
            ? _windowList.CheckedItems.Cast<ListViewItem>().Any(item => item.Tag is MovableWindowInfo)
            : _selectedTileHandles.Count > 0;
        _moveSelectedButton.Enabled = true;
        _moveSelectedButton.ForeColor = hasSelection ? _theme.Foreground : _theme.MutedForeground;
        _moveSelectedButton.FlatAppearance.BorderColor = hasSelection ? _theme.Border : _theme.SurfaceAlt;
        _tileMoveSelectedButton.Enabled = _selectedTileHandles.Count > 0;
        var selectedScreens = _tileStates
            .Where(state => _selectedTileHandles.Contains(state.Window.Handle))
            .Select(state => state.Window.ScreenDeviceName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var screens = GetOrderedScreens();
        _tileMoveSelectedButton.Direction = selectedScreens.Length switch
        {
            1 when selectedScreens[0] == screens.ElementAtOrDefault(0)?.DeviceName => MoveDirection.Right,
            1 when selectedScreens[0] == screens.ElementAtOrDefault(1)?.DeviceName => MoveDirection.Left,
            _ => MoveDirection.Both
        };
        _tileMoveSelectedButton.ApplyTheme(_theme);
    }

    private void ClearSelection()
    {
        var changed = false;
        foreach (ListViewItem item in _windowList.CheckedItems.Cast<ListViewItem>().ToArray())
        {
            item.Checked = false;
            changed = true;
        }

        if (_selectedTileHandles.Count > 0)
        {
            _selectedTileHandles.Clear();
            foreach (var state in _tileStates)
            {
                state.Tile.BackColor = _theme.Surface;
                state.Tile.Invalidate();
            }

            changed = true;
        }

        if (changed)
        {
            UpdateMoveSelectedState();
        }
    }

    private void BringPickerToFront()
    {
        TopMost = false;
        TopMost = true;
        Activate();
    }

    private void StyleButton(Button button, bool selected)
    {
        button.BackColor = selected ? _theme.Selected : _theme.Surface;
        button.ForeColor = button.Enabled ? _theme.Foreground : _theme.MutedForeground;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = selected ? _theme.Accent : _theme.Border;
        button.FlatAppearance.MouseOverBackColor = _theme.SurfaceAlt;
        button.FlatAppearance.MouseDownBackColor = _theme.Selected;
    }

    private void DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var headerColor = _theme.Theme == AppTheme.Dark
            ? Color.FromArgb(48, 48, 48)
            : Color.FromArgb(232, 232, 232);
        using var background = new SolidBrush(headerColor);
        using var border = new Pen(_theme.Border);
        e.Graphics.FillRectangle(background, e.Bounds);
        e.Graphics.DrawRectangle(border, e.Bounds);
        var text = e.Header?.Text ?? string.Empty;
        if (e.ColumnIndex == _sortColumn)
        {
            text += _sortOrder == SortOrder.Ascending ? " ^" : " v";
        }

        TextRenderer.DrawText(
            e.Graphics,
            text,
            _windowList.Font,
            new Rectangle(e.Bounds.Left + 5, e.Bounds.Top, e.Bounds.Width - 10, e.Bounds.Height),
            _theme.Foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var item = e.Item;
        if (item is null || e.SubItem is null)
        {
            return;
        }

        var selected = item.Selected;
        using var background = new SolidBrush(selected ? _theme.Selected : item.BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);

        var textBounds = e.Bounds;
        if (e.ColumnIndex == 0 && item.Tag is MovableWindowInfo)
        {
            var checkRect = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + (e.Bounds.Height - 13) / 2, 13, 13);
            ControlPaint.DrawCheckBox(e.Graphics, checkRect, item.Checked ? ButtonState.Checked : ButtonState.Normal);
            textBounds = new Rectangle(e.Bounds.Left + 22, e.Bounds.Top, e.Bounds.Width - 24, e.Bounds.Height);
        }
        else if (e.ColumnIndex == 1 && item.Tag is MovableWindowInfo window)
        {
            var iconSize = Math.Min(18, e.Bounds.Height - 4);
            var iconBounds = new Rectangle(e.Bounds.Left + 5, e.Bounds.Top + (e.Bounds.Height - iconSize) / 2, iconSize, iconSize);
            e.Graphics.DrawImage(GetTileIcon(window), iconBounds);
            textBounds = new Rectangle(iconBounds.Right + 6, e.Bounds.Top, e.Bounds.Width - iconSize - 13, e.Bounds.Height);
        }

        TextRenderer.DrawText(
            e.Graphics,
            e.SubItem.Text,
            _windowList.Font,
            textBounds,
            item.Tag is MovableWindowInfo stateWindow && stateWindow.IsMinimizedOrOffscreen ? _theme.MutedForeground : item.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void ResizeTableColumns()
    {
        if (_windowList.Columns.Count < 4 || _windowList.ClientSize.Width <= 0)
        {
            return;
        }

        var width = Math.Max(260, _windowList.ClientSize.Width - 2);
        var monitorWidth = 86;
        var stateWidth = Math.Clamp((int)(width * 0.20), 82, 100);
        var appWidth = Math.Clamp((int)(width * 0.30), 120, 170);
        var windowWidth = Math.Max(80, width - monitorWidth - appWidth - stateWidth);

        _windowList.Columns[0].Width = monitorWidth;
        _windowList.Columns[1].Width = appWidth;
        _windowList.Columns[2].Width = windowWidth;
        _windowList.Columns[3].Width = Math.Max(48, width - monitorWidth - appWidth - windowWidth);
        NativeMethods.ShowScrollBar(_windowList.Handle, NativeMethods.ScrollBarCommand.Horz, false);
    }

    private void LayoutBottomPanel()
    {
        if (_bottomPanel.Width <= 0)
        {
            return;
        }

        LayoutPickerActionPanel(
            _bottomPanel,
            _hintLabel,
            _tableActiveButton,
            _tableAllButton,
            _tableMinimizeButton,
            _moveSelectedButton);
    }

    private void LayoutTileActionPanel()
    {
        if (_tileActionPanel.Width <= 0)
        {
            return;
        }

        LayoutPickerActionPanel(
            _tileActionPanel,
            _tileHintLabel,
            _tileActiveButton,
            _tileAllButton,
            _tileMinimizeButton,
            moveButton: null);
    }

    private static void LayoutPickerActionPanel(
        Panel panel,
        Label hintLabel,
        Button activeButton,
        Button allButton,
        Button minimizeButton,
        Button? moveButton)
    {
        var left = panel.Padding.Left;
        var top = panel.Padding.Top;
        var gap = 3;
        PlaceActionButton(activeButton, ref left, top);
        PlaceActionButton(allButton, ref left, top);
        PlaceActionButton(minimizeButton, ref left, top);

        if (moveButton is not null)
        {
            moveButton.Size = new Size(118, 24);
            moveButton.Location = new Point(
                Math.Max(panel.Padding.Left, panel.ClientSize.Width - panel.Padding.Right - moveButton.Width),
                top);
        }

        var right = moveButton is not null ? moveButton.Left - gap : panel.ClientSize.Width - panel.Padding.Right;
        hintLabel.Location = new Point(left + gap, top + 2);
        hintLabel.Size = new Size(Math.Max(0, right - left - gap * 2), 20);
    }

    private static void PlaceActionButton(Button button, ref int left, int top)
    {
        button.Location = new Point(left, top);
        left += button.Width + 3;
    }

    private void ApplyDarkTitleBar()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var enabled = _theme.Theme == AppTheme.Dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref enabled,
            Marshal.SizeOf<int>());
    }

    private string GetWindowStateText(MovableWindowInfo window)
    {
        return window.IsMinimizedOrOffscreen ? _text.WindowStateMinimized : _text.WindowStateOpen;
    }

    private Image GetTileIcon(MovableWindowInfo window)
    {
        return AppIconHelper.GetTileIcon(window, _iconCache);
    }

    private static string FormatMonitorBadge(string screenDeviceName)
    {
        return $"[{GetMonitorNumber(screenDeviceName)}]";
    }

    private string GetWindowTooltip(MovableWindowInfo window)
    {
        return _appWindowCounts.TryGetValue(window.AppName, out var appCount) && appCount <= 1
            ? window.AppName
            : _text.OverlayWindowTooltip(window.AppName, window.Title);
    }

    private static int GetMonitorNumber(string screenDeviceName)
    {
        var screens = GetOrderedScreens();
        var index = Array.FindIndex(screens, screen => screen.DeviceName == screenDeviceName);
        return index >= 0 ? index + 1 : 0;
    }

    private static Screen[] GetOrderedScreens()
    {
        return Screen.AllScreens
            .OrderBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();
    }

    private sealed class TileState
    {
        public TileState(Panel tile, MovableWindowInfo window)
        {
            Tile = tile;
            Window = window;
        }

        public Panel Tile { get; }
        public MovableWindowInfo Window { get; }
        public bool PendingDrag { get; set; }
        public Point DragStart { get; set; }
    }

    private sealed class TileZone
    {
        public TileZone(string deviceName)
        {
            DeviceName = deviceName;
        }

        public string DeviceName { get; }
        public Panel Container { get; set; } = null!;
        public FlowLayoutPanel Flow { get; set; } = null!;
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

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
        }
    }
}
