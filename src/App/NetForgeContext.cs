// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - nucleo de la aplicacion.
//
// El contexto es el dueno de todo lo importante (red, radar, firewall, archivos) y vive
// aunque no haya ventana. Eso es lo que hace posible el Modo Fantasma: se destruye la
// interfaz para liberar memoria y los servicios siguen exactamente igual.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NetForge.Core;
using NetForge.Firewall;
using NetForge.Net;
using NetForge.Native;
using NetForge.Ui;

namespace NetForge.App
{
    public sealed class NetForgeContext : ApplicationContext
    {
        private readonly EventWaitHandle _showSignal;
        private RegisteredWaitHandle _showWait;
        private MainForm _form;
        private TrayIcon _tray;
        private bool _filesWereRunning;
        private bool _shuttingDown;
        private int _collapseCount;

        public NetForgeContext(bool startHidden, EventWaitHandle showSignal)
        {
            _showSignal = showSignal;
            Settings = AppSettings.Load();

            // El idioma se fija ANTES de crear nada visible: los textos se piden una sola vez,
            // al construir la bandeja y la ventana.
            Strings.ApplyLanguage(Settings.Language);

            Profile = CapabilityProbe.Probe();
            Elevated = Profile.Elevated;

            Log.Write("NetForge Studio iniciado sobre " + OsInfo.Describe());
            Log.Write("Windows soportado: " + (Profile.WindowsSupported ? "si" : "no") + "; permisos de administrador: " + (Elevated ? "si" : "no"));
            Log.Write("Motores: " + Profile.WiFiDirectPublisher + " Wi-Fi Direct, " + Profile.HostedNetwork + " red hospedada, " + Profile.TetheringApi + " punto de acceso");

            Firewall = new FirewallRuleManager();
            Log.Write("Firewall: " + Firewall.BackendNote);
            Bans = new FirewallBanList(Firewall);
            Sharing = new IcsSharing();

            Hotspot = new HotspotController(Profile);
            Hotspot.Preference = Settings.Engine;
            Guard = new DaemonGuard(Hotspot);
            Radar = new RadarService(CurrentAccessPoint, CurrentEngineClients, Bans, EnforceBan);
            Files = new FileShareService(Settings);
            ApplyTimings();

            Guard.Resurrected += OnResurrected;
            Guard.GaveUp += OnGaveUp;
            Guard.Noticed += OnGuardNoticed;
            Radar.ClientJoined += OnClientJoined;
            Hotspot.Changed += delegate { if (_tray != null) { _tray.RebuildMenu(); } };

            _tray = new TrayIcon(this);

            _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, OnShowRequested, null, Timeout.Infinite, false);

            if (startHidden)
            {
                Log.Write("Arranque silencioso: solo bandeja");
                TryAutoStart();
            }
            else
            {
                ShowWindow();
                if (Settings.StartMinimized)
                {
                    CollapseWindow();
                }
            }
        }

        public MachineProfile Profile { get; private set; }

        public AppSettings Settings { get; private set; }

        public HotspotController Hotspot { get; private set; }

        public DaemonGuard Guard { get; private set; }

        public RadarService Radar { get; private set; }

        public FirewallRuleManager Firewall { get; private set; }

        public FirewallBanList Bans { get; private set; }

        public FileShareService Files { get; private set; }

        public IcsSharing Sharing { get; private set; }

        public bool Elevated { get; private set; }

        /// <summary>Memoria privada en uso, en MB. Se muestra en la barra inferior y en el informe.</summary>
        public long MemoryMb
        {
            get { return CurrentWorkingSetMb(); }
        }

        public bool WindowVisible
        {
            get { return _form != null && !_form.IsDisposed && _form.Visible; }
        }

        public void ShowWindow()
        {
            if (_shuttingDown)
            {
                return;
            }

            if (_form == null || _form.IsDisposed)
            {
                _form = new MainForm(this);
            }

            try
            {
                _form.Show();
                if (_form.WindowState == FormWindowState.Minimized)
                {
                    _form.WindowState = FormWindowState.Normal;
                }

                _form.Activate();
                _form.BringToFront();
                Log.Write("Panel mostrado");
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo mostrar el panel: " + ex.Message);
            }
        }

