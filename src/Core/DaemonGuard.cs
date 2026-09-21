// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - Daemon Guard: auto-resurreccion del punto de acceso.
//
// Vigila la red cada pocos segundos y la levanta otra vez si Windows la deja caer (driver
// reiniciado, adaptador suspendido, reanudacion tras suspension). Tres reglas que marcan la
// diferencia entre "funciona" y "funciona siempre":
//   1. Un tick nunca se solapa con otro (si el reinicio tarda mas que el intervalo).
//   2. Un Stop manual del usuario desarma el guard: no puede "resucitar" lo que se apago a proposito.
//   3. Tras varios fallos seguidos se aplica espera creciente, para no entrar en tormenta de reinicios.
using System;
using System.Threading;
using Microsoft.Win32;
using NetForge.Ui;

namespace NetForge.Core
{
    public sealed class GuardEvent : EventArgs
    {
        public GuardEvent(string message, int attempt, HotspotStatus status)
        {
            Message = message;
            Attempt = attempt;
            Status = status;
        }

        public string Message { get; private set; }

        public int Attempt { get; private set; }

        public HotspotStatus Status { get; private set; }
    }

    public sealed class DaemonGuard : IDisposable
    {
        private const int BaseBackoffMs = 2000;
        private const int MaxBackoffMs = 30000;

        private readonly HotspotController _controller;
        private readonly object _sync = new object();
        private Timer _timer;
        private int _busy;
        private int _armed;
        private int _failures;
        private int _backoffMs = BaseBackoffMs;
        private DateTime _nextAttemptUtc = DateTime.MinValue;
        private bool _hooked;
        private bool _disposed;

        public DaemonGuard(HotspotController controller)
        {
            if (controller == null)
            {
                throw new ArgumentNullException("controller");
            }

            _controller = controller;
            IntervalMs = 2000;
            MaxConsecutiveFailures = 5;
        }

        public event EventHandler<GuardEvent> Resurrected;

        public event EventHandler<GuardEvent> GaveUp;

        public event EventHandler<GuardEvent> Noticed;

        public int IntervalMs { get; set; }

        public int MaxConsecutiveFailures { get; set; }

        public int TotalResurrections { get; private set; }

        public string LastMessage { get; private set; }

        public bool Armed
        {
            get { return Thread.VolatileRead(ref _armed) == 1; }
        }

        public void Arm()
        {
            if (_disposed)
            {
                return;
            }

            lock (_sync)
            {
                if (Armed)
                {
                    return;
                }

                Thread.VolatileWrite(ref _armed, 1);
                _failures = 0;
                _backoffMs = BaseBackoffMs;
                _nextAttemptUtc = DateTime.MinValue;
                _timer = new Timer(OnTimer, null, IntervalMs, IntervalMs);
                HookSystemEvents();
                LastMessage = "vigilancia activa";
            }
        }

        public void Disarm()
        {
            lock (_sync)
            {
                if (!Armed)
                {
                    return;
                }

                Thread.VolatileWrite(ref _armed, 0);
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }

                UnhookSystemEvents();
                LastMessage = "vigilancia detenida por el usuario";
            }
        }

        /// <summary>Comprobacion inmediata (se usa al reanudar el sistema o al cambiar la red).</summary>
        public void Poke()
        {
            Tick();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Disarm();
            GC.SuppressFinalize(this);
        }

        private void OnTimer(object state)
        {
            Tick();
        }

        private void Tick()
        {
            if (!Armed)
            {
                return;
            }

            // Regla 1: si el tick anterior sigue trabajando, este se descarta.
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                return;
            }

            try
            {
                if (DateTime.UtcNow < _nextAttemptUtc)
                {
                    return;
                }

                HotspotStatus status = _controller.Probe();
                if (status != null && status.State == EngineState.On)
                {
                    if (_failures > 0)
                    {
                        LastMessage = "la red respondio de nuevo por si sola";
                    }

                    _failures = 0;
                    _backoffMs = BaseBackoffMs;
                    return;
                }

                string reason = status == null ? "estado desconocido" : status.LastError;
                if (string.IsNullOrEmpty(reason))
                {
                    reason = "la red dejo de estar activa";
                }

                Raise(Noticed, "caida detectada: " + reason, _failures + 1, status);

                try
                {
                    if (!_controller.Restart())
                    {
                        LastMessage = "no hay red que reiniciar (motor detenido)";
                        return;
                    }

                    TotalResurrections++;
                    _failures = 0;
                    _backoffMs = BaseBackoffMs;
                    LastMessage = "red restaurada (resurreccion " + TotalResurrections + ")";
                    Raise(Resurrected, LastMessage, 0, _controller.Status);
                }
                catch (Exception ex)
                {
                    _failures++;
                    _backoffMs = Math.Min(MaxBackoffMs, _backoffMs * 2);
                    _nextAttemptUtc = DateTime.UtcNow.AddMilliseconds(_backoffMs);
                    LastMessage = "intento " + _failures + " fallido (" + ex.Message + "); reintento en " + (_backoffMs / 1000.0).ToString("0.0") + " s";
                    Raise(Noticed, LastMessage, _failures, _controller.Status);

                    if (_failures >= MaxConsecutiveFailures)
                    {
                        LastMessage = "desisto tras " + _failures + " intentos: " + ex.Message;
                        Raise(GaveUp, LastMessage, _failures, _controller.Status);
                    }
                }
            }
            catch (Exception ex)
            {
                LastMessage = "error en la vigilancia: " + ex.Message;
                Log.Write("DaemonGuard: " + ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private void HookSystemEvents()
        {
            if (_hooked)
            {
                return;
            }

            try
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
                _hooked = true;
            }
            catch (Exception ex)
            {
                Log.Write("DaemonGuard: no se pudieron enganchar los avisos del sistema: " + ex.Message);
            }
        }

        private void UnhookSystemEvents()
        {
            if (!_hooked)
            {
                return;
            }

            try
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            }
            catch (Exception)
            {
                // Al soltar, cualquier fallo es irrelevante.
            }

            _hooked = false;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                Poke();
            }
        }

        private void OnNetworkAvailabilityChanged(object sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
        {
            Poke();
        }

        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            Poke();
        }

        private void Raise(EventHandler<GuardEvent> handler, string message, int attempt, HotspotStatus status)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, new GuardEvent(message, attempt, status));
            }
            catch (Exception)
            {
                // El observador no puede romper la vigilancia.
            }
        }
    }
}
