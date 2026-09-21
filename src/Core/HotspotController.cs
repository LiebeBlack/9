// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - controlador de la red. Encapsula la parte que hace universal a la app:
// mirar que puede hacer esta maquina, ordenar los motores segun lo que el usuario quiere
// (off-grid o compartir internet) y probarlos en cadena hasta que uno levante la red.
using System;
using System.Collections.Generic;
using System.Text;
using NetForge.Core.Engines;

namespace NetForge.Core
{
    public enum EnginePreference
    {
        /// <summary>Automatico: off-grid si no hay internet, punto de acceso movil si lo hay.</summary>
        Auto = 0,

        /// <summary>Fuerza la via que funciona sin conexion (Wi-Fi Direct, despues legacy).</summary>
        OffGrid = 1,

        /// <summary>Prefiere el motor que Windows sabe enrutar para compartir internet.</summary>
        Sharing = 2,

        /// <summary>Fuerza la red hospedada clasica.</summary>
        Legacy = 3
    }

    public sealed class HotspotController : IDisposable
    {
        private readonly MachineProfile _profile;
        private readonly List<IHotspotEngine> _created = new List<IHotspotEngine>();
        private HotspotConfig _config;
        private bool _disposed;

        public HotspotController(MachineProfile profile)
        {
            _profile = profile;
            Preference = EnginePreference.Auto;
            LastError = string.Empty;
            Selection = "(sin red)";
        }

        /// <summary>Constructor para pruebas: permite inyectar motores falsos sin tocar hardware.</summary>
        public HotspotController(MachineProfile profile, IEnumerable<IHotspotEngine> preset)
            : this(profile)
        {
            if (preset == null)
            {
                return;
            }

            foreach (IHotspotEngine engine in preset)
            {
                if (engine != null)
                {
                    _created.Add(engine);
                }
            }
        }

        public event EventHandler Changed;

        public EnginePreference Preference { get; set; }

        public IHotspotEngine Engine { get; private set; }

        public HotspotStatus Status { get; private set; }

        public string Selection { get; private set; }

        public string LastError { get; private set; }

        public HotspotConfig Config
        {
            get { return _config; }
        }

        public bool IsRunning
        {
            get
            {
                HotspotStatus status = Status;
                return status != null && status.State == EngineState.On;
            }
        }

        /// <summary>
        /// Levanta la red probando los motores en orden. Devuelve false solo si todos fallan,
        /// y en ese caso LastError explica que dijo cada uno: nunca se falla en silencio.
        /// </summary>
        public bool Start(HotspotConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            config.Validate();
            Stop();

            _config = config.Clone();
            StringBuilder attempted = new StringBuilder();
            bool anyTried = false;

            foreach (IHotspotEngine candidate in Candidates())
            {
                if (!candidate.IsAvailable)
                {
                    attempted.AppendLine("  - " + candidate.DisplayName + ": no disponible (" + candidate.UnavailableReason + ")");
                    continue;
                }

                anyTried = true;
                try
                {
                    Status = candidate.Start(_config);
                    Engine = candidate;
                    LastError = string.Empty;
                    Selection = candidate.DisplayName + " en " + Status.Endpoint;
                    Raise();
                    return true;
                }
                catch (Exception ex)
                {
                    attempted.AppendLine("  - " + candidate.DisplayName + ": " + ex.Message);
                }
            }

            LastError = anyTried
                ? "ningun motor pudo levantar la red:" + Environment.NewLine + attempted.ToString().TrimEnd()
                : "no hay ningun motor utilizable en este equipo:" + Environment.NewLine + attempted.ToString().TrimEnd();
            Status = new HotspotStatus { State = EngineState.Faulted, LastError = LastError, Ssid = config.Ssid };
            Selection = "(sin red)";
            Raise();
            return false;
        }

        public void Stop()
        {
            if (Engine != null)
            {
                try
                {
                    Engine.Stop();
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }

                Engine = null;
            }

            Status = new HotspotStatus { State = EngineState.Idle };
            Selection = "(sin red)";
            Raise();
        }