        /// <summary>
        /// Modo Fantasma: destruye la ventana, compacta el monton y devuelve las paginas al
        /// sistema. La red, el radar, el firewall y los servidores de archivos no se tocan.
        /// </summary>
        public void CollapseWindow()
        {
            MainForm form = _form;
            _form = null;
            if (form != null)
            {
                try
                {
                    form.Hide();
                    form.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Write("Error al destruir la interfaz: " + ex.Message);
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            PsApi.TrimWorkingSet();

            _collapseCount++;
            long memory = CurrentWorkingSetMb();
            Log.Write("Modo Fantasma: interfaz destruida, " + memory + " MB en uso, red y archivos siguen activos");
            if (_tray != null)
            {
                _tray.Notify(Strings.T("app.title"), Strings.T("tray.ghostmessage"));
            }
        }

        public bool StartNetwork(out string error)
        {
            error = string.Empty;
            HotspotConfig config = new HotspotConfig
            {
                Ssid = Settings.Ssid,
                Passphrase = Settings.Passphrase,
                PreferTwoGhz = Settings.PreferTwoGhz,
                ForcedInterfaceId = null
            };

            try
            {
                config.Validate();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write("No se puede crear la red: " + error);
                return false;
            }

            if (Profile.RadioOn == Capability.No)
            {
                error = Strings.T("error.radiooff");
                Log.Write(error);
                return false;
            }

            Hotspot.Preference = Settings.Engine;
            if (!Hotspot.Start(config))
            {
                error = Hotspot.LastError;
                return false;
            }

            Log.Write("Red levantada con " + Hotspot.Engine.DisplayName + " en " + Hotspot.Status.Endpoint);
            Radar.Start();
            Guard.Arm();

            // Un paso mas y ya se pueden pasar archivos: el portal y el FTP se levantan solos
            // cuando el usuario lo ha pedido en los ajustes (viene activado de fabrica, que es
            // la gracia de "1-Click FTP").
            bool wanted = _filesWereRunning || (Settings.AutoStartServers && !string.IsNullOrEmpty(Settings.Folder));
            if (wanted)
            {
                string ignored;
                if (!StartFileServers(out ignored) && !string.IsNullOrEmpty(ignored))
                {
                    Log.Write("Archivos: no se pudieron levantar con la red: " + ignored);
                }
            }

            if (_tray != null)
            {
                _tray.RebuildMenu();
                _tray.Notify(Strings.T("app.title"), Strings.F("tray.networkup", Settings.Ssid, Hotspot.Status.Endpoint));
            }

            return true;
        }

        public void StopNetwork()
        {
            Guard.Disarm();
            Radar.Stop();
            DisableInternetSharing();
            StopFileServers();
            Hotspot.Stop();
            Log.Write("Red detenida por el usuario");
            if (_tray != null)
            {
                _tray.RebuildMenu();
            }
        }

        /// <summary>
        /// Intervalos del radar y del Daemon Guard segun los ajustes. Se aplican en caliente:
        /// los dos servicios leen la propiedad en cada vuelta del temporizador.
        /// </summary>
        public void ApplyTimings()
        {
            Settings.Normalize();
            Radar.IntervalMs = Settings.RadarSeconds * 1000;
            Guard.IntervalMs = Settings.GuardSeconds * 1000;
            Log.Write("Ritmo: radar cada " + Settings.RadarSeconds + " s, vigilancia cada " + Settings.GuardSeconds + " s");
        }

        /// <summary>
        /// Reconstruye la interfaz para que tome los textos del idioma nuevo. Se hace en el
        /// bucle de mensajes (BeginInvoke) porque este metodo se llama desde un evento de la
        /// propia ventana: destruirla en mitad de su propio manejador deja controles muertos.
        /// </summary>
        public void ReloadInterface()
        {
            RunOnUi(delegate
            {
                bool wasVisible = WindowVisible;
                CollapseWindow();
                if (wasVisible)
                {
                    ShowWindow();
                }
            });
        }

        public bool EnableInternetSharing(out string message)
        {
            ApInterface accessPoint = CurrentAccessPoint();
            if (accessPoint == null)
            {
                message = Strings.T("error.nonetwork");
                return false;
            }

            IcsSharingResult result = Sharing.Enable(accessPoint);
            message = result.Message;
            Log.Write("Compartir internet: " + result.Message);
            return result.Success;
        }

        public void DisableInternetSharing()
        {
            if (Sharing == null || !Sharing.IsSharing)
            {
                return;
            }

            Sharing.Disable();
        }

        public bool StartFileServers(out string error)
        {
            error = string.Empty;
            ApInterface accessPoint = CurrentAccessPoint();

            if (accessPoint == null || accessPoint.Address == null)
            {
                error = Strings.T("files.nored");
                return false;
            }

            if (!Files.Start(accessPoint.Address, out error))
            {
                Log.Write("Archivos: " + error);
                return false;
            }

            _filesWereRunning = true;
            if (_tray != null)
            {
                _tray.RebuildMenu();
            }

            return true;
        }

        public void StopFileServers()
        {
            _filesWereRunning = false;
            if (Files != null)
            {
                Files.Stop();
            }

            if (_tray != null)
            {
                _tray.RebuildMenu();
            }
        }

        public bool BanClient(LanClient client, out string error)
        {
            error = string.Empty;
            if (client == null)
            {
                error = Strings.T("error.noselection");
                return false;
            }

            return Bans.Ban(client.Mac, client.Address, "Bloqueado desde el panel", client.VendorText, out error);
        }

        public bool Pardon(string mac, out string error)
        {
            return Bans.Pardon(mac, out error);
        }

        public int PardonAll(out string error)
        {
            return Bans.PardonAll(out error);
        }

        public string DiagnosticsReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Profile.ToReport());
            sb.AppendLine();
            sb.AppendLine("Estado de la aplicacion");
            sb.AppendLine("  Red local        : " + (Hotspot.IsRunning ? "activa (" + Hotspot.Status.Engine + ")" : "detenida"));
            sb.AppendLine("  Auto-resurreccion: " + (Guard.Armed ? "vigilando" : "inactiva") + ", recuperaciones: " + Guard.TotalResurrections);
            sb.AppendLine("  Radar            : " + (Radar.ScanCycles == 0 ? "sin ciclos" : Radar.ScanCycles + " ciclos") + ", clientes: " + Radar.Snapshot().Count);
            sb.AppendLine("  Bloqueados       : " + Bans.Count);
            sb.AppendLine("  Firewall         : " + Firewall.BackendNote);
            sb.AppendLine("  Base de datos OUI: " + (OuiDatabase.Available
                ? OuiDatabase.VendorCount + " prefijos"
                : "no disponible (" + OuiDatabase.LoadError + ")"));
            sb.AppendLine("  Compartir (ICS)  : " + (Sharing.IsSharing ? "activado" : "desactivado"));
            sb.AppendLine("  Archivos         : " + Files.Describe());
            sb.AppendLine("  Memoria en uso   : " + CurrentWorkingSetMb() + " MB (interfaz destruida " + _collapseCount + " vez/veces)");
            sb.AppendLine("  Preferencias     : " + AppSettings.StorePath);
            sb.AppendLine("  Registro         : " + Log.FilePath);
            sb.AppendLine();
            sb.AppendLine("Motores en este equipo");
            sb.Append(Hotspot.ExplainEngines());
            return sb.ToString();
        }

