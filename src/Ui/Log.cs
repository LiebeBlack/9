// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - registro de actividad.
//
// La app es una aplicacion de ventana (sin consola) y tiene prohibido mostrar errores por
// consola: todo va a un buffer en memoria que alimenta el panel de la interfaz y a un
// fichero rotado en %LOCALAPPDATA%, para que un fallo se pueda diagnosticar despues.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetForge.Ui
{
    public sealed class LogEntry
    {
        public LogEntry(DateTime when, string message)
        {
            When = when;
            Message = message;
        }

        public DateTime When { get; private set; }

        public string Message { get; private set; }

        public override string ToString()
        {
            return When.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + Message;
        }
    }

    public static class Log
    {
        private const int MaxEntries = 500;
        private const int MaxFiles = 5;

        private static readonly object Sync = new object();

        // El registro se escribe desde varios hilos (radar, temporizador, interfaz). Dos
        // anadidos simultaneos sobre el mismo fichero se pisarian: este cerrojo los encola.
        private static readonly object FileSync = new object();
        private static readonly LinkedList<LogEntry> Entries = new LinkedList<LogEntry>();
        private static string _directory;
        private static string _file;
        private static DateTime _fileDay = DateTime.MinValue;

        public static event EventHandler Changed;

        public static int Count
        {
            get
            {
                lock (Sync)
                {
                    return Entries.Count;
                }
            }
        }

        public static void Write(string message)
        {
            if (message == null)
            {
                message = string.Empty;
            }

            LogEntry entry = new LogEntry(DateTime.Now, message);
            lock (Sync)
            {
                Entries.AddLast(entry);
                while (Entries.Count > MaxEntries)
                {
                    Entries.RemoveFirst();
                }
            }

            AppendToFile(entry);
            EventHandler handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(null, EventArgs.Empty);
                }
                catch (Exception)
                {
                    // El observador no puede romper el registro.
                }
            }
        }

        public static void Write(string message, Exception error)
        {
            if (error == null)
            {
                Write(message);
                return;
            }

            Write(message + " -> " + error.GetType().Name + ": " + error.Message);
            if (!string.IsNullOrEmpty(error.StackTrace))
            {
                Write("    " + error.StackTrace.Replace(Environment.NewLine, Environment.NewLine + "    ").TrimEnd());
            }
        }

        public static IList<LogEntry> Snapshot()
        {
            lock (Sync)
            {
                return new List<LogEntry>(Entries);
            }
        }

        public static string Dump()
        {
            StringBuilder sb = new StringBuilder();
            foreach (LogEntry entry in Snapshot())
            {
                sb.AppendLine(entry.When.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + entry.Message);
            }

            return sb.ToString();
        }

        public static string FilePath
        {
            get
            {
                EnsureFile();
                return _file ?? string.Empty;
            }
        }

        public static void OpenFolder()
        {
            string directory = DirectoryPath();
            if (directory == null)
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + directory + "\"");
            }
            catch (Exception ex)
            {
                Write("No se pudo abrir la carpeta de registros: " + ex.Message);
            }
        }

        private static void AppendToFile(LogEntry entry)
        {
            try
            {
                lock (FileSync)
                {
                    EnsureFile();
                    if (_file == null)
                    {
                        return;
                    }

                    File.AppendAllText(_file,
                        entry.When.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + entry.Message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // Si el disco no deja escribir, la app sigue funcionando: nunca se cae por el log.
            }
        }

        private static void EnsureFile()
        {
            lock (FileSync)
            {
                if (_file != null && _fileDay == DateTime.Today)
                {
                    return;
                }

                string directory = DirectoryPath();
                if (directory == null)
                {
                    return;
                }

                try
                {
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    _fileDay = DateTime.Today;
                    _file = Path.Combine(directory, "netforge-" + _fileDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                    Rotate(directory);
                }
                catch (Exception)
                {
                    _file = null;
                }
            }
        }

        private static void Rotate(string directory)
        {
            try
            {
                string[] files = Directory.GetFiles(directory, "netforge-*.log");
                if (files.Length <= MaxFiles)
                {
                    return;
                }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length - MaxFiles; i++)
                {
                    File.Delete(files[i]);
                }
            }
            catch (Exception)
            {
                // La rotacion es un lujo, no un requisito.
            }
        }

        private static string DirectoryPath()
        {
            if (_directory != null)
            {
                return _directory;
            }

            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(root))
                {
                    return null;
                }

                _directory = Path.Combine(root, "NetForge", "logs");
            }
            catch (Exception)
            {
                return null;
            }

            return _directory;
        }
    }
}