        /// <summary>Reinicio silencioso solicitado por el Daemon Guard.</summary>
        public bool Restart()
        {
            if (Engine == null || _config == null)
            {
                return false;
            }

            IHotspotEngine engine = Engine;
            Status = engine.Start(_config);
            Status.Restarts = Status.Restarts + 1;
            LastError = string.Empty;
            Selection = engine.DisplayName + " en " + Status.Endpoint;
            Raise();
            return true;
        }

        /// <summary>Consulta viva del estado en el sistema operativo.</summary>
        public HotspotStatus Probe()
        {
            if (Engine == null)
            {
                return Status ?? new HotspotStatus { State = EngineState.Idle };
            }

            Status = Engine.GetStatus();
            return Status;
        }

        /// <summary>Motores disponibles en esta maquina, en el orden que corresponde a la preferencia.</summary>
        public IList<IHotspotEngine> Candidates()
        {
            IHotspotEngine direct = Get(EngineKind.WiFiDirectGo);
            IHotspotEngine hotspot = Get(EngineKind.MobileHotspot);
            IHotspotEngine legacy = Get(EngineKind.LegacyHostedNetwork);

            List<IHotspotEngine> ordered = new List<IHotspotEngine>();
            switch (Preference)
            {
                case EnginePreference.OffGrid:
                    ordered.Add(direct);
                    ordered.Add(legacy);
                    ordered.Add(hotspot);
                    break;

                case EnginePreference.Sharing:
                    ordered.Add(hotspot);
                    ordered.Add(direct);
                    ordered.Add(legacy);
                    break;

                case EnginePreference.Legacy:
                    ordered.Add(legacy);
                    ordered.Add(direct);
                    ordered.Add(hotspot);
                    break;

                default:
                    if (_profile != null && _profile.HasInternet)
                    {
                        ordered.Add(hotspot);
                        ordered.Add(direct);
                        ordered.Add(legacy);
                    }
                    else
                    {
                        ordered.Add(direct);
                        ordered.Add(hotspot);
                        ordered.Add(legacy);
                    }

                    break;
            }

            return ordered;
        }

        public string ExplainEngines()
        {
            StringBuilder sb = new StringBuilder();
            foreach (IHotspotEngine candidate in Candidates())
            {
                sb.AppendLine(candidate.DisplayName + ": " + (candidate.IsAvailable ? "disponible" : "NO disponible - " + candidate.UnavailableReason));
                if (!string.IsNullOrEmpty(candidate.BehaviourNote))
                {
                    sb.AppendLine("    " + candidate.BehaviourNote);
                }
            }

            if (_profile != null && _profile.HasInternet)
            {
                sb.AppendLine();
                sb.AppendLine("El equipo TIENE internet: se puede repartir marcando \"Compartir internet\".");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("El equipo NO tiene internet: modo off-grid, la intranet se levanta igual.");
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            foreach (IHotspotEngine engine in _created)
            {
                try
                {
                    engine.Dispose();
                }
                catch (Exception)
                {
                    // Cerrar nunca puede lanzar.
                }
            }

            _created.Clear();
            GC.SuppressFinalize(this);
        }

        private IHotspotEngine Get(EngineKind kind)
        {
            foreach (IHotspotEngine existing in _created)
            {
                if (existing.Kind == kind)
                {
                    return existing;
                }
            }

            IHotspotEngine created;
            switch (kind)
            {
                case EngineKind.WiFiDirectGo:
                    created = new WiFiDirectGoEngine(_profile);
                    break;
                case EngineKind.MobileHotspot:
                    created = new MobileHotspotEngine(_profile);
                    break;
                default:
                    created = new LegacyHostedNetworkEngine(_profile);
                    break;
            }

            _created.Add(created);
            return created;
        }

        private void Raise()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception)
                {
                    // Un fallo del observador no puede tumbar el controlador.
                }
            }
        }
    }
}
