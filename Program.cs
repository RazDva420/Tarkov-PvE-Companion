using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Tarkov PvE Companion")]
[assembly: System.Reflection.AssemblyProduct("Tarkov PvE Companion")]
[assembly: System.Reflection.AssemblyVersion("4.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("4.1.0.0")]

namespace TarkovPveCompanion
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                try
                {
                    SelfTest.Run();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("SELF-TEST FAILED: " + ex);
                    Environment.ExitCode = 1;
                }
                return;
            }

            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                ReportFatalError(e.Exception);
                Application.Exit();
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                ReportFatalError(e.ExceptionObject as Exception ?? new Exception("Неизвестная ошибка .NET"));
            };

            try
            {
                MainForm form = new MainForm();
                if (args.Any(delegate(string x) { return string.Equals(x, "--classic", StringComparison.OrdinalIgnoreCase); })) form.SetDesignMode(false);
                if (args.Any(delegate(string x) { return string.Equals(x, "--qol", StringComparison.OrdinalIgnoreCase); })) form.SetDesignMode(true);
                if (args.Any(delegate(string x) { return string.Equals(x, "--portrait", StringComparison.OrdinalIgnoreCase); })) form.Size = new Size(760, 980);
                if (args.Any(delegate(string x) { return string.Equals(x, "--tools", StringComparison.OrdinalIgnoreCase); })) form.OpenToolsTab();
                if (args.Any(delegate(string x) { return string.Equals(x, "--all-floors", StringComparison.OrdinalIgnoreCase); })) form.SetAllFloors(true);
                string mapArgument = args.FirstOrDefault(delegate(string x) { return x.StartsWith("--map=", StringComparison.OrdinalIgnoreCase); });
                if (mapArgument != null) form.SelectBattlePassMap(mapArgument.Substring(6));
                Application.Run(form);
            }
            catch (Exception ex)
            {
                ReportFatalError(ex);
            }
        }

        private static void ReportFatalError(Exception error)
        {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TarkovPveCompanion", "crash.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log));
                File.AppendAllText(log, DateTime.Now.ToString("s") + Environment.NewLine + error + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            MessageBox.Show("Приложение не смогло продолжить работу.\r\n\r\n" + error.Message + "\r\n\r\nПодробности: " + log, "Tarkov PvE Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static class SelfTest
    {
        public static void Run()
        {
            const string items = "{\"data\":{\"items\":{\"id1\":{\"id\":\"id1\",\"name\":\"id1 Name\",\"shortName\":\"id1 ShortName\",\"normalizedName\":\"test-item\",\"width\":2,\"height\":1,\"lastLowPrice\":120000,\"avg24hPrice\":100000,\"lastOfferCount\":7,\"lastScan\":\"2026-08-31T07:00:00Z\",\"sellToTrader\":[{\"trader\":\"54cb57776803fa99248b456e\",\"priceRUB\":80000}]}}}}";
            const string ru = "{\"data\":{\"id1 Name\":\"Тестовый предмет\",\"id1 ShortName\":\"Тест\"}}";
            List<ItemRow> rows = TarkovData.ParseItems(items, ru);
            if (rows.Count != 1 || rows[0].Slots != 2 || rows[0].RubPerSlot != 60000 || rows[0].BestTraderPrice != 80000)
                throw new InvalidOperationException("item parser/slot economy check failed");

            const string history = "{\"data\":[{\"priceMin\":90000,\"price\":100000,\"timestamp\":1788163200000},{\"priceMin\":110000,\"price\":120000,\"timestamp\":1788170400000}]}";
            List<PricePoint> points = TarkovData.ParseHistory(history);
            if (points.Count != 2 || points[1].Minimum != 110000)
                throw new InvalidOperationException("history parser check failed");

            const string goons = "<script>{\\\"timestamp\\\":\\\"2026-08-31T06:00:00Z\\\",\\\"modeId\\\":\\\"PVE\\\",\\\"map\\\":{\\\"name\\\":\\\"Маяк\\\"}},{\\\"timestamp\\\":\\\"2026-08-31T04:00:00Z\\\",\\\"modeId\\\":\\\"PVE\\\",\\\"map\\\":{\\\"name\\\":\\\"Таможня\\\"}}</script>";
            List<GoonReport> reports = GoonReport.ParseMany(goons);
            GoonReport report = reports.FirstOrDefault();
            if (report == null || report.Map != "Маяк" || reports.Count != 2 || reports[1].Map != "Таможня")
                throw new InvalidOperationException("goons parser check failed");

            BattlePassAssetStore assets = BattlePassAssetStore.Open();
            List<BattleMapIndex> maps = assets.LoadIndex();
            if (maps.Count != 12 || maps.Sum(delegate(BattleMapIndex x) { return x.points; }) != 349)
                throw new InvalidOperationException("battle pass asset check failed");
            List<BattleMapPoint> testPoints = assets.LoadPoints(maps[0]);
            using (Image mapImage = assets.LoadMapImage(maps[0], maps[0].floors[0]))
            using (Image markerIcon = assets.LoadIcon("Медицинская документация"))
            using (Image spawnImage = testPoints[0].DecodeImage())
            {
                if (testPoints.Count != maps[0].points || mapImage.Width < 500 || markerIcon.Width < 100 || spawnImage == null || spawnImage.Width < 100)
                    throw new InvalidOperationException("battle pass map/screenshot check failed");
            }
            if (!DocumentMapCanvas.MatchesFloor("floor-2", "floor-1", true) || DocumentMapCanvas.MatchesFloor("floor-2", "floor-1", false))
                throw new InvalidOperationException("all-floors filter check failed");
            ItemSearch.SelfCheck();

            using (MainForm form = new MainForm())
            {
                form.CreateControl();
                form.SetDesignMode(false);
                form.SetDesignMode(true);
                foreach (Size size in new[] { new Size(760, 980), new Size(700, 700), new Size(1360, 740), new Size(760, 980) })
                {
                    form.Size = size;
                    form.PerformLayout();
                }
                form.ShowGoonsReports(reports);
                form.SetDesignMode(false);
                form.SetDesignMode(true);
                form.SetAllFloors(true);
            }

            Console.WriteLine("SELF-TEST OK: data, smart search/layout/typos, slot economy, history, goons summary, 12 maps/349 spawns, Kord icons, all floors, classic/light portrait transitions");
        }
    }

    internal sealed class MainForm : Form
    {
        private const string ItemsUrl = "https://json.tarkov.dev/pve/items";
        private const string RuUrl = "https://json.tarkov.dev/pve/items_ru";
        private const string GoonsUrl = "https://eft.su/goons";
        private const string DocumentsUrl = "https://kord-map-mtdv.ru/";

        private readonly TarkovApi _api = new TarkovApi();
        private readonly BindingSource _marketSource = new BindingSource();
        private readonly BindingSource _slotSource = new BindingSource();
        private readonly BindingSource _compareSource = new BindingSource();
        private readonly BindingList<ItemRow> _comparison = new BindingList<ItemRow>();
        private readonly Dictionary<string, List<PricePoint>> _history = new Dictionary<string, List<PricePoint>>();
        private readonly Timer _autoRefresh = new Timer();
        private readonly Timer _searchDelay = new Timer();
        private readonly string _cacheDirectory;
        private List<ItemRow> _items = new List<ItemRow>();
        private bool _refreshing;
        private bool _refreshingGoons;
        private bool _applyingContentLayout;
        private GoonReport _lastGoonReport;

        private Label _status;
        private Label _count;
        private Button _refreshButton;
        private TabControl _tabs;
        private DataGridView _marketGrid;
        private DataGridView _slotGrid;
        private DataGridView _compareGrid;
        private TextBox _search;
        private TextBox _slotSearch;
        private CheckBox _onlyFlea;
        private ComboBox _sort;
        private Label _detailName;
        private Label _detailPrices;
        private Label _detailMeta;
        private Button _itemLinkButton;
        private Label _chartTitle;
        private PriceChart _chart;
        private Label _goonsMap;
        private Label _goonsTime;
        private Label _goonsStatus;
        private Label _goonsPrevious;
        private Panel _headerGoons;
        private Label _headerGoonsMap;
        private Label _headerGoonsTime;
        private Label _headerGoonsPrevious;
        private Panel _header;
        private Panel _contentHost;
        private Panel _classicTopNav;
        private Panel _adaptiveBottomNav;
        private Label _appTitle;
        private Label _appSubtitle;
        private Button _designToggle;
        private Label _searchHint;
        private Button _clearSearch;
        private bool _showGoons;
        private readonly List<Button> _bottomNavButtons = new List<Button>();
        private readonly List<Button> _classicNavButtons = new List<Button>();
        private bool _qolMode;
        private bool _portraitMode;
        private SplitContainer _marketSplit;
        private Panel _marketToolbar;
        private Panel _detailsPanel;
        private Label _detailCaption;
        private Button _compareButton;
        private Button _graphButton;
        private Label _detailNote;
        private Panel _slotToolbar;
        private Label _slotHelp;
        private TableLayoutPanel _chartLayout;
        private TableLayoutPanel _toolsLayout;
        private BattlePassBrowser _battlePassBrowser;

        public MainForm()
        {
            _cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TarkovPveCompanion");
            _qolMode = LoadDesignPreference();
            Text = "Tarkov PvE Companion";
            MinimumSize = new Size(720, 700);
            Size = new Size(1360, 860);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;

            BuildInterface();

            _autoRefresh.Interval = 5 * 60 * 1000;
            _autoRefresh.Tick += async delegate { await Task.WhenAll(RefreshItemsAsync(false), RefreshGoonsAsync(false)); };
            _searchDelay.Interval = 250;
            _searchDelay.Tick += delegate { _searchDelay.Stop(); ApplyFilters(); };

            Shown += async delegate { _autoRefresh.Start(); if (_tabs.SelectedIndex == 0) _search.Focus(); await LoadOnStartupAsync(); };
            FormClosed += delegate { _api.Dispose(); };
            Resize += delegate { ApplyShellLayout(); };
            KeyDown += MainFormKeyDown;
        }

        private void BuildInterface()
        {
            _header = (Panel)BuildHeader();
            _header.Dock = DockStyle.None;
            Controls.Add(_header);

            _adaptiveBottomNav = BuildAdaptiveBottomNav();
            _adaptiveBottomNav.Visible = false;
            Controls.Add(_adaptiveBottomNav);

            _classicTopNav = BuildClassicTopNav();
            Controls.Add(_classicTopNav);

            _contentHost = new Panel();
            _contentHost.BackColor = Theme.Background;
            Controls.Add(_contentHost);

            _tabs = new TabControl();
            _tabs.Dock = DockStyle.None;
            _tabs.Padding = new Point(18, 7);
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.SizeMode = TabSizeMode.Fixed;
            _tabs.ItemSize = new Size(190, 34);
            _tabs.DrawItem += DrawTab;
            _tabs.Resize += delegate
            {
                Size desired = new Size(Math.Max(170, (_tabs.ClientSize.Width - 8) / 4), 34);
                if (_tabs.ItemSize != desired) _tabs.ItemSize = desired;
            };
            _contentHost.Controls.Add(_tabs);

            _tabs.TabPages.Add(BuildMarketTab());
            _tabs.TabPages.Add(BuildChartTab());
            _tabs.TabPages.Add(BuildSlotTab());
            _tabs.TabPages.Add(BuildToolsTab());
            _tabs.SelectedIndexChanged += delegate { UpdateAdaptiveNavigation(); };

            ApplyShellLayout();
        }

        internal void OpenToolsTab()
        {
            if (_tabs != null && _tabs.TabPages.Count > 3) _tabs.SelectedIndex = 3;
        }

        private void OpenGoonsDetails()
        {
            _showGoons = true;
            OpenToolsTab();
            ApplyResponsiveContent();
            UpdateAdaptiveNavigation();
        }

        internal void SetDesignMode(bool qol)
        {
            _qolMode = qol;
            ApplyShellLayout();
        }

        internal void SetAllFloors(bool enabled)
        {
            if (_battlePassBrowser != null) _battlePassBrowser.SetAllFloors(enabled);
        }

        internal void SelectBattlePassMap(string mapName)
        {
            if (_battlePassBrowser != null) _battlePassBrowser.SelectMap(mapName);
        }

        private Panel BuildAdaptiveBottomNav()
        {
            Panel panel = new Panel();
            panel.BackColor = Theme.Panel2;
            string[] titles = { "Цены", "Карты документов", "Ещё" };
            int[] destinations = { 0, 3, -1 };
            for (int i = 0; i < titles.Length; i++)
            {
                Button button = AdaptiveNavButton(titles[i], destinations[i], true);
                button.Font = Theme.Heading;
                panel.Controls.Add(button);
                _bottomNavButtons.Add(button);
            }
            ContextMenuStrip more = new ContextMenuStrip { BackColor = Theme.Panel2, ForeColor = Theme.Text, ShowImageMargin = false };
            more.Items.Add("График и сравнение", null, delegate { _tabs.SelectedIndex = 1; });
            more.Items.Add("Стоимость за слот", null, delegate { _tabs.SelectedIndex = 2; });
            more.Items.Add("Гуны: история и обновление", null, delegate { OpenGoonsDetails(); });
            more.Items.Add(new ToolStripSeparator());
            more.Items.Add("Классический интерфейс", null, delegate { ToggleDesign(); });
            Button moreButton = _bottomNavButtons[2];
            moreButton.Click += delegate { more.Show(moreButton, new Point(0, moreButton.Height)); };
            panel.Disposed += delegate { more.Dispose(); };
            panel.Resize += delegate
            {
                int width = Math.Max(1, _portraitMode ? panel.ClientSize.Width / 3 : (panel.ClientSize.Width - 120) / 2);
                for (int i = 0; i < _bottomNavButtons.Count; i++)
                    _bottomNavButtons[i].Bounds = new Rectangle(i * width, 0, i == 2 ? panel.ClientSize.Width - i * width : width, panel.ClientSize.Height);
            };
            return panel;
        }

        private Panel BuildClassicTopNav()
        {
            Panel panel = new Panel();
            panel.BackColor = Theme.Background;
            Panel baseline = new Panel { Dock = DockStyle.Bottom, Height = 2, BackColor = Theme.Border };
            panel.Controls.Add(baseline);
            string[] titles = { "ЦЕНЫ PVE", "ГРАФИК И СРАВНЕНИЕ", "ЭКОНОМИКА СЛОТА", "УДОБСТВА PVE" };
            for (int i = 0; i < titles.Length; i++)
            {
                Button button = AdaptiveNavButton(titles[i], i, true);
                button.BackColor = Theme.Background;
                panel.Controls.Add(button);
                _classicNavButtons.Add(button);
            }
            panel.Resize += delegate
            {
                int width = Math.Max(1, panel.ClientSize.Width / 4);
                for (int i = 0; i < _classicNavButtons.Count; i++)
                    _classicNavButtons[i].Bounds = new Rectangle(i * width, 0, i == 3 ? panel.ClientSize.Width - i * width : width, panel.ClientSize.Height - 2);
                baseline.BringToFront();
            };
            return panel;
        }

        private Button AdaptiveNavButton(string text, int index, bool compact)
        {
            Button button = new Button();
            button.Tag = index;
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Theme.Panel;
            button.ForeColor = Theme.Muted;
            button.Font = Theme.Button;
            button.TextAlign = compact ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            button.Click += delegate
            {
                int destination = (int)button.Tag;
                if (destination < 0) return;
                if (destination == 3 && _qolMode) { _showGoons = false; ApplyResponsiveContent(); }
                _tabs.SelectedIndex = destination;
                UpdateAdaptiveNavigation();
            };
            return button;
        }

        private void ToggleDesign()
        {
            _qolMode = !_qolMode;
            SaveDesignPreference();
            ApplyShellLayout();
        }

        private void ApplyShellLayout()
        {
            if (_header == null || _contentHost == null || _tabs == null) return;
            int width = Math.Max(1, ClientSize.Width);
            int height = Math.Max(1, ClientSize.Height);
            _portraitMode = _qolMode && (height > width || width < 900);

            _adaptiveBottomNav.Visible = _qolMode;
            _classicTopNav.Visible = !_qolMode;
            if (!_qolMode)
            {
                MinimumSize = new Size(1050, 700);
                _header.Bounds = new Rectangle(0, 0, width, 76);
                _classicTopNav.Bounds = new Rectangle(0, 76, width, 40);
                _contentHost.Bounds = new Rectangle(0, 116, width, Math.Max(1, height - 116));
                _tabs.Bounds = new Rectangle(-5, -38, _contentHost.ClientSize.Width + 10, _contentHost.ClientSize.Height + 46);
            }
            else if (_portraitMode)
            {
                MinimumSize = new Size(700, 700);
                _header.Bounds = new Rectangle(0, 0, width, 112);
                _adaptiveBottomNav.Bounds = new Rectangle(0, 112, width, 40);
                _contentHost.Bounds = new Rectangle(0, 152, width, Math.Max(1, height - 152));
                _tabs.Bounds = new Rectangle(-5, -38, _contentHost.ClientSize.Width + 10, _contentHost.ClientSize.Height + 46);
            }
            else
            {
                MinimumSize = new Size(700, 700);
                _header.Bounds = new Rectangle(0, 0, width, 76);
                _adaptiveBottomNav.Bounds = new Rectangle(0, 76, width, 52);
                _contentHost.Bounds = new Rectangle(0, 128, width, Math.Max(1, height - 128));
                _tabs.Bounds = new Rectangle(-5, -38, _contentHost.ClientSize.Width + 10, _contentHost.ClientSize.Height + 46);
            }

            _contentHost.BringToFront();
            _header.BringToFront();
            if (_classicTopNav.Visible) _classicTopNav.BringToFront();
            if (_adaptiveBottomNav.Visible) _adaptiveBottomNav.BringToFront();
            LayoutHeader();
            ApplyResponsiveContent();
            UpdateAdaptiveNavigation();
            if (_marketGrid != null) UpdateDetails(SelectedMarketItem());
        }

        private void LayoutHeader()
        {
            if (_header == null || _headerGoons == null || _refreshButton == null) return;
            int width = _header.ClientSize.Width;
            _headerGoons.Visible = true;
            _designToggle.Visible = !_qolMode;
            _appTitle.Visible = true;
            _appSubtitle.Visible = true;
            _status.Visible = true;
            if (_qolMode && _portraitMode)
            {
                _appTitle.Text = "TARKOV  PVE  COMPANION";
                _appTitle.Location = new Point(14, 8);
                _appSubtitle.Text = "ОБЛЕГЧЁННЫЙ · ЦЕНЫ И КАРТЫ";
                _appSubtitle.Location = new Point(16, 42);
                _headerGoons.Bounds = new Rectangle(340, 8, Math.Max(260, width - 354), 60);
                _refreshButton.Bounds = new Rectangle(width - 166, 76, 152, 28);
                _status.Bounds = new Rectangle(14, 76, width - 196, 28);
                _status.TextAlign = ContentAlignment.MiddleLeft;
            }
            else if (_qolMode)
            {
                _appTitle.Text = "TARKOV / PVE";
                _appTitle.Location = new Point(20, 12);
                _appSubtitle.Text = "Цены и карты документов";
                _appSubtitle.Location = new Point(22, 44);
                _headerGoons.Bounds = new Rectangle(270, 8, Math.Max(280, width - 566), 60);
            }
            else
            {
                _appTitle.Text = "TARKOV  PVE  COMPANION";
                _appTitle.Location = new Point(22, 12);
                _appSubtitle.Text = "цены · сравнение · экономика слота · инструменты PvE";
                _appSubtitle.Location = new Point(24, 44);
                _designToggle.Text = "QOL";
                _designToggle.Bounds = new Rectangle(346, 10, 56, 28);
                _headerGoons.Bounds = new Rectangle(418, 8, Math.Max(260, width - 714), 60);
            }
            if (!_portraitMode)
            {
                _refreshButton.Bounds = new Rectangle(width - 184, 10, 164, 32);
                _status.Bounds = new Rectangle(width - 280, 46, 260, 22);
                _status.TextAlign = ContentAlignment.MiddleRight;
            }
            _headerGoonsMap.Width = Math.Max(50, _headerGoons.ClientSize.Width - 20);
            _headerGoonsTime.Width = _headerGoonsMap.Width;
            _headerGoonsPrevious.Width = _headerGoonsMap.Width;
        }

        private void UpdateAdaptiveNavigation()
        {
            if (_tabs == null) return;
            bool showingExtras = _tabs.SelectedIndex == 1 || _tabs.SelectedIndex == 2 || (_showGoons && _tabs.SelectedIndex == 3);
            string[] titles = _portraitMode ? new[] { "ЦЕНЫ PVE", "КАРТЫ ДОКУМЕНТОВ", "ДОПОЛНИТЕЛЬНО" } : new[] { "Цены", "Карты документов", "Ещё" };
            for (int i = 0; i < _bottomNavButtons.Count; i++)
            {
                _bottomNavButtons[i].Text = titles[i];
                _bottomNavButtons[i].Font = _portraitMode ? Theme.Button : Theme.Heading;
            }
            foreach (Button button in _bottomNavButtons.Concat(_classicNavButtons))
            {
                int selectedIndex = _classicNavButtons.Contains(button) ? _tabs.SelectedIndex : (showingExtras ? -1 : _tabs.SelectedIndex);
                bool selected = (int)button.Tag == selectedIndex;
                button.BackColor = selected ? Theme.Selection : (_classicNavButtons.Contains(button) ? Theme.Background : Theme.Panel);
                button.ForeColor = selected ? Theme.Accent : Theme.Muted;
                button.FlatAppearance.BorderColor = selected ? Theme.Teal : Theme.Panel;
                button.FlatAppearance.BorderSize = selected ? 1 : 0;
            }
        }

        private void MainFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F)
            {
                _tabs.SelectedIndex = 0;
                _search.Focus();
                _search.SelectAll();
                e.SuppressKeyPress = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.D)
            {
                ToggleDesign();
                e.SuppressKeyPress = true;
                return;
            }
            if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D4)
            {
                _tabs.SelectedIndex = (int)e.KeyCode - (int)Keys.D1;
                e.SuppressKeyPress = true;
            }
        }

        private bool LoadDesignPreference()
        {
            try
            {
                string path = Path.Combine(_cacheDirectory, "design-mode-v4.txt");
                return !File.Exists(path) || !string.Equals(File.ReadAllText(path).Trim(), "classic", StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        private void SaveDesignPreference()
        {
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                File.WriteAllText(Path.Combine(_cacheDirectory, "design-mode-v4.txt"), _qolMode ? "qol" : "classic");
            }
            catch { }
        }

        private void ApplyResponsiveContent()
        {
            if (_applyingContentLayout) return;
            _applyingContentLayout = true;
            try
            {
                bool portrait = _qolMode && _portraitMode;
                if (_marketToolbar != null)
                {
                    _marketToolbar.Height = _qolMode ? (portrait ? 92 : 132) : 58;
                    if (_marketToolbar.Parent != null) _marketToolbar.Parent.PerformLayout();
                }
                if (_marketSplit != null && _marketSplit.ClientSize.Width > 0 && _marketSplit.ClientSize.Height > 0)
                {
                    Orientation desiredOrientation = portrait ? Orientation.Horizontal : Orientation.Vertical;
                    if (_marketSplit.Orientation != desiredOrientation)
                    {
                        // WinForms reuses SplitterDistance across axes; clamp before changing orientation.
                        _marketSplit.FixedPanel = FixedPanel.None;
                        int maximum = Math.Min(_marketSplit.ClientSize.Width, _marketSplit.ClientSize.Height) - _marketSplit.Panel2MinSize - _marketSplit.SplitterWidth;
                        if (maximum >= _marketSplit.Panel1MinSize)
                        {
                            _marketSplit.SplitterDistance = Math.Max(_marketSplit.Panel1MinSize, Math.Min(maximum, _marketSplit.SplitterDistance));
                            _marketSplit.Orientation = desiredOrientation;
                        }
                    }
                    _marketSplit.FixedPanel = FixedPanel.Panel2;
                    if (portrait)
                    {
                        int desired = Math.Max(90, _marketSplit.ClientSize.Height - 224);
                        desired = Math.Min(_marketSplit.ClientSize.Height - 50, desired);
                        if (desired > 0) _marketSplit.SplitterDistance = desired;
                    }
                    else if (!portrait && _marketSplit.ClientSize.Width > 700)
                    {
                        int desired = Math.Max(380, _marketSplit.ClientSize.Width - 382);
                        desired = Math.Min(_marketSplit.ClientSize.Width - 300, desired);
                        if (desired > 0) _marketSplit.SplitterDistance = desired;
                    }
                }

                if (_marketToolbar != null)
                {
                    _search.Font = _qolMode ? (portrait ? Theme.Body : Theme.Search) : Theme.Body;
                    _sort.Visible = !_qolMode;
                    _clearSearch.Visible = _qolMode;
                    _searchHint.Visible = _qolMode;
                    if (portrait)
                    {
                        int width = _marketToolbar.ClientSize.Width;
                        _search.Bounds = new Rectangle(14, 10, Math.Max(200, width - 112), 28);
                        _clearSearch.Bounds = new Rectangle(width - 86, 8, 72, 28);
                        _onlyFlea.Location = new Point(14, 42);
                        _count.Bounds = new Rectangle(width - 174, 37, 160, 24);
                        _searchHint.Bounds = new Rectangle(14, 68, width - 28, 20);
                    }
                    else if (_qolMode)
                    {
                        int width = _marketToolbar.ClientSize.Width;
                        _marketToolbar.Height = 132;
                        _search.Bounds = new Rectangle(20, 17, Math.Max(200, width - 130), 38);
                        _clearSearch.Bounds = new Rectangle(width - 98, 17, 78, 36);
                        _onlyFlea.Location = new Point(20, 68);
                        _sort.Bounds = new Rectangle(width - 205, 64, 185, 28);
                        _searchHint.Bounds = new Rectangle(20, 100, Math.Max(200, width - 215), 24);
                        _count.Bounds = new Rectangle(width - 190, 96, 170, 28);
                    }
                    else
                    {
                        _marketToolbar.Height = 58;
                        _search.Bounds = new Rectangle(14, 11, 330, 28);
                        _onlyFlea.Location = new Point(360, 17);
                        _sort.Bounds = new Rectangle(610, 13, 170, 28);
                        _count.Bounds = new Rectangle(_marketToolbar.ClientSize.Width - 236, 12, 220, 30);
                    }
                }

                ApplyDetailsLayout(portrait);
                if (_marketGrid != null)
                {
                    string[] essential = { "Name", "FleaPrice", "BestTraderPrice", "RubPerSlot" };
                    foreach (DataGridViewColumn column in _marketGrid.Columns)
                    {
                        column.Visible = !_qolMode || essential.Contains(column.DataPropertyName);
                        column.AutoSizeMode = _qolMode && column.DataPropertyName == "Name" ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None;
                        if (_qolMode && column.DataPropertyName == "Name") column.MinimumWidth = 160;
                        if (!_qolMode && column.DataPropertyName == "Name") column.Width = 260;
                    }
                    int rowHeight = _qolMode && !portrait ? 42 : 34;
                    if (_marketGrid.RowTemplate.Height != rowHeight)
                    {
                        _marketGrid.RowTemplate.Height = rowHeight;
                        foreach (DataGridViewRow row in _marketGrid.Rows) row.Height = rowHeight;
                    }
                }
                if (_slotToolbar != null)
                {
                    if (portrait)
                    {
                        _slotToolbar.Height = 88;
                        _slotSearch.Bounds = new Rectangle(14, 10, Math.Min(330, _slotToolbar.ClientSize.Width - 28), 28);
                        _slotHelp.Bounds = new Rectangle(14, 48, Math.Max(200, _slotToolbar.ClientSize.Width - 28), 30);
                    }
                    else
                    {
                        _slotToolbar.Height = 58;
                        _slotSearch.Bounds = new Rectangle(14, 11, 330, 28);
                        _slotHelp.Bounds = new Rectangle(370, 18, Math.Max(300, _slotToolbar.ClientSize.Width - 384), 22);
                    }
                }
                if (_chartLayout != null && _chartLayout.RowStyles.Count >= 2)
                {
                    _chartLayout.RowStyles[0].Height = portrait ? 48 : 58;
                    _chartLayout.RowStyles[1].Height = portrait ? 52 : 42;
                }
                if (_toolsLayout != null && _toolsLayout.RowStyles.Count >= 2)
                    _toolsLayout.RowStyles[0].Height = _qolMode && !_showGoons ? 0 : (portrait ? 122 : 108);
            }
            finally { _applyingContentLayout = false; }
        }

        private void ApplyDetailsLayout(bool portrait)
        {
            if (_detailsPanel == null) return;
            _detailPrices.Font = _qolMode && !portrait ? Theme.Price : Theme.MonoLarge;
            _detailCaption.Text = _qolMode ? "ЦЕНА НА БАРАХОЛКЕ · PVE" : "ВЫБРАННЫЙ ПРЕДМЕТ";
            if (_qolMode)
            {
                _detailNote.Visible = false;
                if (!portrait)
                {
                    int detailWidth = Math.Max(300, _detailsPanel.ClientSize.Width) - 40;
                    _detailCaption.Location = new Point(20, 22);
                    _detailName.Bounds = new Rectangle(20, 54, detailWidth, 76);
                    _detailPrices.Bounds = new Rectangle(20, 140, detailWidth, 86);
                    _detailMeta.Bounds = new Rectangle(20, 242, detailWidth, 144);
                    _graphButton.Bounds = new Rectangle(20, 398, detailWidth, 38);
                    _compareButton.Bounds = new Rectangle(20, 448, detailWidth, 36);
                    _itemLinkButton.Bounds = new Rectangle(20, 496, detailWidth, 36);
                    if (_detailsPanel.ClientSize.Height < 550)
                    {
                        int actionsTop = Math.Max(264, _detailsPanel.ClientSize.Height - 144);
                        _detailName.Bounds = new Rectangle(20, 48, detailWidth, 52);
                        _detailPrices.Bounds = new Rectangle(20, 110, detailWidth, 50);
                        _detailMeta.Bounds = new Rectangle(20, 174, detailWidth, Math.Max(70, actionsTop - 184));
                        _graphButton.Bounds = new Rectangle(20, actionsTop, detailWidth, 36);
                        _compareButton.Bounds = new Rectangle(20, actionsTop + 46, detailWidth, 36);
                        _itemLinkButton.Bounds = new Rectangle(20, actionsTop + 92, detailWidth, 36);
                    }
                }
                else
                {
                    int half = _detailsPanel.ClientSize.Width / 2;
                    _detailCaption.Location = new Point(14, 12);
                    _detailName.Bounds = new Rectangle(14, 36, half - 28, 48);
                    _detailPrices.Bounds = new Rectangle(14, 94, half - 28, 34);
                    _detailMeta.Bounds = new Rectangle(half + 10, 36, half - 24, 116);
                    int buttonWidth = (half - 52) / 2;
                    _graphButton.Bounds = new Rectangle(14, 174, buttonWidth, 30);
                    _compareButton.Bounds = new Rectangle(26 + buttonWidth, 174, buttonWidth, 30);
                    _itemLinkButton.Bounds = new Rectangle(half + 10, 174, half - 24, 30);
                }
                return;
            }
            _detailNote.Visible = true;
            if (!portrait)
            {
                _detailCaption.Location = new Point(22, 22);
                _detailName.Bounds = new Rectangle(22, 52, 330, 72);
                _detailPrices.Bounds = new Rectangle(22, 130, 330, 105);
                _detailMeta.Bounds = new Rectangle(22, 240, 330, 125);
                _compareButton.Bounds = new Rectangle(22, 382, 150, 38);
                _graphButton.Bounds = new Rectangle(184, 382, 170, 38);
                _itemLinkButton.Bounds = new Rectangle(22, 432, 332, 38);
                _detailNote.Bounds = new Rectangle(22, 492, 330, 90);
                return;
            }

            int width = Math.Max(680, _detailsPanel.ClientSize.Width);
            int rightX = Math.Min(340, width / 2);
            int rightWidth = Math.Max(300, width - rightX - 18);
            _detailCaption.Location = new Point(18, 14);
            _detailName.Bounds = new Rectangle(18, 38, rightX - 32, 54);
            _detailPrices.Bounds = new Rectangle(18, 96, rightX - 32, 100);
            _detailMeta.Bounds = new Rectangle(rightX, 38, rightWidth, 126);
            _itemLinkButton.Bounds = new Rectangle(rightX, 170, rightWidth, 36);
            _compareButton.Bounds = new Rectangle(18, 210, 145, 36);
            _graphButton.Bounds = new Rectangle(174, 210, 150, 36);
            _detailNote.Bounds = new Rectangle(rightX, 215, rightWidth, 72);
        }

        private Control BuildHeader()
        {
            Panel header = new Panel();
            header.Height = 76;
            header.Padding = new Padding(24, 12, 20, 10);
            header.BackColor = Theme.Panel;

            _appTitle = new Label();
            _appTitle.AutoSize = true;
            _appTitle.Text = "TARKOV  PVE  COMPANION";
            _appTitle.Font = Theme.Title;
            _appTitle.ForeColor = Theme.Accent;
            _appTitle.Location = new Point(22, 12);
            header.Controls.Add(_appTitle);

            _appSubtitle = new Label();
            _appSubtitle.AutoSize = true;
            _appSubtitle.Text = "цены • сравнение • экономика слота • инструменты PvE";
            _appSubtitle.ForeColor = Theme.Muted;
            _appSubtitle.Location = new Point(24, 44);
            header.Controls.Add(_appSubtitle);

            _designToggle = MakeButton("QOL", Theme.Teal);
            _designToggle.Size = new Size(56, 28);
            _designToggle.Location = new Point(326, 10);
            _designToggle.Click += delegate { ToggleDesign(); };
            header.Controls.Add(_designToggle);

            _headerGoons = new Panel();
            _headerGoons.BackColor = Theme.Panel2;
            _headerGoons.Location = new Point(390, 8);
            _headerGoons.Size = new Size(475, 58);
            _headerGoons.Cursor = Cursors.Hand;
            header.Controls.Add(_headerGoons);

            Panel goonStripe = new Panel();
            goonStripe.Dock = DockStyle.Left;
            goonStripe.Width = 3;
            goonStripe.BackColor = Theme.Teal;
            _headerGoons.Controls.Add(goonStripe);

            _headerGoonsMap = new Label();
            _headerGoonsMap.AutoSize = false;
            _headerGoonsMap.AutoEllipsis = true;
            _headerGoonsMap.Font = Theme.SmallBold;
            _headerGoonsMap.ForeColor = Theme.Teal;
            _headerGoonsMap.Text = "ГУНЫ PVE • ЗАГРУЗКА…";
            _headerGoonsMap.Location = new Point(12, 5);
            _headerGoonsMap.Size = new Size(450, 18);
            _headerGoons.Controls.Add(_headerGoonsMap);

            _headerGoonsTime = new Label();
            _headerGoonsTime.AutoSize = false;
            _headerGoonsTime.AutoEllipsis = true;
            _headerGoonsTime.ForeColor = Theme.Text;
            _headerGoonsTime.Text = "получаю последнюю отметку eft.su";
            _headerGoonsTime.Location = new Point(12, 22);
            _headerGoonsTime.Size = new Size(450, 17);
            _headerGoons.Controls.Add(_headerGoonsTime);

            _headerGoonsPrevious = new Label();
            _headerGoonsPrevious.AutoSize = false;
            _headerGoonsPrevious.AutoEllipsis = true;
            _headerGoonsPrevious.Font = Theme.Small;
            _headerGoonsPrevious.ForeColor = Theme.Muted;
            _headerGoonsPrevious.Text = "(до этого: —)";
            _headerGoonsPrevious.Location = new Point(12, 39);
            _headerGoonsPrevious.Size = new Size(450, 16);
            _headerGoons.Controls.Add(_headerGoonsPrevious);

            EventHandler openTools = delegate
            {
                OpenGoonsDetails();
            };
            _headerGoons.Click += openTools;
            _headerGoonsMap.Click += openTools;
            _headerGoonsTime.Click += openTools;
            _headerGoonsPrevious.Click += openTools;

            _refreshButton = MakeButton("ОБНОВИТЬ ЦЕНЫ", Theme.Accent);
            _refreshButton.Size = new Size(164, 38);
            _refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _refreshButton.Location = new Point(ClientSize.Width - 184, 19);
            _refreshButton.Click += async delegate { await RefreshItemsAsync(true); };
            header.Controls.Add(_refreshButton);

            _status = new Label();
            _status.AutoSize = false;
            _status.TextAlign = ContentAlignment.MiddleRight;
            _status.ForeColor = Theme.Muted;
            _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _status.Location = new Point(ClientSize.Width - 460, 18);
            _status.Size = new Size(260, 42);
            _status.Text = "Подготовка…";
            header.Controls.Add(_status);

            header.Resize += delegate
            {
                LayoutHeader();
            };
            return header;
        }

        private TabPage BuildMarketTab()
        {
            TabPage page = NewTab("ЦЕНЫ PVE");

            _marketToolbar = new Panel();
            _marketToolbar.Dock = DockStyle.Top;
            _marketToolbar.Height = 58;
            _marketToolbar.Padding = new Padding(14, 10, 14, 8);
            _marketToolbar.BackColor = Theme.Panel2;
            page.Controls.Add(_marketToolbar);

            _search = MakeTextBox("Название или сокращение предмета…");
            _search.MaxLength = 100;
            _search.Location = new Point(14, 11);
            _search.Width = 330;
            _search.TextChanged += SearchChanged;
            _search.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { _search.Clear(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter)
                {
                    _searchDelay.Stop();
                    ApplyFilters();
                    if (_marketGrid.Rows.Count > 0) _marketGrid.Focus();
                    e.SuppressKeyPress = true;
                }
            };
            _marketToolbar.Controls.Add(_search);

            _clearSearch = MakeButton("Сброс", Theme.Card);
            _clearSearch.Click += delegate { _search.Focus(); _search.Clear(); };
            _marketToolbar.Controls.Add(_clearSearch);
            _searchHint = new Label { ForeColor = Theme.Muted, Font = Theme.Small, Text = "Можно с опечаткой или в другой раскладке · Ctrl+F — поиск" };
            _marketToolbar.Controls.Add(_searchHint);

            _onlyFlea = new CheckBox();
            _onlyFlea.AutoSize = true;
            _onlyFlea.Text = "Только с ценой на барахолке";
            _onlyFlea.Checked = true;
            _onlyFlea.ForeColor = Theme.Text;
            _onlyFlea.Location = new Point(360, 17);
            _onlyFlea.CheckedChanged += delegate { ApplyFilters(); };
            _marketToolbar.Controls.Add(_onlyFlea);

            _sort = new ComboBox();
            _sort.DropDownStyle = ComboBoxStyle.DropDownList;
            _sort.FlatStyle = FlatStyle.Flat;
            _sort.BackColor = Theme.Card;
            _sort.ForeColor = Theme.Text;
            _sort.Items.AddRange(new object[] { "Лучшее совпадение", "Цена: дороже", "₽/слот: выше", "Изменение: выше" });
            _sort.SelectedIndex = 0;
            _sort.Location = new Point(610, 13);
            _sort.Width = 170;
            _sort.SelectedIndexChanged += delegate { ApplyFilters(); };
            _marketToolbar.Controls.Add(_sort);

            _count = new Label();
            _count.AutoSize = false;
            _count.TextAlign = ContentAlignment.MiddleRight;
            _count.ForeColor = Theme.Muted;
            _count.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _count.Location = new Point(_marketToolbar.Width - 245, 12);
            _count.Size = new Size(220, 30);
            _marketToolbar.Controls.Add(_count);
            _marketToolbar.Resize += delegate
            {
                if (!_portraitMode) _count.Left = _marketToolbar.ClientSize.Width - _count.Width - 16;
            };

            _marketSplit = new SplitContainer();
            _marketSplit.Dock = DockStyle.Fill;
            _marketSplit.FixedPanel = FixedPanel.Panel2;
            _marketSplit.SplitterWidth = 1;
            _marketSplit.BackColor = Theme.Border;
            _marketSplit.Resize += delegate { ApplyResponsiveContent(); };
            page.Controls.Add(_marketSplit);
            _marketSplit.BringToFront();

            _marketGrid = BuildMarketGrid();
            _marketGrid.DataSource = _marketSource;
            _marketGrid.SelectionChanged += delegate { UpdateDetails(SelectedMarketItem()); };
            _marketGrid.CellDoubleClick += delegate { ShowChartForSelected(); };
            _marketSplit.Panel1.Controls.Add(_marketGrid);
            _marketSplit.Panel2.BackColor = Theme.Panel;
            _marketSplit.Panel2.Controls.Add(BuildDetailsPanel());
            return page;
        }

        private Control BuildDetailsPanel()
        {
            _detailsPanel = new Panel();
            _detailsPanel.Dock = DockStyle.Fill;
            _detailsPanel.Padding = new Padding(22);
            _detailsPanel.BackColor = Theme.Panel;

            _detailCaption = new Label();
            _detailCaption.AutoSize = true;
            _detailCaption.Text = "ВЫБРАННЫЙ ПРЕДМЕТ";
            _detailCaption.Font = Theme.SmallBold;
            _detailCaption.ForeColor = Theme.Accent;
            _detailCaption.Location = new Point(22, 22);
            _detailsPanel.Controls.Add(_detailCaption);

            _detailName = new Label();
            _detailName.AutoSize = false;
            _detailName.Font = Theme.Heading;
            _detailName.ForeColor = Theme.Text;
            _detailName.Location = new Point(22, 52);
            _detailName.Size = new Size(330, 72);
            _detailName.Text = "Выберите предмет";
            _detailsPanel.Controls.Add(_detailName);

            _detailPrices = new Label();
            _detailPrices.AutoSize = false;
            _detailPrices.Font = Theme.MonoLarge;
            _detailPrices.ForeColor = Theme.Good;
            _detailPrices.Location = new Point(22, 130);
            _detailPrices.Size = new Size(330, 105);
            _detailsPanel.Controls.Add(_detailPrices);

            _detailMeta = new Label();
            _detailMeta.AutoSize = false;
            _detailMeta.ForeColor = Theme.Muted;
            _detailMeta.Location = new Point(22, 240);
            _detailMeta.Size = new Size(330, 125);
            _detailsPanel.Controls.Add(_detailMeta);

            _compareButton = MakeButton("+ В СРАВНЕНИЕ", Theme.Accent);
            _compareButton.Location = new Point(22, 382);
            _compareButton.Size = new Size(150, 38);
            _compareButton.Click += delegate { AddSelectedToComparison(); };
            _detailsPanel.Controls.Add(_compareButton);

            _graphButton = MakeButton("ГРАФИК 30 ДНЕЙ", Theme.Teal);
            _graphButton.Location = new Point(184, 382);
            _graphButton.Size = new Size(170, 38);
            _graphButton.Click += delegate { ShowChartForSelected(); };
            _detailsPanel.Controls.Add(_graphButton);

            _itemLinkButton = MakeButton("ОТКРЫТЬ НА TARKOV.DEV", Theme.Card);
            _itemLinkButton.Location = new Point(22, 432);
            _itemLinkButton.Size = new Size(332, 38);
            _itemLinkButton.Click += delegate
            {
                ItemRow item = SelectedMarketItem();
                if (item != null) OpenUrl(item.Link);
            };
            _detailsPanel.Controls.Add(_itemLinkButton);

            _detailNote = new Label();
            _detailNote.AutoSize = false;
            _detailNote.ForeColor = Theme.Faint;
            _detailNote.Location = new Point(22, 492);
            _detailNote.Size = new Size(330, 90);
            _detailNote.Text = "Цена барахолки — последнее минимальное предложение PvE. Сравнение с торговцем не учитывает комиссию размещения.";
            _detailsPanel.Controls.Add(_detailNote);
            return _detailsPanel;
        }

        private TabPage BuildChartTab()
        {
            TabPage page = NewTab("ГРАФИК И СРАВНЕНИЕ");
            _chartLayout = new TableLayoutPanel();
            _chartLayout.Dock = DockStyle.Fill;
            _chartLayout.RowCount = 2;
            _chartLayout.ColumnCount = 1;
            _chartLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            _chartLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            page.Controls.Add(_chartLayout);

            Panel chartPanel = new Panel();
            chartPanel.Dock = DockStyle.Fill;
            chartPanel.Padding = new Padding(18, 12, 18, 14);
            chartPanel.BackColor = Theme.Background;
            _chartLayout.Controls.Add(chartPanel, 0, 0);

            _chartTitle = new Label();
            _chartTitle.Dock = DockStyle.Top;
            _chartTitle.Height = 40;
            _chartTitle.Font = Theme.Heading;
            _chartTitle.ForeColor = Theme.Text;
            _chartTitle.Text = "Выберите предмет в разделе «Цены PvE»";
            chartPanel.Controls.Add(_chartTitle);

            _chart = new PriceChart();
            _chart.Dock = DockStyle.Fill;
            chartPanel.Controls.Add(_chart);
            _chart.BringToFront();

            Panel comparePanel = new Panel();
            comparePanel.Dock = DockStyle.Fill;
            comparePanel.Padding = new Padding(18, 8, 18, 16);
            comparePanel.BackColor = Theme.Panel;
            _chartLayout.Controls.Add(comparePanel, 0, 1);

            Panel compareToolbar = new Panel();
            compareToolbar.Dock = DockStyle.Top;
            compareToolbar.Height = 42;
            compareToolbar.BackColor = Theme.Panel;
            comparePanel.Controls.Add(compareToolbar);

            Label compareTitle = new Label();
            compareTitle.AutoSize = true;
            compareTitle.Text = "СРАВНЕНИЕ (до 6 предметов)";
            compareTitle.Font = Theme.SmallBold;
            compareTitle.ForeColor = Theme.Accent;
            compareTitle.Location = new Point(0, 10);
            compareToolbar.Controls.Add(compareTitle);

            Button remove = MakeButton("УДАЛИТЬ", Theme.Card);
            remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            remove.Size = new Size(100, 32);
            remove.Location = new Point(compareToolbar.Width - 216, 4);
            remove.Click += delegate { RemoveComparison(); };
            compareToolbar.Controls.Add(remove);

            Button clear = MakeButton("ОЧИСТИТЬ", Theme.Card);
            clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.Size = new Size(100, 32);
            clear.Location = new Point(compareToolbar.Width - 104, 4);
            clear.Click += delegate { _comparison.Clear(); };
            compareToolbar.Controls.Add(clear);
            compareToolbar.Resize += delegate
            {
                clear.Left = compareToolbar.ClientSize.Width - clear.Width;
                remove.Left = clear.Left - remove.Width - 12;
            };

            _compareGrid = BuildCompareGrid();
            _compareSource.DataSource = _comparison;
            _compareGrid.DataSource = _compareSource;
            comparePanel.Controls.Add(_compareGrid);
            _compareGrid.BringToFront();
            return page;
        }

        private TabPage BuildSlotTab()
        {
            TabPage page = NewTab("ЭКОНОМИКА СЛОТА");
            _slotToolbar = new Panel();
            _slotToolbar.Dock = DockStyle.Top;
            _slotToolbar.Height = 58;
            _slotToolbar.Padding = new Padding(14, 10, 14, 8);
            _slotToolbar.BackColor = Theme.Panel2;
            page.Controls.Add(_slotToolbar);

            _slotSearch = MakeTextBox("Фильтр предметов…");
            _slotSearch.Location = new Point(14, 11);
            _slotSearch.Width = 330;
            _slotSearch.TextChanged += SearchChanged;
            _slotToolbar.Controls.Add(_slotSearch);

            _slotHelp = new Label();
            _slotHelp.AutoSize = false;
            _slotHelp.Text = "Лучший способ продажи ÷ занятые клетки. Барахолка сравнивается с лучшим торговцем.";
            _slotHelp.ForeColor = Theme.Muted;
            _slotHelp.Location = new Point(370, 18);
            _slotHelp.Size = new Size(720, 22);
            _slotToolbar.Controls.Add(_slotHelp);

            _slotGrid = BuildSlotGrid();
            _slotGrid.DataSource = _slotSource;
            _slotGrid.CellDoubleClick += delegate
            {
                ItemRow item = SelectedSlotItem();
                if (item != null)
                {
                    AddToComparison(item);
                    ShowChart(item);
                }
            };
            page.Controls.Add(_slotGrid);
            _slotGrid.BringToFront();
            return page;
        }

        private TabPage BuildToolsTab()
        {
            TabPage page = NewTab("УДОБСТВА PVE");
            _toolsLayout = new TableLayoutPanel();
            _toolsLayout.Dock = DockStyle.Fill;
            _toolsLayout.ColumnCount = 1;
            _toolsLayout.RowCount = 2;
            _toolsLayout.Margin = Padding.Empty;
            _toolsLayout.Padding = Padding.Empty;
            _toolsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _toolsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            _toolsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(_toolsLayout);

            _battlePassBrowser = new BattlePassBrowser();
            _battlePassBrowser.Margin = Padding.Empty;
            _toolsLayout.Controls.Add(_battlePassBrowser, 0, 1);

            Panel goons = new Panel();
            goons.Dock = DockStyle.Fill;
            goons.Margin = Padding.Empty;
            goons.BackColor = Theme.Card;
            _toolsLayout.Controls.Add(goons, 0, 0);

            Panel stripe = new Panel();
            stripe.Dock = DockStyle.Left;
            stripe.Width = 4;
            stripe.BackColor = Theme.Accent;
            goons.Controls.Add(stripe);

            Label goonsTitle = new Label();
            goonsTitle.AutoSize = true;
            goonsTitle.Text = "ГУНЫ • PVE";
            goonsTitle.Font = Theme.CardTitle;
            goonsTitle.ForeColor = Theme.Accent;
            goonsTitle.Location = new Point(20, 15);
            goons.Controls.Add(goonsTitle);

            _goonsMap = new Label();
            _goonsMap.AutoSize = false;
            _goonsMap.Font = Theme.Heading;
            _goonsMap.ForeColor = Theme.Text;
            _goonsMap.Text = "—";
            _goonsMap.Location = new Point(180, 11);
            _goonsMap.Size = new Size(330, 30);
            goons.Controls.Add(_goonsMap);

            _goonsTime = new Label();
            _goonsTime.AutoSize = false;
            _goonsTime.ForeColor = Theme.Muted;
            _goonsTime.Location = new Point(181, 43);
            _goonsTime.Size = new Size(330, 22);
            goons.Controls.Add(_goonsTime);

            _goonsPrevious = new Label();
            _goonsPrevious.AutoSize = false;
            _goonsPrevious.ForeColor = Theme.Text;
            _goonsPrevious.Location = new Point(20, 75);
            _goonsPrevious.Size = new Size(850, 22);
            _goonsPrevious.Text = "До этого: —";
            goons.Controls.Add(_goonsPrevious);

            _goonsStatus = new Label();
            _goonsStatus.AutoSize = false;
            _goonsStatus.ForeColor = Theme.Faint;
            _goonsStatus.Location = new Point(520, 43);
            _goonsStatus.Size = new Size(300, 22);
            _goonsStatus.Text = "Загрузка отчёта…";
            goons.Controls.Add(_goonsStatus);

            Button refreshGoons = MakeButton("ОБНОВИТЬ", Theme.Card);
            refreshGoons.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refreshGoons.Location = new Point(goons.Width - 310, 25);
            refreshGoons.Size = new Size(110, 36);
            refreshGoons.Click += async delegate { await RefreshGoonsAsync(true); };
            goons.Controls.Add(refreshGoons);

            Button openGoons = MakeButton("ОТКРЫТЬ EFT.SU", Theme.Accent);
            openGoons.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openGoons.Location = new Point(goons.Width - 188, 25);
            openGoons.Size = new Size(170, 36);
            openGoons.Click += delegate { OpenUrl(GoonsUrl); };
            goons.Controls.Add(openGoons);
            goons.Resize += delegate
            {
                openGoons.Left = goons.ClientSize.Width - openGoons.Width - 18;
                refreshGoons.Left = openGoons.Left - refreshGoons.Width - 12;
                _goonsPrevious.Width = Math.Max(300, refreshGoons.Left - _goonsPrevious.Left - 16);
                bool compact = goons.ClientSize.Width < 900;
                _goonsStatus.Visible = !compact;
                _goonsMap.Width = compact ? Math.Max(120, refreshGoons.Left - _goonsMap.Left - 16) : 330;
                _goonsTime.Width = _goonsMap.Width;
            };
            return page;
        }

        private async Task LoadOnStartupAsync()
        {
            Directory.CreateDirectory(_cacheDirectory);
            string itemsPath = Path.Combine(_cacheDirectory, "pve_items.json");
            string ruPath = Path.Combine(_cacheDirectory, "pve_items_ru.json");

            if (File.Exists(itemsPath) && File.Exists(ruPath))
            {
                try
                {
                    _status.Text = "Читаю локальный кэш…";
                    string cachedItems = await Task.Run(delegate { return File.ReadAllText(itemsPath); });
                    string cachedRu = await Task.Run(delegate { return File.ReadAllText(ruPath); });
                    _items = await Task.Run(delegate { return TarkovData.ParseItems(cachedItems, cachedRu); });
                    ApplyFilters();
                    _status.Text = string.Format("Кэш: {0:N0} предметов", _items.Count);
                }
                catch
                {
                    _status.Text = "Кэш повреждён — загружаю заново";
                }
            }

            Task prices = RefreshItemsAsync(false);
            Task goons = RefreshGoonsAsync(false);
            await Task.WhenAll(prices, goons);
        }

        private async Task RefreshItemsAsync(bool userRequested)
        {
            if (_refreshing) return;
            _refreshing = true;
            _refreshButton.Enabled = false;
            _status.ForeColor = Theme.Muted;
            _status.Text = "Загружаю актуальные PvE-цены…";

            try
            {
                string ruPath = Path.Combine(_cacheDirectory, "pve_items_ru.json");
                Task<string> itemTask = _api.GetStringAsync(ItemsUrl);
                Task<string> ruTask;
                if (File.Exists(ruPath) && File.GetLastWriteTimeUtc(ruPath) > DateTime.UtcNow.AddDays(-7))
                    ruTask = Task.Run(delegate { return File.ReadAllText(ruPath); });
                else
                    ruTask = _api.GetStringAsync(RuUrl);

                await Task.WhenAll(itemTask, ruTask);
                string itemsJson = itemTask.Result;
                string ruJson = ruTask.Result;
                List<ItemRow> parsed = await Task.Run(delegate { return TarkovData.ParseItems(itemsJson, ruJson); });
                if (parsed.Count < 100) throw new InvalidDataException("API вернул слишком мало предметов");

                _items = parsed;
                ApplyFilters();
                await Task.Run(delegate
                {
                    SafeWrite(Path.Combine(_cacheDirectory, "pve_items.json"), itemsJson);
                    SafeWrite(ruPath, ruJson);
                });

                DateTime newest = _items.Where(delegate(ItemRow x) { return x.Updated.HasValue; })
                    .Select(delegate(ItemRow x) { return x.Updated.Value; }).DefaultIfEmpty(DateTime.UtcNow).Max();
                _status.ForeColor = Theme.Good;
                _status.Text = string.Format("PvE обновлено {0:HH:mm} • {1:N0} предметов", newest.ToLocalTime(), _items.Count);
                _autoRefresh.Stop();
                _autoRefresh.Start();
            }
            catch (Exception ex)
            {
                _status.ForeColor = Theme.Bad;
                _status.Text = _items.Count > 0 ? "Сеть недоступна — показан кэш" : "Не удалось получить цены";
                if (userRequested || _items.Count == 0)
                    MessageBox.Show(this, "Не удалось обновить PvE-цены.\r\n\r\n" + FriendlyError(ex) + (_items.Count > 0 ? "\r\n\r\nОставлены последние сохранённые данные." : ""), "Tarkov PvE Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _refreshing = false;
                _refreshButton.Enabled = true;
            }
        }

        private async Task RefreshGoonsAsync(bool userRequested)
        {
            if (_refreshingGoons) return;
            _refreshingGoons = true;
            _goonsStatus.ForeColor = Theme.Faint;
            _goonsStatus.Text = "Обновляю отчёт eft.su…";
            try
            {
                string html = await _api.GetStringAsync(GoonsUrl);
                List<GoonReport> reports = await Task.Run(delegate { return GoonReport.ParseMany(html); });
                ShowGoonsReports(reports);
            }
            catch (Exception ex)
            {
                _goonsStatus.ForeColor = Theme.Bad;
                _goonsStatus.Text = "Не удалось прочитать отчёт — откройте eft.su";
                if (_lastGoonReport == null)
                {
                    _headerGoonsMap.Text = "ГУНЫ PVE • НЕТ ДАННЫХ";
                    _headerGoonsPrevious.Text = "(до этого: —)";
                }
                _headerGoonsTime.ForeColor = Theme.Bad;
                _headerGoonsTime.Text = _lastGoonReport == null ? "Отметка: — · нет связи" : string.Format("Отметка: {0:dd.MM HH:mm} · нет связи", _lastGoonReport.Time.ToLocalTime());
                if (userRequested)
                    MessageBox.Show(this, FriendlyError(ex), "Отчёт о Гунах", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { _refreshingGoons = false; }
        }

        internal void ShowGoonsReports(List<GoonReport> reports)
        {
            GoonReport report = reports.FirstOrDefault();
            if (report == null) throw new InvalidDataException("PvE-отчёт не найден на странице");
            List<GoonReport> previous = new List<GoonReport>();
            string lastMap = report.Map;
            foreach (GoonReport candidate in reports.Skip(1))
            {
                if (string.Equals(candidate.Map, lastMap, StringComparison.OrdinalIgnoreCase)) continue;
                previous.Add(candidate);
                lastMap = candidate.Map;
                if (previous.Count == 3) break;
            }
            string previousText = previous.Count == 0 ? "—" : string.Join(" • ", previous.Select(delegate(GoonReport x) { return string.Format("{0} {1:dd.MM HH:mm}", x.Map, x.Time.ToLocalTime()); }).ToArray());
            _goonsMap.Text = report.Map;
            _goonsTime.Text = string.Format("Замечены: {0:dd.MM.yyyy HH:mm}", report.Time.ToLocalTime());
            _goonsPrevious.Text = "До этого: " + previousText;
            _goonsStatus.ForeColor = Theme.Good;
            _goonsStatus.Text = "Источник: eft.su • отчёт сообщества";

            _lastGoonReport = report;
            _headerGoonsMap.Text = "ГУНЫ PVE • " + report.Map.ToUpperInvariant();
            _headerGoonsTime.ForeColor = Theme.Text;
            _headerGoonsTime.Text = string.Format("Отметка: {0:dd.MM HH:mm}", report.Time.ToLocalTime());
            // ponytail: header keeps one previous location; full history stays in the details panel.
            _headerGoonsPrevious.Text = previous.Count == 0 ? "(до этого: —)" : string.Format("(до этого: {0} · {1:dd.MM HH:mm})", previous[0].Map, previous[0].Time.ToLocalTime());
        }

        private void ApplyFilters()
        {
            string query = CleanSearch(_search == null ? "" : _search.Text);
            IEnumerable<ItemRow> market = _items;
            if (_onlyFlea != null && _onlyFlea.Checked)
                market = market.Where(delegate(ItemRow x) { return x.FleaPrice > 0; });
            string matchMode;
            market = ItemSearch.Find(market, query, out matchMode);

            if (_sort != null)
            {
                switch (_qolMode ? 0 : _sort.SelectedIndex)
                {
                    case 1: market = market.OrderByDescending(delegate(ItemRow x) { return x.FleaPrice; }); break;
                    case 2: market = market.OrderByDescending(delegate(ItemRow x) { return x.RubPerSlot; }); break;
                    case 3: market = market.OrderByDescending(delegate(ItemRow x) { return x.DeltaPercent; }); break;
                    default: if (query.Length == 0) market = market.OrderBy(delegate(ItemRow x) { return x.Name; }); break;
                }
            }

            List<ItemRow> marketList = market.ToList();
            _marketSource.DataSource = marketList;
            if (_count != null) _count.Text = string.Format("Найдено: {0:N0}", marketList.Count);
            if (_searchHint != null)
            {
                _searchHint.ForeColor = matchMode == "fuzzy" ? Theme.Accent : Theme.Muted;
                _searchHint.Text = query.Length == 0 ? "Можно с опечаткой или в другой раскладке · Ctrl+F — поиск"
                    : marketList.Count == 0 ? "Ничего не найдено. Попробуйте часть названия или сокращение."
                    : matchMode == "layout" ? "Нашлось в другой раскладке · ваш запрос не изменён"
                    : matchMode == "fuzzy" ? "Похожие названия — проверьте предмет перед оценкой цены"
                    : "Точные совпадения выше · Esc — очистить · Enter — к результатам";
            }

            string slotQuery = CleanSearch(_slotSearch == null ? "" : _slotSearch.Text);
            IEnumerable<ItemRow> slots = _items.Where(delegate(ItemRow x) { return x.BestPrice > 0 && x.Slots > 0; });
            if (slotQuery.Length > 0) slots = ItemSearch.Find(slots, slotQuery, out matchMode);
            _slotSource.DataSource = slots.OrderByDescending(delegate(ItemRow x) { return x.RubPerSlot; }).ThenBy(delegate(ItemRow x) { return x.Name; }).ToList();
        }

        private void SearchChanged(object sender, EventArgs e)
        {
            _searchDelay.Stop();
            _searchDelay.Start();
        }

        private void UpdateDetails(ItemRow item)
        {
            if (item == null)
            {
                _detailName.Text = "Выберите предмет";
                _detailPrices.Text = "";
                _detailMeta.Text = "";
                return;
            }

            _detailName.Text = item.Name;
            if (_qolMode)
            {
                _detailPrices.Text = Money(item.FleaPrice);
                _detailMeta.Text = string.Format("Торговец: {0} · {1}\r\nЗа слот: {2}\r\nРазмер: {3}×{4} · {5:N0} предложений\r\nПоследнее сканирование:\r\n{6}", Money(item.BestTraderPrice), item.TraderName, Money(item.RubPerSlot), item.Width, item.Height, item.OfferCount, item.Updated.HasValue ? item.Updated.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "—");
                return;
            }
            _detailPrices.Text = string.Format("Барахолка  {0}\r\nТорговец   {1}\r\nЗа слот    {2}", Money(item.FleaPrice), Money(item.BestTraderPrice), Money(item.RubPerSlot));
            string delta = item.Average24h > 0 ? string.Format(CultureInfo.InvariantCulture, "{0:+0.0;-0.0;0.0}% к среднему за 24ч", item.DeltaPercent) : "Нет среднего за 24ч";
            _detailMeta.Text = string.Format("{0}\r\nРазмер: {1}×{2} = {3} сл.\r\nПредложений: {4:N0}\r\nЛучший торговец: {5}\r\nСканирование: {6}", delta, item.Width, item.Height, item.Slots, item.OfferCount, item.TraderName, item.Updated.HasValue ? item.Updated.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "—");
        }

        private void AddSelectedToComparison()
        {
            ItemRow item = SelectedMarketItem();
            if (item != null) AddToComparison(item);
        }

        private void AddToComparison(ItemRow item)
        {
            if (_comparison.Any(delegate(ItemRow x) { return x.Id == item.Id; }))
            {
                _tabs.SelectedIndex = 1;
                return;
            }
            if (_comparison.Count >= 6)
            {
                MessageBox.Show(this, "В сравнении уже 6 предметов. Удалите один, чтобы добавить новый.", "Сравнение", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _comparison.Add(item);
            _compareSource.ResetBindings(false);
        }

        private void RemoveComparison()
        {
            if (_compareGrid.CurrentRow == null) return;
            ItemRow item = _compareGrid.CurrentRow.DataBoundItem as ItemRow;
            if (item != null) _comparison.Remove(item);
        }

        private void ShowChartForSelected()
        {
            ItemRow item = SelectedMarketItem();
            if (item != null) ShowChart(item);
        }

        private async void ShowChart(ItemRow item)
        {
            _tabs.SelectedIndex = 1;
            _chartTitle.Text = item.Name + " • загружаю историю…";
            _chart.SetPoints(new List<PricePoint>());
            try
            {
                List<PricePoint> points;
                if (!_history.TryGetValue(item.Id, out points))
                {
                    string json = await _api.GetStringAsync("https://json.tarkov.dev/pve/prices/" + item.Id);
                    points = await Task.Run(delegate { return TarkovData.ParseHistory(json); });
                    _history[item.Id] = points;
                }
                DateTime cutoff = DateTime.UtcNow.AddDays(-30);
                List<PricePoint> visible = points.Where(delegate(PricePoint x) { return x.Time >= cutoff; }).ToList();
                if (visible.Count < 2) visible = points.Skip(Math.Max(0, points.Count - 180)).ToList();
                _chartTitle.Text = string.Format("{0} • история PvE за 30 дней", item.Name);
                _chart.SetPoints(visible);
            }
            catch (Exception ex)
            {
                _chartTitle.Text = item.Name + " • история недоступна";
                MessageBox.Show(this, FriendlyError(ex), "График цены", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private ItemRow SelectedMarketItem()
        {
            return _marketGrid == null || _marketGrid.CurrentRow == null ? null : _marketGrid.CurrentRow.DataBoundItem as ItemRow;
        }

        private ItemRow SelectedSlotItem()
        {
            return _slotGrid == null || _slotGrid.CurrentRow == null ? null : _slotGrid.CurrentRow.DataBoundItem as ItemRow;
        }

        private static void SafeWrite(string path, string contents)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, contents);
            File.Copy(temporary, path, true);
            File.Delete(temporary);
        }

        private static string FriendlyError(Exception ex)
        {
            AggregateException aggregate = ex as AggregateException;
            if (aggregate != null) ex = aggregate.GetBaseException();
            return ex.Message;
        }

        private static string CleanSearch(string text)
        {
            if (text == null || text == "Название или сокращение предмета…" || text == "Фильтр предметов…") return "";
            return text.Trim();
        }

        private static string Money(long value)
        {
            return value <= 0 ? "—" : string.Format("{0:N0} ₽", value);
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть браузер.\r\n" + ex.Message, "Tarkov PvE Companion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private TabPage NewTab(string title)
        {
            TabPage page = new TabPage(title);
            page.BackColor = Theme.Background;
            page.ForeColor = Theme.Text;
            page.Padding = new Padding(0);
            return page;
        }

        private static Button MakeButton(string text, Color color)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = color;
            button.BackColor = color == Theme.Card ? Theme.Card : Color.FromArgb(34, color);
            button.ForeColor = color == Theme.Card ? Theme.Text : color;
            button.Font = Theme.Button;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private static TextBox MakeTextBox(string placeholder)
        {
            TextBox box = new TextBox();
            box.BackColor = Theme.Card;
            box.ForeColor = Theme.Muted;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = Theme.Body;
            box.Text = placeholder;
            box.GotFocus += delegate
            {
                if (box.Text == placeholder)
                {
                    box.Text = "";
                    box.ForeColor = Theme.Text;
                }
            };
            box.LostFocus += delegate
            {
                if (box.Text.Length == 0)
                {
                    box.Text = placeholder;
                    box.ForeColor = Theme.Muted;
                }
            };
            return box;
        }

        private static Panel FeatureCard(string title, string subtitle, Color accent)
        {
            Panel card = new Panel();
            card.Size = new Size(530, 282);
            card.Margin = new Padding(0, 0, 18, 18);
            card.Padding = new Padding(24);
            card.BackColor = Theme.Card;

            Panel stripe = new Panel();
            stripe.Dock = DockStyle.Left;
            stripe.Width = 4;
            stripe.BackColor = accent;
            card.Controls.Add(stripe);

            Label heading = new Label();
            heading.AutoSize = true;
            heading.Font = Theme.CardTitle;
            heading.ForeColor = accent;
            heading.Text = title;
            heading.Location = new Point(24, 22);
            card.Controls.Add(heading);

            Label sub = new Label();
            sub.AutoSize = false;
            sub.ForeColor = Theme.Muted;
            sub.Text = subtitle;
            sub.Location = new Point(26, 55);
            sub.Size = new Size(470, 28);
            card.Controls.Add(sub);
            return card;
        }

        private DataGridView BuildMarketGrid()
        {
            DarkGrid grid = BaseGrid();
            grid.Columns.Add(TextColumn("Предмет", "Name", 260));
            grid.Columns.Add(NumberColumn("Барахолка ₽", "FleaPrice", 105));
            grid.Columns.Add(NumberColumn("Средняя 24ч", "Average24h", 105));
            grid.Columns.Add(DecimalColumn("Δ к 24ч %", "DeltaPercent", 85));
            grid.Columns.Add(NumberColumn("Торговец ₽", "BestTraderPrice", 100));
            grid.Columns.Add(TextColumn("Кому", "TraderName", 90));
            grid.Columns.Add(NumberColumn("Слоты", "Slots", 55));
            grid.Columns.Add(NumberColumn("₽/слот", "RubPerSlot", 100));
            grid.Columns.Add(NumberColumn("Оферов", "OfferCount", 65));
            grid.CellFormatting += GridCellFormatting;
            return grid;
        }

        private DataGridView BuildCompareGrid()
        {
            DarkGrid grid = BaseGrid();
            grid.Columns.Add(TextColumn("Предмет", "Name", 300));
            grid.Columns.Add(NumberColumn("Барахолка ₽", "FleaPrice", 120));
            grid.Columns.Add(NumberColumn("Средняя 24ч", "Average24h", 120));
            grid.Columns.Add(DecimalColumn("Δ %", "DeltaPercent", 80));
            grid.Columns.Add(NumberColumn("Торговец ₽", "BestTraderPrice", 120));
            grid.Columns.Add(TextColumn("Лучше продать", "BestSource", 120));
            grid.Columns.Add(NumberColumn("Слоты", "Slots", 70));
            grid.Columns.Add(NumberColumn("₽/слот", "RubPerSlot", 120));
            grid.CellFormatting += GridCellFormatting;
            return grid;
        }

        private DataGridView BuildSlotGrid()
        {
            DarkGrid grid = BaseGrid();
            grid.Columns.Add(NumberColumn("Место", "Rank", 70));
            grid.Columns.Add(TextColumn("Предмет", "Name", 370));
            grid.Columns.Add(NumberColumn("Размер", "Slots", 85));
            grid.Columns.Add(NumberColumn("Лучшая цена ₽", "BestPrice", 145));
            grid.Columns.Add(TextColumn("Куда продать", "BestSource", 150));
            grid.Columns.Add(NumberColumn("₽ за слот", "RubPerSlot", 150));
            grid.Columns.Add(NumberColumn("Барахолка ₽", "FleaPrice", 130));
            grid.Columns.Add(NumberColumn("Торговец ₽", "BestTraderPrice", 130));
            grid.CellFormatting += GridCellFormatting;
            grid.RowPostPaint += delegate(object sender, DataGridViewRowPostPaintEventArgs e)
            {
                if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count)
                    grid.Rows[e.RowIndex].Cells[0].Value = e.RowIndex + 1;
            };
            return grid;
        }

        private static DarkGrid BaseGrid()
        {
            DarkGrid grid = new DarkGrid();
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.BackgroundColor = Theme.Background;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Theme.Border;
            grid.RowTemplate.Height = 34;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 38;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel2;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Accent;
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.SmallBold;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Panel2;
            grid.DefaultCellStyle.BackColor = Theme.Background;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Theme.Selection;
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.Font = Theme.Body;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.Panel;
            return grid;
        }

        private static DataGridViewTextBoxColumn TextColumn(string title, string property, int width)
        {
            return new DataGridViewTextBoxColumn { HeaderText = title, DataPropertyName = property, Width = width, SortMode = DataGridViewColumnSortMode.Automatic };
        }

        private static DataGridViewTextBoxColumn NumberColumn(string title, string property, int width)
        {
            DataGridViewTextBoxColumn column = TextColumn(title, property, width);
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Format = "N0";
            column.DefaultCellStyle.NullValue = "—";
            return column;
        }

        private static DataGridViewTextBoxColumn DecimalColumn(string title, string property, int width)
        {
            DataGridViewTextBoxColumn column = TextColumn(title, property, width);
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Format = "+0.0;-0.0;0.0";
            return column;
        }

        private static void GridCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            DataGridView grid = (DataGridView)sender;
            string property = grid.Columns[e.ColumnIndex].DataPropertyName;
            if (property == "DeltaPercent" && e.Value != null)
            {
                double value;
                if (double.TryParse(Convert.ToString(e.Value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    e.CellStyle.ForeColor = value >= 0 ? Theme.Good : Theme.Bad;
            }
            else if (property == "RubPerSlot" || property == "BestPrice")
            {
                e.CellStyle.ForeColor = Theme.Good;
                e.CellStyle.Font = Theme.Mono;
            }
            else if (property == "FleaPrice")
            {
                e.CellStyle.ForeColor = Theme.Accent;
            }
        }

        private void DrawTab(object sender, DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Rectangle rect = e.Bounds;
            using (SolidBrush background = new SolidBrush(selected ? Theme.Card : Theme.Panel))
                e.Graphics.FillRectangle(background, rect);
            if (selected)
            {
                using (SolidBrush line = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(line, rect.Left, rect.Bottom - 3, rect.Width, 3);
            }
            TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, Theme.Button, rect, selected ? Theme.Accent : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    internal sealed class TarkovApi : IDisposable
    {
        private readonly HttpClient _client;

        public TarkovApi()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            _client = new HttpClient(handler);
            _client.Timeout = TimeSpan.FromSeconds(90);
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("TarkovPveCompanion/1.0 (+local desktop app)");
            _client.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html;q=0.9,*/*;q=0.5");
        }

        public async Task<string> GetStringAsync(string url)
        {
            HttpResponseMessage response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(string.Format("HTTP {0} при запросе {1}", (int)response.StatusCode, url));
            return await response.Content.ReadAsStringAsync();
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }

    internal static class TarkovData
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 250 };
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly Dictionary<string, string> TraderNames = new Dictionary<string, string>
        {
            { "54cb50c76803fa8b248b4571", "Прапор" },
            { "54cb57776803fa99248b456e", "Терапевт" },
            { "58330581ace78e27b8b10cee", "Лыжник" },
            { "5935c25fb3acc3127c3d8cd9", "Миротворец" },
            { "5a7c2eca46aef81a7ca2145d", "Механик" },
            { "5ac3b934156ae10c4430e83c", "Барахольщик" },
            { "5c0647fdd443bc2504c2d371", "Егерь" },
            { "6617beeaa9cfa777ca915b7c", "Реф" }
        };

        public static List<ItemRow> ParseItems(string itemsJson, string translationsJson)
        {
            Dictionary<string, object> root = Dict(Json.DeserializeObject(itemsJson));
            Dictionary<string, object> data = Dict(root["data"]);
            Dictionary<string, object> items = Dict(data["items"]);
            Dictionary<string, object> translationsRoot = Dict(Json.DeserializeObject(translationsJson));
            Dictionary<string, object> translations = Dict(translationsRoot["data"]);
            List<ItemRow> result = new List<ItemRow>(items.Count);

            foreach (KeyValuePair<string, object> pair in items)
            {
                Dictionary<string, object> raw = Dict(pair.Value);
                string id = Text(raw, "id");
                string nameKey = Text(raw, "name");
                string shortKey = Text(raw, "shortName");
                string normalized = Text(raw, "normalizedName").Replace('-', ' ');
                string name = Translate(translations, nameKey, normalized.Length > 0 ? normalized : id);
                string shortName = Translate(translations, shortKey, "");
                int width = (int)Math.Max(1, Number(raw, "width"));
                int height = (int)Math.Max(1, Number(raw, "height"));
                long flea = Math.Max(0, Number(raw, "lastLowPrice"));
                long average = Math.Max(0, Number(raw, "avg24hPrice"));
                long bestTrader = 0;
                string trader = "—";

                object tradersValue;
                if (raw.TryGetValue("sellToTrader", out tradersValue))
                {
                    foreach (object value in Array(tradersValue))
                    {
                        Dictionary<string, object> offer = Dict(value);
                        long price = Math.Max(0, Number(offer, "priceRUB"));
                        if (price > bestTrader)
                        {
                            bestTrader = price;
                            string traderId = Text(offer, "trader");
                            trader = TraderNames.ContainsKey(traderId) ? TraderNames[traderId] : "Торговец";
                        }
                    }
                }

                DateTime parsedTime;
                DateTime? updated = DateTime.TryParse(Text(raw, "lastScan"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsedTime) ? parsedTime : (DateTime?)null;
                string link = Text(raw, "link");
                if (link.Length == 0 && id.Length > 0) link = "https://tarkov.dev/item/" + Text(raw, "normalizedName");

                result.Add(new ItemRow
                {
                    Id = id,
                    Name = name,
                    ShortName = shortName,
                    NormalizedName = normalized,
                    Width = width,
                    Height = height,
                    FleaPrice = flea,
                    Average24h = average,
                    BestTraderPrice = bestTrader,
                    TraderName = trader,
                    OfferCount = (int)Math.Max(0, Number(raw, "lastOfferCount")),
                    Updated = updated,
                    Link = link
                });
            }
            return result;
        }

        public static List<PricePoint> ParseHistory(string historyJson)
        {
            Dictionary<string, object> root = Dict(Json.DeserializeObject(historyJson));
            List<PricePoint> result = new List<PricePoint>();
            foreach (object value in Array(root["data"]))
            {
                Dictionary<string, object> raw = Dict(value);
                long timestamp = Number(raw, "timestamp");
                long price = Number(raw, "price");
                long minimum = Number(raw, "priceMin");
                if (timestamp > 0 && (price > 0 || minimum > 0))
                    result.Add(new PricePoint { Time = Epoch.AddMilliseconds(timestamp), Average = price, Minimum = minimum });
            }
            return result.OrderBy(delegate(PricePoint x) { return x.Time; }).ToList();
        }

        private static string Translate(Dictionary<string, object> translations, string key, string fallback)
        {
            object value;
            if (key.Length > 0 && translations.TryGetValue(key, out value) && value != null)
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (text.Length > 0) return text;
            }
            return fallback;
        }

        private static Dictionary<string, object> Dict(object value)
        {
            Dictionary<string, object> dictionary = value as Dictionary<string, object>;
            if (dictionary == null) throw new InvalidDataException("Неожиданный формат JSON от tarkov.dev");
            return dictionary;
        }

        private static object[] Array(object value)
        {
            object[] array = value as object[];
            return array ?? new object[0];
        }

        private static string Text(Dictionary<string, object> raw, string key)
        {
            object value;
            return raw.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        private static long Number(Dictionary<string, object> raw, string key)
        {
            object value;
            if (!raw.TryGetValue(key, out value) || value == null) return 0;
            try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }
    }

    internal static class ItemSearch
    {
        internal static string Normalize(string value)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            foreach (char c in (value ?? "").ToLowerInvariant().Replace('ё', 'е'))
            {
                if (char.IsLetterOrDigit(c)) text.Append(c);
                else if (text.Length > 0 && text[text.Length - 1] != ' ') text.Append(' ');
            }
            return text.ToString().Trim();
        }

        private static string SwapLayout(string value, bool toRussian)
        {
            const string english = "`qwertyuiop[]asdfghjkl;'zxcvbnm,.";
            const string russian = "ёйцукенгшщзхъфывапролджэячсмитьбю";
            string from = toRussian ? english : russian;
            string to = toRussian ? russian : english;
            return new string((value ?? "").ToLowerInvariant().Select(delegate(char c) { int i = from.IndexOf(c); return i < 0 ? c : to[i]; }).ToArray());
        }

        internal static List<ItemRow> Find(IEnumerable<ItemRow> source, string query, out string mode)
        {
            mode = "";
            List<ItemRow> items = source.ToList();
            string original = Normalize(query);
            if (original.Length == 0) return items;
            string[] variants = new[] { original, Normalize(SwapLayout(query, true)), Normalize(SwapLayout(query, false)) }
                .Where(delegate(string x) { return x.Length > 0; }).Distinct().ToArray();
            List<KeyValuePair<ItemRow, int>> matches = new List<KeyValuePair<ItemRow, int>>();
            foreach (ItemRow item in items)
            {
                int best = int.MaxValue;
                for (int i = 0; i < variants.Length; i++)
                {
                    string variant = variants[i];
                    string[] tokens = variant.Split(' ');
                    if (!tokens.All(delegate(string token) { return TokenMatches(item, token); })) continue;
                    int rank = item.SearchName == variant || item.SearchShortName == variant ? 0 : item.SearchName.StartsWith(variant, StringComparison.Ordinal) ? 1 : 2;
                    best = Math.Min(best, rank + (i == 0 ? 0 : 4));
                }
                if (best < int.MaxValue) matches.Add(new KeyValuePair<ItemRow, int>(item, best));
            }
            if (matches.Count > 0)
            {
                matches = matches.OrderBy(delegate(KeyValuePair<ItemRow, int> x) { return x.Value; }).ThenBy(delegate(KeyValuePair<ItemRow, int> x) { return x.Key.Name; }).ToList();
                mode = matches[0].Value >= 4 ? "layout" : "";
                return matches.Select(delegate(KeyValuePair<ItemRow, int> x) { return x.Key; }).ToList();
            }

            // ponytail: fuzzy fallback only when exact/layout search is empty; 2 edits total, no numeric correction.
            foreach (ItemRow item in items)
            {
                int best = 3;
                foreach (string variant in variants)
                {
                    int cost = 0;
                    foreach (string token in variant.Split(' '))
                    {
                        if (TokenMatches(item, token)) continue;
                        if (token.Length < 4 || token.Any(char.IsDigit)) { cost = 3; break; }
                        int limit = token.Length >= 8 ? 2 : 1;
                        int nearest = limit + 1;
                        foreach (string word in item.SearchWords)
                            nearest = Math.Min(nearest, EditDistance(token, word, limit));
                        if (nearest > limit) { cost = 3; break; }
                        cost += nearest;
                        if (cost > 2) break;
                    }
                    best = Math.Min(best, cost);
                }
                if (best <= 2) matches.Add(new KeyValuePair<ItemRow, int>(item, best * 2 + (variants.Any(delegate(string v) { return IsSingleSwap(v, item.SearchShortName); }) ? 0 : 1)));
            }
            mode = matches.Count > 0 ? "fuzzy" : "";
            return matches.OrderBy(delegate(KeyValuePair<ItemRow, int> x) { return x.Value; })
                .ThenBy(delegate(KeyValuePair<ItemRow, int> x) { return x.Key.Name; }).Take(50)
                .Select(delegate(KeyValuePair<ItemRow, int> x) { return x.Key; }).ToList();
        }

        private static bool TokenMatches(ItemRow item, string token)
        {
            return token.All(char.IsDigit) ? item.SearchWords.Contains(token) : item.SearchText.IndexOf(token, StringComparison.Ordinal) >= 0;
        }

        private static bool IsSingleSwap(string a, string b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == b[i]) continue;
                return a[i] == b[i + 1] && a[i + 1] == b[i] && a.Substring(i + 2) == b.Substring(i + 2);
            }
            return false;
        }

        internal static int EditDistance(string a, string b, int limit)
        {
            if (Math.Abs(a.Length - b.Length) > limit) return limit + 1;
            int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
            int[] older = previous;
            for (int i = 1; i <= a.Length; i++)
            {
                int[] current = new int[b.Length + 1];
                current[0] = i;
                int minimum = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) current[j] = Math.Min(current[j], older[j - 2] + 1);
                    minimum = Math.Min(minimum, current[j]);
                }
                if (minimum > limit) return limit + 1;
                older = previous;
                previous = current;
            }
            return previous[b.Length];
        }

        internal static void SelfCheck()
        {
            List<ItemRow> items = new List<ItemRow>
            {
                new ItemRow { Name = "Сахар", ShortName = "Сахар", NormalizedName = "sugar" },
                new ItemRow { Name = "Кожный трансиллюминатор LEDX", ShortName = "LEDX", NormalizedName = "ledx-skin-transilluminator" },
                new ItemRow { Name = "Лампа накаливания", ShortName = "Лампа" },
                new ItemRow { Name = "Патроны 5.45x39 PS", ShortName = "PS" },
                new ItemRow { Name = "Банка консервированной сайры", ShortName = "Сайра" }
            };
            string mode;
            foreach (string query in new[] { "cf[fh", "сахра", "сахр", "САХАР" })
                if (Find(items, query, out mode).FirstOrDefault() != items[0]) throw new InvalidOperationException("Search failed: " + query);
            if (Find(items, "дувч", out mode).FirstOrDefault() != items[1] || mode != "layout") throw new InvalidOperationException("Reverse-layout search failed");
            if (Find(items, "лампаа", out mode).FirstOrDefault() != items[2] || mode != "fuzzy") throw new InvalidOperationException("Typo search failed");
            if (Find(items, "5.56", out mode).Count != 0 || Find(items, "абракадабра", out mode).Count != 0) throw new InvalidOperationException("Search false-positive check failed");
            if (Find(items, "", out mode).Count != items.Count || Normalize("Ёж / АК-74") != "еж ак 74") throw new InvalidOperationException("Search normalization failed");
        }
    }

    internal sealed class ItemRow
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ShortName { get; set; }
        public string NormalizedName { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long FleaPrice { get; set; }
        public long Average24h { get; set; }
        public long BestTraderPrice { get; set; }
        public string TraderName { get; set; }
        public int OfferCount { get; set; }
        public DateTime? Updated { get; set; }
        public string Link { get; set; }
        public int Slots { get { return Math.Max(1, Width * Height); } }
        public long BestPrice { get { return Math.Max(FleaPrice, BestTraderPrice); } }
        public long RubPerSlot { get { return BestPrice / Slots; } }
        public string BestSource { get { return FleaPrice >= BestTraderPrice && FleaPrice > 0 ? "Барахолка" : TraderName; } }
        public double DeltaPercent { get { return Average24h > 0 && FleaPrice > 0 ? ((double)FleaPrice / Average24h - 1.0) * 100.0 : 0; } }
        private string _searchText;
        private string[] _searchWords;
        private string _searchName;
        private string _searchShortName;
        public string SearchText { get { return _searchText ?? (_searchText = ItemSearch.Normalize(Name + " " + ShortName + " " + NormalizedName)); } }
        internal string[] SearchWords { get { return _searchWords ?? (_searchWords = SearchText.Split(' ').Distinct().ToArray()); } }
        internal string SearchName { get { return _searchName ?? (_searchName = ItemSearch.Normalize(Name)); } }
        internal string SearchShortName { get { return _searchShortName ?? (_searchShortName = ItemSearch.Normalize(ShortName)); } }
        public int Rank { get { return 0; } }
    }

    internal sealed class PricePoint
    {
        public DateTime Time { get; set; }
        public long Average { get; set; }
        public long Minimum { get; set; }
    }

    internal sealed class GoonReport
    {
        public string Map { get; set; }
        public DateTime Time { get; set; }

        public static GoonReport Parse(string html)
        {
            return ParseMany(html).FirstOrDefault();
        }

        public static List<GoonReport> ParseMany(string html)
        {
            string normalized = html.Replace("\\\"", "\"");
            Regex regex = new Regex("\\\"timestamp\\\":\\\"(?<time>[^\\\"]+)\\\".{0,600}?\\\"modeId\\\":\\\"PVE\\\".{0,600}?\\\"map\\\":\\{\\\"name\\\":\\\"(?<map>[^\\\"]+)\\\"", RegexOptions.Singleline | RegexOptions.CultureInvariant);
            List<GoonReport> reports = new List<GoonReport>();
            foreach (Match match in regex.Matches(normalized))
            {
                DateTime time;
                if (!DateTime.TryParse(match.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time)) continue;
                reports.Add(new GoonReport { Map = match.Groups["map"].Value, Time = time });
            }
            return reports
                .GroupBy(delegate(GoonReport x) { return x.Map + "|" + x.Time.Ticks.ToString(CultureInfo.InvariantCulture); })
                .Select(delegate(IGrouping<string, GoonReport> x) { return x.First(); })
                .OrderByDescending(delegate(GoonReport x) { return x.Time; })
                .ToList();
        }
    }

    internal sealed class PriceChart : Control
    {
        private List<PricePoint> _points = new List<PricePoint>();

        public PriceChart()
        {
            DoubleBuffered = true;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Padding = new Padding(64, 22, 22, 44);
        }

        public void SetPoints(List<PricePoint> points)
        {
            _points = points ?? new List<PricePoint>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Theme.Card);

            Rectangle plot = new Rectangle(Padding.Left, Padding.Top, Math.Max(10, Width - Padding.Horizontal), Math.Max(10, Height - Padding.Vertical));
            using (Pen border = new Pen(Theme.Border)) e.Graphics.DrawRectangle(border, plot);
            if (_points.Count < 2)
            {
                TextRenderer.DrawText(e.Graphics, "Нет данных для графика", Theme.Body, plot, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            long minimum = _points.Select(delegate(PricePoint x) { return Math.Min(x.Minimum > 0 ? x.Minimum : x.Average, x.Average > 0 ? x.Average : x.Minimum); }).Where(delegate(long x) { return x > 0; }).DefaultIfEmpty(0).Min();
            long maximum = _points.Select(delegate(PricePoint x) { return Math.Max(x.Average, x.Minimum); }).DefaultIfEmpty(1).Max();
            if (maximum <= minimum) maximum = minimum + 1;
            double margin = (maximum - minimum) * 0.08;
            double low = Math.Max(0, minimum - margin);
            double high = maximum + margin;

            for (int i = 0; i <= 4; i++)
            {
                int y = plot.Top + (int)(plot.Height * i / 4.0);
                using (Pen grid = new Pen(Theme.Border)) e.Graphics.DrawLine(grid, plot.Left, y, plot.Right, y);
                long labelValue = (long)(high - (high - low) * i / 4.0);
                TextRenderer.DrawText(e.Graphics, ShortMoney(labelValue), Theme.Small, new Rectangle(0, y - 9, plot.Left - 8, 18), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }

            DateTime first = _points[0].Time;
            DateTime last = _points[_points.Count - 1].Time;
            double span = Math.Max(1, (last - first).TotalSeconds);
            PointF[] average = _points.Where(delegate(PricePoint x) { return x.Average > 0; }).Select(delegate(PricePoint x) { return PointFor(x.Time, x.Average, first, span, low, high, plot); }).ToArray();
            PointF[] minima = _points.Where(delegate(PricePoint x) { return x.Minimum > 0; }).Select(delegate(PricePoint x) { return PointFor(x.Time, x.Minimum, first, span, low, high, plot); }).ToArray();
            if (average.Length > 1)
                using (Pen line = new Pen(Theme.Accent, 2.5f)) e.Graphics.DrawLines(line, average);
            if (minima.Length > 1)
                using (Pen line = new Pen(Theme.Teal, 1.8f)) e.Graphics.DrawLines(line, minima);

            TextRenderer.DrawText(e.Graphics, first.ToLocalTime().ToString("dd.MM"), Theme.Small, new Rectangle(plot.Left, plot.Bottom + 8, 80, 22), Theme.Muted, TextFormatFlags.Left);
            TextRenderer.DrawText(e.Graphics, last.ToLocalTime().ToString("dd.MM"), Theme.Small, new Rectangle(plot.Right - 80, plot.Bottom + 8, 80, 22), Theme.Muted, TextFormatFlags.Right);

            Rectangle legend = new Rectangle(plot.Right - 300, plot.Top + 10, 285, 24);
            using (Pen p = new Pen(Theme.Accent, 3)) e.Graphics.DrawLine(p, legend.Left, legend.Top + 7, legend.Left + 28, legend.Top + 7);
            TextRenderer.DrawText(e.Graphics, "средняя", Theme.Small, new Point(legend.Left + 34, legend.Top), Theme.Text);
            using (Pen p = new Pen(Theme.Teal, 3)) e.Graphics.DrawLine(p, legend.Left + 125, legend.Top + 7, legend.Left + 153, legend.Top + 7);
            TextRenderer.DrawText(e.Graphics, "минимальная", Theme.Small, new Point(legend.Left + 159, legend.Top), Theme.Text);
        }

        private static PointF PointFor(DateTime time, long value, DateTime first, double span, double low, double high, Rectangle plot)
        {
            float x = plot.Left + (float)((time - first).TotalSeconds / span * plot.Width);
            float y = plot.Bottom - (float)((value - low) / (high - low) * plot.Height);
            return new PointF(x, y);
        }

        private static string ShortMoney(long value)
        {
            if (value >= 1000000) return string.Format(CultureInfo.InvariantCulture, "{0:0.0}м", value / 1000000.0);
            if (value >= 1000) return string.Format(CultureInfo.InvariantCulture, "{0:0}к", value / 1000.0);
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class DarkGrid : DataGridView
    {
        private const int ScrollSize = 10;
        private int _dragAxis;
        private int _dragOffset;

        public DarkGrid()
        {
            DoubleBuffered = true;
            ScrollBars = ScrollBars.None;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle vertical = VerticalTrack();
            Rectangle horizontal = HorizontalTrack();
            using (SolidBrush track = new SolidBrush(Theme.Panel2))
            using (SolidBrush thumb = new SolidBrush(Theme.Teal))
            {
                if (!vertical.IsEmpty)
                {
                    e.Graphics.FillRectangle(track, vertical);
                    e.Graphics.FillRectangle(thumb, VerticalThumb(vertical));
                }
                if (!horizontal.IsEmpty)
                {
                    e.Graphics.FillRectangle(track, horizontal);
                    e.Graphics.FillRectangle(thumb, HorizontalThumb(horizontal));
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Rectangle vertical = VerticalTrack();
            Rectangle horizontal = HorizontalTrack();
            if (!vertical.IsEmpty && vertical.Contains(e.Location))
            {
                Rectangle thumb = VerticalThumb(vertical);
                _dragAxis = 1;
                _dragOffset = thumb.Contains(e.Location) ? e.Y - thumb.Y : thumb.Height / 2;
                SetVerticalPosition(e.Y - _dragOffset, vertical, thumb.Height);
                Capture = true;
                return;
            }
            if (!horizontal.IsEmpty && horizontal.Contains(e.Location))
            {
                Rectangle thumb = HorizontalThumb(horizontal);
                _dragAxis = 2;
                _dragOffset = thumb.Contains(e.Location) ? e.X - thumb.X : thumb.Width / 2;
                SetHorizontalPosition(e.X - _dragOffset, horizontal, thumb.Width);
                Capture = true;
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragAxis == 1)
            {
                Rectangle track = VerticalTrack();
                if (!track.IsEmpty) SetVerticalPosition(e.Y - _dragOffset, track, VerticalThumb(track).Height);
                return;
            }
            if (_dragAxis == 2)
            {
                Rectangle track = HorizontalTrack();
                if (!track.IsEmpty) SetHorizontalPosition(e.X - _dragOffset, track, HorizontalThumb(track).Width);
                return;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragAxis = 0;
            Capture = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Shift) == Keys.Shift && !HorizontalTrack().IsEmpty)
            {
                int maximum = HorizontalMaximum();
                HorizontalScrollingOffset = Math.Max(0, Math.Min(maximum, HorizontalScrollingOffset - Math.Sign(e.Delta) * 90));
            }
            else if (!VerticalTrack().IsEmpty)
            {
                int first = FirstDisplayedScrollingRowIndex < 0 ? 0 : FirstDisplayedScrollingRowIndex;
                SetFirstDisplayedRow(first - Math.Sign(e.Delta) * 3);
            }
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs e)
        {
            base.OnScroll(e);
            Invalidate();
        }

        protected override void OnDataBindingComplete(DataGridViewBindingCompleteEventArgs e)
        {
            base.OnDataBindingComplete(e);
            Invalidate();
        }

        protected override void OnRowsAdded(DataGridViewRowsAddedEventArgs e)
        {
            base.OnRowsAdded(e);
            Invalidate();
        }

        protected override void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e)
        {
            base.OnRowsRemoved(e);
            Invalidate();
        }

        protected override void OnColumnWidthChanged(DataGridViewColumnEventArgs e)
        {
            base.OnColumnWidthChanged(e);
            Invalidate();
        }

        private Rectangle VerticalTrack()
        {
            int displayed = DisplayedRowCount(false);
            if (Rows.Count <= 0 || displayed <= 0 || displayed >= Rows.Count) return Rectangle.Empty;
            bool horizontal = HorizontalMaximum() > 0;
            int top = ColumnHeadersVisible ? ColumnHeadersHeight + 2 : 2;
            int height = ClientSize.Height - top - (horizontal ? ScrollSize + 2 : 2);
            return height > 30 ? new Rectangle(ClientSize.Width - ScrollSize, top, ScrollSize - 2, height) : Rectangle.Empty;
        }

        private Rectangle HorizontalTrack()
        {
            if (HorizontalMaximum() <= 0) return Rectangle.Empty;
            bool vertical = Rows.Count > DisplayedRowCount(false);
            int width = ClientSize.Width - (vertical ? ScrollSize + 2 : 2);
            return width > 50 ? new Rectangle(2, ClientSize.Height - ScrollSize, width, ScrollSize - 2) : Rectangle.Empty;
        }

        private Rectangle VerticalThumb(Rectangle track)
        {
            int displayed = Math.Max(1, DisplayedRowCount(false));
            int height = Math.Max(28, track.Height * displayed / Math.Max(displayed, Rows.Count));
            int maximum = Math.Max(1, Rows.Count - displayed);
            int first = Math.Max(0, FirstDisplayedScrollingRowIndex);
            int top = track.Y + (track.Height - height) * Math.Min(maximum, first) / maximum;
            return new Rectangle(track.X, top, track.Width, height);
        }

        private Rectangle HorizontalThumb(Rectangle track)
        {
            int total = Math.Max(ClientSize.Width, VisibleColumnsWidth());
            int available = Math.Max(1, ClientSize.Width - ScrollSize);
            int width = Math.Max(40, track.Width * available / total);
            int maximum = Math.Max(1, HorizontalMaximum());
            int left = track.X + (track.Width - width) * Math.Min(maximum, HorizontalScrollingOffset) / maximum;
            return new Rectangle(left, track.Y, width, track.Height);
        }

        private void SetVerticalPosition(int top, Rectangle track, int thumbHeight)
        {
            int range = Math.Max(1, track.Height - thumbHeight);
            int clamped = Math.Max(track.Y, Math.Min(track.Bottom - thumbHeight, top));
            int displayed = Math.Max(1, DisplayedRowCount(false));
            int maximum = Math.Max(0, Rows.Count - displayed);
            SetFirstDisplayedRow((clamped - track.Y) * maximum / range);
        }

        private void SetFirstDisplayedRow(int index)
        {
            if (Rows.Count == 0) return;
            index = Math.Max(0, Math.Min(Rows.Count - 1, index));
            try { FirstDisplayedScrollingRowIndex = index; }
            catch { }
        }

        private void SetHorizontalPosition(int left, Rectangle track, int thumbWidth)
        {
            int range = Math.Max(1, track.Width - thumbWidth);
            int clamped = Math.Max(track.X, Math.Min(track.Right - thumbWidth, left));
            HorizontalScrollingOffset = (clamped - track.X) * HorizontalMaximum() / range;
            Invalidate();
        }

        private int HorizontalMaximum()
        {
            return Math.Max(0, VisibleColumnsWidth() - ClientSize.Width);
        }

        private int VisibleColumnsWidth()
        {
            int width = RowHeadersVisible ? RowHeadersWidth : 0;
            foreach (DataGridViewColumn column in Columns)
                if (column.Visible) width += column.Width + (column.DividerWidth > 0 ? column.DividerWidth : 0);
            return width;
        }
    }

    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(12, 14, 16);
        public static readonly Color Panel = Color.FromArgb(18, 21, 24);
        public static readonly Color Panel2 = Color.FromArgb(23, 27, 30);
        public static readonly Color Card = Color.FromArgb(28, 32, 35);
        public static readonly Color Border = Color.FromArgb(49, 54, 58);
        public static readonly Color Selection = Color.FromArgb(54, 61, 64);
        public static readonly Color Text = Color.FromArgb(232, 230, 218);
        public static readonly Color Muted = Color.FromArgb(157, 159, 153);
        public static readonly Color Faint = Color.FromArgb(111, 116, 113);
        public static readonly Color Accent = Color.FromArgb(214, 178, 102);
        public static readonly Color Teal = Color.FromArgb(86, 183, 169);
        public static readonly Color Good = Color.FromArgb(113, 202, 139);
        public static readonly Color Bad = Color.FromArgb(226, 105, 94);
        public static readonly Font Body = new Font("Segoe UI", 9.25f, FontStyle.Regular);
        public static readonly Font Small = new Font("Segoe UI", 8.25f, FontStyle.Regular);
        public static readonly Font SmallBold = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static readonly Font Button = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static readonly Font Title = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
        public static readonly Font Heading = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
        public static readonly Font CardTitle = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
        public static readonly Font CardNumber = new Font("Segoe UI Semibold", 27f, FontStyle.Bold);
        public static readonly Font Mono = new Font("Consolas", 9.5f, FontStyle.Bold);
        public static readonly Font MonoLarge = new Font("Consolas", 12.5f, FontStyle.Bold);
        public static readonly Font Search = new Font("Segoe UI", 17f, FontStyle.Regular);
        public static readonly Font Price = new Font("Segoe UI Semibold", 22f, FontStyle.Bold);
    }
}
