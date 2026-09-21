// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - servidor FTP efimero (1-Click FTP).
//
// Se implementa a mano y solo con lo que usan los clientes de movil: canal de control por
// texto, modo pasivo (PASV) y transferencias binarias. Nada de dependencias externas.
//
// Seguridad, que es donde se rompen casi todos los servidores caseros:
//  - Cada ruta se resuelve con GetFullPath y se comprueba que siga dentro de la carpeta
//    elegida, asi que "..", simbolos y rutas absolutas no escapan del directorio.
//  - Por defecto es SOLO DESCARGA: que alguien escanee un QR no deberia poder borrar nada.
//  - Se escucha en la direccion de la red local, no en 0.0.0.0, para no abrir el servidor
//    por otras tarjetas (Ethernet, VPN, adaptadores virtuales).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NetForge.Ui;

namespace NetForge.Ftp
{
    public sealed class FtpOptions
    {
        public string RootPath { get; set; }
        public IPAddress BindAddress { get; set; }
        public int Port { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public bool AllowAnonymous { get; set; }
        public bool AllowUpload { get; set; }
        public bool AllowDelete { get; set; }
        public int MaxSessions { get; set; }
        public int IdleTimeoutMs { get; set; }

        public FtpOptions()
        {
            RootPath = string.Empty;
            Port = 2121;
            UserName = "netforge";
            Password = string.Empty;
            AllowAnonymous = false;
            AllowUpload = false;
            AllowDelete = false;
            MaxSessions = 16;
            IdleTimeoutMs = 120000;
        }

        public FtpOptions Clone()
        {
            return new FtpOptions
            {
                RootPath = RootPath,
                BindAddress = BindAddress,
                Port = Port,
                UserName = UserName,
                Password = Password,
                AllowAnonymous = AllowAnonymous,
                AllowUpload = AllowUpload,
                AllowDelete = AllowDelete,
                MaxSessions = MaxSessions,
                IdleTimeoutMs = IdleTimeoutMs
            };
        }
    }

    public sealed class FtpServer : IDisposable
    {
        private readonly FtpOptions _options;
        private readonly object _sync = new object();
        private readonly List<FtpSession> _sessions = new List<FtpSession>();
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private long _bytesTransferred;
        private bool _disposed;

        public FtpServer(FtpOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            _options = options.Clone();
        }

        public event EventHandler Changed;

        public bool IsRunning
        {
            get { return _running; }
        }

        public int ActiveSessions
        {
            get
            {
                lock (_sync)
                {
                    return _sessions.Count;
                }
            }
        }

        public long BytesTransferred
        {
            get { return Interlocked.Read(ref _bytesTransferred); }
        }

        public int Port { get; private set; }

        public string Url
        {
            get
            {
                if (_options.BindAddress == null)
                {
                    return string.Empty;
                }

                return "ftp://" + _options.BindAddress + ":" + Port + "/";
            }
        }

        public string LastError { get; private set; }

        public bool Start(out string error)
        {
            error = string.Empty;
            if (_running)
            {
                return true;
            }

            if (string.IsNullOrEmpty(_options.RootPath) || !Directory.Exists(_options.RootPath))
            {
                error = "la carpeta compartida no existe";
                return false;
            }

            try
            {
                IPAddress address = _options.BindAddress ?? IPAddress.Any;
                TcpListener listener = new TcpListener(address, _options.Port);
                listener.Start();
                _listener = listener;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _running = true;

                _acceptThread = new Thread(AcceptLoop);
                _acceptThread.IsBackground = true;
                _acceptThread.Name = "NetForge.Ftp.Accept";
                _acceptThread.Start();

                Log.Write("FTP: escuchando en " + Url + " (" + _options.RootPath + ")" +
                          (_options.AllowUpload ? ", subida permitida" : ", solo descarga"));
                Raise();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                LastError = error;
                Log.Write("FTP: no pudo arrancar: " + ex.Message);
                return false;
            }
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                }
            }
            catch (Exception)
            {
                // Ya estaba cerrado.
            }

            List<FtpSession> sessions;
            lock (_sync)
            {
                sessions = new List<FtpSession>(_sessions);
            }

            foreach (FtpSession session in sessions)
            {
                session.Close();
            }

            _listener = null;
            Port = 0;
            Log.Write("FTP: detenido");
            Raise();
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

