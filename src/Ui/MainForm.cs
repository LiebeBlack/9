// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - ventana principal.
//
// Regla de oro de la interfaz: la red es lo importante y la ventana es prescindible. Por eso
// todos los manejadores de eventos comprueban antes si la ventana sigue viva; cuando entra en
// Modo Fantasma se destruye y los servicios siguen trabajando sin interfaz.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;
using NetForge.App;
using NetForge.Core;
using NetForge.Firewall;
using NetForge.Net;

namespace NetForge.Ui
{
    public sealed class MainForm : Form
    {
        private readonly NetForgeContext _context;
        private readonly Timer _tick;
        private bool _released;

        /// <summary>
        /// Verdadero mientras la ventana se rellena sola. Los manejadores de los controles
        /// comprueban esta marca porque asignar un valor desde el codigo dispara el mismo evento
        /// que un clic: sin ella, pintar la lista de opciones guardaba las preferencias (y
        /// arrancaba o borraba la tarea de arranque automatico) cada vez que la interfaz se
        /// refrescaba, incluida la autocomprobacion, que llegaba a escribir en el fichero real
        /// del usuario. Un refresco no es una decision del usuario.
        /// </summary>
        private bool _loading;

        private TabControl _tabs;
        private TabPage _clientsPage;
        private StatusPill _stateLabel;
        private StatusPill _endpointLabel;
        private StatusPill _guardLabel;
        private StatusPill _bannerLabel;
        private TextBox _ssid;
        private TextBox _password;
        private CheckBox _showPassword;
        private ComboBox _engine;
        private Button _startStop;
        private CheckBox _share;
        private CheckBox _autostart;
        private CheckBox _startMinimized;

        private DataGridView _clients;
        private Button _hammer;
        private Button _export;
        private StatusPill _clientsCount;
        private TextBox _filter;
        private Button _clearFilter;

        // Resumen de un vistazo: cuatro tarjetas que atajan a la pestana de la que hablan.
        private StatTile _tileClients;
        private StatTile _tileNet;
        private StatTile _tileFiles;
        private StatTile _tileGuard;
        private IList<LanClient> _lastClients;

        private ComboBox _language;
        private ComboBox _band;
        private NumericUpDown _radarSeconds;
        private NumericUpDown _guardSeconds;
        private NumericUpDown _portalPort;
        private NumericUpDown _ftpPort;
        private Button _saveSettings;
        private Button _defaultSettings;
        private Button _openDataFolder;
        private Label _settingsNote;
        private Label _serviceState;
        private Label _serviceNote;
        private Button _startService;
        private CheckBox _filesAuto;
        private CheckBox _notify;
        private CheckBox _notifyJoin;
        private TextBox _ftpUser;
        private Label _autostartState;
        private Button _repairAutostart;
        private ContextMenuStrip _clientMenu;
        private ToolStripMenuItem _menuBan;
        private ToolStripMenuItem _menuPardon;

        private StatusPill _statusMemory;
        private StatusPill _statusPorts;
        private StatusPill _statusServers;
        private StatusPill _statusClients;

        private DataGridView _bans;
        private Button _pardon;
        private Button _pardonAll;

        private TextBox _folder;
        private Button _browse;
        private CheckBox _upload;
        private CheckBox _delete;
        private Button _startServers;
        private Button _stopServers;
        private PictureBox _qr;
        private Label _qrHint;
        private LinkLabel _portalLink;
        private Button _copyLink;
        private LinkLabel _ftpLink;
        private StatusPill _filesStats;

        private TextBox _diagnostics;
        private Button _refreshDiagnostics;
        private Button _copyDiagnostics;

        private LogList _logList;
        private Button _saveLog;
        private Button _openLog;
        private TextBox _logFilter;
        private CheckBox _logWarnOnly;
        private string _logSignature;

        private Button _saveDiagnostics;
        private Button _openFolder;
        private string _lastQrPayload;

        public MainForm(NetForgeContext context)
        {
            _context = context;
            BuildUi();
            Wire();
            RefreshAll();
            _tick = new Timer();
            _tick.Interval = 1500;
            _tick.Tick += OnTick;
            _tick.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // La "X" no cierra: destruye la interfaz y deja la red trabajando.
                e.Cancel = true;
                _context.CollapseWindow();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            Release();
            if (disposing && _tick != null)
            {
                _tick.Stop();
                _tick.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Atajos de teclado: la ventana es un panel de control y se maneja mucho con el teclado
        /// delante. Ctrl+F busca, Ctrl+E exporta, Ctrl+S guarda los ajustes, F5 refresca y
        /// Ctrl+1 a Ctrl+6 saltan de pestana.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_released || IsDisposed)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            Keys key = keyData & Keys.KeyCode;
            bool control = (keyData & Keys.Control) == Keys.Control;

            if (control && key == Keys.F)
            {
                SelectTab(0);
                if (_filter != null)
                {
                    _filter.Focus();
                }

                return true;
            }

            if (control && key == Keys.E)
            {
                OnExportClients(this, EventArgs.Empty);
                return true;
            }

            if (control && key == Keys.S)
            {
                OnSaveSettings(this, EventArgs.Empty);
                return true;
            }

            if (!control && key == Keys.F5)
            {
                RefreshAll();
                return true;
            }

            // Los numeros se comparan como enteros: C# no deja comparar ni restar valores de un
            // enumerado entre si, solo con ==.
            int digit = (int)key;
            if (control && digit >= (int)Keys.D0 && digit <= (int)Keys.D9)
            {
                int index = digit == (int)Keys.D0 ? 9 : digit - (int)Keys.D1;
                if (_tabs != null && index >= 0 && index < _tabs.TabPages.Count)
                {
                    SelectTab(index);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SelectTab(int index)
        {
            if (_tabs != null && index >= 0 && index < _tabs.TabPages.Count)
            {
                _tabs.SelectedIndex = index;
            }
        }

        private void Release()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _context.Radar.Updated -= OnRadarUpdated;
            _context.Hotspot.Changed -= OnAnythingChanged;
            _context.Guard.Noticed -= OnGuardEvent;
            _context.Guard.Resurrected -= OnGuardEvent;
            _context.Guard.GaveUp -= OnGuardEvent;
            _context.Bans.Changed -= OnAnythingChanged;
            _context.Files.Changed -= OnAnythingChanged;
            Log.Changed -= OnLogChanged;
        }

        private void Wire()
        {
            _context.Radar.Updated += OnRadarUpdated;
            _context.Hotspot.Changed += OnAnythingChanged;
            _context.Guard.Noticed += OnGuardEvent;
            _context.Guard.Resurrected += OnGuardEvent;
            _context.Guard.GaveUp += OnGuardEvent;
            _context.Bans.Changed += OnAnythingChanged;
            _context.Files.Changed += OnAnythingChanged;
            Log.Changed += OnLogChanged;
        }

        private void BuildUi()
        {
            Theme.StyleForm(this);
            Text = Strings.T("app.title");
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
                // Sin icono propio se usa el generico de Windows.
            }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.BackColor = Theme.Background;
            Controls.Add(root);

            root.Padding = new Padding(10, 10, 10, 0);
            root.Controls.Add(BuildHeader(), 0, 0);

            FlatTabControl tabs = Theme.Tabs();
            _tabs = tabs;
            _clientsPage = BuildClientsPage();
            tabs.TabPages.Add(_clientsPage);
            tabs.TabPages.Add(BuildBansPage());
            tabs.TabPages.Add(BuildFilesPage());
            tabs.TabPages.Add(BuildSettingsPage());
            tabs.TabPages.Add(BuildDiagnosticsPage());
            tabs.TabPages.Add(BuildLogPage());
            tabs.FitTabs();
            Theme.Tip(tabs, Strings.T("app.shortcuts"));
            root.Controls.Add(tabs, 0, 1);
            root.Controls.Add(BuildStatusBar(), 0, 2);
        }

        /// <summary>
        /// Barra inferior: lo que se quiere saber de un vistazo (memoria real y puertos en uso)
        /// sin tener que abrir el diagnostico ni leer el registro.
        /// </summary>
        private Control BuildStatusBar()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Bottom;
            panel.AutoSize = true;
            panel.BackColor = Theme.Surface;
            panel.Padding = new Padding(10, 4, 10, 4);
            panel.WrapContents = false;

            _statusMemory = StatusSegment(panel);
            _statusPorts = StatusSegment(panel);
            _statusServers = StatusSegment(panel);
            _statusClients = StatusSegment(panel);
            return panel;
        }

        /// <summary>
        /// Un tramo de la barra inferior: punto de color y texto, separado del anterior por una
        /// linea fina. Asi la barra se lee por bloques y no como una frase larga.
        /// </summary>
        private static StatusPill StatusSegment(FlowLayoutPanel panel)
        {
            if (panel.Controls.Count > 0)
            {
                Panel separator = new Panel();
                separator.Width = 1;
                separator.Height = 14;
                separator.BackColor = Theme.BorderSoft;
                separator.Margin = new Padding(6, 4, 10, 0);
                panel.Controls.Add(separator);
            }

            StatusPill pill = Theme.Pill(string.Empty);
            pill.BackColor = Theme.Surface;
            pill.FillColor = Color.Transparent;
            pill.ShowDot = true;
            pill.DotColor = Theme.TextMuted;
            pill.InkColor = Theme.TextMuted;
            pill.Font = Theme.SmallFont;
            pill.Margin = new Padding(0, 1, 0, 0);
            panel.Controls.Add(pill);
            return pill;
        }

        /// <summary>
        /// Cabecera: marca, titulo, pildora de estado y direccion de la red en la primera fila;
        /// el formulario de la red debajo, alineado por columnas; y las opciones al final.
        ///
        /// Antes esto era un unico panel que se repartia solo: al no caber, el campo de la clave
        /// se iba a la linea siguiente y quedaba pegado a la etiqueta. Con una rejilla de columnas
        /// cada campo sabe donde empieza, y en la ventana mas pequena no se pisan.
        /// </summary>
        private Control BuildHeader()
        {
            CardPanel card = Theme.Card();
            card.Dock = DockStyle.Top;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.Radius = 12;
            card.Padding = new Padding(16, 10, 16, 14);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Top;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.ColumnCount = 1;
            layout.BackColor = Theme.Surface;
            layout.Margin = new Padding(0);

            layout.Controls.Add(BuildBrandRow(), 0, 0);
            layout.Controls.Add(BuildNetworkForm(), 0, 1);
            layout.Controls.Add(BuildHeaderOptions(), 0, 2);

            card.Controls.Add(layout);
            return card;
        }

        private Control BuildBrandRow()
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.Dock = DockStyle.Top;
            row.ColumnCount = 4;
            row.BackColor = Theme.Surface;
            row.Margin = new Padding(0, 0, 0, 4);

            row.Controls.Add(Theme.Logo(), 0, 0);

            TableLayoutPanel titles = new TableLayoutPanel();
            titles.AutoSize = true;
            titles.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            titles.ColumnCount = 1;
            titles.RowCount = 2;
            titles.BackColor = Theme.Surface;
            titles.Margin = new Padding(0, 0, 16, 0);

            Label title = new Label();
            title.Text = Strings.T("app.title");
            title.Font = Theme.TitleFont;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Margin = new Padding(0);
            titles.Controls.Add(title, 0, 0);

            Label subtitle = new Label();
            subtitle.Text = Strings.T("app.subtitle");
            subtitle.Font = Theme.SmallFont;
            subtitle.ForeColor = Theme.TextMuted;
            subtitle.AutoSize = true;
            subtitle.Margin = new Padding(0, 2, 0, 0);
            titles.Controls.Add(subtitle, 0, 1);
            row.Controls.Add(titles, 1, 0);

            _stateLabel = Theme.Pill(Strings.T("network.state.idle"));
            _stateLabel.Font = Theme.StateFont;
            _stateLabel.FillColor = Theme.SurfaceHigh;
            _stateLabel.DotColor = Theme.TextMuted;
            _stateLabel.InkColor = Theme.Text;
            _stateLabel.Margin = new Padding(0, 4, 8, 0);
            row.Controls.Add(_stateLabel, 2, 0);

            _endpointLabel = Theme.Pill(string.Empty);
            _endpointLabel.ShowDot = false;
            _endpointLabel.Font = Theme.SmallFont;
            _endpointLabel.FillColor = Theme.AccentSoft;
            _endpointLabel.InkColor = Theme.Accent;
            _endpointLabel.Margin = new Padding(0, 7, 0, 0);
            _endpointLabel.Visible = false;
            row.Controls.Add(_endpointLabel, 3, 0);
            return row;
        }

