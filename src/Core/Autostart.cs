// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - arranque automatico.
//
// No se usa la clave Run del registro a proposito: como la app necesita permisos de
// administrador, el arranque del registro dispararia un aviso de UAC en cada inicio de
// sesion. Una tarea programada con el nivel mas alto arranca sin preguntar.
using System;
using System.Diagnostics;
using System.IO;
using NetForge.Ui;

namespace NetForge.Core
{
    /// <summary>Estado de la tarea de arranque, sin depender del idioma de Windows.</summary>
    public enum AutostartState
    {
        /// <summary>La tarea no existe.</summary>
        Missing,

        /// <summary>Existe y apunta a este ejecutable.</summary>
        Current,

        /// <summary>Existe pero apunta a otra ruta (la aplicacion se movio o se renombro).</summary>
        OtherPath,

        /// <summary>Existe pero no se pudo leer, o Windows no la deja consultar.</summary>
        Unknown
    }

    public static class Autostart
    {
        public const string TaskName = "NetForge Studio";

        public static bool IsEnabled()
        {
            ShellResult result = ShellRunner.Run("schtasks", "/query /tn " + ShellRunner.Quote(TaskName), 15000);
            return result.Ok;
        }

        /// <summary>
        /// Estado real de la tarea. Se lee el XML de la tarea y no el informe de texto: el texto
        /// de schtasks esta traducido al idioma de Windows y las etiquetas XML no, asi que es la
        /// unica forma de que esto funcione en un Windows en espanol, en ingles o en japones.
        /// </summary>
        public static AutostartState Status(out string recordedCommand)
        {
            recordedCommand = string.Empty;
            ShellResult result = ShellRunner.Run("schtasks", "/query /tn " + ShellRunner.Quote(TaskName) + " /xml", 15000);
            if (!result.Ok)
            {
                // Sin tarea (o sin permiso para consultarla) no hay nada que reparar.
                return AutostartState.Missing;
            }

            string command;
            bool highestAvailable;
            if (!ParseTaskXml(result.Combined, out command, out highestAvailable) || string.IsNullOrEmpty(command))
            {
                return AutostartState.Unknown;
            }

            recordedCommand = command;
            string current = ExecutablePath();
            if (string.IsNullOrEmpty(current))
            {
                return AutostartState.Unknown;
            }

            return SamePath(command, current) ? AutostartState.Current : AutostartState.OtherPath;
        }

        /// <summary>
        /// Saca el ejecutable y el nivel de privilegios del XML de la tarea. Publico y puro para
        /// poder probarlo con XML de ejemplo, sin tocar el Programador de tareas de nadie.
        /// </summary>
        public static bool ParseTaskXml(string xml, out string command, out bool highestAvailable)
        {
            command = string.Empty;
            highestAvailable = false;
            if (string.IsNullOrEmpty(xml))
            {
                return false;
            }

            command = ReadElement(xml, "Command");
            string runLevel = ReadElement(xml, "RunLevel");
            highestAvailable = string.Equals(runLevel, "HighestAvailable", StringComparison.OrdinalIgnoreCase);
            return command.Length > 0;
        }

        private static string ReadElement(string xml, string name)
        {
            string open = "<" + name + ">";
            string close = "</" + name + ">";
            int start = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return string.Empty;
            }

            start += open.Length;
            int end = xml.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return string.Empty;
            }

            return xml.Substring(start, end - start).Trim();
        }

        /// <summary>
        /// Compara la ruta guardada en la tarea con la del ejecutable actual. No se usa
        /// Path.GetFullPath porque la ruta guardada puede venir entre comillas o con barras
        /// invertidas dobles, y una excepcion aqui no puede impedir arrancar.
        /// </summary>
        private static bool SamePath(string left, string right)
        {
            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        }

        public static bool Enable(out string error)
        {
            error = string.Empty;
            string executable = ExecutablePath();
            if (string.IsNullOrEmpty(executable))
            {
                error = "no se pudo determinar la ruta del ejecutable";
                return false;
            }

            string arguments = "/create /tn " + ShellRunner.Quote(TaskName) +
                               " /tr " + ShellRunner.Quote("\"" + executable + "\" /silent") +
                               " /sc onlogon /rl highest /f";
            ShellResult result = ShellRunner.Run("schtasks", arguments, 30000);
            if (!result.Ok)
            {
                error = "schtasks devolvio " + result.ExitCode + ": " + Shorten(result.Combined);
                Log.Write("Arranque automatico: " + error);
                return false;
            }

            Log.Write("Arranque automatico activado (tarea programada con privilegios altos)");
            return true;
        }

        public static bool Disable(out string error)
        {
            error = string.Empty;
            ShellResult result = ShellRunner.Run("schtasks", "/delete /tn " + ShellRunner.Quote(TaskName) + " /f", 20000);
            if (!result.Ok && !LooksMissing(result))
            {
                error = "schtasks devolvio " + result.ExitCode + ": " + Shorten(result.Combined);
                Log.Write("Arranque automatico: " + error);
                return false;
            }

            Log.Write("Arranque automatico desactivado");
            return true;
        }

        private static bool LooksMissing(ShellResult result)
        {
            // Borrar algo que no existe no es un error para el usuario. Se comprueba con una
            // consulta posterior en vez de leer el mensaje traducido de schtasks.
            ShellResult query = ShellRunner.Run("schtasks", "/query /tn " + ShellRunner.Quote(TaskName), 15000);
            return !query.Ok;
        }

        private static string ExecutablePath()
        {
            try
            {
                Process process = Process.GetCurrentProcess();
                string path = process.MainModule == null ? null : process.MainModule.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception)
            {
                // Sin acceso al modulo principal se cae al ensamblado.
            }

            try
            {
                return System.Reflection.Assembly.GetExecutingAssembly().Location;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string single = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return single.Length > 200 ? single.Substring(0, 200) + "..." : single;
        }
    }
}