        internal void AddBytes(long count)
        {
            if (count <= 0)
            {
                return;
            }

            Interlocked.Add(ref _bytesTransferred, count);
        }

        internal FtpOptions Options
        {
            get { return _options; }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    // El listener se cerro: fin del bucle.
                    break;
                }

                bool accepted = false;
                lock (_sync)
                {
                    if (_sessions.Count < _options.MaxSessions)
                    {
                        FtpSession session = new FtpSession(this, client);
                        _sessions.Add(session);
                        accepted = true;
                        ThreadPool.QueueUserWorkItem(delegate { RunSession(session); });
                    }
                }

                if (!accepted)
                {
                    try
                    {
                        byte[] busy = Encoding.ASCII.GetBytes("421 Demasiadas conexiones activas.\r\n");
                        client.GetStream().Write(busy, 0, busy.Length);
                        client.Close();
                    }
                    catch (Exception)
                    {
                        // Ignorar.
                    }
                }
            }
        }

        private void RunSession(FtpSession session)
        {
            try
            {
                session.Run();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            finally
            {
                session.Close();
                lock (_sync)
                {
                    _sessions.Remove(session);
                }

                Raise();
            }
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
                // El observador no puede romper el servidor.
            }
        }
    }

    internal sealed class FtpSession
    {
        private readonly FtpServer _server;
        private readonly TcpClient _client;
        private NetworkStream _stream;
        private StreamReader _reader;
        private StreamWriter _writer;
        private string _currentDirectory = "/";
        private bool _authenticated;
        private string _userName = string.Empty;
        private bool _passive;
        private TcpListener _passiveListener;
        private bool _disposed;

        public FtpSession(FtpServer server, TcpClient client)
        {
            _server = server;
            _client = client;
        }

        public void Run()
        {
            _client.NoDelay = true;
            _client.ReceiveTimeout = _server.Options.IdleTimeoutMs;
            _stream = _client.GetStream();
            _reader = new StreamReader(_stream, Encoding.UTF8, false, 4096, true);
            _writer = new StreamWriter(_stream, new UTF8Encoding(false));
            _writer.NewLine = "\r\n";
            _writer.AutoFlush = true;

            Reply("220 NetForge Studio - servidor de archivos local");

            bool running = true;
            while (running)
            {
                string line;
                try
                {
                    line = _reader.ReadLine();
                }
                catch (Exception)
                {
                    break;
                }

                if (line == null)
                {
                    break;
                }

                string command;
                string argument;
                SplitCommand(line, out command, out argument);

                try
                {
                    running = Handle(command, argument);
                }
                catch (Exception ex)
                {
                    Log.Write("FTP: error atendiendo " + command + ": " + ex.Message);
                    Reply("451 Error interno del servidor");
                }
            }
        }

        public void Close()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                ClosePassive();
            }
            catch (Exception)
            {
                // Ignorar.
            }

            try
            {
                if (_writer != null)
                {
                    _writer.Dispose();
                }

                if (_reader != null)
                {
                    _reader.Dispose();
                }

                if (_stream != null)
                {
                    _stream.Dispose();
                }

                if (_client != null)
                {
                    _client.Close();
                }
            }
            catch (Exception)
            {
                // Cerrar nunca lanza.
            }
        }

        private static void SplitCommand(string line, out string command, out string argument)
        {
            string trimmed = line.Trim();
            int space = trimmed.IndexOf(' ');
            if (space < 0)
            {
                command = trimmed.ToUpperInvariant();
                argument = string.Empty;
                return;
            }

            command = trimmed.Substring(0, space).ToUpperInvariant();
            argument = trimmed.Substring(space + 1).Trim();
        }

        private bool Handle(string command, string argument)
        {
            switch (command)
            {
                case "USER":
                    _userName = argument;
                    if (_server.Options.AllowAnonymous && (argument.Length == 0 || string.Equals(argument, "anonymous", StringComparison.OrdinalIgnoreCase)))
                    {
                        _authenticated = true;
                        Reply("230 Sesion anonima aceptada");
                    }
                    else
                    {
                        Reply("331 Usuario aceptado, hace falta la clave");
                    }

                    return true;

                case "PASS":
                    if (_authenticated || CheckPassword(argument))
                    {
                        _authenticated = true;
                        Reply("230 Sesion iniciada. Carpeta: " + _currentDirectory);
                    }
                    else
                    {
                        Reply("530 Usuario o clave incorrectos");
                    }

                    return true;

                case "QUIT":
                    Reply("221 Hasta luego");
                    return false;

                case "NOOP":
                    Reply("200 Vale");
                    return true;

                case "SYST":
                    Reply("215 UNIX Type: L8");
                    return true;

                case "FEAT":
                    Reply("211-Caracteristicas:");
                    Reply(" SIZE");
                    Reply(" MDTM");
                    Reply(" PASV");
                    Reply(" UTF8");
                    Reply("211 Fin");
                    return true;

                case "OPTS":
                    Reply("200 Opcion aceptada");
                    return true;

                case "CLNT":
                    Reply("200 Vale");
                    return true;

                case "TYPE":
                    Reply("200 Tipo de transferencia binario");
                    return true;

                case "MODE":
                case "STRU":
                    Reply("200 Vale");
                    return true;

                case "PWD":
                case "XPWD":
                    Reply("257 \"" + _currentDirectory + "\" es la carpeta actual");
                    return true;

                case "CWD":
                case "XCWD":
                    return ChangeDirectory(argument, false);

                case "CDUP":
                case "XCUP":
                    return ChangeDirectory("..", false);

                case "PASV":
                    return EnterPassive();

                case "EPSV":
                    return EnterExtendedPassive();

                case "LIST":
                    return SendListing(argument, true);

                case "NLST":
                    return SendListing(argument, false);

                case "MLSD":
                    return SendMachineListing(argument);

                case "SIZE":
                    return SendSize(argument);

                case "MDTM":
                    return SendModifiedTime(argument);

                case "RETR":
                    return SendFile(argument);

                case "STOR":
                    return ReceiveFile(argument);

                case "DELE":
                    return DeleteFile(argument);

                case "MKD":
                case "XMKD":
                    return MakeDirectory(argument);

                case "RMD":
                case "XRMD":
                    return RemoveDirectory(argument);

                case "ABOR":
                    Reply("226 Sin transferencia que cancelar");
                    return true;

                case "REST":
                    Reply("350 Reinicio no soportado");
                    return true;

                default:
                    Reply("502 Orden no implementada: " + command);
                    return true;
            }
        }

        private bool CheckPassword(string supplied)
        {
            if (!string.Equals(_userName, _server.Options.UserName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(supplied, _server.Options.Password, StringComparison.Ordinal);
        }

        private bool RequireAuthentication()
        {
            if (_authenticated)
            {
                return true;
            }

            Reply("530 Primero hay que iniciar sesion");
            return false;
        }

        private bool ChangeDirectory(string argument, bool allowAbsolute)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            string target;
            if (!TryResolve(argument, out target))
            {
                Reply("550 Esa carpeta no existe");
                return true;
            }

            if (!Directory.Exists(target))
            {
                Reply("550 Esa carpeta no existe");
                return true;
            }

            _currentDirectory = ToVirtualPath(target);
            Reply("250 Carpeta actual: " + _currentDirectory);
            return true;
        }

        private bool EnterPassive()
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            try
            {
                ClosePassive();
                IPAddress address = _server.Options.BindAddress ?? IPAddress.Any;
                _passiveListener = new TcpListener(address, 0);
                _passiveListener.Start();
                int port = ((IPEndPoint)_passiveListener.LocalEndpoint).Port;
                _passive = true;

                string host = address.Equals(IPAddress.Any)
                    ? ((IPEndPoint)_client.Client.LocalEndPoint).Address.ToString()
                    : address.ToString();
                Reply("227 Entrando en modo pasivo (" + host.Replace('.', ',') + "," + (port / 256) + "," + (port % 256) + ")");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("FTP: PASV fallo: " + ex.Message);
                Reply("425 No se pudo abrir el canal de datos");
                return true;
            }
        }

        private bool EnterExtendedPassive()
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            try
            {
                ClosePassive();
                IPAddress address = _server.Options.BindAddress ?? IPAddress.Any;
                _passiveListener = new TcpListener(address, 0);
                _passiveListener.Start();
                int port = ((IPEndPoint)_passiveListener.LocalEndpoint).Port;
                _passive = true;
                Reply("229 Entrando en modo pasivo extendido (|||" + port + "|)");
                return true;
            }
            catch (Exception ex)
            {
                Log.Write("FTP: EPSV fallo: " + ex.Message);
                Reply("425 No se pudo abrir el canal de datos");
                return true;
            }
        }

        private bool SendListing(string argument, bool detailed)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            string target;
            if (!TryResolve(string.IsNullOrEmpty(argument) ? _currentDirectory : argument, out target))
            {
                Reply("550 Ruta no valida");
                return true;
            }

            // El 150 va ANTES de aceptar el canal de datos: los clientes esperan la
            // confirmacion para conectarse, y si el servidor se queda esperando primero se
            // produce un bloqueo mutuo hasta que salta el tiempo de espera.
            Reply("150 Abriendo listado");
            TcpClient dataClient = AcceptPassive();
            if (dataClient == null)
            {
                return true;
            }

            try
            {
                using (NetworkStream data = dataClient.GetStream())
                {
                    string body = detailed ? BuildDetailedListing(target) : BuildNameListing(target);
                    byte[] payload = Encoding.UTF8.GetBytes(body);
                    data.Write(payload, 0, payload.Length);
                    _server.AddBytes(payload.Length);
                }

                Reply("226 Listado completado");
            }
            catch (Exception ex)
            {
                Log.Write("FTP: LIST fallo: " + ex.Message);
                Reply("426 Error durante el listado");
            }
            finally
            {
                try
                {
                    dataClient.Close();
                }
                catch (Exception)
                {
                    // Ignorar.
                }

                ClosePassive();
            }

            return true;
        }

        private bool SendMachineListing(string argument)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            string target;
            if (!TryResolve(string.IsNullOrEmpty(argument) ? _currentDirectory : argument, out target))
            {
                Reply("550 Ruta no valida");
                return true;
            }

            Reply("150 Abriendo listado");
            TcpClient dataClient = AcceptPassive();
            if (dataClient == null)
            {
                return true;
            }

            try
            {
                using (NetworkStream data = dataClient.GetStream())
                {
                    StringBuilder sb = new StringBuilder();
                    DirectoryInfo directory = new DirectoryInfo(target);
                    if (directory.Exists)
                    {
                        foreach (DirectoryInfo child in directory.GetDirectories())
                        {
                            sb.AppendLine("type=dir;modify=" + child.LastWriteTimeUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "; " + child.Name);
                        }

                        foreach (FileInfo child in directory.GetFiles())
                        {
                            sb.AppendLine("type=file;size=" + child.Length + ";modify=" + child.LastWriteTimeUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "; " + child.Name);
                        }
                    }

                    byte[] payload = Encoding.UTF8.GetBytes(sb.ToString());
                    data.Write(payload, 0, payload.Length);
                    _server.AddBytes(payload.Length);
                }

                Reply("226 Listado completado");
            }
            catch (Exception ex)
            {
                Log.Write("FTP: MLSD fallo: " + ex.Message);
                Reply("426 Error durante el listado");
            }
            finally
            {
                try
                {
                    dataClient.Close();
                }
                catch (Exception)
                {
                    // Ignorar.
                }

                ClosePassive();
            }

            return true;
        }

        private bool SendSize(string argument)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            string target;
            if (!TryResolve(argument, out target) || !File.Exists(target))
            {
                Reply("550 Ese archivo no existe");
                return true;
            }

            Reply("213 " + new FileInfo(target).Length);
            return true;
        }

        private bool SendModifiedTime(string argument)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            string target;
            if (!TryResolve(argument, out target) || !File.Exists(target))
            {
                Reply("550 Ese archivo no existe");
                return true;
            }

            Reply("213 " + File.GetLastWriteTimeUtc(target).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            return true;
        }

        private bool SendFile(string argument)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            string target;
            if (!TryResolve(argument, out target) || !File.Exists(target))
            {
                Reply("550 Ese archivo no existe");
                return true;
            }

            FileInfo info = new FileInfo(target);
            Reply("150 Enviando " + info.Name + " (" + info.Length + " bytes)");
            TcpClient dataClient = AcceptPassive();
            if (dataClient == null)
            {
                return true;
            }

            long transferred = 0;
            try
            {
                using (NetworkStream data = dataClient.GetStream())
                using (FileStream source = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024))
                {
                    byte[] buffer = new byte[64 * 1024];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        data.Write(buffer, 0, read);
                        transferred += read;
                        _server.AddBytes(read);
                    }
                }

                Reply("226 Transferencia completada (" + transferred + " bytes)");
            }
            catch (Exception ex)
            {
                Log.Write("FTP: RETR fallo: " + ex.Message);
                Reply("426 Transferencia interrumpida tras " + transferred + " bytes");
            }
            finally
            {
                try
                {
                    dataClient.Close();
                }
                catch (Exception)
                {
                    // Ignorar.
                }

                ClosePassive();
            }

            return true;
        }

        private bool ReceiveFile(string argument)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            if (!_server.Options.AllowUpload)
            {
                Reply("550 Este servidor esta en modo solo descarga");
                return true;
            }

            string target;
            if (!TryResolve(argument, out target))
            {
                Reply("550 Ruta no valida");
                return true;
            }

            Reply("150 Listo para recibir " + Path.GetFileName(target));
            TcpClient dataClient = AcceptPassive();
            if (dataClient == null)
            {
                return true;
            }

            long transferred = 0;
            try
            {
                using (NetworkStream data = dataClient.GetStream())
                using (FileStream destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
                {
                    byte[] buffer = new byte[64 * 1024];
                    int read;
                    while ((read = data.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        destination.Write(buffer, 0, read);
                        transferred += read;
                        _server.AddBytes(read);
                    }
                }

                Reply("226 Archivo recibido (" + transferred + " bytes)");
                Log.Write("FTP: recibido " + Path.GetFileName(target) + " (" + transferred + " bytes)");
            }
            catch (Exception ex)
            {
                Log.Write("FTP: STOR fallo: " + ex.Message);
                Reply("426 Subida interrumpida tras " + transferred + " bytes");
            }
            finally
            {
                try
                {
                    dataClient.Close();
                }
                catch (Exception)
                {
                    // Ignorar.
                }

                ClosePassive();
            }

            return true;
        }

        private bool DeleteFile(string argument)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            if (!_server.Options.AllowDelete)
            {
                Reply("550 Borrar esta desactivado en este servidor");
                return true;
            }

            string target;
            if (!TryResolve(argument, out target) || !File.Exists(target))
            {
                Reply("550 Ese archivo no existe");
                return true;
            }

            try
            {
                File.Delete(target);
                Reply("250 Archivo borrado");
                Log.Write("FTP: borrado " + Path.GetFileName(target));
            }
            catch (Exception ex)
            {
                Reply("550 No se pudo borrar: " + ex.Message);
            }

            return true;
        }

        private bool MakeDirectory(string argument)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            if (!_server.Options.AllowUpload)
            {
                Reply("550 Este servidor esta en modo solo descarga");
                return true;
            }

            string target;
            if (!TryResolve(argument, out target))
            {
                Reply("550 Ruta no valida");
                return true;
            }

            try
            {
                Directory.CreateDirectory(target);
                Reply("257 Carpeta creada");
            }
            catch (Exception ex)
            {
                Reply("550 No se pudo crear: " + ex.Message);
            }

            return true;
        }

        private bool RemoveDirectory(string argument)
        {
            if (!RequireAuthentication())
            {
                return true;
            }

            if (!_server.Options.AllowDelete)
            {
                Reply("550 Borrar esta desactivado en este servidor");
                return true;
            }

            string target;
            if (!TryResolve(argument, out target) || !Directory.Exists(target))
            {
                Reply("550 Esa carpeta no existe");
                return true;
            }

            try
            {
                if (Directory.GetFileSystemEntries(target).Length > 0)
                {
                    Reply("550 La carpeta no esta vacia");
                    return true;
                }

                Directory.Delete(target);
                Reply("250 Carpeta borrada");
            }
            catch (Exception ex)
            {
                Reply("550 No se pudo borrar: " + ex.Message);
            }

            return true;
        }

        private TcpClient AcceptPassive()
        {
            if (!_passive || _passiveListener == null)
            {
                Reply("425 Primero hay que pedir el modo pasivo (PASV)");
                return null;
            }

            // Espera acotada: un cliente que pide PASV y nunca se conecta no puede dejar la
            // sesion colgada para siempre.
            int waited = 0;
            try
            {
                while (waited < 15000)
                {
                    if (_passiveListener.Pending())
                    {
                        TcpClient dataClient = _passiveListener.AcceptTcpClient();
                        dataClient.NoDelay = true;
                        return dataClient;
                    }

                    if (!_server.IsRunning)
                    {
                        break;
                    }

                    System.Threading.Thread.Sleep(50);
                    waited += 50;
                }

                Log.Write("FTP: el cliente no abrio el canal de datos en 15 s");
                Reply("425 No se pudo abrir el canal de datos");
                ClosePassive();
                return null;
            }
            catch (Exception ex)
            {
                Log.Write("FTP: no llego el canal de datos: " + ex.Message);
                Reply("425 No se pudo abrir el canal de datos");
                ClosePassive();
                return null;
            }
        }

        private void ClosePassive()
        {
            _passive = false;
            TcpListener listener = _passiveListener;
            _passiveListener = null;
            if (listener != null)
            {
                try
                {
                    listener.Stop();
                }
                catch (Exception)
                {
                    // Ignorar.
                }
            }
        }

        private void Reply(string text)
        {
            try
            {
                _writer.WriteLine(text);
            }
            catch (Exception)
            {
                // El cliente se fue: el bucle de control lo detectara.
            }
        }

        /// <summary>
        /// Convierte una ruta pedida por el cliente en una ruta real DENTRO de la carpeta
        /// compartida. Cualquier intento de escapar (.. o ruta absoluta) se rechaza.
        /// </summary>
        private bool TryResolve(string argument, out string fullPath)
        {
            fullPath = null;
            string root = Path.GetFullPath(_server.Options.RootPath);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                root += Path.DirectorySeparatorChar;
            }

            string requested = argument == null ? string.Empty : argument.Replace('/', Path.DirectorySeparatorChar).Trim();
            if (requested.Length == 0)
            {
                requested = _currentDirectory;
            }

            string combined;
            if (requested.StartsWith("/", StringComparison.Ordinal) || requested.StartsWith("\\", StringComparison.Ordinal))
            {
                combined = Path.Combine(root, requested.TrimStart('/', '\\'));
            }
            else
            {
                string basePath = Path.Combine(root, _currentDirectory.TrimStart('/', '\\'));
                combined = Path.Combine(basePath, requested);
            }

            try
            {
                string candidate = Path.GetFullPath(combined);
                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(candidate + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Write("FTP: intento de salir de la carpeta compartida: " + argument);
                    return false;
                }

                fullPath = candidate;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string ToVirtualPath(string fullPath)
        {
            string root = Path.GetFullPath(_server.Options.RootPath);
            string relative = fullPath.Substring(Math.Min(fullPath.Length, root.Length)).TrimStart('\\', '/');
            if (relative.Length == 0)
            {
                return "/";
            }

            return "/" + relative.Replace('\\', '/');
        }

        private static string BuildNameListing(string target)
        {
            StringBuilder sb = new StringBuilder();
            if (Directory.Exists(target))
            {
                foreach (string entry in Directory.GetFileSystemEntries(target))
                {
                    sb.Append(Path.GetFileName(entry)).Append("\r\n");
                }
            }
            else if (File.Exists(target))
            {
                sb.Append(Path.GetFileName(target)).Append("\r\n");
            }

            return sb.ToString();
        }

        private static string BuildDetailedListing(string target)
        {
            StringBuilder sb = new StringBuilder();
            if (Directory.Exists(target))
            {
                foreach (string entry in Directory.GetFileSystemEntries(target))
                {
                    sb.Append(DescribeEntry(entry)).Append("\r\n");
                }
            }
            else if (File.Exists(target))
            {
                sb.Append(DescribeEntry(target)).Append("\r\n");
            }

            return sb.ToString();
        }

        private static string DescribeEntry(string path)
        {
            try
            {
                bool isDirectory = Directory.Exists(path);
                DateTime modified = isDirectory ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path);
                long size = isDirectory ? 0 : new FileInfo(path).Length;
                string name = Path.GetFileName(path);
                string permissions = isDirectory ? "drwxr-xr-x" : "-rw-r--r--";
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} 1 ftp ftp {1,12} {2} {3}",
                    permissions,
                    size,
                    modified.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture),
                    name);
            }
            catch (Exception)
            {
                return "-rw-r--r-- 1 ftp ftp 0 Jan 01 00:00 " + Path.GetFileName(path);
            }
        }
    }
}
