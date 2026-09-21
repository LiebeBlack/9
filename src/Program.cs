// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - punto de entrada.
//
// Es una aplicacion de ventana: no hay consola y por tanto no hay "errores en consola".
// Todo se registra en fichero y ningun fallo debe tumbar la red.
using System;
using System.Threading;
using System.Windows.Forms;
using NetForge.App;
using NetForge.Ui;

namespace NetForge
{
    internal static class Program
    {
        private const string InstanceName = @"Local\NetForgeStudio.Instance";
        private const string ShowSignalName = @"Local\NetForgeStudio.Show";

        [STAThread]
        private static int Main(string[] args)
        {
            if (HasFlag(args, "/self-test") || HasFlag(args, "--self-test"))
            {
                return SelfTest.Run();
            }

            bool silent = HasFlag(args, "/silent") || HasFlag(args, "--silent");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            bool createdNew;
            using (Mutex instance = new Mutex(true, InstanceName, out createdNew))
            {
                if (!createdNew)
                {
                    // Ya hay una instancia: se le pide que muestre el panel y esta se retira.
                    try
                    {
                        using (EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowSignalName))
                        {
                            existing.Set();
                        }
                    }
                    catch (Exception)
                    {
                        // Si la otra instancia esta cerrando, no hay nada que avisar.
                    }

                    return 0;
                }

                using (EventWaitHandle showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName))
                using (NetForgeContext context = new NetForgeContext(silent, showSignal))
                {
                    try
                    {
                        Application.Run(context);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Fallo al ejecutar la aplicacion", ex);
                        return 1;
                    }
                }
            }

            return 0;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null)
            {
                return false;
            }

            foreach (string argument in args)
            {
                if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Log.Write("Excepcion no controlada en la interfaz", e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Write("Excepcion no controlada", e.ExceptionObject as Exception);
        }
    }
}
