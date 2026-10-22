using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TarkovPveCompanion
{
    internal sealed class BattlePassBrowser : UserControl
    {
        private readonly ListBox _locations = new ListBox();
        private readonly ComboBox _floors = new ComboBox();
        private readonly ListBox _floorList = new ListBox();
        private readonly CheckBox _typeA = new CheckBox();
        private readonly CheckBox _typeB = new CheckBox();
        private readonly CheckBox _contrastIcons = new CheckBox();
        private readonly CheckBox _floorArrows = new CheckBox();
        private readonly CheckBox _allFloors = new CheckBox();
        private readonly KordOptionRow _filterRowA = new KordOptionRow();
        private readonly KordOptionRow _filterRowB = new KordOptionRow();
        private readonly KordOptionRow _arrowsRow = new KordOptionRow();
        private readonly KordOptionRow _allFloorsRow = new KordOptionRow();
        private readonly KordOptionRow _contrastRow = new KordOptionRow();
        private readonly ComboBox _mapPicker = new ComboBox();
        private readonly Label _status = new Label();
        private readonly Label _visibleCount = new Label();
        private readonly Label _detailType = new Label();
        private readonly Label _detailText = new Label();
        private readonly PictureBox _screenshot = new PictureBox();
        private readonly DocumentMapCanvas _canvas = new DocumentMapCanvas();
        private BattlePassAssetStore _store;
        private List<BattleMapIndex> _maps;
        private List<BattleMapPoint> _points;
        private BattleMapIndex _currentMap;
        private Image _detailImage;
        private string _typeAName;
        private string _typeBName;
        private string _requestedMap;
        private readonly HashSet<string> _enabledTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _initializing;
        private bool _initialized;
        private bool _portraitLayout;
        private bool _applyingLayout;
        private bool _syncingControls;
        private bool _syncingFloors;
        private SplitContainer _outer;
        private SplitContainer _content;
        private TableLayoutPanel _mapLayout;
        private Panel _toolbar;
        private Button _minusButton;
        private Button _plusButton;
        private Button _resetButton;

        public BattlePassBrowser()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            BuildInterface();
            VisibleChanged += delegate { if (Visible) InitializeAsync(); };
            Resize += delegate { ApplyResponsiveLayout(); };
            _canvas.PointSelected += ShowPoint;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_detailImage != null) _detailImage.Dispose();
                _canvas.DisposeMap();
            }
            base.Dispose(disposing);
        }

        private void BuildInterface()
        {
            TableLayoutPanel rootLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(rootLayout);

            Panel titleBar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel2, Padding = new Padding(16, 8, 12, 6), Margin = Padding.Empty };
            Label title = new Label { AutoSize = true, Text = "ДОКУМЕНТЫ BATTLE PASS", Font = Theme.CardTitle, ForeColor = Theme.Teal, Location = new Point(16, 7) };
            _status.AutoSize = false;
            _status.Text = "Офлайн-карты • 12 локаций • 349 подтверждённых спаунов";
            _status.ForeColor = Theme.Muted;
            _status.Location = new Point(17, 31);
            _status.Size = new Size(650, 20);
            titleBar.Controls.Add(title);
            titleBar.Controls.Add(_status);

            Button sources = MapButton("ИСТОЧНИКИ", Theme.Card);
            sources.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            sources.Size = new Size(112, 34);
            sources.Location = new Point(titleBar.Width - 124, 10);
            sources.Click += delegate { ShowSources(); };
            titleBar.Controls.Add(sources);
            titleBar.Resize += delegate
            {
                sources.Left = titleBar.ClientSize.Width - sources.Width - 12;
                _mapPicker.Left = sources.Left - _mapPicker.Width - 12;
                _mapPicker.Top = 13;
            };

            _mapPicker.DropDownStyle = ComboBoxStyle.DropDownList;
            _mapPicker.FlatStyle = FlatStyle.Flat;
            _mapPicker.BackColor = Theme.Card;
            _mapPicker.ForeColor = Theme.Text;
            _mapPicker.Size = new Size(190, 28);
            _mapPicker.Visible = false;
            _mapPicker.SelectedIndexChanged += delegate
            {
                if (_mapPicker.Visible && _mapPicker.SelectedIndex >= 0 && _locations.SelectedIndex != _mapPicker.SelectedIndex)
                    _locations.SelectedIndex = _mapPicker.SelectedIndex;
            };
            titleBar.Controls.Add(_mapPicker);
            rootLayout.Controls.Add(titleBar, 0, 0);

            _outer = new SplitContainer();
            _outer.Dock = DockStyle.Fill;
            _outer.Orientation = Orientation.Vertical;
            _outer.SplitterWidth = 1;
            _outer.BackColor = Theme.Border;
            _outer.Margin = Padding.Empty;
            rootLayout.Controls.Add(_outer, 0, 1);
            _outer.Resize += delegate
            {
                if (!_portraitLayout && _outer.ClientSize.Width > 560 && _outer.SplitterDistance != 250) _outer.SplitterDistance = 250;
            };

            TableLayoutPanel sideLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = Padding.Empty, Padding = Padding.Empty };
            sideLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 166));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _outer.Panel1.Controls.Add(sideLayout);

            Panel sideHeader = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Padding.Empty };
            sideHeader.Controls.Add(new Label { AutoSize = true, Text = "ЛОКАЦИИ", Font = Theme.SmallBold, ForeColor = Theme.Accent, Location = new Point(14, 14) });
            _outer.Panel1.BackColor = Theme.Panel;
            sideLayout.Controls.Add(sideHeader, 0, 0);

            _locations.Dock = DockStyle.None;
            _locations.BackColor = Theme.Panel;
            _locations.ForeColor = Theme.Text;
            _locations.BorderStyle = BorderStyle.None;
            _locations.IntegralHeight = false;
            _locations.ItemHeight = 42;
            _locations.DrawMode = DrawMode.OwnerDrawFixed;
            _locations.Margin = Padding.Empty;
            _locations.DrawItem += DrawLocation;
            _locations.SelectedIndexChanged += delegate
            {
                if (_mapPicker.SelectedIndex != _locations.SelectedIndex) _mapPicker.SelectedIndex = _locations.SelectedIndex;
                LoadSelectedMap();
            };
            sideLayout.Controls.Add(ClippedListHost(_locations), 0, 1);

            sideLayout.Controls.Add(SectionHeader("ФИЛЬТР ДОКУМЕНТОВ"), 0, 2);
            Panel filterPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Padding.Empty };
            _filterRowA.Bounds = new Rectangle(0, 0, 250, 52);
            _filterRowA.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _filterRowB.Bounds = new Rectangle(0, 52, 250, 52);
            _filterRowB.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _filterRowA.CheckedChanged += delegate { if (!_syncingControls) SetTypeEnabled(_typeAName, _filterRowA.Checked); };
            _filterRowB.CheckedChanged += delegate { if (!_syncingControls) SetTypeEnabled(_typeBName, _filterRowB.Checked); };
            filterPanel.Controls.Add(_filterRowA);
            filterPanel.Controls.Add(_filterRowB);
            filterPanel.Resize += delegate { _filterRowA.Width = filterPanel.ClientSize.Width; _filterRowB.Width = filterPanel.ClientSize.Width; };
            sideLayout.Controls.Add(filterPanel, 0, 3);

            sideLayout.Controls.Add(SectionHeader("ОТОБРАЖЕНИЕ"), 0, 4);
            Panel displayPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Padding.Empty };
            _arrowsRow.Configure("СТРЕЛКИ ЭТАЖЕЙ", -1, null);
            _allFloorsRow.Configure("ИГНОРИРОВАТЬ ЭТАЖИ", -1, null);
            _contrastRow.Configure("КОНТРАСТ ИКОНОК", -1, null);
            _arrowsRow.Checked = true;
            _contrastRow.Checked = true;
            _arrowsRow.Bounds = new Rectangle(0, 0, 250, 34);
            _allFloorsRow.Bounds = new Rectangle(0, 34, 250, 34);
            _contrastRow.Bounds = new Rectangle(0, 68, 250, 34);
            foreach (KordOptionRow row in new[] { _arrowsRow, _allFloorsRow, _contrastRow }) row.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _arrowsRow.CheckedChanged += delegate { if (!_syncingControls) SetFloorArrows(_arrowsRow.Checked); };
            _allFloorsRow.CheckedChanged += delegate { if (!_syncingControls) SetAllFloorsState(_allFloorsRow.Checked); };
            _contrastRow.CheckedChanged += delegate { if (!_syncingControls) SetContrastIcons(_contrastRow.Checked); };
            displayPanel.Controls.Add(_arrowsRow);
            displayPanel.Controls.Add(_allFloorsRow);
            displayPanel.Controls.Add(_contrastRow);
            displayPanel.Resize += delegate { foreach (Control row in displayPanel.Controls) row.Width = displayPanel.ClientSize.Width; };
            sideLayout.Controls.Add(displayPanel, 0, 5);

            sideLayout.Controls.Add(SectionHeader("ЭТАЖИ"), 0, 6);
            _floorList.Dock = DockStyle.None;
            _floorList.BackColor = Theme.Panel;
            _floorList.ForeColor = Theme.Muted;
            _floorList.BorderStyle = BorderStyle.None;
            _floorList.IntegralHeight = false;
            _floorList.ItemHeight = 42;
            _floorList.DrawMode = DrawMode.OwnerDrawFixed;
            _floorList.DrawItem += DrawFloor;
            _floorList.SelectedIndexChanged += delegate
            {
                if (_syncingFloors || _floorList.SelectedIndex < 0) return;
                _syncingFloors = true;
                _floors.SelectedIndex = _floorList.SelectedIndex;
                _syncingFloors = false;
            };
            sideLayout.Controls.Add(ClippedListHost(_floorList), 0, 7);

            _content = new SplitContainer();
            _content.Dock = DockStyle.Fill;
            _content.Orientation = Orientation.Vertical;
            _content.SplitterWidth = 1;
            _content.BackColor = Theme.Border;
            _outer.Panel2.Controls.Add(_content);
            _content.Resize += delegate
            {
                ApplyResponsiveLayout();
            };

            _mapLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            _mapLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _mapLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            _mapLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _content.Panel1.Controls.Add(_mapLayout);

            _toolbar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(10, 8, 8, 7), Margin = Padding.Empty };
            _mapLayout.Controls.Add(_toolbar, 0, 0);
            _floors.DropDownStyle = ComboBoxStyle.DropDownList;
            _floors.FlatStyle = FlatStyle.Flat;
            _floors.BackColor = Theme.Card;
            _floors.ForeColor = Theme.Text;
            _floors.Location = new Point(10, 10);
            _floors.Size = new Size(112, 28);
            _floors.SelectedIndexChanged += delegate
            {
                if (!_syncingFloors && _floorList.SelectedIndex != _floors.SelectedIndex)
                {
                    _syncingFloors = true;
                    _floorList.SelectedIndex = _floors.SelectedIndex;
                    _syncingFloors = false;
                }
                LoadSelectedFloor();
            };
            _toolbar.Controls.Add(_floors);

            ConfigureFilter(_typeA, "ДОКУМЕНТ 1", Theme.Good, 136);
            ConfigureFilter(_typeB, "ДОКУМЕНТ 2", Theme.Teal, 306);
            ConfigureFilter(_allFloors, "ВСЕ ЭТАЖИ", Theme.Good, 10);
            ConfigureFilter(_contrastIcons, "КОНТРАСТ ИКОНОК", Theme.Text, 160);
            ConfigureFilter(_floorArrows, "СТРЕЛКИ ЭТАЖЕЙ", Theme.Text, 330);
            _typeA.Checked = true;
            _typeB.Checked = true;
            _contrastIcons.Checked = true;
            _floorArrows.Checked = true;
            _typeA.CheckedChanged += delegate { if (!_syncingControls) SetTypeEnabled(_typeAName, _typeA.Checked); };
            _typeB.CheckedChanged += delegate { if (!_syncingControls) SetTypeEnabled(_typeBName, _typeB.Checked); };
            _allFloors.CheckedChanged += delegate { if (!_syncingControls) SetAllFloorsState(_allFloors.Checked); };
            _contrastIcons.CheckedChanged += delegate { if (!_syncingControls) SetContrastIcons(_contrastIcons.Checked); };
            _floorArrows.CheckedChanged += delegate { if (!_syncingControls) SetFloorArrows(_floorArrows.Checked); };
            _toolbar.Controls.Add(_typeA);
            _toolbar.Controls.Add(_typeB);
            _toolbar.Controls.Add(_allFloors);
            _toolbar.Controls.Add(_contrastIcons);
            _toolbar.Controls.Add(_floorArrows);

            _minusButton = MapButton("−", Theme.Card);
            _minusButton.Size = new Size(34, 30);
            _minusButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _minusButton.Location = new Point(_toolbar.Width - 170, 8);
            _minusButton.Font = Theme.Heading;
            _minusButton.Click += delegate { _canvas.ChangeZoom(0.8f); };
            _toolbar.Controls.Add(_minusButton);

            _plusButton = MapButton("+", Theme.Card);
            _plusButton.Size = new Size(34, 30);
            _plusButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _plusButton.Location = new Point(_toolbar.Width - 132, 8);
            _plusButton.Font = Theme.Heading;
            _plusButton.Click += delegate { _canvas.ChangeZoom(1.25f); };
            _toolbar.Controls.Add(_plusButton);

            _resetButton = MapButton("СБРОС", Theme.Card);
            _resetButton.Size = new Size(84, 30);
            _resetButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _resetButton.Location = new Point(_toolbar.Width - 94, 8);
            _resetButton.Click += delegate { _canvas.ResetView(); };
            _toolbar.Controls.Add(_resetButton);
            _toolbar.Resize += delegate
            {
                ApplyToolbarLayout();
            };

            _canvas.Dock = DockStyle.Fill;
            _canvas.Margin = Padding.Empty;
            _mapLayout.Controls.Add(_canvas, 0, 1);

            BuildDetails(_content.Panel2);
            _content.Panel2Collapsed = true;
            ApplyResponsiveLayout();
        }

        private void BuildDetails(Control parent)
        {
            parent.BackColor = Theme.Panel;
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(18, 16, 18, 12);
            table.ColumnCount = 1;
            table.RowCount = 7;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            parent.Controls.Add(table);

            Panel detailHeader = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Padding.Empty };
            detailHeader.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "ТОЧКА ДОКУМЕНТА", Font = Theme.SmallBold, ForeColor = Theme.Accent, TextAlign = ContentAlignment.MiddleLeft });
            Button closeDetails = MapButton("×", Theme.Card);
            closeDetails.Dock = DockStyle.Right;
            closeDetails.Width = 30;
            closeDetails.Font = Theme.Heading;
            closeDetails.Click += delegate { _content.Panel2Collapsed = true; };
            detailHeader.Controls.Add(closeDetails);
            closeDetails.BringToFront();
            table.Controls.Add(detailHeader, 0, 0);
            _detailType.Dock = DockStyle.Fill;
            _detailType.Font = Theme.SmallBold;
            _detailType.ForeColor = Theme.Teal;
            _detailType.Text = "Выберите метку на карте";
            table.Controls.Add(_detailType, 0, 1);

            _detailText.Dock = DockStyle.Fill;
            _detailText.ForeColor = Theme.Text;
            _detailText.Text = "Нажмите на цветную метку — справа появятся описание и скриншот места спауна.";
            table.Controls.Add(_detailText, 0, 2);

            _screenshot.Dock = DockStyle.Fill;
            _screenshot.BackColor = Theme.Card;
            _screenshot.BorderStyle = BorderStyle.FixedSingle;
            _screenshot.SizeMode = PictureBoxSizeMode.Zoom;
            _screenshot.Cursor = Cursors.Hand;
            _screenshot.Click += delegate { ShowLargeScreenshot(); };
            table.Controls.Add(_screenshot, 0, 3);

            _visibleCount.Dock = DockStyle.Fill;
            _visibleCount.ForeColor = Theme.Muted;
            _visibleCount.TextAlign = ContentAlignment.MiddleLeft;
            table.Controls.Add(_visibleCount, 0, 4);

            LinkLabel mapSource = SourceLink("KordMap • Метадвижение", "https://kord-map-mtdv.ru/");
            table.Controls.Add(mapSource, 0, 5);
            LinkLabel license = SourceLink("Карты • CC BY-NC-SA 4.0", "https://creativecommons.org/licenses/by-nc-sa/4.0/");
            table.Controls.Add(license, 0, 6);
        }

        private void ApplyResponsiveLayout()
        {
            if (_applyingLayout || _outer == null || _content == null || _mapLayout == null) return;
            _applyingLayout = true;
            try
            {
                _portraitLayout = Height > Width || Width < 900;
                _outer.Panel1Collapsed = _portraitLayout;
                _mapPicker.Visible = _portraitLayout;
                _status.Visible = !_portraitLayout;

                Orientation desired = _portraitLayout ? Orientation.Horizontal : Orientation.Vertical;
                if (_content.Orientation != desired) _content.Orientation = desired;
                _content.FixedPanel = FixedPanel.Panel2;
                if (_portraitLayout && !_content.Panel2Collapsed)
                {
                    if (_content.ClientSize.Height > 420)
                    {
                        int detailHeight = Math.Max(220, Math.Min(360, (int)(_content.ClientSize.Height * 0.36)));
                        int distance = _content.ClientSize.Height - detailHeight;
                        if (distance > 0) _content.SplitterDistance = distance;
                    }
                }
                else if (!_portraitLayout)
                {
                    if (_outer.ClientSize.Width > 560) _outer.SplitterDistance = 250;
                    if (!_content.Panel2Collapsed && _content.ClientSize.Width > 650)
                    {
                        int distance = Math.Max(350, _content.ClientSize.Width - 320);
                        distance = Math.Min(_content.ClientSize.Width - 160, distance);
                        if (distance > 0) _content.SplitterDistance = distance;
                    }
                }
                ApplyToolbarLayout();
            }
            finally
            {
                _applyingLayout = false;
            }
        }

        private void ApplyToolbarLayout()
        {
            if (_toolbar == null || _mapLayout == null || _resetButton == null) return;
            bool compact = _portraitLayout;
            _mapLayout.RowStyles[0].Height = compact ? 86 : 48;
            foreach (Control control in new Control[] { _floors, _typeA, _typeB, _allFloors, _contrastIcons, _floorArrows }) control.Visible = compact;
            _floors.Location = new Point(10, 10);
            if (compact)
            {
                _typeA.Location = new Point(136, 12);
                _typeB.Location = new Point(332, 12);
                _allFloors.Location = new Point(10, 52);
                _contrastIcons.Location = new Point(166, 52);
                _floorArrows.Location = new Point(356, 52);
                _resetButton.Top = 47;
                _plusButton.Top = 47;
                _minusButton.Top = 47;
            }
            else
            {
                _resetButton.Top = 8;
                _plusButton.Top = 8;
                _minusButton.Top = 8;
            }
            _resetButton.Left = _toolbar.ClientSize.Width - _resetButton.Width - 10;
            _plusButton.Left = _resetButton.Left - _plusButton.Width - 4;
            _minusButton.Left = _plusButton.Left - _minusButton.Width - 4;
        }

        private async void InitializeAsync()
        {
            if (_initialized || _initializing || !Visible) return;
            _initializing = true;
            _status.Text = "Распаковываю офлайн-карты…";
            try
            {
                BattlePassAssetStore store = null;
                List<BattleMapIndex> maps = null;
                await Task.Run(delegate
                {
                    store = BattlePassAssetStore.Open();
                    maps = store.LoadIndex();
                });
                _store = store;
                _maps = maps;
                _locations.Items.Clear();
                _mapPicker.Items.Clear();
                foreach (BattleMapIndex map in maps)
                {
                    _locations.Items.Add(map);
                    _mapPicker.Items.Add(map);
                }
                _status.ForeColor = Theme.Muted;
                _status.Text = string.Format("Офлайн-карты • {0} локаций • {1} подтверждённых спаунов", maps.Count, maps.Sum(delegate(BattleMapIndex x) { return x.points; }));
                _initialized = true;
                int selected = FindMapIndex(_requestedMap);
                if (selected < 0 && _locations.Items.Count > 0) selected = 0;
                if (selected >= 0) _locations.SelectedIndex = selected;
            }
            catch (Exception ex)
            {
                _status.ForeColor = Theme.Bad;
                _status.Text = "Не удалось открыть встроенные карты";
                MessageBox.Show(FindForm(), "Не удалось подготовить офлайн-карты.\r\n\r\n" + ex.Message, "Документы Battle Pass", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _initializing = false;
            }
        }

        private void LoadSelectedMap()
        {
            if (!_initialized || _locations.SelectedItem == null) return;
            try
            {
                _currentMap = (BattleMapIndex)_locations.SelectedItem;
                _points = _store.LoadPoints(_currentMap);
                List<string> types = _points.Select(delegate(BattleMapPoint point) { return point.iconType; })
                    .Where(delegate(string type) { return !string.IsNullOrWhiteSpace(type); })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(delegate(string type) { return type; }, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _typeAName = types.Count > 0 ? types[0] : null;
                _typeBName = types.Count > 1 ? types[1] : null;

                _syncingControls = true;
                _enabledTypes.Clear();
                foreach (string type in types) _enabledTypes.Add(type);
                ConfigureType(_filterRowA, _typeA, _typeAName);
                ConfigureType(_filterRowB, _typeB, _typeBName);
                _syncingControls = false;

                Dictionary<string, Image> icons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
                foreach (string type in types) icons[type] = _store.LoadIcon(type);
                _canvas.SetTypeIcons(icons);

                _syncingFloors = true;
                _floors.Items.Clear();
                _floorList.Items.Clear();
                foreach (BattleFloor floor in _currentMap.floors)
                {
                    _floors.Items.Add(floor);
                    _floorList.Items.Add(floor);
                }
                int selected = _currentMap.floors.FindIndex(delegate(BattleFloor floor) { return string.Equals(floor.id, "floor-1", StringComparison.Ordinal); });
                if (selected < 0 && _floors.Items.Count > 0) selected = 0;
                if (selected >= 0)
                {
                    _floors.SelectedIndex = selected;
                    _floorList.SelectedIndex = selected;
                }
                _syncingFloors = false;
                LoadSelectedFloor();
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), ex.Message, "Карта документов", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LoadSelectedFloor()
        {
            if (_store == null || _currentMap == null || _floors.SelectedItem == null) return;
            BattleFloor floor = (BattleFloor)_floors.SelectedItem;
            Image image = _store.LoadMapImage(_currentMap, floor);
            _canvas.SetMap(image, _currentMap.name, _currentMap.viewWidth, _currentMap.viewHeight, floor.id, _points);
            ApplyFilters();
            ClearPoint();
        }

        private void ApplyFilters()
        {
            _canvas.SetFilters(_enabledTypes);
            _canvas.SetIgnoreFloors(_allFloors.Checked);
            _canvas.SetContrastIcons(_contrastIcons.Checked);
            _canvas.SetFloorArrows(_floorArrows.Checked);
            _visibleCount.Text = _allFloors.Checked
                ? string.Format("Все этажи: {0} точек", _canvas.VisiblePointCount)
                : string.Format("На этаже: {0} точек", _canvas.VisiblePointCount);
        }

        internal void SetAllFloors(bool enabled)
        {
            SetAllFloorsState(enabled);
        }

        internal void SelectMap(string mapName)
        {
            _requestedMap = mapName;
            if (!_initialized) return;
            int index = FindMapIndex(mapName);
            if (index >= 0) _locations.SelectedIndex = index;
        }

        private int FindMapIndex(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName)) return -1;
            for (int i = 0; i < _locations.Items.Count; i++)
            {
                BattleMapIndex map = _locations.Items[i] as BattleMapIndex;
                if (map != null && (string.Equals(map.name, mapName, StringComparison.CurrentCultureIgnoreCase) || string.Equals(map.slug, mapName, StringComparison.OrdinalIgnoreCase))) return i;
            }
            return -1;
        }

        private void ConfigureType(KordOptionRow row, CheckBox compact, string type)
        {
            bool exists = !string.IsNullOrWhiteSpace(type);
            row.Visible = exists;
            compact.Visible = exists && _portraitLayout;
            if (!exists) return;
            int count = _points.Count(delegate(BattleMapPoint point) { return string.Equals(point.iconType, type, StringComparison.OrdinalIgnoreCase); });
            row.Configure(type, count, _store.LoadIcon(type));
            row.Checked = true;
            compact.Text = ShortTypeName(type);
            compact.ForeColor = DocumentTypeStyle.PinColor(type);
            compact.Checked = true;
        }

        private static string ShortTypeName(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "ДОКУМЕНТ";
            string text = type.Replace(" документация", "").Replace("Документация", "").Trim();
            if (text.StartsWith("Чертежи", StringComparison.OrdinalIgnoreCase)) text = "ЧЕРТЕЖИ И ТЕХ.";
            return text.ToUpperInvariant();
        }

        private void SetTypeEnabled(string type, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(type)) return;
            if (enabled) _enabledTypes.Add(type); else _enabledTypes.Remove(type);
            _syncingControls = true;
            if (string.Equals(type, _typeAName, StringComparison.OrdinalIgnoreCase)) { _typeA.Checked = enabled; _filterRowA.Checked = enabled; }
            if (string.Equals(type, _typeBName, StringComparison.OrdinalIgnoreCase)) { _typeB.Checked = enabled; _filterRowB.Checked = enabled; }
            _syncingControls = false;
            ApplyFilters();
        }

        private void SetAllFloorsState(bool enabled)
        {
            _syncingControls = true;
            _allFloors.Checked = enabled;
            _allFloorsRow.Checked = enabled;
            _syncingControls = false;
            ApplyFilters();
        }

        private void SetContrastIcons(bool enabled)
        {
            _syncingControls = true;
            _contrastIcons.Checked = enabled;
            _contrastRow.Checked = enabled;
            _syncingControls = false;
            ApplyFilters();
        }

        private void SetFloorArrows(bool enabled)
        {
            _syncingControls = true;
            _floorArrows.Checked = enabled;
            _arrowsRow.Checked = enabled;
            _syncingControls = false;
            ApplyFilters();
        }

        private void ShowPoint(BattleMapPoint point)
        {
            if (_detailImage != null)
            {
                _screenshot.Image = null;
                _detailImage.Dispose();
                _detailImage = null;
            }
            if (_content.Panel2Collapsed)
            {
                _content.Panel2Collapsed = false;
                ApplyResponsiveLayout();
            }
            _detailType.Text = point.iconType + " • Этаж " + FloorName(point.floor);
            _detailType.ForeColor = DocumentTypeStyle.PinColor(point.iconType);
            _detailText.Text = string.IsNullOrWhiteSpace(point.details) ? "Описание места не добавлено." : point.details;
            _detailImage = point.DecodeImage();
            _screenshot.Image = _detailImage;
        }

        private void ClearPoint()
        {
            if (_detailImage != null)
            {
                _screenshot.Image = null;
                _detailImage.Dispose();
                _detailImage = null;
            }
            _detailType.Text = "Выберите метку на карте";
            _detailType.ForeColor = Theme.Teal;
            _detailText.Text = "Нажмите на цветную метку — справа появятся описание и скриншот места спауна.";
            if (_content != null) _content.Panel2Collapsed = true;
        }

        private static string FloorName(string floor)
        {
            return string.IsNullOrWhiteSpace(floor) ? "?" : floor.Replace("floor-", "");
        }

        private void ShowLargeScreenshot()
        {
            if (_detailImage == null) return;
            Form dialog = new Form();
            dialog.Text = "Скриншот места спауна документа";
            dialog.BackColor = Theme.Background;
            dialog.ForeColor = Theme.Text;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.Size = new Size(1050, 760);
            dialog.MinimumSize = new Size(640, 480);
            PictureBox picture = new PictureBox { Dock = DockStyle.Fill, BackColor = Theme.Background, SizeMode = PictureBoxSizeMode.Zoom, Image = new Bitmap(_detailImage) };
            dialog.Controls.Add(picture);
            dialog.FormClosed += delegate { if (picture.Image != null) picture.Image.Dispose(); };
            dialog.ShowDialog(FindForm());
        }

        private void ShowSources()
        {
            string text = "Точки и скриншоты: Тарковское Метадвижение\r\n" +
                          "Исходный проект: KalleLeskinen/KordMap\r\n" +
                          "SVG-карты: the-hideout/tarkov-dev-svg-maps, Shebuka\r\n" +
                          "Icebreaker и Labyrinth: RE3MR\r\n\r\n" +
                          "Лицензия материалов: CC BY-NC-SA 4.0.\r\n" +
                          "Приложение неофициальное и не связано с Battlestate Games.";
            if (MessageBox.Show(FindForm(), text + "\r\n\r\nОткрыть репозиторий KordMap?", "Источники и лицензия", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                OpenUrl("https://github.com/KalleLeskinen/KordMap");
        }

        private void DrawLocation(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= _locations.Items.Count) return;
            BattleMapIndex map = (BattleMapIndex)_locations.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush brush = new SolidBrush(selected ? Theme.Selection : Theme.Panel)) e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected) using (SolidBrush line = new SolidBrush(Theme.Teal)) e.Graphics.FillRectangle(line, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, map.name, Theme.SmallBold, new Rectangle(e.Bounds.Left + 13, e.Bounds.Top + 5, e.Bounds.Width - 25, 18), selected ? Theme.Text : Theme.Muted, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, string.Format("{0} точек", map.points), Theme.Small, new Rectangle(e.Bounds.Left + 13, e.Bounds.Top + 23, e.Bounds.Width - 25, 16), Theme.Faint, TextFormatFlags.EndEllipsis);
        }

        private void DrawFloor(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= _floorList.Items.Count) return;
            BattleFloor floor = (BattleFloor)_floorList.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush brush = new SolidBrush(selected ? Color.FromArgb(7, 9, 10) : Theme.Panel)) e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected) using (SolidBrush line = new SolidBrush(Theme.Teal)) e.Graphics.FillRectangle(line, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, floor.ToString(), Theme.SmallBold, new Rectangle(e.Bounds.Left + 16, e.Bounds.Top, e.Bounds.Width - 24, e.Bounds.Height), selected ? Theme.Teal : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static Panel SectionHeader(string text)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(8, 9, 10), Margin = Padding.Empty };
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 0, 0, 0),
                Text = text,
                Font = Theme.SmallBold,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.MiddleLeft
            });
            return panel;
        }

        private static Panel ClippedListHost(ListBox list)
        {
            Panel host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Padding.Empty };
            host.Controls.Add(list);
            Action fit = delegate
            {
                list.Location = Point.Empty;
                list.Size = new Size(host.ClientSize.Width + SystemInformation.VerticalScrollBarWidth, host.ClientSize.Height);
            };
            host.Resize += delegate { fit(); };
            fit();
            return host;
        }

        private void ConfigureFilter(CheckBox box, string text, Color color, int left)
        {
            box.AutoSize = false;
            box.Size = new Size(184, 24);
            box.Text = text;
            box.Font = Theme.SmallBold;
            box.ForeColor = color;
            box.BackColor = Theme.Panel;
            box.Location = new Point(left, 14);
        }

        private static Button MapButton(string text, Color color)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = color == Theme.Card ? Theme.Border : color;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = Theme.Card;
            button.ForeColor = color == Theme.Card ? Theme.Text : color;
            button.Font = Theme.Button;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private static LinkLabel SourceLink(string text, string url)
        {
            LinkLabel link = new LinkLabel();
            link.Dock = DockStyle.Fill;
            link.Text = text;
            link.Font = Theme.Small;
            link.LinkColor = Theme.Muted;
            link.ActiveLinkColor = Theme.Teal;
            link.VisitedLinkColor = Theme.Muted;
            link.TextAlign = ContentAlignment.MiddleLeft;
            link.LinkClicked += delegate { OpenUrl(url); };
            return link;
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Не удалось открыть ссылку", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    internal sealed class KordOptionRow : Control
    {
        private bool _checked = true;
        private bool _hovered;
        private Image _icon;
        private string _caption = "";
        private int _count = -1;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public KordOptionRow()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            BackColor = Theme.Panel;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public void Configure(string caption, int count, Image icon)
        {
            if (_icon != null) _icon.Dispose();
            _caption = caption ?? "";
            _count = count;
            _icon = icon;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _icon != null) { _icon.Dispose(); _icon = null; }
            base.Dispose(disposing);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(_hovered ? Theme.Card : Theme.Panel);
            int boxTop = (Height - 16) / 2;
            Rectangle box = new Rectangle(12, boxTop, 16, 16);
            using (SolidBrush fill = new SolidBrush(_checked ? Theme.Teal : Theme.Card)) e.Graphics.FillRectangle(fill, box);
            using (Pen border = new Pen(_checked ? Theme.Teal : Theme.Border)) e.Graphics.DrawRectangle(border, box);
            if (_checked)
            {
                using (Pen tick = new Pen(Color.FromArgb(8, 15, 15), 2f))
                {
                    tick.StartCap = LineCap.Round;
                    tick.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(tick, new[] { new Point(15, boxTop + 8), new Point(19, boxTop + 12), new Point(25, boxTop + 4) });
                }
            }

            int textLeft = 40;
            if (_icon != null)
            {
                Rectangle iconBounds = new Rectangle(38, Math.Max(2, (Height - 36) / 2), 30, 36);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                DrawContained(e.Graphics, _icon, iconBounds, 1f, false);
                textLeft = 78;
            }
            string text = _caption + (_count >= 0 ? " (" + _count + ")" : "");
            TextRenderer.DrawText(e.Graphics, text, Theme.SmallBold, new Rectangle(textLeft, 2, Width - textLeft - 8, Height - 4), _checked ? Theme.Text : Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }

        internal static void DrawContained(Graphics graphics, Image image, RectangleF bounds, float alpha, bool grayscale)
        {
            if (image == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            float scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
            RectangleF target = new RectangleF(bounds.Left + (bounds.Width - image.Width * scale) / 2f, bounds.Top + (bounds.Height - image.Height * scale) / 2f, image.Width * scale, image.Height * scale);
            using (ImageAttributes attributes = new ImageAttributes())
            {
                if (grayscale)
                {
                    attributes.SetColorMatrix(new ColorMatrix(new float[][]
                    {
                        new float[] { .30f, .30f, .30f, 0, 0 },
                        new float[] { .59f, .59f, .59f, 0, 0 },
                        new float[] { .11f, .11f, .11f, 0, 0 },
                        new float[] { 0, 0, 0, alpha, 0 },
                        new float[] { 0, 0, 0, 0, 1 }
                    }));
                }
                else if (alpha < 1f)
                {
                    attributes.SetColorMatrix(new ColorMatrix(new float[][]
                    {
                        new float[] { 1, 0, 0, 0, 0 }, new float[] { 0, 1, 0, 0, 0 }, new float[] { 0, 0, 1, 0, 0 },
                        new float[] { 0, 0, 0, alpha, 0 }, new float[] { 0, 0, 0, 0, 1 }
                    }));
                }
                graphics.DrawImage(image, Rectangle.Round(target), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }
        }
    }

    internal sealed class DocumentMapCanvas : Control
    {
        private Image _map;
        private readonly Dictionary<string, Image> _typeIcons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _enabledTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<BattleMapPoint> _points = new List<BattleMapPoint>();
        private string _floor;
        private string _mapName;
        private double _viewWidth = 1;
        private double _viewHeight = 1;
        private float _zoom = 1f;
        private PointF _pan = PointF.Empty;
        private Point _dragStart;
        private bool _dragging;
        private bool _ignoreFloors;
        private bool _contrastIcons = true;
        private bool _floorArrows = true;
        private BattleMapPoint _selected;

        public event Action<BattleMapPoint> PointSelected;
        public int VisiblePointCount { get { return VisiblePoints().Count(); } }

        public DocumentMapCanvas()
        {
            DoubleBuffered = true;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeMap();
                DisposeIcons();
            }
            base.Dispose(disposing);
        }

        public void SetMap(Image image, string mapName, double viewWidth, double viewHeight, string floor, List<BattleMapPoint> points)
        {
            DisposeMap();
            _map = image;
            _viewWidth = Math.Max(1, viewWidth);
            _viewHeight = Math.Max(1, viewHeight);
            _floor = floor;
            _mapName = mapName;
            _points = points ?? new List<BattleMapPoint>();
            _selected = null;
            ResetView();
        }

        public void DisposeMap()
        {
            if (_map != null) _map.Dispose();
            _map = null;
        }

        private void DisposeIcons()
        {
            foreach (Image icon in _typeIcons.Values) icon.Dispose();
            _typeIcons.Clear();
        }

        public void SetTypeIcons(Dictionary<string, Image> icons)
        {
            DisposeIcons();
            if (icons != null) foreach (KeyValuePair<string, Image> pair in icons) _typeIcons[pair.Key] = pair.Value;
            Invalidate();
        }

        public void SetFilters(IEnumerable<string> types)
        {
            _enabledTypes.Clear();
            if (types != null) foreach (string type in types) if (!string.IsNullOrWhiteSpace(type)) _enabledTypes.Add(type);
            if (_selected != null && !IsVisible(_selected)) _selected = null;
            Invalidate();
        }

        public void SetIgnoreFloors(bool ignore) { _ignoreFloors = ignore; Invalidate(); }
        public void SetContrastIcons(bool enabled) { _contrastIcons = enabled; Invalidate(); }
        public void SetFloorArrows(bool enabled) { _floorArrows = enabled; Invalidate(); }
        public void ResetView() { _zoom = 1f; _pan = PointF.Empty; Invalidate(); }
        public void ChangeZoom(float factor) { _zoom = Math.Max(0.5f, Math.Min(6f, _zoom * factor)); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Theme.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (_map == null)
            {
                TextRenderer.DrawText(e.Graphics, "Выберите локацию", Theme.Heading, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            RectangleF destination = Destination();
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(_map, destination);

            List<BattleMapPoint> visible = VisiblePoints().OrderBy(delegate(BattleMapPoint point) { return string.Equals(point.floor, _floor, StringComparison.Ordinal) ? 1 : 0; }).ToList();
            foreach (BattleMapPoint point in visible) DrawMarker(e.Graphics, point, destination);

            string floorText = (_floor ?? "").Replace("floor-", "");
            string floorCaption = _ignoreFloors ? "ВСЕ ЭТАЖИ" : "ЭТАЖ " + floorText;
            string mapCaption = string.Format("{0}  •  {1}  •  {2} ТОЧЕК", (_mapName ?? "КАРТА").ToUpperInvariant(), floorCaption, visible.Count);
            Size captionSize = TextRenderer.MeasureText(mapCaption, Theme.SmallBold);
            Rectangle caption = new Rectangle(14, 14, captionSize.Width + 22, 28);
            using (SolidBrush captionBack = new SolidBrush(Color.FromArgb(225, Theme.Panel2))) e.Graphics.FillRectangle(captionBack, caption);
            using (Pen captionBorder = new Pen(Theme.Border)) e.Graphics.DrawRectangle(captionBorder, caption);
            TextRenderer.DrawText(e.Graphics, mapCaption, Theme.SmallBold, new Rectangle(caption.Left + 11, caption.Top + 5, caption.Width - 18, 19), Theme.Text, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, "Колесо — масштаб • перетаскивание — обзор • клик — скриншот", Theme.Small, new Rectangle(12, Height - 30, Width - 24, 22), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private void DrawMarker(Graphics graphics, BattleMapPoint point, RectangleF destination)
        {
            PointF anchor = ScreenPoint(point, destination);
            bool ghost = _ignoreFloors && !string.Equals(point.floor, _floor, StringComparison.Ordinal);
            bool selected = point == _selected;
            float width = selected ? 42f : 36f;
            float height = selected ? 50f : 44f;
            float alpha = ghost ? .64f : 1f;
            Color pin = DocumentTypeStyle.PinColor(point.iconType);

            using (GraphicsPath shape = PinPath(anchor, width, height, 3f))
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(ghost ? 70 : 145, 0, 0, 0))) graphics.FillPath(shadow, shape);
            using (GraphicsPath shape = PinPath(anchor, width, height, 0f))
            {
                Color fillColor = Color.FromArgb((int)(alpha * 255), pin);
                using (SolidBrush fill = new SolidBrush(fillColor)) graphics.FillPath(fill, shape);
                Color outline = _contrastIcons ? Color.FromArgb((int)(alpha * 245), Color.White) : Color.FromArgb((int)(alpha * 230), Color.FromArgb(7, 9, 10));
                using (Pen border = new Pen(outline, selected ? 3f : 1.5f)) graphics.DrawPath(border, shape);
            }

            Image icon;
            if (_typeIcons.TryGetValue(point.iconType ?? "", out icon))
            {
                RectangleF iconBounds = new RectangleF(anchor.X - width * .26f, anchor.Y - height * .76f, width * .52f, height * .49f);
                if (_contrastIcons && !ghost)
                {
                    RectangleF back = iconBounds;
                    back.Inflate(2f, 2f);
                    using (SolidBrush halo = new SolidBrush(Color.FromArgb(185, Color.White))) graphics.FillEllipse(halo, back);
                }
                KordOptionRow.DrawContained(graphics, icon, iconBounds, alpha, ghost);
            }

            if (ghost && _floorArrows) DrawFloorArrow(graphics, point, anchor, width);
        }

        private static GraphicsPath PinPath(PointF anchor, float width, float height, float yOffset)
        {
            float left = anchor.X - width / 2f;
            float top = anchor.Y - height + yOffset;
            float right = anchor.X + width / 2f;
            float bodyBottom = top + height * .68f;
            GraphicsPath path = new GraphicsPath();
            path.StartFigure();
            path.AddBezier(anchor.X, top, right, top, right, bodyBottom, anchor.X, top + height);
            path.AddBezier(anchor.X, top + height, left, bodyBottom, left, top, anchor.X, top);
            path.CloseFigure();
            return path;
        }

        private void DrawFloorArrow(Graphics graphics, BattleMapPoint point, PointF anchor, float width)
        {
            bool up = FloorNumber(point.floor) > FloorNumber(_floor);
            RectangleF badge = new RectangleF(anchor.X + width * .13f, anchor.Y - width * 1.15f, 17, 17);
            using (SolidBrush background = new SolidBrush(Color.FromArgb(220, 0, 0, 0))) graphics.FillEllipse(background, badge);
            Color color = up ? Color.FromArgb(34, 197, 94) : Color.FromArgb(239, 68, 68);
            TextRenderer.DrawText(graphics, up ? "▲" : "▼", Theme.SmallBold, Rectangle.Round(badge), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static int FloorNumber(string floor)
        {
            int value;
            return int.TryParse((floor ?? "").Replace("floor-", ""), out value) ? value : 0;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            BattleMapPoint hit = HitTest(e.Location);
            if (hit != null)
            {
                _selected = hit;
                Invalidate();
                if (PointSelected != null) PointSelected(hit);
                return;
            }
            _dragging = true;
            _dragStart = e.Location;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            _pan.X += e.X - _dragStart.X;
            _pan.Y += e.Y - _dragStart.Y;
            _dragStart = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; Cursor = Cursors.Cross; }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ChangeZoom(e.Delta > 0 ? 1.18f : .85f); }

        private BattleMapPoint HitTest(Point point)
        {
            RectangleF destination = Destination();
            foreach (BattleMapPoint candidate in VisiblePoints().Reverse())
            {
                PointF screen = ScreenPoint(candidate, destination);
                double dx = screen.X - point.X;
                double dy = screen.Y - 20 - point.Y;
                if (dx * dx + dy * dy <= 25 * 25) return candidate;
            }
            return null;
        }

        private IEnumerable<BattleMapPoint> VisiblePoints() { return _points.Where(delegate(BattleMapPoint point) { return IsVisible(point); }); }
        private bool IsVisible(BattleMapPoint point) { return MatchesFloor(point.floor, _floor, _ignoreFloors) && _enabledTypes.Contains(point.iconType ?? ""); }
        internal static bool MatchesFloor(string pointFloor, string selectedFloor, bool ignoreFloors) { return ignoreFloors || string.Equals(pointFloor, selectedFloor, StringComparison.Ordinal); }

        private RectangleF Destination()
        {
            if (_map == null) return RectangleF.Empty;
            float availableWidth = Math.Max(1, ClientSize.Width - 28);
            float availableHeight = Math.Max(1, ClientSize.Height - 48);
            float fit = Math.Min(availableWidth / _map.Width, availableHeight / _map.Height);
            float width = _map.Width * fit * _zoom;
            float height = _map.Height * fit * _zoom;
            return new RectangleF((ClientSize.Width - width) / 2f + _pan.X, (ClientSize.Height - height) / 2f + _pan.Y - 8, width, height);
        }

        private PointF ScreenPoint(BattleMapPoint point, RectangleF destination)
        {
            return new PointF(destination.Left + (float)(point.x / _viewWidth) * destination.Width, destination.Top + (float)(point.y / _viewHeight) * destination.Height);
        }
    }

    internal sealed class BattlePassAssetStore
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 250 };
        private readonly string _root;

        private BattlePassAssetStore(string root) { _root = root; }

        public static BattlePassAssetStore Open()
        {
            string root = WritableCacheRoot();
            string ready = Path.Combine(root, ".ready");
            if (!File.Exists(ready))
            {
                Directory.CreateDirectory(root);
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("BattlePassAssets.zip"))
                {
                    if (resource == null) throw new InvalidDataException("Встроенный пакет BattlePassAssets.zip не найден");
                    using (ZipArchive archive = new ZipArchive(resource, ZipArchiveMode.Read, false))
                    {
                        string prefix = Path.GetFullPath(root + Path.DirectorySeparatorChar);
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
                            if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Недопустимый путь во встроенном архиве");
                            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                            {
                                Directory.CreateDirectory(destination);
                                continue;
                            }
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            using (Stream input = entry.Open())
                            using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                        }
                    }
                }
                File.WriteAllText(ready, "3.1");
            }
            return new BattlePassAssetStore(root);
        }

        private static string WritableCacheRoot()
        {
            string[] parents =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Path.GetTempPath()
            };
            foreach (string parent in parents)
            {
                if (string.IsNullOrWhiteSpace(parent)) continue;
                string root = Path.Combine(parent, "TarkovPveCompanion", "battlepass-3.1");
                string probe = Path.Combine(root, ".write-test");
                try
                {
                    Directory.CreateDirectory(root);
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return root;
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            throw new UnauthorizedAccessException("Не найден доступный каталог для распаковки офлайн-карт");
        }

        public List<BattleMapIndex> LoadIndex()
        {
            return Json.Deserialize<List<BattleMapIndex>>(File.ReadAllText(Path.Combine(_root, "index.json")));
        }

        public List<BattleMapPoint> LoadPoints(BattleMapIndex map)
        {
            BattlePointRoot root = Json.Deserialize<BattlePointRoot>(File.ReadAllText(Path.Combine(_root, map.slug, "points.json")));
            return root == null || root.points == null ? new List<BattleMapPoint>() : root.points;
        }

        public Image LoadIcon(string type)
        {
            string file = DocumentTypeStyle.IconFile(type);
            byte[] bytes = File.ReadAllBytes(Path.Combine(_root, "icons", file));
            using (MemoryStream stream = new MemoryStream(bytes))
            using (Image image = Image.FromStream(stream)) return new Bitmap(image);
        }

        public Image LoadMapImage(BattleMapIndex map, BattleFloor floor)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(_root, map.slug, floor.image));
            using (MemoryStream stream = new MemoryStream(bytes))
            using (Image image = Image.FromStream(stream)) return new Bitmap(image);
        }
    }

    internal static class DocumentTypeStyle
    {
        internal static string IconFile(string type)
        {
            string value = type ?? "";
            if (value.IndexOf("Чертеж", StringComparison.OrdinalIgnoreCase) >= 0) return "blueprints-and-technical-documentation.png";
            if (value.IndexOf("Финансов", StringComparison.OrdinalIgnoreCase) >= 0) return "financial-documents.png";
            if (value.IndexOf("Медицин", StringComparison.OrdinalIgnoreCase) >= 0) return "medical-documents.png";
            if (value.IndexOf("Личные", StringComparison.OrdinalIgnoreCase) >= 0) return "pmc-personnel-files.png";
            if (value.IndexOf("Проект", StringComparison.OrdinalIgnoreCase) >= 0) return "project-documentation.png";
            if (value.IndexOf("Эксплуата", StringComparison.OrdinalIgnoreCase) >= 0) return "technical-documentation.png";
            if (value.IndexOf("Тестов", StringComparison.OrdinalIgnoreCase) >= 0) return "test-documentation.png";
            return "user-documentation.png";
        }

        internal static Color PinColor(string type)
        {
            string value = type ?? "";
            if (value.IndexOf("Медицин", StringComparison.OrdinalIgnoreCase) >= 0) return Color.FromArgb(0, 238, 78);
            if (value.IndexOf("Пользователь", StringComparison.OrdinalIgnoreCase) >= 0) return Color.FromArgb(68, 70, 180);
            if (value.IndexOf("Финансов", StringComparison.OrdinalIgnoreCase) >= 0) return Color.FromArgb(232, 178, 68);
            if (value.IndexOf("Проект", StringComparison.OrdinalIgnoreCase) >= 0) return Color.FromArgb(54, 206, 189);
            if (value.IndexOf("Чертеж", StringComparison.OrdinalIgnoreCase) >= 0) return Color.FromArgb(64, 188, 225);
            if (value.IndexOf("Личные", StringComparison.OrdinalIgnoreCase) >= 0) return Color.FromArgb(218, 78, 77);
            if (value.IndexOf("Тестов", StringComparison.OrdinalIgnoreCase) >= 0) return Color.FromArgb(230, 127, 57);
            return Color.FromArgb(74, 132, 214);
        }
    }

    internal sealed class BattleMapIndex
    {
        public string slug { get; set; }
        public string name { get; set; }
        public int points { get; set; }
        public double viewWidth { get; set; }
        public double viewHeight { get; set; }
        public List<BattleFloor> floors { get; set; }
        public override string ToString() { return name; }
    }

    internal sealed class BattleFloor
    {
        public string id { get; set; }
        public string image { get; set; }
        public int points { get; set; }
        public override string ToString()
        {
            string value = id == null ? "?" : id.Replace("floor-", "");
            return "Этаж " + value;
        }
    }

    internal sealed class BattlePointRoot
    {
        public List<BattleMapPoint> points { get; set; }
    }

    internal sealed class BattleMapPoint
    {
        public object id { get; set; }
        public string floor { get; set; }
        public double x { get; set; }
        public double y { get; set; }
        public string iconType { get; set; }
        public string details { get; set; }
        public string image { get; set; }

        public bool IsFinancial { get { return string.Equals(iconType, "Финансовая документация", StringComparison.OrdinalIgnoreCase); } }

        public Image DecodeImage()
        {
            if (string.IsNullOrWhiteSpace(image)) return null;
            int comma = image.IndexOf(',');
            string payload = comma >= 0 ? image.Substring(comma + 1) : image;
            byte[] bytes = Convert.FromBase64String(payload);
            using (MemoryStream stream = new MemoryStream(bytes))
            using (Image decoded = Image.FromStream(stream)) return new Bitmap(decoded);
        }
    }
}