        private Control BuildNetworkForm()
        {
            TableLayoutPanel form = new TableLayoutPanel();
            form.AutoSize = true;
            form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            form.Dock = DockStyle.Top;
            form.ColumnCount = 6;
            form.BackColor = Theme.Surface;
            form.Margin = new Padding(0, 6, 0, 0);

            Label ssidCaption = Theme.Caption(Strings.T("network.ssid"));
            ssidCaption.Margin = new Padding(0, 9, 8, 4);
            form.Controls.Add(ssidCaption, 0, 0);

            _ssid = Theme.Input(180);
            form.Controls.Add(_ssid, 1, 0);
            Theme.Tip(_ssid, Strings.T("tip.ssid"));

            Label passwordCaption = Theme.Caption(Strings.T("network.password"));
            passwordCaption.Margin = new Padding(14, 9, 8, 4);
            form.Controls.Add(passwordCaption, 2, 0);

            _password = Theme.Input(180);
            _password.UseSystemPasswordChar = true;
            form.Controls.Add(_password, 3, 0);
            Theme.Tip(_password, Strings.T("tip.password"));

            _showPassword = Theme.Check(Strings.T("network.show"));
            _showPassword.CheckedChanged += delegate { _password.UseSystemPasswordChar = !_showPassword.Checked; };
            form.Controls.Add(_showPassword, 4, 0);
            form.SetColumnSpan(_showPassword, 2);

            Label engineCaption = Theme.Caption(Strings.T("network.engine"));
            engineCaption.Margin = new Padding(0, 9, 8, 4);
            form.Controls.Add(engineCaption, 0, 1);

            _engine = Theme.Combo(230);
            _engine.Items.Add(Strings.T("network.engine.auto"));
            _engine.Items.Add(Strings.T("network.engine.offgrid"));
            _engine.Items.Add(Strings.T("network.engine.sharing"));
            _engine.Items.Add(Strings.T("network.engine.legacy"));
            form.Controls.Add(_engine, 1, 1);
            Theme.Tip(_engine, Strings.T("tip.engine"));

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            actions.Margin = new Padding(0, 0, 0, 0);

            _startStop = Theme.Primary(Strings.T("network.create"));
            Theme.SetGlyph(_startStop, GlyphKind.Play);
            _startStop.Click += OnStartStop;
            Theme.Tip(_startStop, Strings.T("tip.start"));
            actions.Controls.Add(_startStop);

            _share = Theme.Check(Strings.T("network.share"));
            _share.CheckedChanged += OnShareChanged;
            Theme.Tip(_share, Strings.T("tip.share"));
            actions.Controls.Add(_share);

            form.Controls.Add(actions, 2, 1);
            form.SetColumnSpan(actions, 4);
            return form;
        }

        private Control BuildHeaderOptions()
        {
            FlowLayoutPanel options = new FlowLayoutPanel();
            options.AutoSize = true;
            options.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            options.WrapContents = true;
            options.Dock = DockStyle.Top;
            options.BackColor = Theme.Surface;
            options.Margin = new Padding(0, 4, 0, 0);

            _autostart = Theme.Check(Strings.T("autostart"));
            _autostart.CheckedChanged += OnAutostartChanged;
            Theme.Tip(_autostart, Strings.T("tip.autostart"));
            options.Controls.Add(_autostart);

            _startMinimized = Theme.Check(Strings.T("autostart.minimized"));
            _startMinimized.CheckedChanged += delegate
            {
                if (_released || _loading)
                {
                    return;
                }

                _context.Settings.StartMinimized = _startMinimized.Checked;
                _context.Settings.Save();
            };
            options.Controls.Add(_startMinimized);

            _guardLabel = Theme.Pill(string.Empty);
            _guardLabel.ShowDot = true;
            _guardLabel.DotColor = Theme.TextMuted;
            _guardLabel.InkColor = Theme.TextMuted;
            _guardLabel.FillColor = Color.Transparent;
            _guardLabel.Font = Theme.SmallFont;
            _guardLabel.BackColor = Theme.Surface;
            _guardLabel.Margin = new Padding(10, 1, 6, 0);
            Theme.Tip(_guardLabel, Strings.T("tip.guard"));
            options.Controls.Add(_guardLabel);

            _bannerLabel = Theme.Pill(string.Empty);
            _bannerLabel.ShowDot = true;
            _bannerLabel.DotColor = Theme.Warning;
            _bannerLabel.InkColor = Theme.Warning;
            _bannerLabel.FillColor = Theme.WarningSoft;
            _bannerLabel.Font = Theme.SmallFont;
            _bannerLabel.BackColor = Theme.Surface;
            _bannerLabel.Margin = new Padding(6, 1, 0, 0);
            _bannerLabel.Visible = false;
            options.Controls.Add(_bannerLabel);
            return options;
        }

        private TabPage BuildClientsPage()
        {
            TabPage page = Theme.Page(Strings.T("tab.clients"));

            _clients = Theme.Grid();
            _clients.Dock = DockStyle.Fill;
            // Ordenar por cualquier columna es cosa del propio grid (SortMode automatico); lo que
            // hay que hacer es volver a aplicar ese orden despues de cada auditoria del radar.
            _clients.SortCompare += OnClientsSortCompare;
            _clients.Columns.Add("ip", Strings.T("clients.ip"));
            _clients.Columns.Add("mac", Strings.T("clients.mac"));
            _clients.Columns.Add("vendor", Strings.T("clients.vendor"));
            _clients.Columns.Add("host", Strings.T("clients.host"));
            _clients.Columns.Add("state", Strings.T("clients.state"));
            _clients.Columns.Add("source", Strings.T("clients.source"));
            _clients.Columns["ip"].FillWeight = 90;
            _clients.Columns["mac"].FillWeight = 110;
            _clients.Columns["vendor"].FillWeight = 150;
            _clients.Columns["host"].FillWeight = 110;
            _clients.Columns["state"].FillWeight = 70;
            _clients.Columns["source"].FillWeight = 110;

            // La caja de busqueda lleva su etiqueta delante: sin ella, un campo vacio en la
            // cabecera de una tabla no dice que hace.
            FlowLayoutPanel filterRow = new FlowLayoutPanel();
            filterRow.Dock = DockStyle.Top;
            filterRow.AutoSize = true;
            filterRow.WrapContents = true;
            filterRow.BackColor = Theme.Background;

            Label filterCaption = Theme.Caption(Strings.T("clients.filter"));
            filterCaption.Margin = new Padding(0, 9, 8, 4);
            filterRow.Controls.Add(filterCaption);

            _filter = Theme.Input(240);
            _filter.TextChanged += OnFilterChanged;
            Theme.Tip(_filter, Strings.T("tip.filter"));
            filterRow.Controls.Add(_filter);

            _clearFilter = Theme.Secondary(Strings.T("clients.clear"));
            Theme.SetGlyph(_clearFilter, GlyphKind.Clear);
            _clearFilter.Click += delegate
            {
                _filter.Text = string.Empty;
                filterRow.Focus();
            };
            filterRow.Controls.Add(_clearFilter);

            _clientsCount = Theme.Pill(string.Empty);
            _clientsCount.ShowDot = true;
            _clientsCount.DotColor = Theme.Accent;
            _clientsCount.InkColor = Theme.TextMuted;
            _clientsCount.FillColor = Color.Transparent;
            _clientsCount.Font = Theme.SmallFont;
            _clientsCount.BackColor = Theme.Background;
            _clientsCount.Margin = new Padding(10, 3, 0, 0);
            filterRow.Controls.Add(_clientsCount);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;
            bar.BackColor = Theme.Background;
            bar.Padding = new Padding(0, 4, 0, 0);

            // Resumen de un vistazo: tres cifras y un estado que llevan a la pestana de la que
            // hablan. Se rellenan en RefreshTiles, y solo escriben cuando el valor cambia: cada
            // escritura repinta el recuadro entero.
            FlowLayoutPanel summary = new FlowLayoutPanel();
            summary.Dock = DockStyle.Top;
            summary.AutoSize = true;
            summary.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            summary.WrapContents = true;
            summary.BackColor = Theme.Background;
            summary.Padding = new Padding(0, 0, 0, 10);

            _tileClients = Theme.Tile(Strings.T("tile.clients"));
            _tileClients.Click += OnTileClients;
            Theme.Tip(_tileClients, Strings.T("tip.tileclients"));
            summary.Controls.Add(_tileClients);

            _tileNet = Theme.Tile(Strings.T("tile.network"));
            _tileNet.Click += OnTileNetwork;
            Theme.Tip(_tileNet, Strings.T("tip.tilenetwork"));
            summary.Controls.Add(_tileNet);

            _tileFiles = Theme.Tile(Strings.T("tile.files"));
            _tileFiles.Click += OnTileFiles;
            Theme.Tip(_tileFiles, Strings.T("tip.tilefiles"));
            summary.Controls.Add(_tileFiles);

            _tileGuard = Theme.Tile(Strings.T("tile.guard"));
            _tileGuard.Click += OnTileGuard;
            Theme.Tip(_tileGuard, Strings.T("tip.tileguard"));
            summary.Controls.Add(_tileGuard);

            _hammer = Theme.DangerButton(Strings.T("clients.hammer"));
            Theme.SetGlyph(_hammer, GlyphKind.Shield);
            _hammer.Click += OnHammer;
            Theme.Tip(_hammer, Strings.T("tip.hammer"));
            bar.Controls.Add(_hammer);

            _export = Theme.Secondary(Strings.T("clients.export"));
            Theme.SetGlyph(_export, GlyphKind.Download);
            _export.Click += OnExportClients;
            Theme.Tip(_export, Strings.T("tip.export"));
            bar.Controls.Add(_export);

            // Menu contextual sobre la tabla: banear e indultar sin mover el raton hasta la
            // barra de abajo, y acciones de portapapeles para llevarse la direccion a otra
            // herramienta. Se abre con clic derecho y con la tecla de menu.
            _clientMenu = new ContextMenuStrip();
            _menuBan = new ToolStripMenuItem(Strings.T("clients.menu.ban"), null, OnHammer);
            _menuPardon = new ToolStripMenuItem(Strings.T("clients.menu.pardon"), null, OnMenuPardon);
            _clientMenu.Items.Add(_menuBan);
            _clientMenu.Items.Add(_menuPardon);
            _clientMenu.Items.Add(new ToolStripSeparator());
            _clientMenu.Items.Add(Strings.T("clients.menu.copyip"), null, delegate { OnClientsCopy("ip"); });
            _clientMenu.Items.Add(Strings.T("clients.menu.copymac"), null, delegate { OnClientsCopy("mac"); });
            _clientMenu.Items.Add(new ToolStripSeparator());
            _clientMenu.Items.Add(Strings.T("clients.menu.open"), null, OnClientsOpenBrowser);
            _clientMenu.Opening += OnClientsMenuOpening;
            _clients.ContextMenuStrip = _clientMenu;

            // Doble clic sobre un equipo: se abre su direccion en el navegador. Con la lista
            // delante, es lo primero que apetece hacer para ver que tiene dentro.
            _clients.CellDoubleClick += OnClientsOpenRow;
            Theme.Tip(_clients, Strings.T("tip.clients"));

            page.Controls.Add(_clients);
            page.Controls.Add(filterRow);
            page.Controls.Add(summary);
            page.Controls.Add(bar);
            return page;
        }

