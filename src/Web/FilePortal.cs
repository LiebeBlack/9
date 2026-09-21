// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - portal de archivos por HTTP.
//
// Existe porque el FTP ya no basta en un movil moderno: los navegadores dejaron de hablar
// ftp:// y muchas apps de archivos no lo soportan. Con HTTP cualquier telefono abre la
// pagina del QR y sube o baja archivos sin instalar nada. El FTP se mantiene porque el
// especificado lo pide y sigue siendo util para clientes de escritorio.
//
// Detalles que marcan la diferencia:
//  - Subidas en flujo continuo: una pelicula de varios GB no pasa por memoria.
//  - Descargas con soporte de Range: sin eso, ver un video en el movil no funciona.
//  - Misma jaula de rutas que el FTP: nunca se sale de la carpeta elegida.
//  - Pagina sin recursos externos: funciona sin internet, que es el proposito de la app.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using NetForge.Ui;

namespace NetForge.Web
{
    public sealed class FilePortalOptions
    {
        public string RootPath { get; set; }
        public IPAddress BindAddress { get; set; }
        public int Port { get; set; }
        public bool AllowUpload { get; set; }
        public bool AllowDelete { get; set; }
        public string Title { get; set; }

        public FilePortalOptions()
        {
            RootPath = string.Empty;
            Port = 8080;
            AllowUpload = true;
            AllowDelete = false;
            Title = "NetForge Studio";
        }

        public FilePortalOptions Clone()
        {
            return new FilePortalOptions
            {
                RootPath = RootPath,
                BindAddress = BindAddress,
                Port = Port,
                AllowUpload = AllowUpload,
                AllowDelete = AllowDelete,
                Title = Title
            };
        }
    }

    public sealed class FilePortal : IDisposable
    {
        private readonly FilePortalOptions _options;
        private readonly object _sync = new object();
        private readonly Semaphore UploadSlots = new Semaphore(4, 4);
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private long _bytesServed;
        private long _bytesReceived;
        private long _uploads;
        private bool _disposed;

        public FilePortal(FilePortalOptions options)
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

        public int Port { get; private set; }

        public string Url
        {
            get
            {
                if (!_running || _options.BindAddress == null)
                {
                    return string.Empty;
                }

                return "http://" + _options.BindAddress + ":" + Port + "/";
            }
        }

        public long BytesServed
        {
            get { return Interlocked.Read(ref _bytesServed); }
        }

        public long BytesReceived
        {
            get { return Interlocked.Read(ref _bytesReceived); }
        }

        public long UploadCount
        {
            get { return Interlocked.Read(ref _uploads); }
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
                int port = _options.Port;
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add("http://" + address + ":" + port + "/");
                listener.Start();

                _listener = listener;
                Port = port;
                _running = true;

                _thread = new Thread(Loop);
                _thread.IsBackground = true;
                _thread.Name = "NetForge.Portal";
                _thread.Start();

                Log.Write("Portal: " + Url + " sirviendo " + _options.RootPath);
                Raise();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                LastError = error;
                Log.Write("Portal: no pudo arrancar: " + ex.Message);
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
                    _listener.Close();
                }
            }
            catch (Exception)
            {
                // Ya estaba cerrado.
            }

            _listener = null;
            Port = 0;
            Log.Write("Portal: detenido");
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
            UploadSlots.Close();
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (Exception)
                {
                    break;
                }

