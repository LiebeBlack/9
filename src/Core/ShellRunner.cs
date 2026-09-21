// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - ejecucion de herramientas del sistema (netsh, schtasks, net, tracert).
// Regla del proyecto: se lee el CODIGO DE SALIDA, nunca el texto, porque el texto sale
// traducido en cada Windows. La salida solo se guarda para el log.
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace NetForge.Core
{
    public sealed class ShellResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; }
        public string StdErr { get; set; }
        public string CommandLine { get; set; }
        public bool TimedOut { get; set; }

        public bool Ok
        {
            get { return !TimedOut && ExitCode == 0; }
        }

        public string Combined
        {
            get
            {
                string output = (StdOut ?? string.Empty).Trim();
                string errors = (StdErr ?? string.Empty).Trim();
                if (errors.Length == 0)
                {
                    return output;
                }

                if (output.Length == 0)
                {
                    return errors;
                }

                return output + Environment.NewLine + errors;
            }
        }

        public override string ToString()
        {
            return (CommandLine ?? "comando") + " -> " + ExitCode + (TimedOut ? " (timeout)" : string.Empty);
        }
    }

    public static class ShellRunner
    {
        public static ShellResult Run(string fileName, string arguments, int timeoutMs = 20000)
        {
            ShellResult result = new ShellResult
            {
                CommandLine = fileName + " " + arguments,
                StdOut = string.Empty,
                StdErr = string.Empty,
                ExitCode = -1
            };

            ProcessStartInfo info = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                using (Process process = Process.Start(info))
                {
                    if (process == null)
                    {
                        result.StdErr = "no se pudo iniciar el proceso";
                        return result;
                    }

                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        result.TimedOut = true;
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // Si ya murio, no hay nada que hacer.
                        }

                        process.WaitForExit(2000);
                    }

                    try
                    {
                        result.StdOut = stdout.Wait(3000) ? stdout.Result : string.Empty;
                        result.StdErr = stderr.Wait(3000) ? stderr.Result : string.Empty;
                    }
                    catch (AggregateException)
                    {
                        // Lectura interrumpida por la muerte del proceso: basta el codigo.
                    }

                    result.ExitCode = process.HasExited ? process.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                result.StdErr = ex.Message;
                result.ExitCode = -1;
            }

            return result;
        }

        /// <summary>Comillas para argumentos de netsh/schtasks (CreateProcess no interpreta el shell).</summary>
        public static string Quote(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