        private TabPage BuildBansPage()
        {
            TabPage page = Theme.Page(Strings.T("tab.parole"));

            _bans = Theme.Grid();
            _bans.Dock = DockStyle.Fill;
            _bans.Columns.Add("mac", Strings.T("parole.mac"));
            _bans.Columns.Add("vendor", Strings.T("parole.vendor"));
            _bans.Columns.Add("ip", Strings.T("parole.ip"));
            _bans.Columns.Add("when", Strings.T("parole.when"));
            _bans.Columns.Add("reason", Strings.T("parole.reason"));
            _bans.Columns["mac"].FillWeight = 110;
            _bans.Columns["vendor"].FillWeight = 140;
            _bans.Columns["ip"].FillWeight = 90;
            _bans.Columns["when"].FillWeight = 80;
            _bans.Columns["reason"].FillWeight = 160;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;

            bar.BackColor = Theme.Background;
            bar.Padding = new Padding(0, 4, 0, 0);

            _pardon = Theme.Primary(Strings.T("parole.pardon"));
            Theme.SetGlyph(_pardon, GlyphKind.Check);
            _pardon.Click += OnPardon;
            bar.Controls.Add(_pardon);

            _pardonAll = Theme.Secondary(Strings.T("parole.pardonall"));
            Theme.SetGlyph(_pardonAll, GlyphKind.Check);
            _pardonAll.Click += OnPardonAll;
            bar.Controls.Add(_pardonAll);

            page.Controls.Add(_bans);
            page.Controls.Add(bar);
            return page;
        }

        private TabPage BuildFilesPage()
        {
            TabPage page = Theme.Page(Strings.T("tab.files"));

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 1;
            layout.BackColor = Theme.Background;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));

            // Columna izquierda: una tarjeta para la carpeta y otra para los servidores. Antes
            // era una lista plana de secciones; con tarjetas se ve que cada grupo se configura
            // por separado, y donde acaba el bloque de los puertos.
            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Top;
            left.ColumnCount = 1;
            left.AutoSize = true;
            left.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            left.BackColor = Theme.Background;
            left.Margin = new Padding(0, 0, 10, 0);
            left.Controls.Add(BuildShareCard(), 0, 0);
            left.Controls.Add(BuildServersCard(), 0, 1);