                ThreadPool.QueueUserWorkItem(delegate { Handle(context); });
            }
        }

        private void Handle(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url.AbsolutePath;
                string method = context.Request.HttpMethod;

                if (method == "GET" && (path == "/" || path == "/index.html"))
                {
                    SendIndex(context);
                    return;
                }

                if (method == "GET" && path.StartsWith("/f/", StringComparison.Ordinal))
                {
                    SendFile(context, Decode(path.Substring(3)));
                    return;
                }

                if (method == "POST" && path == "/subir")
                {
                    ReceiveUpload(context);
                    return;
                }

                if (method == "POST" && path.StartsWith("/borrar/", StringComparison.Ordinal))
                {
                    DeleteEntry(context, Decode(path.Substring(8)));
                    return;
                }

                if (method == "GET" && path == "/favicon.ico")
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }

                Fail(context, 404, "No encontrado");
            }
            catch (Exception ex)
            {
                Log.Write("Portal: error atendiendo la peticion: " + ex.Message);
                try
                {
                    Fail(context, 500, "Error interno");
                }
                catch (Exception)
                {
                    // El cliente ya no escucha.
                }
            }
        }

        private void SendIndex(HttpListenerContext context)
        {
            StringBuilder sb = new StringBuilder();
            string root = _options.RootPath;
            DirectoryInfo directory = new DirectoryInfo(root);

            sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>").Append(Html(_options.Title)).Append("</title>");
            sb.Append("<style>");
            sb.Append(":root{color-scheme:dark}");
            sb.Append("body{margin:0;font-family:system-ui,Segoe UI,Roboto,sans-serif;background:#0d1117;color:#e6edf3}");
            sb.Append("header{padding:20px 16px;background:#161b22;border-bottom:1px solid #30363d}");
            sb.Append("h1{margin:0;font-size:20px;letter-spacing:.4px}");
            sb.Append("p.sub{margin:4px 0 0;color:#8b949e;font-size:13px}");
            sb.Append("main{padding:16px;max-width:900px;margin:0 auto}");
            sb.Append("ul{list-style:none;padding:0;margin:0}");
            sb.Append("li{display:flex;align-items:center;gap:12px;padding:12px;border:1px solid #21262d;border-radius:10px;margin-bottom:8px;background:#11161d}");
            sb.Append("a.file{color:#58a6ff;text-decoration:none;word-break:break-all;flex:1}");
            sb.Append("span.meta{color:#8b949e;font-size:12px;white-space:nowrap}");
            sb.Append(".drop{border:2px dashed #30363d;border-radius:12px;padding:24px;text-align:center;color:#8b949e;margin-bottom:16px}");
            sb.Append("button{background:#238636;color:#fff;border:0;border-radius:8px;padding:10px 16px;font-size:15px}");
            sb.Append("button.danger{background:#8b2b2b;padding:6px 10px;font-size:12px}");
            sb.Append("input[type=file]{color:#e6edf3;margin-bottom:10px}");
            sb.Append("footer{padding:16px;text-align:center;color:#484f58;font-size:12px}");
            sb.Append("</style></head><body>");
            sb.Append("<header><h1>").Append(Html(_options.Title)).Append("</h1>");
            sb.Append("<p class=\"sub\">Archivos compartidos en la red local, sin cables ni internet</p></header><main>");

            if (_options.AllowUpload)
            {
                sb.Append("<form class=\"drop\" method=\"post\" action=\"/subir\" enctype=\"multipart/form-data\">");
                sb.Append("<div style=\"margin-bottom:10px\">Elegir archivos para subir a este equipo</div>");
                sb.Append("<input type=\"file\" name=\"archivos\" multiple><br>");
                sb.Append("<button type=\"submit\">Subir</button></form>");
            }

            sb.Append("<ul>");
            int entries = 0;
            try
            {
                foreach (DirectoryInfo child in directory.GetDirectories())
                {
                    sb.Append("<li><span>&#128193;</span><span class=\"file\">").Append(Html(child.Name)).Append("</span>");
                    sb.Append("<span class=\"meta\">carpeta</span></li>");
                    entries++;
                }

                foreach (FileInfo child in directory.GetFiles())
                {
                    sb.Append("<li><span>&#128196;</span>");
                    sb.Append("<a class=\"file\" href=\"/f/").Append(UrlEncode(child.Name)).Append("\">").Append(Html(child.Name)).Append("</a>");
                    sb.Append("<span class=\"meta\">").Append(Size(child.Length)).Append("</span>");
                    if (_options.AllowDelete)
                    {
                        sb.Append("<form method=\"post\" action=\"/borrar/").Append(UrlEncode(child.Name)).Append("\">");
                        sb.Append("<button class=\"danger\" type=\"submit\">Borrar</button></form>");
                    }

                    sb.Append("</li>");
                    entries++;
                }
            }
            catch (Exception ex)
            {
                sb.Append("<li>No se pudo listar la carpeta: ").Append(Html(ex.Message)).Append("</li>");
            }

            if (entries == 0)
            {
                sb.Append("<li>La carpeta esta vacia.</li>");
            }

            sb.Append("</ul></main>");
            sb.Append("<footer>Servido por NetForge Studio desde ").Append(Html(_options.BindAddress == null ? "?" : _options.BindAddress.ToString()));
            sb.Append(" &middot; trafico de subida: ").Append(Size(Interlocked.Read(ref _bytesReceived)));
            sb.Append("</footer></body></html>");

            byte[] payload = Encoding.UTF8.GetBytes(sb.ToString());
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = payload.Length;
            context.Response.OutputStream.Write(payload, 0, payload.Length);
            context.Response.Close();
        }

        private void SendFile(HttpListenerContext context, string name)
        {
            string path;
            if (!TryResolve(name, out path) || !File.Exists(path))
            {
                Fail(context, 404, "Archivo no encontrado");
                return;
            }

            FileInfo info = new FileInfo(path);
            long start = 0;
            long end = info.Length - 1;
            bool partial = false;

            string range = context.Request.Headers["Range"];
            if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                string spec = range.Substring(6).Split(',')[0].Trim();
                int dash = spec.IndexOf('-');
                if (dash >= 0)
                {
                    string fromText = spec.Substring(0, dash);
                    string toText = spec.Substring(dash + 1);
                    long parsed;
                    if (fromText.Length == 0 && long.TryParse(toText, out parsed))
                    {
                        start = Math.Max(0, info.Length - parsed);
                        partial = true;
                    }
                    else if (long.TryParse(fromText, out parsed))
                    {
                        start = parsed;
                        if (toText.Length > 0 && long.TryParse(toText, out parsed))
                        {
                            end = Math.Min(end, parsed);
                        }

                        partial = true;
                    }

                    if (start > end || start >= info.Length)
                    {
                        context.Response.StatusCode = 416;
                        context.Response.Headers["Content-Range"] = "bytes */" + info.Length;
                        context.Response.Close();
                        return;
                    }
                }
            }

            long length = end - start + 1;
            context.Response.StatusCode = partial ? 206 : 200;
            context.Response.ContentType = ContentTypeFor(info.Name);
            context.Response.ContentLength64 = length;
            context.Response.AddHeader("Accept-Ranges", "bytes");
            context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + info.Name.Replace("\"", string.Empty) + "\"");
            if (partial)
            {
                context.Response.AddHeader("Content-Range", "bytes " + start + "-" + end + "/" + info.Length);
            }

            try
            {
                using (FileStream source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024))
                {
                    source.Seek(start, SeekOrigin.Begin);
                    byte[] buffer = new byte[64 * 1024];
                    long remaining = length;
                    while (remaining > 0)
                    {
                        int want = (int)Math.Min(buffer.Length, remaining);
                        int read = source.Read(buffer, 0, want);
                        if (read <= 0)
                        {
                            break;
                        }

                        context.Response.OutputStream.Write(buffer, 0, read);
                        remaining -= read;
                        Interlocked.Add(ref _bytesServed, read);
                    }
                }
            }
            catch (Exception)
            {
                // El cliente corto la descarga: es normal (video pausado, pestana cerrada).
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch (Exception)
                {
                    // Ignorar.
                }
            }
        }

        private void ReceiveUpload(HttpListenerContext context)
        {
            if (!_options.AllowUpload)
            {
                Fail(context, 403, "Las subidas estan desactivadas");
                return;
            }

            if (!UploadSlots.WaitOne(TimeSpan.FromSeconds(30)))
            {
                Fail(context, 503, "Demasiadas subidas a la vez, intenta de nuevo");
                return;
            }

            try
            {
                string contentType = context.Request.ContentType ?? string.Empty;
                List<string> saved = new List<string>();
                if (contentType.IndexOf("multipart/form-data", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string boundary = ExtractBoundary(contentType);
                    if (string.IsNullOrEmpty(boundary))
                    {
                        Fail(context, 400, "Peticion sin delimitador de formulario");
                        return;
                    }

                    saved = MultipartReader.Read(context.Request.InputStream, boundary, _options.RootPath);
                }
                else
                {
                    string name = SanitizeName(context.Request.Headers["X-Filename"]);
                    if (string.IsNullOrEmpty(name))
                    {
                        Fail(context, 400, "Falta el nombre del archivo");
                        return;
                    }

                    long written = SaveStream(context.Request.InputStream, name, context.Request.ContentLength64);
                    if (written >= 0)
                    {
                        saved.Add(name);
                    }
                }

                Interlocked.Add(ref _uploads, saved.Count);
                Log.Write("Portal: " + saved.Count + " archivo(s) recibido(s)");
                Redirect(context, "/");
            }
            catch (Exception ex)
            {
                Log.Write("Portal: fallo la subida: " + ex.Message);
                Fail(context, 500, "No se pudo guardar: " + ex.Message);
            }
            finally
            {
                UploadSlots.Release();
                Raise();
            }
        }

        private void DeleteEntry(HttpListenerContext context, string name)
        {
            if (!_options.AllowDelete)
            {
                Fail(context, 403, "Borrar esta desactivado");
                return;
            }

            string path;
            if (TryResolve(name, out path) && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                    Log.Write("Portal: borrado " + name);
                }
                catch (Exception ex)
                {
                    Fail(context, 500, "No se pudo borrar: " + ex.Message);
                    return;
                }
            }

            Redirect(context, "/");
        }

        private long SaveStream(Stream input, string name, long contentLength)
        {
            string path;
            if (!TryResolve(name, out path))
            {
                return -1;
            }

            long total = 0;
            using (FileStream destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    destination.Write(buffer, 0, read);
                    total += read;
                    Interlocked.Add(ref _bytesReceived, read);
                }
            }

            return total;
        }

        internal bool TryResolve(string name, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string root = Path.GetFullPath(_options.RootPath);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                root += Path.DirectorySeparatorChar;
            }

            // Una ruta que empieza por separador es absoluta: se rechaza en vez de intentar
            // adivinar la intencion. Path.Combine la dejaria escapar de la carpeta compartida.
            string cleaned = name.Replace('/', Path.DirectorySeparatorChar);
            if (cleaned.StartsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                cleaned.StartsWith("\\", StringComparison.Ordinal) ||
                (cleaned.Length > 1 && cleaned[1] == ':'))
            {
                Log.Write("Portal: ruta absoluta rechazada: " + name);
                return false;
            }

            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(root, cleaned));
            }
            catch (Exception)
            {
                return false;
            }

            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                Log.Write("Portal: intento de salir de la carpeta compartida: " + name);
                return false;
            }

            fullPath = candidate;
            return true;
        }

        internal static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string cleaned = name.Replace('\\', '/');
            int slash = cleaned.LastIndexOf('/');
            if (slash >= 0)
            {
                cleaned = cleaned.Substring(slash + 1);
            }

            // "C:evil.txt" trae el prefijo de unidad y "notas.txt:flujo" un flujo alternativo
            // de NTFS: en ambos casos el nombre real es lo que va despues de los dos puntos.
            int colon = cleaned.LastIndexOf(':');
            if (colon >= 0)
            {
                cleaned = cleaned.Substring(colon + 1);
            }

            cleaned = cleaned.Trim();
            if (cleaned.Length == 0 || cleaned == "." || cleaned == "..")
            {
                return string.Empty;
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                cleaned = cleaned.Replace(invalid, '_');
            }

            return cleaned;
        }

        internal static string UniqueName(string root, string name)
        {
            string candidate = SanitizeName(name);
            if (candidate.Length == 0)
            {
                candidate = "archivo";
            }

            string path = Path.Combine(root, candidate);
            if (!File.Exists(path))
            {
                return candidate;
            }

            string baseName = Path.GetFileNameWithoutExtension(candidate);
            string extension = Path.GetExtension(candidate);
            for (int i = 1; i < 10000; i++)
            {
                string attempt = baseName + " (" + i + ")" + extension;
                if (!File.Exists(Path.Combine(root, attempt)))
                {
                    return attempt;
                }
            }

            return baseName + "-" + DateTime.Now.Ticks + extension;
        }

        private static string ExtractBoundary(string contentType)
        {
            const string marker = "boundary=";
            int index = contentType.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return string.Empty;
            }

            string value = contentType.Substring(index + marker.Length).Trim();
            int semicolon = value.IndexOf(';');
            if (semicolon >= 0)
            {
                value = value.Substring(0, semicolon);
            }

            return value.Trim('"');
        }

        private static void Redirect(HttpListenerContext context, string location)
        {
            context.Response.StatusCode = 303;
            context.Response.RedirectLocation = location;
            context.Response.Close();
        }

        private static void Fail(HttpListenerContext context, int status, string message)
        {
            byte[] payload = Encoding.UTF8.GetBytes("<!DOCTYPE html><meta charset=\"utf-8\"><body style=\"background:#0d1117;color:#e6edf3;font-family:sans-serif;padding:24px\"><h2>" +
                                                    status + "</h2><p>" + Html(message) + "</p><p><a style=\"color:#58a6ff\" href=\"/\">Volver</a></p>");
            context.Response.StatusCode = status;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = payload.Length;
            context.Response.OutputStream.Write(payload, 0, payload.Length);
            context.Response.Close();
        }

        private static string Decode(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (Exception)
            {
                return value;
            }
        }

        private static string UrlEncode(string value)
        {
            return Uri.EscapeDataString(value);
        }

        private static string Html(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string Size(long bytes)
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

        private static string ContentTypeFor(string name)
        {
            string extension = Path.GetExtension(name).ToLowerInvariant();
            switch (extension)
            {
                case ".mp4": return "video/mp4";
                case ".mkv": return "video/x-matroska";
                case ".webm": return "video/webm";
                case ".mp3": return "audio/mpeg";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".pdf": return "application/pdf";
                case ".txt": return "text/plain; charset=utf-8";
                case ".html": return "text/html; charset=utf-8";
                case ".zip": return "application/zip";
                case ".json": return "application/json";
                default: return "application/octet-stream";
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
                // El observador no puede romper el portal.
            }
        }
    }

    /// <summary>
    /// Lector de formularios multipart en flujo continuo. No carga el archivo en memoria:
    /// va escribiendo cada parte en disco mientras busca el delimitador siguiente, que es lo
    /// que permite subir una pelicula de varios gigabytes desde el movil.
    /// </summary>
    internal static class MultipartReader
    {
        private const int BoundaryFound = 1;
        private const int BoundaryFinal = 0;
        private const int StreamEnded = -1;

        public static List<string> Read(Stream rawInput, string boundary, string root)
        {
            List<string> saved = new List<string>();
            Stream input = rawInput is BufferedStream ? rawInput : new BufferedStream(rawInput, 64 * 1024);
            string opening = "--" + boundary;
            byte[] marker = Encoding.ASCII.GetBytes("\r\n--" + boundary);

            // Preambulo: lineas hasta el primer delimitador de apertura.
            string line;
            int guard = 0;
            do
            {
                line = ReadLine(input);
                guard++;
                if (line == null || guard > 1000)
                {
                    return saved;
                }
            }
            while (!line.StartsWith(opening, StringComparison.Ordinal));

            while (true)
            {
                string headers = ReadHeaders(input);
                if (headers == null)
                {
                    break;
                }

                string fileName = ExtractFileName(headers);
                string savedName = null;
                FileStream destination = null;
                if (!string.IsNullOrEmpty(fileName))
                {
                    string unique = FilePortal.UniqueName(root, fileName);
                    savedName = unique;
                    destination = new FileStream(Path.Combine(root, unique), FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
                }

                int outcome;
                try
                {
                    outcome = TransferUntilBoundary(input, marker, destination);
                }
                finally
                {
                    if (destination != null)
                    {
                        destination.Dispose();
                    }
                }

                if (savedName != null)
                {
                    saved.Add(savedName);
                }

                if (outcome != BoundaryFound)
                {
                    break;
                }
            }

            return saved;
        }

        private static string ReadLine(Stream input)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                int value = input.ReadByte();
                if (value < 0)
                {
                    return sb.Length == 0 ? null : sb.ToString();
                }

                if (value == '\n')
                {
                    return sb.ToString().TrimEnd('\r');
                }

                sb.Append((char)value);
                if (sb.Length > 8192)
                {
                    return sb.ToString();
                }
            }
        }

        private static string ReadHeaders(Stream input)
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                string line = ReadLine(input);
                if (line == null)
                {
                    return sb.Length == 0 ? null : sb.ToString();
                }

                if (line.Length == 0)
                {
                    return sb.ToString();
                }

                sb.Append(line).Append('\n');
            }
        }

        private static string ExtractFileName(string headers)
        {
            if (string.IsNullOrEmpty(headers))
            {
                return null;
            }

            const string marker = "filename=";
            int index = headers.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            string value = headers.Substring(index + marker.Length).Trim();
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                value = value.Substring(1);
                int end = value.IndexOf('"');
                if (end >= 0)
                {
                    value = value.Substring(0, end);
                }
            }
            else
            {
                int end = value.IndexOf(';');
                if (end < 0)
                {
                    end = value.IndexOf('\n');
                }

                if (end >= 0)
                {
                    value = value.Substring(0, end);
                }
            }

            return FilePortal.SanitizeName(value);
        }

        /// <summary>
        /// Copia la parte actual al destino hasta encontrar el delimitador. Se compara byte a
        /// byte contra el delimitador y, si la comparacion falla a mitad, los bytes ya
        /// consumidos se escriben como datos: asi un trozo de delimitador nunca se pierde ni
        /// se confunde con el final de una parte.
        /// </summary>
        private static int TransferUntilBoundary(Stream input, byte[] marker, FileStream destination)
        {
            int matched = 0;
            while (true)
            {
                int value = input.ReadByte();
                if (value < 0)
                {
                    if (destination != null && matched > 0)
                    {
                        destination.Write(marker, 0, matched);
                    }

                    return StreamEnded;
                }

                byte current = (byte)value;
                if (current == marker[matched])
                {
                    matched++;
                    if (matched == marker.Length)
                    {
                        // Delimitador completo: "--" cierra el formulario, "\r\n" abre otra parte.
                        int first = input.ReadByte();
                        int second = input.ReadByte();
                        if (first == '-' && second == '-')
                        {
                            return BoundaryFinal;
                        }

                        if (first == '\r' && second == '\n')
                        {
                            return BoundaryFound;
                        }

                        // No era un delimitador real (el estandar exige que tras el
                        // delimitador venga CRLF o "--"): esos bytes son datos del archivo.
                        // Ocurre cuando el contenido casualmente empieza como el delimitador.
                        if (destination != null)
                        {
                            destination.Write(marker, 0, marker.Length);
                            if (first >= 0)
                            {
                                destination.WriteByte((byte)first);
                            }

                            if (second >= 0)
                            {
                                destination.WriteByte((byte)second);
                            }
                        }

                        matched = 0;
                        if (first < 0)
                        {
                            return StreamEnded;
                        }

                        if (second < 0)
                        {
                            return StreamEnded;
                        }

                        continue;
                    }

                    continue;
                }

                if (matched > 0)
                {
                    if (destination != null)
                    {
                        destination.Write(marker, 0, matched);
                    }

                    matched = 0;
                }

                if (current == marker[0])
                {
                    matched = 1;
                }
                else if (destination != null)
                {
                    destination.WriteByte(current);
                }
            }
        }
    }
}
