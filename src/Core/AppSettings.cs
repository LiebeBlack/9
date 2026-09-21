// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - preferencias del usuario.
//
// Formato de texto plano y legible (clave=valor) para que se pueda revisar y corregir a
// mano si algo sale mal, sin depender de un serializador concreto.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NetForge.Core
{
    public sealed class AppSettings
    {
        private const string FileName = "settings.txt";

        public string Ssid { get; set; }
        public string Passphrase { get; set; }
        public string Folder { get; set; }
        public string FtpUser { get; set; }
        public bool ShareFolderUploads { get; set; }
        public bool ShareFolderDelete { get; set; }
        public bool Autostart { get; set; }
        public bool StartMinimized { get; set; }
        public EnginePreference Engine { get; set; }

        /// <summary>Entrada del usuario: vacio = seguir el idioma de Windows.</summary>
        public string Language { get; set; }

        /// <summary>Bandas preferidas del adaptador virtual. 2,4 GHz llega mas lejos; 5 GHz va mas rapido.</summary>
        public bool PreferTwoGhz { get; set; }

        /// <summary>Cada cuanto audita la red el radar (1 a 30 segundos).</summary>
        public int RadarSeconds { get; set; }

        /// <summary>Cada cuanto comprueba la red el Daemon Guard (1 a 30 segundos).</summary>
        public int GuardSeconds { get; set; }

        public int PortalPort { get; set; }

        public int FtpPort { get; set; }

        /// <summary>
        /// Levantar los servidores de archivos en cuanto la red esta en pie. Es la promesa de
        /// "1-Click FTP": crear la red y poder pasar archivos sin un segundo paso.
        /// </summary>
        public bool AutoStartServers { get; set; }

        /// <summary>Avisos emergentes de Windows (bandeja) para lo que pasa sin la ventana abierta.</summary>
        public bool Notifications { get; set; }

        /// <summary>Avisar cuando aparece un dispositivo nuevo en la red local.</summary>
        public bool NotifyOnJoin { get; set; }

        public AppSettings()
        {
            Ssid = "NetForge";
            Passphrase = string.Empty;
            Folder = string.Empty;
            FtpUser = "netforge";
            ShareFolderUploads = true;
            ShareFolderDelete = false;
            Autostart = false;
            StartMinimized = false;
            Engine = EnginePreference.Auto;
            Language = string.Empty;
            PreferTwoGhz = true;
            RadarSeconds = 3;
            GuardSeconds = 2;
            PortalPort = 8080;
            FtpPort = 2121;
            AutoStartServers = true;
            Notifications = true;
            NotifyOnJoin = true;
        }

        public const int MinRadarSeconds = 1;
        public const int MaxRadarSeconds = 30;
        public const int MinGuardSeconds = 1;
        public const int MaxGuardSeconds = 30;
        public const int MinPort = 1024;
        public const int MaxPort = 65535;

        /// <summary>
        /// Deja los valores dentro de rangos usables. Un fichero de preferencias editado a mano
        /// (o una version anterior) no puede dejar la aplicacion auditando la red cada cero
        /// segundos ni escuchando en un puerto reservado.
        /// </summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(Ssid))
            {
                Ssid = "NetForge";
            }

            // Un nombre de usuario vacio es una decision, no un descuido: significa FTP en modo
            // anonimo, util en una red local de confianza. Por eso no se rellena aqui a la
            // fuerza: hacerlo haria el modo anonimo inalcanzable desde la interfaz.
            Language = (Language == "es" || Language == "en") ? Language : string.Empty;

            // El usuario del FTP viaja dentro del protocolo y ahora se puede escribir a mano:
            // un espacio o un caracter de control partiria la linea "USER" y el cliente no
            // podria entrar. Se queda en letras, cifras y los separadores habituales.
            FtpUser = SanitizeUserName(FtpUser);
            RadarSeconds = Clamp(RadarSeconds, MinRadarSeconds, MaxRadarSeconds);
            GuardSeconds = Clamp(GuardSeconds, MinGuardSeconds, MaxGuardSeconds);
            PortalPort = Clamp(PortalPort, MinPort, MaxPort);
            FtpPort = Clamp(FtpPort, MinPort, MaxPort);

            int preference = (int)Engine;
            if (preference < 0 || preference > 3)
            {
                Engine = EnginePreference.Auto;
            }
        }

        /// <summary>
        /// Nombre de usuario utilizable en FTP y en la URL del portal. Devuelve cadena vacia si
        /// no queda nada aprovechable: eso significa modo anonimo, no un valor por defecto
        /// disfrazado (rellenar "netforge" a la fuerza impediria elegir el modo anonimo).
        /// </summary>
        public static string SanitizeUserName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                bool allowed = (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                    || character == '.' || character == '-' || character == '_';
                if (allowed && sb.Length < 32)
                {
                    sb.Append(character);
                }
            }

            return sb.Length == 0 ? string.Empty : sb.ToString();
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }

        /// <summary>
        /// Carpeta alternativa para el fichero de preferencias. La usa la autocomprobacion para
        /// probar guardar y cargar de verdad sin tocar las preferencias de quien ejecuta el test.
        /// </summary>
        public static string OverrideStoreFolder { get; set; }

        public static string StorePath
        {
            get
            {
                try
                {
                    if (!string.IsNullOrEmpty(OverrideStoreFolder))
                    {
                        if (!Directory.Exists(OverrideStoreFolder))
                        {
                            Directory.CreateDirectory(OverrideStoreFolder);
                        }

                        return Path.Combine(OverrideStoreFolder, FileName);
                    }

                    string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (string.IsNullOrEmpty(root))
                    {
                        return string.Empty;
                    }

                    string directory = Path.Combine(root, "NetForge");
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    return Path.Combine(directory, FileName);
                }
                catch (Exception)
                {
                    return string.Empty;
                }
            }
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            try
            {
                string path = StorePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    settings.Passphrase = GeneratePassphrase();
                    settings.Folder = DefaultFolder();
                    return settings;
                }

                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    int number;
                    switch (key)
                    {
                        case "ssid":
                            if (value.Length > 0)
                            {
                                settings.Ssid = value;
                            }

                            break;
                        case "passphrase":
                            settings.Passphrase = value;
                            break;
                        case "folder":
                            settings.Folder = value;
                            break;
                        case "ftpuser":
                            settings.FtpUser = value;
                            break;
                        case "upload":
                            settings.ShareFolderUploads = value == "1";
                            break;
                        case "delete":
                            settings.ShareFolderDelete = value == "1";
                            break;
                        case "autostart":
                            settings.Autostart = value == "1";
                            break;
                        case "minimized":
                            settings.StartMinimized = value == "1";
                            break;
                        case "engine":
                            int preference;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out preference) &&
                                preference >= 0 && preference <= 3)
                            {
                                settings.Engine = (EnginePreference)preference;
                            }

                            break;
                        case "language":
                            settings.Language = (value == "es" || value == "en") ? value : string.Empty;
                            break;
                        case "band":
                            settings.PreferTwoGhz = value != "5";
                            break;
                        case "radar":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                            {
                                settings.RadarSeconds = number;
                            }

                            break;
                        case "guard":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                            {
                                settings.GuardSeconds = number;
                            }

                            break;
                        case "portalport":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                            {
                                settings.PortalPort = number;
                            }

                            break;
                        case "ftpport":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                            {
                                settings.FtpPort = number;
                            }

                            break;
                        case "filesauto":
                            settings.AutoStartServers = value == "1";
                            break;
                        case "notify":
                            settings.Notifications = value == "1";
                            break;
                        case "notifyjoin":
                            settings.NotifyOnJoin = value == "1";
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Ui.Log.Write("No se pudieron leer las preferencias: " + ex.Message);
            }

            if (string.IsNullOrEmpty(settings.Passphrase))
            {
                settings.Passphrase = GeneratePassphrase();
            }

            if (string.IsNullOrEmpty(settings.Folder))
            {
                settings.Folder = DefaultFolder();
            }

            settings.Normalize();
            return settings;
        }

        public void Save()
        {
            Normalize();
            try
            {
                string path = StorePath;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# NetForge Studio - preferencias (editable a mano)");
                sb.AppendLine("ssid=" + Clean(Ssid));
                sb.AppendLine("passphrase=" + Clean(Passphrase));
                sb.AppendLine("folder=" + Clean(Folder));
                sb.AppendLine("ftpuser=" + Clean(FtpUser));
                sb.AppendLine("upload=" + (ShareFolderUploads ? "1" : "0"));
                sb.AppendLine("delete=" + (ShareFolderDelete ? "1" : "0"));
                sb.AppendLine("autostart=" + (Autostart ? "1" : "0"));
                sb.AppendLine("minimized=" + (StartMinimized ? "1" : "0"));
                sb.AppendLine("engine=" + ((int)Engine).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("language=" + Clean(Language));
                sb.AppendLine("band=" + (PreferTwoGhz ? "24" : "5"));
                sb.AppendLine("radar=" + RadarSeconds.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("guard=" + GuardSeconds.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("portalport=" + PortalPort.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("ftpport=" + FtpPort.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("filesauto=" + (AutoStartServers ? "1" : "0"));
                sb.AppendLine("notify=" + (Notifications ? "1" : "0"));
                sb.AppendLine("notifyjoin=" + (NotifyOnJoin ? "1" : "0"));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Ui.Log.Write("No se pudieron guardar las preferencias: " + ex.Message);
            }
        }

        /// <summary>
        /// Clave aleatoria de arranque. Se genera con el generador criptografico del sistema:
        /// una clave por defecto del tipo "12345678" convertiria la red en publica de hecho.
        /// </summary>
        public static string GeneratePassphrase()
        {
            const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            byte[] bytes = new byte[14];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            StringBuilder sb = new StringBuilder(bytes.Length);
            foreach (byte value in bytes)
            {
                sb.Append(alphabet[value % alphabet.Length]);
            }

            return sb.ToString();
        }

        private static string DefaultFolder()
        {
            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(documents) && Directory.Exists(documents))
                {
                    return documents;
                }

                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
                {
                    return desktop;
                }
            }
            catch (Exception)
            {
                // Sin carpeta por defecto: el usuario elige una.
            }

            return Environment.CurrentDirectory;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        }
    }
}