            layout.Controls.Add(left, 0, 0);
            layout.Controls.Add(BuildQrCard(), 1, 0);
            page.Controls.Add(layout);
            return page;
        }

        /// <summary>Carpeta compartida y permisos: lo primero que se toca al compartir archivos.</summary>
        private Control BuildShareCard()
        {
            CardPanel card = Theme.Card();
            card.Dock = DockStyle.Top;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel body = CardBody(card, Strings.T("files.folder"));

            // El ancho de la columna izquierda depende del tamano de la ventana, asi que estas
            // filas se dejan envolver: con la ventana en su minimo, los botones bajan a la linea
            // siguiente en vez de quedar recortados fuera de la tarjeta.
            FlowLayoutPanel folderRow = new FlowLayoutPanel();
            folderRow.AutoSize = true;
            folderRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            folderRow.WrapContents = true;
            _folder = Theme.Input(260);
            folderRow.Controls.Add(_folder);
            _browse = Theme.Secondary(Strings.T("files.browse"));
            Theme.SetGlyph(_browse, GlyphKind.Folder);
            _browse.Click += OnBrowse;
            folderRow.Controls.Add(_browse);
            _openFolder = Theme.Secondary(Strings.T("files.openfolder"));
            Theme.SetGlyph(_openFolder, GlyphKind.Folder);
            _openFolder.Click += OnOpenSharedFolder;
            Theme.Tip(_openFolder, Strings.T("tip.openfolder"));
            folderRow.Controls.Add(_openFolder);
            body.Controls.Add(folderRow, 0, 1);

            body.Controls.Add(Theme.Section(Strings.T("files.card.share")), 0, 2);

            FlowLayoutPanel switches = new FlowLayoutPanel();
            switches.AutoSize = true;
            switches.WrapContents = true;
            _upload = Theme.Check(Strings.T("files.allowupload"));
            _upload.CheckedChanged += OnShareOptionsChanged;
            Theme.Tip(_upload, Strings.T("tip.upload"));
            switches.Controls.Add(_upload);
            _delete = Theme.Check(Strings.T("files.allowdelete"));
            _delete.CheckedChanged += OnShareOptionsChanged;
            Theme.Tip(_delete, Strings.T("tip.delete"));
            switches.Controls.Add(_delete);
            body.Controls.Add(switches, 0, 3);
            return card;
        }

        /// <summary>Servidores de archivos: arranque, direcciones y contadores de trafico.</summary>
        private Control BuildServersCard()
        {
            CardPanel card = Theme.Card();
            card.Dock = DockStyle.Top;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel body = CardBody(card, Strings.T("files.servers"));

            FlowLayoutPanel servers = new FlowLayoutPanel();
            servers.AutoSize = true;
            servers.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            servers.WrapContents = true;

            _startServers = Theme.Primary(Strings.T("files.start"));
            Theme.SetGlyph(_startServers, GlyphKind.Play);
            _startServers.Click += OnStartServers;
            Theme.Tip(_startServers, Strings.T("tip.startservers"));
            servers.Controls.Add(_startServers);

            _stopServers = Theme.Secondary(Strings.T("files.stop"));
            Theme.SetGlyph(_stopServers, GlyphKind.Stop);
            _stopServers.Click += OnStopServers;
            servers.Controls.Add(_stopServers);
            body.Controls.Add(servers, 0, 1);

            // Cada direccion lleva su titulo en una linea propia: asi el enlace nunca compite por
            // el ancho con su etiqueta y no hay texto cortado.
            body.Controls.Add(Theme.Section(Strings.T("files.portal")), 0, 2);

            FlowLayoutPanel portalRow = new FlowLayoutPanel();
            portalRow.AutoSize = true;
            portalRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            portalRow.WrapContents = true;
            _portalLink = Theme.Link(string.Empty);
            _portalLink.LinkClicked += OnOpenLink;
            portalRow.Controls.Add(_portalLink);
            _copyLink = Theme.Secondary(Strings.T("files.copy"));
            Theme.SetGlyph(_copyLink, GlyphKind.Copy);
            _copyLink.Click += OnCopyLink;
            Theme.Tip(_copyLink, Strings.T("tip.copylink"));
            portalRow.Controls.Add(_copyLink);
            body.Controls.Add(portalRow, 0, 3);

            body.Controls.Add(Theme.Section(Strings.T("files.ftp")), 0, 4);

            FlowLayoutPanel ftpRow = new FlowLayoutPanel();
            ftpRow.AutoSize = true;
            ftpRow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ftpRow.WrapContents = true;
            _ftpLink = Theme.Link(string.Empty);
            _ftpLink.LinkClicked += OnOpenLink;
            ftpRow.Controls.Add(_ftpLink);
            body.Controls.Add(ftpRow, 0, 5);

            _filesStats = Theme.Pill(string.Empty);
            _filesStats.ShowDot = true;
            _filesStats.DotColor = Theme.Success;
            _filesStats.InkColor = Theme.TextMuted;
            _filesStats.FillColor = Color.Transparent;
            _filesStats.Font = Theme.SmallFont;
            _filesStats.BackColor = Theme.Surface;
            _filesStats.Margin = new Padding(0, 8, 0, 0);
            body.Controls.Add(_filesStats, 0, 6);
            return card;
        }

        /// <summary>El QR, que es el camino comodo para abrir el portal desde el movil.</summary>
        private Control BuildQrCard()
        {
            CardPanel card = Theme.Card();
            card.Dock = DockStyle.Fill;

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Top;
            body.AutoSize = true;
            body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            body.ColumnCount = 1;
            body.BackColor = Theme.Surface;
            body.Margin = new Padding(0);
            body.Controls.Add(Theme.Section(Strings.T("files.card.qr")), 0, 0);

            _qr = new PictureBox();
            _qr.Size = new Size(228, 228);
            _qr.SizeMode = PictureBoxSizeMode.Zoom;
            _qr.BackColor = Color.White;
            _qr.Margin = new Padding(0, 6, 0, 8);
            Theme.Tip(_qr, Strings.T("tip.qr"));
            body.Controls.Add(_qr, 0, 1);

            _qrHint = Theme.Note(string.Empty, 240);
            body.Controls.Add(_qrHint, 0, 2);
            card.Controls.Add(body);
            return card;
        }

        /// <summary>
        /// Cuerpo de una tarjeta: el titulo de la seccion y una columna donde el que llama va
        /// anadiendo filas. Todas las tarjetas del programa se montan igual, asi que todas se
        /// ven igual.
        /// </summary>
        private static TableLayoutPanel CardBody(CardPanel card, string title)
        {
            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Top;
            body.AutoSize = true;
            body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            body.ColumnCount = 1;
            body.BackColor = Theme.Surface;
            body.Margin = new Padding(0);

            if (!string.IsNullOrEmpty(title))
            {
                body.Controls.Add(Theme.Section(title), 0, 0);
            }

            card.Controls.Add(body);
            return body;
        }

        /// <summary>
        /// Panel de ajustes. Todo lo que se puede tocar sin editar ficheros: idioma, banda,
        /// ritmo de trabajo y puertos. Se guarda en el mismo fichero de texto legible que ya
        /// existia, y los valores se sanean al cargar y al guardar.
        /// </summary>
        private TabPage BuildSettingsPage()
        {
            TabPage page = Theme.Page(Strings.T("settings.tab"));

            // Los ajustes son la pestana mas alta: el contenido vive dentro de un panel que se
            // desplaza, y los botones van fijos abajo. Poner AutoScroll en la propia pestana no
            // sirve: el panel anclado abajo se va con el desplazamiento y "Guardar y aplicar"
            // desaparece de la vista, que es justo lo que se vio en la revision visual.
            Panel scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.BackColor = Theme.Background;
            page.Controls.Add(scroll);

            // Los ajustes se apilan en tarjetas: una por tema. Es la pestana mas larga, y con
            // todo seguido en una lista no se veia donde acababa un grupo y empezaba el siguiente.
            TableLayoutPanel stack = new TableLayoutPanel();
            stack.Dock = DockStyle.Top;
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.ColumnCount = 1;
            stack.BackColor = Theme.Background;
            stack.Margin = new Padding(0);
            scroll.Controls.Add(stack);

            int row = 1;
            TableLayoutPanel layout = SettingsCard(stack, Strings.T("settings.section.app"));

            _language = new ComboBox();
            _language.DropDownStyle = ComboBoxStyle.DropDownList;
            _language.Width = 240;
            Theme.StyleInput(_language);
            _language.Items.Add(Strings.T("settings.language.auto"));
            _language.Items.Add("Espanol");
            _language.Items.Add("English");
            AddSetting(layout, Strings.T("settings.language"), _language, ref row);

            _settingsNote = new Label();
            _settingsNote.Text = Strings.T("settings.language.reload");
            _settingsNote.AutoSize = true;
            _settingsNote.MaximumSize = new Size(520, 0);
            _settingsNote.ForeColor = Theme.TextMuted;
            _settingsNote.Margin = new Padding(0, 4, 0, 10);
            layout.Controls.Add(_settingsNote, 1, row++);

            row = 1;
            layout = SettingsCard(stack, Strings.T("settings.section.network"));

            _band = new ComboBox();
            _band.DropDownStyle = ComboBoxStyle.DropDownList;
            _band.Width = 240;
            Theme.StyleInput(_band);
            _band.Items.Add(Strings.T("settings.band.2"));
            _band.Items.Add(Strings.T("settings.band.5"));
            AddSetting(layout, Strings.T("settings.band"), _band, ref row);

            row = 1;
            layout = SettingsCard(stack, Strings.T("settings.section.timing"));

            _radarSeconds = NewNumber(AppSettings.MinRadarSeconds, AppSettings.MaxRadarSeconds, 1, " s");
            AddSetting(layout, Strings.T("settings.radar"), _radarSeconds, ref row);

            _guardSeconds = NewNumber(AppSettings.MinGuardSeconds, AppSettings.MaxGuardSeconds, 1, " s");
            AddSetting(layout, Strings.T("settings.guard"), _guardSeconds, ref row);

            row = 1;
            layout = SettingsCard(stack, Strings.T("settings.section.service"));

            _serviceState = new Label();
            _serviceState.AutoSize = true;
            _serviceState.ForeColor = Theme.TextMuted;
            _serviceState.Margin = new Padding(0, 8, 12, 0);

            _serviceNote = new Label();
            _serviceNote.Text = Strings.T("settings.service.why");
            _serviceNote.AutoSize = true;
            _serviceNote.MaximumSize = new Size(520, 0);
            _serviceNote.ForeColor = Theme.TextMuted;
            _serviceNote.Margin = new Padding(0, 4, 0, 8);

            FlowLayoutPanel serviceRow = new FlowLayoutPanel();
            serviceRow.AutoSize = true;
            serviceRow.Margin = new Padding(0, 4, 0, 0);
            _startService = Theme.Secondary(Strings.T("settings.service.start"));
            Theme.SetGlyph(_startService, GlyphKind.Shield);
            _startService.Click += OnStartService;
            serviceRow.Controls.Add(_serviceState);
            serviceRow.Controls.Add(_startService);
            layout.Controls.Add(serviceRow, 1, row++);
            layout.Controls.Add(_serviceNote, 1, row++);

            row = 1;
            layout = SettingsCard(stack, Strings.T("settings.section.startup"));

            _autostartState = new Label();
            _autostartState.AutoSize = true;
            _autostartState.MaximumSize = new Size(520, 0);
            _autostartState.ForeColor = Theme.TextMuted;
            _autostartState.Margin = new Padding(0, 8, 12, 0);

            _repairAutostart = Theme.Secondary(Strings.T("settings.autostart.repair"));
            Theme.SetGlyph(_repairAutostart, GlyphKind.Refresh);
            _repairAutostart.Click += OnRepairAutostart;

            FlowLayoutPanel autostartRow = new FlowLayoutPanel();
            autostartRow.AutoSize = true;
            autostartRow.Margin = new Padding(0, 4, 0, 0);
            autostartRow.Controls.Add(_autostartState);
            autostartRow.Controls.Add(_repairAutostart);
            layout.Controls.Add(autostartRow, 1, row++);

            Label autostartNote = new Label();
            autostartNote.Text = Strings.T("settings.autostart.why");
            autostartNote.AutoSize = true;
            autostartNote.MaximumSize = new Size(520, 0);
            autostartNote.ForeColor = Theme.TextMuted;
            autostartNote.Margin = new Padding(0, 4, 0, 8);
            layout.Controls.Add(autostartNote, 1, row++);

            row = 1;
            layout = SettingsCard(stack, Strings.T("settings.section.files"));

            _filesAuto = Theme.Check(Strings.T("settings.filesauto"));
            layout.Controls.Add(_filesAuto, 1, row++);

            _portalPort = NewNumber(AppSettings.MinPort, AppSettings.MaxPort, 100, string.Empty);
            _portalPort.ThousandsSeparator = false;
            AddSetting(layout, Strings.T("settings.portalport"), _portalPort, ref row);

            _ftpPort = NewNumber(AppSettings.MinPort, AppSettings.MaxPort, 100, string.Empty);
            _ftpPort.ThousandsSeparator = false;
            AddSetting(layout, Strings.T("settings.ftpport"), _ftpPort, ref row);

            _ftpUser = Theme.Input(160);
            AddSetting(layout, Strings.T("settings.ftpuser"), _ftpUser, ref row);

            row = 1;
            layout = SettingsCard(stack, Strings.T("settings.section.alerts"));

            _notify = Theme.Check(Strings.T("settings.notify"));
            layout.Controls.Add(_notify, 1, row++);

            _notifyJoin = Theme.Check(Strings.T("settings.notifyjoin"));
            layout.Controls.Add(_notifyJoin, 1, row++);

            Label notifyNote = new Label();
            notifyNote.Text = Strings.T("settings.notifynote");
            notifyNote.AutoSize = true;
            notifyNote.MaximumSize = new Size(520, 0);
            notifyNote.ForeColor = Theme.TextMuted;
            notifyNote.Margin = new Padding(0, 4, 0, 8);
            layout.Controls.Add(notifyNote, 1, row++);

            // Dos notas al pie, fuera de las tarjetas: valen para toda la pestana, no para un
            // grupo concreto.
            Label shortcutNote = Theme.Note(Strings.T("app.shortcuts"), 620);
            shortcutNote.Margin = new Padding(4, 2, 4, 2);
            stack.Controls.Add(shortcutNote);

            Label hint = Theme.Note(Strings.T("settings.restartnetwork"), 620);
            hint.Margin = new Padding(4, 0, 4, 10);
            stack.Controls.Add(hint);

            // Los botones van en una barra fija abajo, fuera de la zona que se desplaza: si no,
            // con la ventana pequena hay que bajar para encontrar "Guardar" y parece que no existe.
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.AutoSize = true;
            buttons.BackColor = Theme.Background;
            buttons.Padding = new Padding(0, 6, 0, 2);

            _saveSettings = Theme.Primary(Strings.T("settings.save"));
            Theme.SetGlyph(_saveSettings, GlyphKind.Save);
            _saveSettings.Click += OnSaveSettings;
            buttons.Controls.Add(_saveSettings);

            _defaultSettings = Theme.Secondary(Strings.T("settings.defaults"));
            Theme.SetGlyph(_defaultSettings, GlyphKind.Refresh);
            _defaultSettings.Click += OnRestoreDefaults;
            buttons.Controls.Add(_defaultSettings);

            _openDataFolder = Theme.Secondary(Strings.T("settings.openfolder"));
            Theme.SetGlyph(_openDataFolder, GlyphKind.Folder);
            _openDataFolder.Click += delegate { Log.OpenFolder(); };
            buttons.Controls.Add(_openDataFolder);

            page.Controls.Add(buttons);
            return page;
        }

        private static NumericUpDown NewNumber(int minimum, int maximum, int step, string suffix)
        {
            NumericUpDown box = new NumericUpDown();
            box.Minimum = minimum;
            box.Maximum = maximum;
            box.Increment = step;
            box.Width = 120;
            box.TextAlign = HorizontalAlignment.Right;
            Theme.StyleInput(box);
            if (!string.IsNullOrEmpty(suffix))
            {
                Label unit = new Label();
                unit.Text = suffix;
                unit.AutoSize = true;
                unit.ForeColor = Theme.TextMuted;
                box.Tag = unit;
            }

            return box;
        }

        /// <summary>
        /// Anade una tarjeta de ajustes al monton y devuelve su cuerpo: dos columnas, la
        /// etiqueta a la izquierda y el control a la derecha, con el titulo ocupando las dos.
        /// </summary>
        private static TableLayoutPanel SettingsCard(TableLayoutPanel stack, string title)
        {
            CardPanel card = Theme.Card();
            card.Dock = DockStyle.Top;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Top;
            body.AutoSize = true;
            body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            body.ColumnCount = 2;
            body.BackColor = Theme.Surface;
            body.Margin = new Padding(0);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label heading = Theme.Section(title);
            heading.Margin = new Padding(0, 0, 0, 2);
            body.Controls.Add(heading, 0, 0);
            body.SetColumnSpan(heading, 2);

            card.Controls.Add(body);
            stack.Controls.Add(card);
            return body;
        }

        private static void AddSetting(TableLayoutPanel layout, string caption, Control input, ref int row)
        {
            Label label = new Label();
            label.Text = caption;
            label.AutoSize = true;
            label.ForeColor = Theme.Text;
            label.Margin = new Padding(0, 8, 16, 4);
            layout.Controls.Add(label, 0, row);

            FlowLayoutPanel holder = new FlowLayoutPanel();
            holder.AutoSize = true;
            holder.Margin = new Padding(0, 4, 0, 0);
            holder.Controls.Add(input);
            if (input.Tag is Label)
            {
                holder.Controls.Add((Label)input.Tag);
            }

            layout.Controls.Add(holder, 1, row);
            row++;
        }

        private TabPage BuildDiagnosticsPage()
        {
            TabPage page = Theme.Page(Strings.T("tab.diagnostics"));

            CardPanel card = Theme.Card();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(8);

            _diagnostics = new TextBox();
            _diagnostics.Multiline = true;
            _diagnostics.ReadOnly = true;
            _diagnostics.ScrollBars = ScrollBars.Both;
            _diagnostics.WordWrap = false;
            _diagnostics.Dock = DockStyle.Fill;
            _diagnostics.BorderStyle = BorderStyle.None;
            _diagnostics.Font = Theme.MonoFont;
            Theme.StyleInput(_diagnostics);
            card.Controls.Add(_diagnostics);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;
            bar.BackColor = Theme.Background;
            bar.Padding = new Padding(0, 4, 0, 0);

            _refreshDiagnostics = Theme.Primary(Strings.T("diag.refresh"));
            Theme.SetGlyph(_refreshDiagnostics, GlyphKind.Refresh);
            _refreshDiagnostics.Click += OnRefreshDiagnostics;
            Theme.Tip(_refreshDiagnostics, Strings.T("tip.refreshdiag"));
            bar.Controls.Add(_refreshDiagnostics);

            _copyDiagnostics = Theme.Secondary(Strings.T("diag.copy"));
            Theme.SetGlyph(_copyDiagnostics, GlyphKind.Copy);
            _copyDiagnostics.Click += OnCopyDiagnostics;
            bar.Controls.Add(_copyDiagnostics);

            _saveDiagnostics = Theme.Secondary(Strings.T("diag.save"));
            Theme.SetGlyph(_saveDiagnostics, GlyphKind.Save);
            _saveDiagnostics.Click += OnSaveDiagnostics;
            Theme.Tip(_saveDiagnostics, Strings.T("tip.savereport"));
            bar.Controls.Add(_saveDiagnostics);

            page.Controls.Add(card);
            page.Controls.Add(bar);
            return page;
        }

        private TabPage BuildLogPage()
        {
            TabPage page = Theme.Page(Strings.T("tab.log"));

            _logList = new LogList();
            _logList.Dock = DockStyle.Fill;
            Theme.Tip(_logList, Strings.T("app.logfilter"));

            FlowLayoutPanel filterRow = new FlowLayoutPanel();
            filterRow.Dock = DockStyle.Top;
            filterRow.AutoSize = true;
            filterRow.BackColor = Theme.Background;

            Label caption = Theme.Caption(Strings.T("log.filter"));
            caption.Margin = new Padding(0, 9, 8, 4);
            filterRow.Controls.Add(caption);

            _logFilter = Theme.Input(220);
            _logFilter.TextChanged += OnLogFilterChanged;
            Theme.Tip(_logFilter, Strings.T("tip.logfilter"));
            filterRow.Controls.Add(_logFilter);

            _logWarnOnly = Theme.Check(Strings.T("log.warnonly"));
            _logWarnOnly.CheckedChanged += OnLogFilterChanged;
            Theme.Tip(_logWarnOnly, Strings.T("tip.warnonly"));
            filterRow.Controls.Add(_logWarnOnly);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;
            bar.BackColor = Theme.Background;
            bar.Padding = new Padding(0, 4, 0, 0);

            _saveLog = Theme.Secondary(Strings.T("log.dump"));
            Theme.SetGlyph(_saveLog, GlyphKind.Save);
            _saveLog.Click += OnSaveLog;
            Theme.Tip(_saveLog, Strings.T("tip.savelog"));
            bar.Controls.Add(_saveLog);

            _openLog = Theme.Secondary(Strings.T("log.open"));
            Theme.SetGlyph(_openLog, GlyphKind.Folder);
            _openLog.Click += delegate { Log.OpenFolder(); };
            Theme.Tip(_openLog, Strings.T("tip.openlog"));
            bar.Controls.Add(_openLog);

            page.Controls.Add(_logList);
            page.Controls.Add(filterRow);
            page.Controls.Add(bar);
            return page;
        }

        // ----------------------------------------------------------------- acciones

        private void OnTick(object sender, EventArgs e)
        {
            if (_released || IsDisposed)
            {
                return;
            }

            RefreshNetworkState();
            RefreshFilesState();
            RefreshStatusBar();
        }

        private void OnAnythingChanged(object sender, EventArgs e)
        {
            RunOnUi(RefreshAll);
        }

        private void OnGuardEvent(object sender, GuardEvent e)
        {
            RunOnUi(delegate
            {
                _guardLabel.Text = Strings.T("network.guard") + ": " + e.Message;
                RefreshLog();
            });
        }

        private void OnLogChanged(object sender, EventArgs e)
        {
            RunOnUi(RefreshLog);
        }

        private void OnRadarUpdated(object sender, RadarEventArgs e)
        {
            RunOnUi(delegate { ApplyClients(e.Clients); });
        }

        private void OnStartStop(object sender, EventArgs e)
        {
            if (_context.Hotspot.IsRunning)
            {
                _context.StopNetwork();
                RefreshAll();
                return;
            }

            _context.Settings.Ssid = _ssid.Text.Trim();
            _context.Settings.Passphrase = _password.Text;
            _context.Settings.Engine = (EnginePreference)Math.Max(0, _engine.SelectedIndex);
            _context.Settings.Save();

            string error;
            if (!_context.StartNetwork(out error))
            {
                MessageBox.Show(this, error, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RefreshAll();
        }

        private void OnShareChanged(object sender, EventArgs e)
        {
            if (_context.Settings == null || _released)
            {
                return;
            }

            string message;
            if (_share.Checked)
            {
                if (!_context.EnableInternetSharing(out message))
                {
                    MessageBox.Show(this, message, Strings.T("common.warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                _context.DisableInternetSharing();
            }

            RefreshAll();
        }

        private void OnAutostartChanged(object sender, EventArgs e)
        {
            if (_released || _loading)
            {
                return;
            }

            string error;
            bool wanted = _autostart.Checked;
            bool done = wanted ? Autostart.Enable(out error) : Autostart.Disable(out error);
            if (!done)
            {
                MessageBox.Show(this, error, Strings.T("common.warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _autostart.Checked = Autostart.IsEnabled();
                return;
            }

            _context.Settings.Autostart = wanted;
            _context.Settings.Save();
        }

        private void OnHammer(object sender, EventArgs e)
        {
            LanClient client = SelectedClient();
            if (client == null)
            {
                return;
            }

            string question = Strings.T("clients.ask") + " " + client.AddressText + " (" + client.VendorText + ")?";
            if (MessageBox.Show(this, question, Strings.T("common.question"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            string error;
            if (!_context.BanClient(client, out error))
            {
                MessageBox.Show(this, error, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RefreshAll();
        }

        private void OnExportClients(object sender, EventArgs e)
        {
            IList<LanClient> clients = _context.Radar.Snapshot();
            if (clients.Count == 0)
            {
                MessageBox.Show(this, Strings.T("clients.empty"), Strings.T("common.report"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV|*.csv";
                dialog.FileName = "netforge-clientes.csv";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("ip,mac,fabricante,nombre,bloqueado,origen,primera_vez,ultima_vez");
                foreach (LanClient client in clients)
                {
                    sb.AppendLine(string.Join(",", new string[]
                    {
                        Escape(client.AddressText),
                        Escape(client.Mac),
                        Escape(client.VendorText),
                        Escape(client.HostName),
                        client.IsBanned ? "si" : "no",
                        Escape(client.SourceText),
                        client.FirstSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        client.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    }));
                }

                try
                {
                    File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
                    Log.Write("Clientes exportados a " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnPardon(object sender, EventArgs e)
        {
            if (_bans.SelectedRows.Count == 0)
            {
                return;
            }

            object value = _bans.SelectedRows[0].Cells["mac"].Value;
            string mac = value == null ? string.Empty : value.ToString();
            string error;
            if (!_context.Pardon(mac, out error))
            {
                MessageBox.Show(this, error, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RefreshAll();
        }

        private void OnPardonAll(object sender, EventArgs e)
        {
            string error;
            int count = _context.PardonAll(out error);
            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show(this, error, Strings.T("common.warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                Log.Write("Indulto general: " + count + " dispositivo(s)");
            }

            RefreshAll();
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = Strings.T("files.folder");
                if (Directory.Exists(_folder.Text))
                {
                    dialog.SelectedPath = _folder.Text;
                }

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _folder.Text = dialog.SelectedPath;
                _context.Settings.Folder = dialog.SelectedPath;
                _context.Settings.Save();
                RefreshFilesState();
            }
        }

        private void OnShareOptionsChanged(object sender, EventArgs e)
        {
            if (_released || _loading)
            {
                return;
            }

            _context.Settings.ShareFolderUploads = _upload.Checked;
            _context.Settings.ShareFolderDelete = _delete.Checked;
            _context.Settings.Folder = _folder.Text;
            _context.Settings.Save();
        }

        private void OnStartServers(object sender, EventArgs e)
        {
            _context.Settings.Folder = _folder.Text;
            _context.Settings.ShareFolderUploads = _upload.Checked;
            _context.Settings.ShareFolderDelete = _delete.Checked;
            _context.Settings.Save();

            string error;
            if (!_context.StartFileServers(out error))
            {
                MessageBox.Show(this, error, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RefreshAll();
        }

        private void OnStopServers(object sender, EventArgs e)
        {
            _context.StopFileServers();
            RefreshAll();
        }

        /// <summary>
        /// El QR es el camino comodo, pero pegar la direccion a mano tiene que ser posible:
        /// hay moviles sin camara util, tablets con el teclado delante y navegadores de
        /// escritorio en otro equipo de la misma red.
        /// </summary>
        private void OnCopyLink(object sender, EventArgs e)
        {
            string portal = _context.Files.PortalUrl;
            string ftp = _context.Files.FtpUrl;
            if (string.IsNullOrEmpty(portal) && string.IsNullOrEmpty(ftp))
            {
                MessageBox.Show(this, Strings.T("files.nored"), Strings.T("common.warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string text = string.IsNullOrEmpty(portal) ? ftp : portal;
            if (!string.IsNullOrEmpty(portal) && !string.IsNullOrEmpty(ftp))
            {
                text = portal + Environment.NewLine + ftp;
            }

            try
            {
                Clipboard.SetText(text);
                Log.Write("Enlace copiado al portapapeles: " + text.Replace(Environment.NewLine, " | "));
                MessageBox.Show(this, text, Strings.T("common.ok"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                // Con el portapapeles ocupado por otra aplicacion, al menos se ve la direccion.
                Log.Write("No se pudo copiar el enlace: " + ex.Message);
                MessageBox.Show(this, text, Strings.T("common.warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnOpenLink(object sender, LinkLabelLinkClickedEventArgs e)
        {
            LinkLabel link = sender as LinkLabel;
            if (link == null || string.IsNullOrEmpty(link.Text))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(link.Text);
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo abrir el enlace: " + ex.Message);
            }
        }

        private void OnRefreshDiagnostics(object sender, EventArgs e)
        {
            _diagnostics.Text = _context.DiagnosticsReport();
        }

        private void OnCopyDiagnostics(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(_diagnostics.Text);
                Log.Write("Informe de diagnostico copiado al portapapeles");
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo copiar el informe: " + ex.Message);
            }
        }

        private void OnSaveLog(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Texto|*.txt|Log|*.log";
                dialog.FileName = "netforge-registro.txt";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    File.WriteAllText(dialog.FileName, Log.Dump(), Encoding.UTF8);
                    Log.Write("Registro guardado en " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ----------------------------------------------------------------- refresco

        private void RefreshAll()
        {
            if (_released || IsDisposed)
            {
                return;
            }

            AppSettings settings = _context.Settings;
            _loading = true;
            try
            {
                _ssid.Text = settings.Ssid;
                _password.Text = settings.Passphrase;
                _engine.SelectedIndex = (int)settings.Engine;
                _autostart.Checked = Autostart.IsEnabled();
                _startMinimized.Checked = settings.StartMinimized;
                _folder.Text = settings.Folder;
                _upload.Checked = settings.ShareFolderUploads;
                _delete.Checked = settings.ShareFolderDelete;
                _diagnostics.Text = _context.DiagnosticsReport();

                // El aviso de falta de permisos es una capsula: si no hay nada que avisar se
                // oculta, pero tiene que volver a aparecer cuando toque.
                string banner = _context.Elevated ? string.Empty : Strings.T("banner.noelevated");
                _bannerLabel.Text = banner;
                _bannerLabel.Visible = banner.Length > 0;
            }
            finally
            {
                _loading = false;
            }

            RefreshNetworkState();
            ApplyClients(_context.Radar.Snapshot());
            RefreshBans();
            RefreshFilesState();
            RefreshSettings();
            RefreshServiceState();
            RefreshLog();
            RefreshStatusBar();
        }

        private void RefreshNetworkState()
        {
            if (_released || IsDisposed)
            {
                return;
            }

            HotspotStatus status = _context.Hotspot.Status ?? new HotspotStatus();
            string stateText;
            Color color;
            Color soft;
            switch (status.State)
            {
                case EngineState.On:
                    stateText = Strings.T("network.state.on");
                    color = Theme.Success;
                    soft = Theme.SuccessSoft;
                    break;
                case EngineState.Starting:
                    stateText = Strings.T("network.state.starting");
                    color = Theme.Warning;
                    soft = Theme.WarningSoft;
                    break;
                case EngineState.Stopping:
                    stateText = Strings.T("network.state.stopping");
                    color = Theme.Warning;
                    soft = Theme.WarningSoft;
                    break;
                case EngineState.Faulted:
                    stateText = Strings.T("network.state.faulted");
                    color = Theme.Danger;
                    soft = Theme.DangerSoft;
                    break;
                default:
                    stateText = Strings.T("network.state.idle");
                    color = Theme.TextMuted;
                    soft = Theme.SurfaceHigh;
                    break;
            }

            // El color cuenta el estado y el punto lo subraya, pero el texto siempre se lee: el
            // gris apagado sobre su propia capsula se quedaria corto de contraste.
            _stateLabel.Text = stateText;
            _stateLabel.DotColor = color;
            _stateLabel.FillColor = soft;
            _stateLabel.InkColor = color == Theme.TextMuted ? Theme.Text : color;

            string endpoint = status.State == EngineState.On
                ? "SSID " + status.Ssid + "  |  " + status.Endpoint + "  |  " + status.Engine
                : string.Empty;
            _endpointLabel.Text = endpoint;
            _endpointLabel.Visible = endpoint.Length > 0;

            _startStop.Text = _context.Hotspot.IsRunning ? Strings.T("network.stop") : Strings.T("network.create");
            Theme.SetGlyph(_startStop, _context.Hotspot.IsRunning ? GlyphKind.Stop : GlyphKind.Play);
            _guardLabel.Text = Strings.T("network.guard") + ": " + (_context.Guard.Armed
                ? _context.Guard.TotalResurrections + " recuperacion(es)"
                : "inactiva");
            _guardLabel.DotColor = _context.Guard.Armed ? Theme.Success : Theme.TextMuted;

            // El titulo de la ventana dice lo mismo que la cabecera: en la barra de tareas, con
            // la ventana detras de otras, es lo unico que se ve.
            Text = status.State == EngineState.On
                ? Strings.T("app.title") + " - " + status.Ssid
                : Strings.T("app.title");
        }

        private void RefreshFilesState()
        {
            if (_released || IsDisposed)
            {
                return;
            }

            bool running = _context.Files.IsRunning;
            _startServers.Enabled = !running;
            _stopServers.Enabled = running;
            _copyLink.Enabled = running;
            _startServers.Text = Strings.T("files.start");
            _stopServers.Text = Strings.T("files.stop");

            string portal = _context.Files.PortalUrl;
            _portalLink.Text = string.IsNullOrEmpty(portal) ? Strings.T("files.off") : portal;
            string ftp = _context.Files.FtpUrl;
            _ftpLink.Text = string.IsNullOrEmpty(ftp) ? Strings.T("files.off") : ftp;

            _filesStats.Text = running
                ? Strings.T("files.sent") + ": " + FormatSize(_context.Files.BytesServed) +
                  "   " + Strings.T("files.received") + ": " + FormatSize(_context.Files.BytesReceived) +
                  "   " + Strings.T("files.connections") + ": " + _context.Files.ClientCount
                : Strings.T("files.nored");
            _filesStats.DotColor = running ? Theme.Success : Theme.TextMuted;

            RefreshQr();
        }

        private void RefreshQr()
        {
            // El reloj de la ventana pasa por aqui cada segundo y medio. Codificar el QR otra vez
            // cuando la direccion no ha cambiado es tirar trabajo y hace parpadear la imagen, asi
            // que solo se regenera cuando cambia lo que lleva dentro.
            string key = _context.Files.IsRunning
                ? _context.Files.PortalUrl + "|" + _context.Settings.FtpUser
                : "(apagado)";
            if (string.Equals(key, _lastQrPayload, StringComparison.Ordinal))
            {
                return;
            }

            _lastQrPayload = key;

            string payload;
            Image image = _context.Files.CreateQr(4, Color.Black, Color.White, out payload);
            Image previous = _qr.Image;
            _qr.Image = image;
            if (previous != null)
            {
                previous.Dispose();
            }

            if (image == null)
            {
                _qrHint.Text = _context.Files.IsRunning
                    ? Strings.T("files.noqr")
                    : Strings.T("files.nored");
            }
            else
            {
                _qrHint.Text = payload + Environment.NewLine + Strings.T("files.user") + ": " + _context.Settings.FtpUser;
            }
        }

        private void ApplyClients(IList<LanClient> clients)
        {
            if (_released || IsDisposed)
            {
                return;
            }

            // Se guarda la ultima foto del radar: el filtro no puede trabajar sobre lo que hay
            // dibujado, porque al escribir se redibuja y se perderia el texto a medio escribir.
            _lastClients = clients;

            string selected = null;
            if (_clients.SelectedRows.Count > 0)
            {
                object value = _clients.SelectedRows[0].Cells["mac"].Value;
                selected = value == null ? null : value.ToString();
            }

            string filter = _filter == null ? string.Empty : _filter.Text.Trim();
            List<LanClient> visible = new List<LanClient>();
            foreach (LanClient client in clients)
            {
                if (MatchesFilter(client, filter))
                {
                    visible.Add(client);
                }
            }

            _clients.SuspendLayout();
            try
            {
                // El radar repinta cada pocos segundos. Si los equipos siguen siendo los mismos,
                // las celdas se actualizan en su sitio: asi no se pierde el orden que eligio el
                // usuario, ni el desplazamiento, ni parpadea la tabla entera.
                bool sameRows;
                if (visible.Count == 0)
                {
                    sameRows = _clients.Rows.Count == 1;
                }
                else if (_clients.Rows.Count == visible.Count)
                {
                    sameRows = true;
                    for (int i = 0; i < visible.Count; i++)
                    {
                        object mac = _clients.Rows[i].Cells["mac"].Value;
                        if (mac == null || !string.Equals(mac.ToString(), visible[i].Mac, StringComparison.OrdinalIgnoreCase))
                        {
                            sameRows = false;
                            break;
                        }
                    }
                }
                else
                {
                    sameRows = false;
                }

                if (sameRows && visible.Count > 0)
                {
                    for (int i = 0; i < visible.Count; i++)
                    {
                        UpdateClientRow(_clients.Rows[i], visible[i]);
                    }
                }
                else if (!sameRows)
                {
                    _clients.Rows.Clear();
                    if (visible.Count == 0)
                    {
                        AddEmptyClientRow();
                    }
                    else
                    {
                        foreach (LanClient client in visible)
                        {
                            int index = _clients.Rows.Add(ClientCells(client));
                            StyleClientRow(_clients.Rows[index], client);
                            if (selected != null && string.Equals(selected, client.Mac, StringComparison.OrdinalIgnoreCase))
                            {
                                _clients.Rows[index].Selected = true;
                            }
                        }
                    }

                    // El radar repinta la tabla cada pocos segundos: sin volver a aplicar el orden
                    // elegido, ordenar por fabricante duraria lo que tarda la siguiente auditoria.
                    if (_clients.SortedColumn != null)
                    {
                        try
                        {
                            _clients.Sort(_clients.SortedColumn, _clients.SortOrder == SortOrder.Descending
                                ? ListSortDirection.Descending
                                : ListSortDirection.Ascending);
                        }
                        catch (InvalidOperationException)
                        {
                            // Una columna que desaparece no puede tumbar la lista.
                        }
                    }
                }
            }
            finally
            {
                _clients.ResumeLayout();
            }

            _clientsCount.Text = string.IsNullOrEmpty(filter)
                ? visible.Count + " " + Strings.T("network.clients")
                : visible.Count + " / " + clients.Count + " " + Strings.T("network.clients");
            _clientsCount.DotColor = visible.Count > 0 ? Theme.Accent : Theme.TextMuted;
        }

        /// <summary>Las seis celdas de un equipo, con el mismo texto que se ve en pantalla.</summary>
        private static string[] ClientCells(LanClient client)
        {
            return new string[]
            {
                client.AddressText,
                client.Mac,
                client.VendorText,
                string.IsNullOrEmpty(client.HostName) ? "-" : client.HostName,
                client.IsBanned ? Strings.T("clients.banned") : client.PresenceText,
                client.SourceText
            };
        }

        private static void StyleClientRow(DataGridViewRow row, LanClient client)
        {
            row.DefaultCellStyle.ForeColor = client.IsBanned ? Theme.Danger : Theme.Text;
        }

        /// <summary>Copia los valores nuevos solo si han cambiado: cada escritura repinta.</summary>
        private static void UpdateClientRow(DataGridViewRow row, LanClient client)
        {
            string[] cells = ClientCells(client);
            for (int i = 0; i < cells.Length && i < row.Cells.Count; i++)
            {
                string current = Convert.ToString(row.Cells[i].Value, CultureInfo.CurrentCulture);
                if (!string.Equals(current, cells[i], StringComparison.Ordinal))
                {
                    row.Cells[i].Value = cells[i];
                }
            }

            StyleClientRow(row, client);
        }

        /// <summary>
        /// Una tabla vacia sin explicacion parece una aplicacion rota. Se deja una sola linea con
        /// el aviso, en gris y sin MAC: pulsar sobre ella no ofrece bloquear a nadie.
        /// </summary>
        private void AddEmptyClientRow()
        {
            int index = _clients.Rows.Add(
                Strings.T("clients.empty"),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
            DataGridViewRow row = _clients.Rows[index];
            row.DefaultCellStyle.ForeColor = Theme.TextMuted;
            row.DefaultCellStyle.SelectionBackColor = Theme.SurfaceAlt;
            row.DefaultCellStyle.SelectionForeColor = Theme.TextMuted;
            row.Height = 32;
            row.Selected = false;
        }

        /// <summary>
        /// Busca en lo que el usuario ve: IP, MAC, fabricante, nombre y estado. Es publica para
        /// que la autocomprobacion pueda verificar el filtro sin simular clics.
        /// </summary>
        public static bool MatchesFilter(LanClient client, string filter)
        {
            if (client == null)
            {
                // Una entrada nula no se puede dibujar ni comparar: nunca pasa el filtro.
                return false;
            }

            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            return Contains(client.AddressText, filter)
                || Contains(client.Mac, filter)
                || Contains(client.VendorText, filter)
                || Contains(client.HostName, filter)
                || Contains(client.PresenceText, filter)
                || Contains(client.SourceText, filter);
        }

        private static bool Contains(string text, string value)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            if (_lastClients != null)
            {
                ApplyClients(_lastClients);
            }
        }

        /// <summary>
        /// Ordena las direcciones como direcciones y no como texto: si no, 192.168.137.10 iria
        /// antes que 192.168.137.9 y la lista dejaria de tener sentido.
        /// </summary>
        private void OnClientsSortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (e.Column.Name != "ip")
            {
                return;
            }

            IPAddress left;
            IPAddress right;
            bool leftIsAddress = IPAddress.TryParse(Convert.ToString(e.CellValue1), out left);
            bool rightIsAddress = IPAddress.TryParse(Convert.ToString(e.CellValue2), out right);
            if (!leftIsAddress || !rightIsAddress)
            {
                return;
            }

            e.SortResult = CompareAddresses(left, right);
            e.Handled = true;
        }

        private static int CompareAddresses(IPAddress left, IPAddress right)
        {
            byte[] a = left.GetAddressBytes();
            byte[] b = right.GetAddressBytes();
            int length = Math.Min(a.Length, b.Length);
            for (int i = 0; i < length; i++)
            {
                if (a[i] != b[i])
                {
                    return a[i] < b[i] ? -1 : 1;
                }
            }

            return a.Length.CompareTo(b.Length);
        }

        private void OnSaveSettings(object sender, EventArgs e)
        {
            AppSettings settings = _context.Settings;
            bool languageChanged = ApplySettingsFromUi(settings);
            settings.Save();
            Log.Write("Ajustes guardados en " + AppSettings.StorePath);

            if (languageChanged)
            {
                // Los textos se piden al construir la interfaz: la unica forma de cambiarlos
                // todos de golpe es reconstruirla, que es justo lo que hace el Modo Fantasma.
                _context.ReloadInterface();
                return;
            }

            _context.ApplyTimings();
            _settingsNote.Text = Strings.T("settings.applied");
            RefreshAll();
        }

        /// <summary>
        /// Arranca el servicio de uso compartido de Windows y lo deja en arranque automatico. Es
        /// el unico servicio del sistema que toca esta aplicacion, y solo a peticion del usuario.
        /// </summary>
        private void OnStartService(object sender, EventArgs e)
        {
            string error = IcsSharing.EnsureServiceRunning();
            if (error != null)
            {
                MessageBox.Show(this, error, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            RefreshServiceState();
            MessageBox.Show(this, Strings.T("settings.service.started"), Strings.T("common.ok"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RefreshServiceState()
        {
            if (_serviceState == null || _released || IsDisposed)
            {
                return;
            }

            string state = IcsSharing.DescribeService();
            _serviceState.Text = Strings.T("settings.service.state") + ": " + Strings.T("settings.service." + state);
            _serviceState.ForeColor = state == "running" ? Theme.Success : Theme.TextMuted;
            _startService.Enabled = state != "running" && state != "missing";
        }

        /// <summary>
        /// Estado de la tarea de arranque, dicho en cristiano: si apunta a otra ruta, la
        /// aplicacion se movio y el usuario lo necesita saber antes de reiniciar el PC.
        /// </summary>
        private void RefreshAutostartState()
        {
            if (_autostartState == null || _released || IsDisposed)
            {
                return;
            }

            string recorded;
            AutostartState state = Autostart.Status(out recorded);
            string detail;
            switch (state)
            {
                case AutostartState.Current:
                    detail = Strings.T("settings.autostart.current");
                    _autostartState.ForeColor = Theme.Success;
                    break;
                case AutostartState.OtherPath:
                    detail = Strings.F("settings.autostart.otherpath", recorded);
                    _autostartState.ForeColor = Theme.Warning;
                    break;
                case AutostartState.Missing:
                    detail = Strings.T("settings.autostart.missing");
                    _autostartState.ForeColor = Theme.TextMuted;
                    break;
                default:
                    detail = Strings.T("settings.autostart.unknown");
                    _autostartState.ForeColor = Theme.Warning;
                    break;
            }

            _autostartState.Text = Strings.T("settings.autostart.state") + ": " + detail;
            _repairAutostart.Enabled = state != AutostartState.Current;
        }

        private void OnRepairAutostart(object sender, EventArgs e)
        {
            if (_released || _loading)
            {
                return;
            }

            string error;
            if (!Autostart.Enable(out error))
            {
                MessageBox.Show(this, error, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Log.Write("Tarea de arranque reparada");
            MessageBox.Show(this, Strings.T("settings.autostart.repaired"), Strings.T("common.ok"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshAll();
        }

        private void OnClientsMenuOpening(object sender, CancelEventArgs e)
        {
            LanClient client = SelectedClient();
            if (client == null)
            {
                // Sin seleccion no hay menu que pueda hacer algo util.
                e.Cancel = true;
                return;
            }

            _menuBan.Enabled = !client.IsBanned;
            _menuPardon.Enabled = client.IsBanned;
        }

        /// <summary>Indulto desde el menu contextual de clientes, no solo desde Indultos.</summary>
        private void OnMenuPardon(object sender, EventArgs e)
        {
            LanClient client = SelectedClient();
            if (client == null)
            {
                return;
            }

            string error;
            if (!_context.Pardon(client.Mac, out error))
            {
                MessageBox.Show(this, error, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RefreshAll();
        }

        private void OnClientsCopy(string column)
        {
            LanClient client = SelectedClient();
            if (client == null)
            {
                return;
            }

            string text = column == "ip" ? client.AddressText : client.Mac;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(text);
                Log.Write(Strings.F("clients.copied", text));
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Otra aplicacion tiene el portapapeles secuestrado: al menos queda en el registro.
            }
        }

        private void OnClientsOpenBrowser(object sender, EventArgs e)
        {
            LanClient client = SelectedClient();
            if (client == null || string.IsNullOrEmpty(client.AddressText))
            {
                return;
            }

            try
            {
                using (Process.Start("http://" + client.AddressText + "/")) { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRestoreDefaults(object sender, EventArgs e)
        {
            // Se restauran los valores tecnicos, no lo que el usuario ya eligio (nombre de la
            // red, clave, carpeta y usuario): borrar eso sin avisar seria una faena.
            AppSettings defaults = new AppSettings();
            AppSettings settings = _context.Settings;
            settings.Engine = defaults.Engine;
            settings.PreferTwoGhz = defaults.PreferTwoGhz;
            settings.RadarSeconds = defaults.RadarSeconds;
            settings.GuardSeconds = defaults.GuardSeconds;
            settings.PortalPort = defaults.PortalPort;
            settings.FtpPort = defaults.FtpPort;
            settings.AutoStartServers = defaults.AutoStartServers;
            settings.Notifications = defaults.Notifications;
            settings.NotifyOnJoin = defaults.NotifyOnJoin;
            settings.Save();
            _context.ApplyTimings();
            Log.Write("Ajustes restaurados a los valores por defecto");
            _settingsNote.Text = Strings.T("settings.applied");
            RefreshSettings();
        }

        /// <summary>Vuelca la interfaz en los ajustes. Devuelve true si cambio el idioma.</summary>
        private bool ApplySettingsFromUi(AppSettings settings)
        {
            string previousLanguage = settings.Language;

            switch (_language.SelectedIndex)
            {
                case 1:
                    settings.Language = "es";
                    break;
                case 2:
                    settings.Language = "en";
                    break;
                default:
                    settings.Language = string.Empty;
                    break;
            }

            settings.PreferTwoGhz = _band.SelectedIndex != 1;
            settings.RadarSeconds = (int)_radarSeconds.Value;
            settings.GuardSeconds = (int)_guardSeconds.Value;
            settings.PortalPort = (int)_portalPort.Value;
            settings.FtpPort = (int)_ftpPort.Value;
            settings.AutoStartServers = _filesAuto.Checked;
            settings.Notifications = _notify.Checked;
            settings.NotifyOnJoin = _notifyJoin.Checked;
            settings.FtpUser = _ftpUser.Text.Trim();
            settings.Normalize();

            bool languageChanged = !string.Equals(previousLanguage, settings.Language, StringComparison.Ordinal);
            if (languageChanged)
            {
                Strings.ApplyLanguage(settings.Language);
            }

            return languageChanged;
        }

        private void RefreshSettings()
        {
            if (_language == null || _released || IsDisposed)
            {
                return;
            }

            AppSettings settings = _context.Settings;
            _language.SelectedIndex = settings.Language == "es" ? 1 : (settings.Language == "en" ? 2 : 0);
            _band.SelectedIndex = settings.PreferTwoGhz ? 0 : 1;
            SetValue(_radarSeconds, settings.RadarSeconds);
            SetValue(_guardSeconds, settings.GuardSeconds);
            SetValue(_portalPort, settings.PortalPort);
            SetValue(_ftpPort, settings.FtpPort);
            _filesAuto.Checked = settings.AutoStartServers;
            _notify.Checked = settings.Notifications;
            _notifyJoin.Checked = settings.NotifyOnJoin;
            _ftpUser.Text = settings.FtpUser;
            RefreshAutostartState();
        }

        private static void SetValue(NumericUpDown box, int value)
        {
            if (value < box.Minimum)
            {
                value = (int)box.Minimum;
            }
            else if (value > box.Maximum)
            {
                value = (int)box.Maximum;
            }

            box.Value = value;
        }

        /// <summary>Barra inferior: memoria real en uso y puertos de los servidores.</summary>
        private void RefreshStatusBar()
        {
            if (_statusMemory == null || _released || IsDisposed)
            {
                return;
            }

            _statusMemory.Text = Strings.T("status.memory") + ": " + _context.MemoryMb + " MB";
            _statusPorts.Text = Strings.T("status.ports") + ": " + _context.Settings.PortalPort +
                " / " + _context.Settings.FtpPort;

            bool servers = _context.Files.IsRunning;
            _statusServers.Text = Strings.T("status.servers") + ": " +
                (servers ? Strings.T("status.up") : Strings.T("status.down"));
            _statusServers.DotColor = servers ? Theme.Success : Theme.TextMuted;

            int clients = _lastClients == null ? 0 : _lastClients.Count;
            _statusClients.Text = Strings.T("status.clients") + ": " + clients;
            _statusClients.DotColor = clients > 0 ? Theme.Accent : Theme.TextMuted;

            RefreshTiles(clients);
        }

        /// <summary>
        /// Tarjetas de resumen de la pestana de clientes: la red, los equipos, los servidores y el
        /// vigilante en cuatro recuadros. Nada de esto inventa datos: cada valor sale del mismo
        /// sitio que la cabecera y la barra inferior, asi que no pueden contradecirse.
        /// </summary>
        private void RefreshTiles(int clients)
        {
            if (_tileClients == null || _released || IsDisposed)
            {
                return;
            }

            bool servers = _context.Files.IsRunning;
            bool armed = _context.Guard.Armed;
            string network = _stateLabel == null || string.IsNullOrEmpty(_stateLabel.Text)
                ? Strings.T("network.state.idle")
                : _stateLabel.Text;

            SetTile(_tileClients, clients.ToString(CultureInfo.InvariantCulture), clients > 0 ? Theme.Accent : Theme.TextMuted);
            SetTile(_tileNet, network, _stateLabel == null ? Theme.TextMuted : _stateLabel.DotColor);
            SetTile(_tileFiles, Strings.T(servers ? "tile.files.up" : "tile.files.down"), servers ? Theme.Success : Theme.TextMuted);
            SetTile(_tileGuard, Strings.T(armed ? "tile.guard.armed" : "tile.guard.paused"), armed ? Theme.Success : Theme.TextMuted);
        }

        private static void SetTile(StatTile tile, string value, Color dot)
        {
            if (tile == null)
            {
                return;
            }

            if (!string.Equals(tile.Text, value, StringComparison.Ordinal))
            {
                tile.Text = value;
            }

            if (tile.DotColor != dot)
            {
                tile.DotColor = dot;
            }
        }

        // Las tarjetas llevan a la pestana de la que hablan: la de la red es el diagnostico,
        // donde esta el detalle del motor; la del vigilante, los ajustes que lo arman.
        private void OnTileClients(object sender, EventArgs e)
        {
            SelectTab(0);
            if (_filter != null)
            {
                _filter.Focus();
            }
        }

        private void OnTileNetwork(object sender, EventArgs e)
        {
            SelectTab(4);
        }

        private void OnTileFiles(object sender, EventArgs e)
        {
            SelectTab(2);
        }

        private void OnTileGuard(object sender, EventArgs e)
        {
            SelectTab(3);
        }

        private void RefreshBans()
        {
            if (_released || IsDisposed)
            {
                return;
            }

            _bans.Rows.Clear();
            IList<BannedDevice> devices = _context.Bans.Devices();
            if (devices.Count == 0)
            {
                // Una tabla vacia sin explicacion parece un fallo de la aplicacion. El aviso va
                // en gris y sin seleccion posible: no es una fila, es una nota.
                int index = _bans.Rows.Add(string.Empty, string.Empty, string.Empty, string.Empty, Strings.T("parole.empty"));
                DataGridViewRow row = _bans.Rows[index];
                row.DefaultCellStyle.ForeColor = Theme.TextMuted;
                row.DefaultCellStyle.SelectionBackColor = Theme.SurfaceAlt;
                row.DefaultCellStyle.SelectionForeColor = Theme.TextMuted;
                row.Selected = false;
            }
            else
            {
                foreach (BannedDevice device in devices)
                {
                    _bans.Rows.Add(device.Mac, device.Vendor, device.AddressText, device.BannedText, device.Reason);
                }
            }

            _pardon.Enabled = devices.Count > 0;
            _pardonAll.Enabled = devices.Count > 0;
        }

        private void RefreshLog()
        {
            if (_released || IsDisposed || _logList == null)
            {
                return;
            }

            IList<LogEntry> entries = Log.Snapshot();
            string filter = _logFilter == null ? string.Empty : _logFilter.Text.Trim();
            bool warnOnly = _logWarnOnly != null && _logWarnOnly.Checked;

            // Rehacer la lista solo cuando cambia algo de verdad: el registro se escribe solo, y
            // reconstruir cuatrocientas lineas en cada vuelta se nota en el desplazamiento.
            string signature = entries.Count + "|" + filter + "|" + (warnOnly ? "1" : "0");
            if (string.Equals(signature, _logSignature, StringComparison.Ordinal))
            {
                return;
            }

            _logSignature = signature;

            _logList.BeginUpdate();
            try
            {
                _logList.Items.Clear();
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    string line = entries[i].ToString();
                    if (warnOnly && LogList.Classify(line) == LogKind.Info)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(filter) && line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    _logList.Items.Add(line);
                }
            }
            finally
            {
                _logList.EndUpdate();
            }
        }

        private void OnLogFilterChanged(object sender, EventArgs e)
        {
            _logSignature = null;
            RefreshLog();
        }

        /// <summary>
        /// Guarda el informe de diagnostico en un fichero de texto: es lo que se adjunta cuando
        /// se pide ayuda por un fallo.
        /// </summary>
        private void OnSaveDiagnostics(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Texto|*.txt|Log|*.log";
                dialog.FileName = "netforge-informe.txt";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    File.WriteAllText(dialog.FileName, _diagnostics.Text, new UTF8Encoding(false));
                    Log.Write("Informe de diagnostico guardado en " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>Abre en el Explorador la carpeta que se esta compartiendo.</summary>
        private void OnOpenSharedFolder(object sender, EventArgs e)
        {
            string folder = _context.Settings.Folder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show(this, Strings.T("files.foldermissing"), Strings.T("common.warning"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                using (Process.Start("explorer.exe", "\"" + folder + "\"")) { }
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo abrir la carpeta compartida: " + ex.Message);
                MessageBox.Show(this, ex.Message, Strings.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Doble clic sobre un equipo: se abre su direccion, como en el menu contextual.</summary>
        private void OnClientsOpenRow(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            OnClientsOpenBrowser(sender, EventArgs.Empty);
        }

        private LanClient SelectedClient()
        {
            if (_clients.SelectedRows.Count == 0)
            {
                return null;
            }

            object value = _clients.SelectedRows[0].Cells["mac"].Value;
            string mac = value == null ? string.Empty : value.ToString();
            if (string.IsNullOrEmpty(mac))
            {
                return null;
            }

            foreach (LanClient client in _context.Radar.Snapshot())
            {
                if (string.Equals(client.Mac, mac, StringComparison.OrdinalIgnoreCase))
                {
                    return client;
                }
            }

            return null;
        }

        private void RunOnUi(MethodInvoker action)
        {
            if (_released || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException)
            {
                // Modo Fantasma gano la carrera: la ventana ya no existe.
            }
            catch (InvalidOperationException)
            {
                // La ventana se destruyo entre la comprobacion y la llamada.
            }
        }

        private static string FormatSize(long bytes)
        {
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\"", "\"\"");
        }
    }
}
