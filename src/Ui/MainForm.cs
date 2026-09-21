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

        private Label _stateLabel;
        private Label _endpointLabel;
        private Label _guardLabel;
        private Label _bannerLabel;
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
        private Label _clientsCount;
        private TextBox _filter;
        private Button _clearFilter;
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

        private Label _statusMemory;
        private Label _statusPorts;

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
        private Label _filesStats;

        private TextBox _diagnostics;
        private Button _refreshDiagnostics;
        private Button _copyDiagnostics;

        private ListBox _logList;
        private Button _saveLog;
        private Button _openLog;

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

            root.Controls.Add(BuildHeader(), 0, 0);

            TabControl tabs = Theme.Tabs();
            tabs.TabPages.Add(BuildClientsPage());
            tabs.TabPages.Add(BuildBansPage());
            tabs.TabPages.Add(BuildFilesPage());
            tabs.TabPages.Add(BuildSettingsPage());
            tabs.TabPages.Add(BuildDiagnosticsPage());
            tabs.TabPages.Add(BuildLogPage());
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
            panel.Padding = new Padding(10, 5, 10, 5);

            _statusMemory = new Label();
            _statusMemory.AutoSize = true;
            _statusMemory.ForeColor = Theme.TextMuted;
            _statusMemory.Margin = new Padding(0, 2, 18, 0);
            panel.Controls.Add(_statusMemory);

            _statusPorts = new Label();
            _statusPorts.AutoSize = true;
            _statusPorts.ForeColor = Theme.TextMuted;
            _statusPorts.Margin = new Padding(0, 2, 0, 0);
            panel.Controls.Add(_statusPorts);
            return panel;
        }

        private Control BuildHeader()
        {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Top;
            panel.AutoSize = true;
            panel.ColumnCount = 1;
            panel.BackColor = Theme.Surface;
            panel.Padding = new Padding(14, 10, 14, 10);

            TableLayoutPanel titleRow = new TableLayoutPanel();
            titleRow.AutoSize = true;
            titleRow.ColumnCount = 3;
            titleRow.Dock = DockStyle.Top;

            Label title = new Label();
            title.Text = Strings.T("app.title");
            title.Font = Theme.TitleFont;
            title.ForeColor = Theme.Text;
            title.AutoSize = true;
            title.Margin = new Padding(0, 0, 14, 0);
            titleRow.Controls.Add(title, 0, 0);

            _stateLabel = new Label();
            _stateLabel.AutoSize = true;
            _stateLabel.Font = new Font("Segoe UI Semibold", 11f);
            _stateLabel.ForeColor = Theme.TextMuted;
            _stateLabel.Margin = new Padding(0, 4, 14, 0);
            titleRow.Controls.Add(_stateLabel, 1, 0);

            _endpointLabel = new Label();
            _endpointLabel.AutoSize = true;
            _endpointLabel.ForeColor = Theme.TextMuted;
            _endpointLabel.Margin = new Padding(0, 8, 0, 0);
            titleRow.Controls.Add(_endpointLabel, 2, 0);
            panel.Controls.Add(titleRow, 0, 0);

            FlowLayoutPanel fields = new FlowLayoutPanel();
            fields.AutoSize = true;
            fields.WrapContents = true;
            fields.Dock = DockStyle.Top;

            fields.Controls.Add(Theme.Caption(Strings.T("network.ssid")));
            _ssid = Theme.Input(170);
            fields.Controls.Add(_ssid);

            fields.Controls.Add(Theme.Caption(Strings.T("network.password")));
            _password = Theme.Input(170);
            _password.UseSystemPasswordChar = true;
            fields.Controls.Add(_password);

            _showPassword = Theme.Check(Strings.T("network.show"));
            _showPassword.CheckedChanged += delegate { _password.UseSystemPasswordChar = !_showPassword.Checked; };
            fields.Controls.Add(_showPassword);

            fields.Controls.Add(Theme.Caption(Strings.T("network.engine")));
            _engine = new ComboBox();
            _engine.DropDownStyle = ComboBoxStyle.DropDownList;
            _engine.Width = 230;
            Theme.StyleInput(_engine);
            _engine.Items.Add(Strings.T("network.engine.auto"));
            _engine.Items.Add(Strings.T("network.engine.offgrid"));
            _engine.Items.Add(Strings.T("network.engine.sharing"));
            _engine.Items.Add(Strings.T("network.engine.legacy"));
            fields.Controls.Add(_engine);

            _startStop = Theme.Primary(Strings.T("network.create"));
            _startStop.Click += OnStartStop;
            fields.Controls.Add(_startStop);

            _share = Theme.Check(Strings.T("network.share"));
            _share.CheckedChanged += OnShareChanged;
            fields.Controls.Add(_share);

            panel.Controls.Add(fields, 0, 1);

            FlowLayoutPanel options = new FlowLayoutPanel();
            options.AutoSize = true;
            options.WrapContents = true;
            options.Dock = DockStyle.Top;

            _autostart = Theme.Check(Strings.T("autostart"));
            _autostart.CheckedChanged += OnAutostartChanged;
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

            _guardLabel = new Label();
            _guardLabel.AutoSize = true;
            _guardLabel.ForeColor = Theme.TextMuted;
            _guardLabel.Margin = new Padding(12, 8, 0, 0);
            options.Controls.Add(_guardLabel);

            _bannerLabel = new Label();
            _bannerLabel.AutoSize = true;
            _bannerLabel.ForeColor = Theme.Warning;
            _bannerLabel.Margin = new Padding(12, 8, 0, 0);
            options.Controls.Add(_bannerLabel);

            panel.Controls.Add(options, 0, 2);

            Label subtitle = new Label();
            subtitle.Text = Strings.T("app.subtitle");
            subtitle.AutoSize = true;
            subtitle.ForeColor = Theme.TextMuted;
            subtitle.Margin = new Padding(0, 6, 0, 0);
            panel.Controls.Add(subtitle, 0, 3);

            return panel;
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

            FlowLayoutPanel filterRow = new FlowLayoutPanel();
            filterRow.Dock = DockStyle.Top;
            filterRow.AutoSize = true;

            _filter = Theme.Input(260);
            _filter.TextChanged += OnFilterChanged;
            filterRow.Controls.Add(_filter);

            _clearFilter = Theme.Secondary(Strings.T("clients.clear"));
            _clearFilter.Click += delegate
            {
                _filter.Text = string.Empty;
                filterRow.Focus();
            };
            filterRow.Controls.Add(_clearFilter);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;

            _clientsCount = new Label();
            _clientsCount.AutoSize = true;
            _clientsCount.ForeColor = Theme.TextMuted;
            _clientsCount.Margin = new Padding(4, 12, 12, 0);
            bar.Controls.Add(_clientsCount);

            _hammer = Theme.DangerButton(Strings.T("clients.hammer"));
            _hammer.Click += OnHammer;
            bar.Controls.Add(_hammer);

            _export = Theme.Secondary(Strings.T("clients.export"));
            _export.Click += OnExportClients;
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

            page.Controls.Add(_clients);
            page.Controls.Add(filterRow);
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

            _pardon = Theme.Primary(Strings.T("parole.pardon"));
            _pardon.Click += OnPardon;
            bar.Controls.Add(_pardon);

            _pardonAll = Theme.Secondary(Strings.T("parole.pardonall"));
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
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));

            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.ColumnCount = 1;
            left.AutoSize = true;

            left.Controls.Add(Theme.Section(Strings.T("files.folder")), 0, 0);

            FlowLayoutPanel folderRow = new FlowLayoutPanel();
            folderRow.AutoSize = true;
            _folder = Theme.Input(360);
            folderRow.Controls.Add(_folder);
            _browse = Theme.Secondary(Strings.T("files.browse"));
            _browse.Click += OnBrowse;
            folderRow.Controls.Add(_browse);
            left.Controls.Add(folderRow, 0, 1);

            FlowLayoutPanel switches = new FlowLayoutPanel();
            switches.AutoSize = true;
            switches.WrapContents = true;
            _upload = Theme.Check(Strings.T("files.allowupload"));
            _upload.CheckedChanged += OnShareOptionsChanged;
            switches.Controls.Add(_upload);
            _delete = Theme.Check(Strings.T("files.allowdelete"));
            _delete.CheckedChanged += OnShareOptionsChanged;
            switches.Controls.Add(_delete);
            left.Controls.Add(switches, 0, 2);

            FlowLayoutPanel servers = new FlowLayoutPanel();
            servers.AutoSize = true;
            _startServers = Theme.Primary(Strings.T("files.start"));
            _startServers.Click += OnStartServers;
            servers.Controls.Add(_startServers);
            _stopServers = Theme.Secondary(Strings.T("files.stop"));
            _stopServers.Click += OnStopServers;
            servers.Controls.Add(_stopServers);
            left.Controls.Add(servers, 0, 3);

            left.Controls.Add(Theme.Section(Strings.T("files.portal")), 0, 4);
            _portalLink = new LinkLabel();
            _portalLink.AutoSize = true;
            _portalLink.ForeColor = Theme.Accent;
            _portalLink.LinkColor = Theme.Accent;
            _portalLink.LinkClicked += OnOpenLink;
            left.Controls.Add(_portalLink, 0, 5);

            FlowLayoutPanel linkRow = new FlowLayoutPanel();
            linkRow.AutoSize = true;
            _copyLink = Theme.Secondary(Strings.T("files.copy"));
            _copyLink.Click += OnCopyLink;
            linkRow.Controls.Add(_copyLink);
            left.Controls.Add(linkRow, 0, 6);

            left.Controls.Add(Theme.Section(Strings.T("files.ftp")), 0, 7);
            _ftpLink = new LinkLabel();
            _ftpLink.AutoSize = true;
            _ftpLink.ForeColor = Theme.Accent;
            _ftpLink.LinkColor = Theme.Accent;
            _ftpLink.LinkClicked += OnOpenLink;
            left.Controls.Add(_ftpLink, 0, 8);

            _filesStats = new Label();
            _filesStats.AutoSize = true;
            _filesStats.ForeColor = Theme.TextMuted;
            _filesStats.Margin = new Padding(0, 10, 0, 0);
            left.Controls.Add(_filesStats, 0, 9);

            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.AutoSize = true;
            right.Controls.Add(Theme.Section(Strings.T("files.qr")), 0, 0);

            _qr = new PictureBox();
            _qr.Size = new Size(240, 240);
            _qr.SizeMode = PictureBoxSizeMode.Zoom;
            _qr.BackColor = Color.White;
            _qr.Margin = new Padding(0, 6, 0, 6);
            right.Controls.Add(_qr, 0, 1);

            _qrHint = new Label();
            _qrHint.AutoSize = true;
            _qrHint.MaximumSize = new Size(260, 0);
            _qrHint.ForeColor = Theme.TextMuted;
            right.Controls.Add(_qrHint, 0, 2);

            layout.Controls.Add(left, 0, 0);
            layout.Controls.Add(right, 1, 0);
            page.Controls.Add(layout);
            return page;
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

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Top;
            layout.ColumnCount = 2;
            layout.AutoSize = true;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            int row = 0;
            AddSection(layout, Strings.T("settings.section.app"), ref row);

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

            AddSection(layout, Strings.T("settings.section.network"), ref row);

            _band = new ComboBox();
            _band.DropDownStyle = ComboBoxStyle.DropDownList;
            _band.Width = 240;
            Theme.StyleInput(_band);
            _band.Items.Add(Strings.T("settings.band.2"));
            _band.Items.Add(Strings.T("settings.band.5"));
            AddSetting(layout, Strings.T("settings.band"), _band, ref row);

            AddSection(layout, Strings.T("settings.section.timing"), ref row);

            _radarSeconds = NewNumber(AppSettings.MinRadarSeconds, AppSettings.MaxRadarSeconds, 1, " s");
            AddSetting(layout, Strings.T("settings.radar"), _radarSeconds, ref row);

            _guardSeconds = NewNumber(AppSettings.MinGuardSeconds, AppSettings.MaxGuardSeconds, 1, " s");
            AddSetting(layout, Strings.T("settings.guard"), _guardSeconds, ref row);

            AddSection(layout, Strings.T("settings.section.service"), ref row);

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
            _startService.Click += OnStartService;
            serviceRow.Controls.Add(_serviceState);
            serviceRow.Controls.Add(_startService);
            layout.Controls.Add(serviceRow, 1, row++);
            layout.Controls.Add(_serviceNote, 1, row++);

            AddSection(layout, Strings.T("settings.section.startup"), ref row);

            _autostartState = new Label();
            _autostartState.AutoSize = true;
            _autostartState.MaximumSize = new Size(520, 0);
            _autostartState.ForeColor = Theme.TextMuted;
            _autostartState.Margin = new Padding(0, 8, 12, 0);

            _repairAutostart = Theme.Secondary(Strings.T("settings.autostart.repair"));
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

            AddSection(layout, Strings.T("settings.section.files"), ref row);

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

            AddSection(layout, Strings.T("settings.section.alerts"), ref row);

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

            Label hint = new Label();
            hint.Text = Strings.T("settings.restartnetwork");
            hint.AutoSize = true;
            hint.MaximumSize = new Size(520, 0);
            hint.ForeColor = Theme.TextMuted;
            hint.Margin = new Padding(0, 4, 0, 0);
            layout.Controls.Add(hint, 1, row++);

            // Los botones van en una barra fija abajo, fuera de la zona que se desplaza: si no,
            // con la ventana pequena hay que bajar para encontrar "Guardar" y parece que no existe.
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.AutoSize = true;
            buttons.BackColor = Theme.Background;
            buttons.Padding = new Padding(0, 6, 0, 2);

            _saveSettings = Theme.Primary(Strings.T("settings.save"));
            _saveSettings.Click += OnSaveSettings;
            buttons.Controls.Add(_saveSettings);

            _defaultSettings = Theme.Secondary(Strings.T("settings.defaults"));
            _defaultSettings.Click += OnRestoreDefaults;
            buttons.Controls.Add(_defaultSettings);

            _openDataFolder = Theme.Secondary(Strings.T("settings.openfolder"));
            _openDataFolder.Click += delegate { Log.OpenFolder(); };
            buttons.Controls.Add(_openDataFolder);

            scroll.Controls.Add(layout);
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

        private static void AddSection(TableLayoutPanel layout, string title, ref int row)
        {
            Label label = Theme.Section(title);
            label.Margin = new Padding(0, row == 0 ? 0 : 14, 0, 4);
            layout.Controls.Add(label, 0, row++);
            layout.SetColumnSpan(label, 2);
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

            _diagnostics = new TextBox();
            _diagnostics.Multiline = true;
            _diagnostics.ReadOnly = true;
            _diagnostics.ScrollBars = ScrollBars.Both;
            _diagnostics.WordWrap = false;
            _diagnostics.Dock = DockStyle.Fill;
            _diagnostics.Font = Theme.MonoFont;
            Theme.StyleInput(_diagnostics);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;

            _refreshDiagnostics = Theme.Primary(Strings.T("diag.refresh"));
            _refreshDiagnostics.Click += OnRefreshDiagnostics;
            bar.Controls.Add(_refreshDiagnostics);

            _copyDiagnostics = Theme.Secondary(Strings.T("diag.copy"));
            _copyDiagnostics.Click += OnCopyDiagnostics;
            bar.Controls.Add(_copyDiagnostics);

            page.Controls.Add(_diagnostics);
            page.Controls.Add(bar);
            return page;
        }

        private TabPage BuildLogPage()
        {
            TabPage page = Theme.Page(Strings.T("tab.log"));

            _logList = new ListBox();
            _logList.Dock = DockStyle.Fill;
            _logList.BackColor = Theme.SurfaceAlt;
            _logList.ForeColor = Theme.Text;
            _logList.Font = Theme.MonoFont;
            _logList.BorderStyle = BorderStyle.None;

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Bottom;
            bar.AutoSize = true;

            _saveLog = Theme.Secondary(Strings.T("log.dump"));
            _saveLog.Click += OnSaveLog;
            bar.Controls.Add(_saveLog);

            _openLog = Theme.Secondary(Strings.T("log.open"));
            _openLog.Click += delegate { Log.OpenFolder(); };
            bar.Controls.Add(_openLog);

            page.Controls.Add(_logList);
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
                _bannerLabel.Text = _context.Elevated ? string.Empty : Strings.T("banner.noelevated");
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
            switch (status.State)
            {
                case EngineState.On:
                    stateText = Strings.T("network.state.on");
                    color = Theme.Success;
                    break;
                case EngineState.Starting:
                    stateText = Strings.T("network.state.starting");
                    color = Theme.Warning;
                    break;
                case EngineState.Stopping:
                    stateText = Strings.T("network.state.stopping");
                    color = Theme.Warning;
                    break;
                case EngineState.Faulted:
                    stateText = Strings.T("network.state.faulted");
                    color = Theme.Danger;
                    break;
                default:
                    stateText = Strings.T("network.state.idle");
                    color = Theme.TextMuted;
                    break;
            }

            _stateLabel.Text = stateText;
            _stateLabel.ForeColor = color;
            _endpointLabel.Text = status.State == EngineState.On
                ? "SSID " + status.Ssid + "  |  " + status.Endpoint + "  |  " + status.Engine
                : string.Empty;
            _startStop.Text = _context.Hotspot.IsRunning ? Strings.T("network.stop") : Strings.T("network.create");
            _guardLabel.Text = Strings.T("network.guard") + ": " + (_context.Guard.Armed
                ? _context.Guard.TotalResurrections + " recuperacion(es)"
                : "inactiva");
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
                  "   conexiones: " + _context.Files.ClientCount
                : Strings.T("files.nored");

            RefreshQr();
        }

        private void RefreshQr()
        {
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
            int shown = 0;

            _clients.SuspendLayout();
            try
            {
                _clients.Rows.Clear();
                foreach (LanClient client in clients)
                {
                    if (!MatchesFilter(client, filter))
                    {
                        continue;
                    }

                    int index = _clients.Rows.Add(
                        client.AddressText,
                        client.Mac,
                        client.VendorText,
                        string.IsNullOrEmpty(client.HostName) ? "-" : client.HostName,
                        client.IsBanned ? Strings.T("clients.banned") : client.PresenceText,
                        client.SourceText);
                    if (client.IsBanned)
                    {
                        _clients.Rows[index].DefaultCellStyle.ForeColor = Theme.Danger;
                    }

                    if (selected != null && string.Equals(selected, client.Mac, StringComparison.OrdinalIgnoreCase))
                    {
                        _clients.Rows[index].Selected = true;
                    }

                    shown++;
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
            finally
            {
                _clients.ResumeLayout();
            }

            _clientsCount.Text = string.IsNullOrEmpty(filter)
                ? shown + " " + Strings.T("network.clients")
                : shown + " / " + clients.Count + " " + Strings.T("network.clients");
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
                " (web), " + _context.Settings.FtpPort + " (ftp)";
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
                // Una tabla vacia sin explicacion parece un fallo de la aplicacion.
                _bans.Rows.Add(string.Empty, string.Empty, string.Empty, string.Empty, Strings.T("parole.empty"));
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
            if (_released || IsDisposed)
            {
                return;
            }

            IList<LogEntry> entries = Log.Snapshot();
            if (_logList.Items.Count == entries.Count)
            {
                return;
            }

            _logList.BeginUpdate();
            try
            {
                _logList.Items.Clear();
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    _logList.Items.Add(entries[i].ToString());
                }
            }
            finally
            {
                _logList.EndUpdate();
            }
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
