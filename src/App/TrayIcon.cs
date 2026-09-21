// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - icono en la bandeja del sistema (Modo Fantasma).
using System;
using System.Drawing;
using System.Windows.Forms;
using NetForge.Core;
using NetForge.Ui;

namespace NetForge.App
{
    public sealed class TrayIcon : IDisposable
    {
        private readonly NetForgeContext _context;
        private readonly NotifyIcon _icon;
        private DateTime _lastBalloonUtc = DateTime.MinValue;

        public TrayIcon(NetForgeContext context)
        {
            _context = context;
            _icon = new NotifyIcon();
            _icon.Icon = LoadIcon();
            _icon.Text = Strings.T("tray.tip");
            _icon.Visible = true;
            _icon.DoubleClick += delegate { _context.ShowWindow(); };
            RebuildMenu();
        }

        public void RebuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(Strings.T("tray.show"), null, delegate { _context.ShowWindow(); });

            // Abrir el portal desde la bandeja: con la ventana plegada, este es el camino
            // corto para comprobar que los moviles pueden entrar.
            ToolStripMenuItem openPortal = new ToolStripMenuItem(Strings.T("tray.portal"), null,
                delegate { OpenPortal(); });
            openPortal.Enabled = !string.IsNullOrEmpty(_context.Files.PortalUrl);
            menu.Items.Add(openPortal);
            ToolStripMenuItem copyPortal = new ToolStripMenuItem(Strings.T("tray.copyportal"), null,
                delegate { CopyPortal(); });
            copyPortal.Enabled = !string.IsNullOrEmpty(_context.Files.PortalUrl);
            menu.Items.Add(copyPortal);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem stopNetwork = new ToolStripMenuItem(Strings.T("network.stop"), null,
                delegate { _context.StopNetwork(); RebuildMenu(); });
            stopNetwork.Enabled = _context.Hotspot.IsRunning;
            menu.Items.Add(stopNetwork);

            ToolStripMenuItem stopFiles = new ToolStripMenuItem(Strings.T("tray.files"), null,
                delegate { _context.StopFileServers(); RebuildMenu(); });
            stopFiles.Enabled = _context.Files.IsRunning;
            menu.Items.Add(stopFiles);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem(Strings.T("tray.exit"), null, delegate { ConfirmExit(); });
            menu.Items.Add(exit);

            _icon.ContextMenuStrip = menu;
            UpdateTooltip();
        }

        /// <summary>
        /// Salir desde la bandeja apaga la red y los servidores de archivos: es la accion mas
        /// destructiva del menu, asi que se pregunta antes. Un clic de mas en la bandeja no
        /// puede dejar a los moviles sin red.
        /// </summary>
        private void ConfirmExit()
        {
            DialogResult answer = MessageBox.Show(
                Strings.T("exit.text"),
                Strings.T("exit.title"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                _context.ExitApp();
            }
            else
            {
                Log.Write("Salida cancelada desde la bandeja");
            }
        }

        public void UpdateTooltip()
        {
            try
            {
                HotspotStatus status = _context.Hotspot.Status;
                string text = Strings.T("app.title");
                if (status != null && status.State == EngineState.On)
                {
                    text += " - " + status.Ssid + " en " + status.Endpoint;
                }

                if (text.Length > 62)
                {
                    text = text.Substring(0, 62);
                }

                _icon.Text = text;
            }
            catch (Exception)
            {
                // La tooltip nunca es critica.
            }
        }

        /// <summary>
        /// Aviso emergente, limitado para no convertirse en ruido. Si el usuario ha apagado los
        /// avisos en los ajustes, no se muestra ninguno: el registro de actividad sigue
        /// guardando todo, que es lo que se consulta despues.
        /// </summary>
        public void Notify(string title, string text)
        {
            if (_context.Settings != null && !_context.Settings.Notifications)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastBalloonUtc).TotalSeconds < 20)
            {
                return;
            }

            _lastBalloonUtc = DateTime.UtcNow;
            try
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipText = text;
                _icon.BalloonTipIcon = ToolTipIcon.Info;
                _icon.ShowBalloonTip(6000);
            }
            catch (Exception)
            {
                // Sin bandeja disponible no hay nada que hacer.
            }
        }

        public void Dispose()
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
            }
            catch (Exception)
            {
                // Cerrar nunca lanza.
            }
        }

        private void OpenPortal()
        {
            string url = _context.Files.PortalUrl;
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(url);
                Log.Write("Portal abierto desde la bandeja: " + url);
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo abrir el portal: " + ex.Message);
            }
        }

        private void CopyPortal()
        {
            string url = _context.Files.PortalUrl;
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            try
            {
                Clipboard.SetText(url);
                Log.Write("Direccion del portal copiada: " + url);
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo copiar la direccion del portal: " + ex.Message);
            }
        }

        private static Icon LoadIcon()
        {
            try
            {
                Icon own = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (own != null)
                {
                    return own;
                }
            }
            catch (Exception)
            {
                // Sin icono propio se usa uno del sistema.
            }

            return SystemIcons.Shield;
        }
    }
}
