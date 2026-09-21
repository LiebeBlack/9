// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - sincronizacion de operaciones WinRT sin depender del ensamblado
// union "Windows.winmd" (que no esta presente en Windows 10/11: la metadata viene
// dividida por espacios de nombres en %windir%\System32\WinMetadata).
//
// System.Runtime.WindowsRuntime.dll expone AsTask(), pero su firma referencia tipos
// del ensamblado "Windows", asi que no puede compilarse sin la metadata union.
// Este helper usa el contrato IAsyncOperation/IAsyncAction directamente.
using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Windows.Foundation;

namespace NetForge.Native
{
    /// <summary>Puente sincrono (se ejecuta en un hilo de trabajo) para las API WinRT.</summary>
    public static class WinRtAsync
    {
        public const int DefaultTimeoutMs = 20000;

        /// <summary>Espera una operacion con resultado. Lanza la excepcion de la operacion si falla.</summary>
        public static T Wait<T>(IAsyncOperation<T> operation, int timeoutMs = DefaultTimeoutMs)
        {
            if (operation == null)
            {
                throw new ArgumentNullException("operation");
            }

            using (ManualResetEventSlim done = new ManualResetEventSlim(false))
            {
                T result = default(T);
                Exception failure = null;
                bool finished = false;

                operation.Completed = delegate(IAsyncOperation<T> info, AsyncStatus status)
                {
                    if (status == AsyncStatus.Completed)
                    {
                        try
                        {
                            result = info.GetResults();
                        }
                        catch (Exception ex)
                        {
                            failure = ex;
                        }
                    }
                    else if (status == AsyncStatus.Error)
                    {
                        failure = SafeError(info);
                    }
                    else
                    {
                        failure = new OperationCanceledException("Operacion WinRT cancelada (estado " + status + ").");
                    }

                    finished = true;
                    done.Set();
                };

                if (!done.Wait(timeoutMs))
                {
                    throw new TimeoutException("Operacion WinRT sin respuesta en " + timeoutMs + " ms.");
                }

                if (failure != null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                if (!finished)
                {
                    throw new InvalidOperationException("Estado inconsistente en la operacion WinRT.");
                }

                return result;
            }
        }

        /// <summary>Espera una operacion sin resultado (IAsyncAction).</summary>
        public static void Wait(IAsyncAction action, int timeoutMs = DefaultTimeoutMs)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            using (ManualResetEventSlim done = new ManualResetEventSlim(false))
            {
                Exception failure = null;

                action.Completed = delegate(IAsyncAction info, AsyncStatus status)
                {
                    if (status == AsyncStatus.Error)
                    {
                        failure = SafeError(info);
                    }
                    else if (status != AsyncStatus.Completed)
                    {
                        failure = new OperationCanceledException("Accion WinRT cancelada (estado " + status + ").");
                    }

                    done.Set();
                };

                if (!done.Wait(timeoutMs))
                {
                    throw new TimeoutException("Accion WinRT sin respuesta en " + timeoutMs + " ms.");
                }

                if (failure != null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }
        }

        private static Exception SafeError(object info)
        {
            try
            {
                Exception ex = ((IAsyncInfo)info).ErrorCode;
                if (ex != null)
                {
                    return new InvalidOperationException("WinRT: " + ex.Message, ex);
                }
            }
            catch (Exception)
            {
                // La proyeccion no siempre expone ErrorCode en el estado de error.
            }

            return new InvalidOperationException("Operacion WinRT fallida sin codigo de error expuesto.");
        }
    }
}