        public void ExitApp()
        {
            if (_shuttingDown)
            {
                return;
            }

            _shuttingDown = true;
            Log.Write("Cerrando la aplicacion");
            try
            {
                StopNetwork();
            }
            catch (Exception ex)
            {
                Log.Write("Error al detener la red: " + ex.Message);
            }

            try
            {
                Files.Dispose();
                Radar.Dispose();
                Guard.Dispose();
                Hotspot.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write("Error al liberar los servicios: " + ex.Message);
            }

            if (_showWait != null)
            {
                _showWait.Unregister(null);
                _showWait = null;
            }

            if (_tray != null)
            {
                _tray.Dispose();
                _tray = null;
            }

            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_form != null && !_form.IsDisposed)
                {
                    _form.Dispose();
                }

                if (_tray != null)
                {
                    _tray.Dispose();
                    _tray = null;
                }
            }

            base.Dispose(disposing);
        }

        private void TryAutoStart()
        {
            string error;
            if (!StartNetwork(out error))
            {
                Log.Write("Arranque automatico de la red fallido: " + error);
                if (_tray != null)
                {
                    _tray.Notify(Strings.T("common.warning"), error);
                }

                return;
            }

            // StartNetwork ya levanta los servidores si asi esta pedido; aqui solo queda el
            // caso de que el arranque automatico de la red este apagado y se hayan pedido los
            // archivos a mano antes.
            if (!Settings.AutoStartServers && _filesWereRunning)
            {
                string ignored;
                StartFileServers(out ignored);
            }
        }

        private void OnResurrected(object sender, GuardEvent e)
        {
            Log.Write("Daemon Guard: " + e.Message);
            if (_tray != null)
            {
                _tray.Notify(Strings.T("app.title"), e.Message);
                _tray.RebuildMenu();
            }

            // La direccion del adaptador puede haber cambiado al renacer la red: los
            // servidores de archivos escuchan en una direccion concreta y hay que reabrir.
            if (_filesWereRunning)
            {
                string ignored;
                StartFileServers(out ignored);
            }

            if (_shuttingDown)
            {
                return;
            }

            RunOnUi(delegate
            {
                if (_form != null && !_form.IsDisposed)
                {
                    _form.Refresh();
                }
            });
        }

        private void OnGaveUp(object sender, GuardEvent e)
        {
            Log.Write("Daemon Guard: " + e.Message);
            if (_tray != null)
            {
                _tray.Notify(Strings.T("common.warning"), e.Message);
            }
        }

        private void OnGuardNoticed(object sender, GuardEvent e)
        {
            if (_tray != null)
            {
                _tray.UpdateTooltip();
            }
        }

        /// <summary>
        /// Un dispositivo nuevo. Con la ventana plegada (Modo Fantasma) lo unico que avisa de
        /// que alguien ha entrado en la red es este aviso emergente.
        /// </summary>
        private void OnClientJoined(object sender, ClientJoinedEventArgs e)
        {
            if (!Settings.Notifications || !Settings.NotifyOnJoin || e == null || e.Client == null)
            {
                return;
            }

            string address = string.IsNullOrEmpty(e.Client.AddressText) ? string.Empty : e.Client.AddressText;
            Log.Write("Dispositivo nuevo en la red: " + e.Client.Mac + " " + address + " " + e.Client.VendorText);
            if (_tray != null)
            {
                _tray.Notify(Strings.T("tray.joined"), address + "  " + e.Client.VendorText);
            }
        }

        private void OnShowRequested(object state, bool timedOut)
        {
            if (timedOut || _shuttingDown)
            {
                return;
            }

            RunOnUi(ShowWindow);
        }

        private ApInterface CurrentAccessPoint()
        {
            IHotspotEngine engine = Hotspot.Engine;
            if (engine == null)
            {
                return null;
            }

            return engine.AccessPoint;
        }

        private IList<EngineClient> CurrentEngineClients()
        {
            IHotspotEngine engine = Hotspot.Engine;
            IClientReportingEngine reporting = engine as IClientReportingEngine;
            if (reporting == null)
            {
                return null;
            }

            return reporting.GetClients();
        }

        private void EnforceBan(string mac, IPAddress address)
        {
            Bans.Enforce(mac, address);
        }

        private void RunOnUi(MethodInvoker action)
        {
            try
            {
                if (_form != null && !_form.IsDisposed && _form.IsHandleCreated)
                {
                    _form.BeginInvoke(action);
                }
            }
            catch (Exception)
            {
                // La ventana desaparecio: no hay nada que refrescar.
            }
        }

        private static long CurrentWorkingSetMb()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    process.Refresh();
                    return process.WorkingSet64 / (1024 * 1024);
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
