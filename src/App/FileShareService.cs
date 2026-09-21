// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - servidores de archivos (1-Click FTP + portal) y su codigo QR.
using System;
using System.Drawing;
using System.IO;
using System.Net;
using NetForge.Core;
using NetForge.Ftp;
using NetForge.Qr;
using NetForge.Ui;
using NetForge.Web;

namespace NetForge.App
{
    public sealed class FileShareService : IDisposable
    {
        private readonly AppSettings _settings;
        private FtpServer _ftp;
        private FilePortal _portal;
        private bool _disposed;

        public FileShareService(AppSettings settings)
        {
            _settings = settings;
        }

        public event EventHandler Changed;

        public bool IsRunning
        {
            get { return _ftp != null || _portal != null; }
        }

        public string PortalUrl
        {
            get { return _portal == null ? string.Empty : _portal.Url; }
        }

        public string FtpUrl
        {
            get { return _ftp == null ? string.Empty : _ftp.Url; }
        }

        public string LastError { get; private set; }

        public long BytesServed
        {
            get
            {
                long total = 0;
                if (_ftp != null)
                {
                    total += _ftp.BytesTransferred;
                }

                if (_portal != null)
                {
                    total += _portal.BytesServed;
                }

                return total;
            }
        }

        public long BytesReceived
        {
            get { return _portal == null ? 0 : _portal.BytesReceived; }
        }

        public int ClientCount
        {
            get { return _ftp == null ? 0 : _ftp.ActiveSessions; }
        }

        /// <summary>
        /// Levanta los dos servidores en la direccion de la red local. Devuelve false solo si
        /// NINGUNO arranca: si el FTP falla pero el portal funciona, sigue siendo util.
        /// </summary>
        public bool Start(IPAddress address, out string error)
        {
            error = string.Empty;
            Stop();

            if (address == null)
            {
                error = Strings.T("error.noapaddress");
                return false;
            }

            if (string.IsNullOrEmpty(_settings.Folder) || !Directory.Exists(_settings.Folder))
            {
                error = Strings.T("error.nofolder");
                return false;
            }

            // Los puertos salen de los ajustes, con el rango ya saneado al cargarlos.
            FilePortalOptions portalOptions = new FilePortalOptions
            {
                RootPath = _settings.Folder,
                BindAddress = address,
                Port = _settings.PortalPort,
                AllowUpload = _settings.ShareFolderUploads,
                AllowDelete = _settings.ShareFolderDelete
            };

            // Un nombre de usuario vacio deja el FTP en acceso anonimo: una red local de
            // confianza no necesita credenciales, y asi el telefono no pide nada al entrar.
            bool anonymous = string.IsNullOrWhiteSpace(_settings.FtpUser);
            FtpOptions ftpOptions = new FtpOptions
            {
                RootPath = _settings.Folder,
                BindAddress = address,
                Port = _settings.FtpPort,
                UserName = anonymous ? "anonymous" : _settings.FtpUser.Trim(),
                Password = _settings.Passphrase,
                AllowUpload = _settings.ShareFolderUploads,
                AllowDelete = _settings.ShareFolderDelete,
                AllowAnonymous = anonymous
            };

            string portalError = string.Empty;
            string ftpError = string.Empty;
            FilePortal portal = new FilePortal(portalOptions);
            if (portal.Start(out portalError))
            {
                portal.Changed += Forward;
                _portal = portal;
            }
            else
            {
                portal.Dispose();
            }

            FtpServer ftp = new FtpServer(ftpOptions);
            if (ftp.Start(out ftpError))
            {
                ftp.Changed += Forward;
                _ftp = ftp;
            }

            if (_portal == null && _ftp == null)
            {
                error = Strings.F("error.servers", portalError, ftpError);
                LastError = error;
                return false;
            }

            if (_portal == null)
            {
                LastError = Strings.F("error.serverpartial", portalError);
                Log.Write("Archivos: " + LastError);
            }

            Log.Write("Archivos compartidos: " + Describe());
            Raise();
            return true;
        }

        public void Stop()
        {
            bool wasRunning = IsRunning;
            if (_portal != null)
            {
                _portal.Changed -= Forward;
                _portal.Dispose();
                _portal = null;
            }

            if (_ftp != null)
            {
                _ftp.Changed -= Forward;
                _ftp.Dispose();
                _ftp = null;
            }

            if (wasRunning)
            {
                Raise();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }

        /// <summary>
        /// Codigo QR del portal web. Se prioriza HTTP sobre FTP porque los navegadores
        /// modernos ya no abren enlaces ftp://.
        /// </summary>
        public Bitmap CreateQr(int moduleSize, Color dark, Color light, out string payload)
        {
            payload = PortalUrl;
            if (string.IsNullOrEmpty(payload))
            {
                payload = FtpUrl;
            }

            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }

            try
            {
                QrCode code = QrEncoder.Encode(payload, QrEcc.Medium);
                return QrRenderer.Render(code, moduleSize, dark, light);
            }
            catch (Exception ex)
            {
                Log.Write("QR: no se pudo generar: " + ex.Message);
                return null;
            }
        }

        public string Describe()
        {
            string portal = string.IsNullOrEmpty(PortalUrl) ? "portal: apagado" : "portal: " + PortalUrl;
            string ftp = string.IsNullOrEmpty(FtpUrl) ? "ftp: apagado" : "ftp: " + FtpUrl;
            return portal + " | " + ftp;
        }

        private void Forward(object sender, EventArgs e)
        {
            Raise();
        }

        private void Raise()
        {
            EventHandler handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // El observador no puede romper el servicio.
            }
        }
    }
}
