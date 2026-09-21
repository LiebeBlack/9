// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - contrato de los motores de red. Cada motor (Wi-Fi Direct, punto de
// acceso movil, red hospedada legacy) implementa lo mismo, de forma que el selector y el
// Daemon Guard no saben ni les importa cual esta activo.
using System;
using System.Net;

namespace NetForge.Core
{
    public enum EngineKind
    {
        None = 0,
        WiFiDirectGo = 1,
        MobileHotspot = 2,
        LegacyHostedNetwork = 3
    }

    public enum EngineState
    {
        Idle = 0,
        Starting = 1,
        On = 2,
        Stopping = 3,
        Faulted = 4
    }

    /// <summary>Configuracion del punto de acceso, validada antes de tocar el hardware.</summary>
    public sealed class HotspotConfig
    {
        public string Ssid { get; set; }
        public string Passphrase { get; set; }
        public bool PreferTwoGhz { get; set; }
        public string ForcedInterfaceId { get; set; }

        public HotspotConfig()
        {
            Ssid = "NetForge";
            Passphrase = string.Empty;
            PreferTwoGhz = true;
            ForcedInterfaceId = null;
        }

        public HotspotConfig Clone()
        {
            return new HotspotConfig
            {
                Ssid = this.Ssid,
                Passphrase = this.Passphrase,
                PreferTwoGhz = this.PreferTwoGhz,
                ForcedInterfaceId = this.ForcedInterfaceId
            };
        }

        /// <summary>
        /// Valida contra las reglas reales del estandar: el SSID son 1..32 bytes UTF-8 (no
        /// caracteres) y la clave WPA2-PSK son 8..63 caracteres ASCII o 64 hexadecimales.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(Ssid))
            {
                throw new ArgumentException("El nombre de la red (SSID) no puede estar vacio.");
            }

            int ssidBytes = System.Text.Encoding.UTF8.GetByteCount(Ssid);
            if (ssidBytes > 32)
            {
                throw new ArgumentException("El SSID ocupa " + ssidBytes + " bytes UTF-8 y el maximo son 32.");
            }

            if (string.IsNullOrEmpty(Passphrase))
            {
                throw new ArgumentException("La clave no puede estar vacia: la red se crearia abierta.");
            }

            // El SSID y la clave acaban dentro de una linea de comandos de netsh: se
            // rechazan comillas y caracteres de control para que no puedan romperla.
            RejectUnsafe(Ssid, "SSID");
            RejectUnsafe(Passphrase, "clave");

            if (Passphrase.Length >= 8 && Passphrase.Length <= 63)
            {
                return;
            }

            if (Passphrase.Length == 64 && IsHex(Passphrase))
            {
                return;
            }

            throw new ArgumentException("La clave debe tener entre 8 y 63 caracteres (o 64 hexadecimales).");
        }

        private static void RejectUnsafe(string value, string label)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"' || char.IsControl(c))
                {
                    throw new ArgumentException("El campo " + label + " no admite comillas ni caracteres de control.");
                }
            }
        }

        private static bool IsHex(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class HotspotStatus
    {
        public EngineKind Engine { get; set; }
        public EngineState State { get; set; }
        public string Ssid { get; set; }
        public IPAddress GatewayAddress { get; set; }
        public string InterfaceId { get; set; }
        public string InterfaceDescription { get; set; }
        public int ChannelFrequency { get; set; }
        public string LastError { get; set; }
        public DateTime StartedUtc { get; set; }
        public int Restarts { get; set; }

        public HotspotStatus()
        {
            Engine = EngineKind.None;
            State = EngineState.Idle;
            Ssid = string.Empty;
            LastError = string.Empty;
            InterfaceDescription = string.Empty;
        }

        public string Endpoint
        {
            get { return GatewayAddress == null ? "(sin direccion)" : GatewayAddress.ToString(); }
        }
    }

    public interface IHotspotEngine : IDisposable
    {
        EngineKind Kind { get; }

        string DisplayName { get; }

        /// <summary>Motivo por el que el motor no esta disponible en esta maquina (o cadena vacia).</summary>
        string UnavailableReason { get; }

        /// <summary>Como se comporta este motor en la maquina (DHCP, internet, limitaciones).</summary>
        string BehaviourNote { get; }

        bool IsAvailable { get; }

        /// <summary>Levanta la red. Lanza con un mensaje claro si no puede.</summary>
        HotspotStatus Start(HotspotConfig config);

        void Stop();

        /// <summary>Consulta real del estado en el sistema operativo (nunca cacheada).</summary>
        HotspotStatus GetStatus();

        /// <summary>Adaptador virtual del punto de acceso, con su indice para filtrar el ARP.</summary>
        ApInterface AccessPoint { get; }
    }

    /// <summary>
    /// Base comun: gestiona transiciones de estado, captura de errores y localizacion del
    /// adaptador del punto de acceso, que es lo que todos los motores comparten.
    /// </summary>
    public abstract class HotspotEngineBase : IHotspotEngine
    {
        private readonly object _sync = new object();
        private HotspotStatus _status = new HotspotStatus();

        /// <summary>Cuanto se espera a que Windows cree el adaptador virtual y le asigne IP.</summary>
        protected virtual int AccessPointTimeoutMs
        {
            get { return 8000; }
        }

        public abstract EngineKind Kind { get; }

        public abstract string DisplayName { get; }

        public virtual string UnavailableReason
        {
            get { return string.Empty; }
        }

        public virtual bool IsAvailable
        {
            get { return string.IsNullOrEmpty(UnavailableReason); }
        }

        public virtual string BehaviourNote
        {
            get { return string.Empty; }
        }

        protected HotspotConfig Config { get; private set; }

        protected ApInterface AccessPointInterface { get; set; }

        public ApInterface AccessPoint
        {
            get { return AccessPointInterface; }
        }

        protected object SyncRoot
        {
            get { return _sync; }
        }

        public HotspotStatus Start(HotspotConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            config.Validate();

            lock (_sync)
            {
                Config = config.Clone();
                _status = new HotspotStatus
                {
                    Engine = this.Kind,
                    State = EngineState.Starting,
                    Ssid = config.Ssid,
                    StartedUtc = DateTime.UtcNow,
                    Restarts = _status.Restarts
                };

                try
                {
                    StartCore(config);
                    AccessPointInterface = WaitForAccessPoint(config.ForcedInterfaceId, AccessPointTimeoutMs);
                    _status.State = EngineState.On;
                    _status.LastError = string.Empty;
                    if (AccessPointInterface != null)
                    {
                        _status.GatewayAddress = AccessPointInterface.Address;
                        _status.InterfaceId = AccessPointInterface.Id;
                        _status.InterfaceDescription = AccessPointInterface.Description;
                    }

                    _status.StartedUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    _status.State = EngineState.Faulted;
                    _status.LastError = Describe(ex);

                    // El motor pudo quedarse a medio arrancar (radio publicando, adaptador
                    // creado). Se deshace antes de devolver el error: si no, el controlador
                    // probaria otro motor con el anterior todavia emitiendo.
                    try
                    {
                        StopCore();
                    }
                    catch (Exception stopError)
                    {
                        _status.LastError = _status.LastError + "; al deshacer, " + Describe(stopError);
                    }
                    finally
                    {
                        AccessPointInterface = null;
                    }

                    throw new InvalidOperationException(DisplayName + " no pudo levantar la red: " + _status.LastError, ex);
                }

                return Copy(_status);
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                _status.State = EngineState.Stopping;
                try
                {
                    StopCore();
                    _status.State = EngineState.Idle;
                    AccessPointInterface = null;
                }
                catch (Exception ex)
                {
                    // Si el motor no confirma la parada, la red puede seguir emitiendo: se
                    // informa como fallo en lugar de decir que esta apagada, y se conserva el
                    // adaptador para que el radar siga mirando la red de verdad.
                    _status.State = EngineState.Faulted;
                    _status.LastError = Describe(ex);
                }
            }
        }

        public HotspotStatus GetStatus()
        {
            lock (_sync)
            {
                HotspotStatus live = _status;
                try
                {
                    bool on = ProbeCore();
                    if (on)
                    {
                        if (live.State != EngineState.On)
                        {
                            live.State = EngineState.On;
                        }

                        if (AccessPointInterface == null)
                        {
                            AccessPointInterface = InterfaceLocator.FindAccessPoint(Config == null ? null : Config.ForcedInterfaceId);
                        }

                        if (AccessPointInterface != null)
                        {
                            live.GatewayAddress = AccessPointInterface.Address;
                            live.InterfaceId = AccessPointInterface.Id;
                            live.InterfaceDescription = AccessPointInterface.Description;
                        }
                    }
                    else if (live.State == EngineState.On)
                    {
                        live.State = EngineState.Faulted;
                        live.LastError = "la red dejo de emitir (adaptador en suspension o driver caido)";
                    }
                }
                catch (Exception ex)
                {
                    if (live.State == EngineState.On)
                    {
                        live.State = EngineState.Faulted;
                    }

                    live.LastError = Describe(ex);
                }

                return Copy(live);
            }
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            catch (Exception)
            {
                // Detener nunca debe lanzar: el cierre de la app no puede fallar.
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Espera a que exista el adaptador del punto de acceso con direccion util. Windows
        /// tarda entre uno y diez segundos en crearlo, y asumir que esta listo al instante es
        /// la via rapida a un "no hay IP" en la interfaz.
        /// </summary>
        protected ApInterface WaitForAccessPoint(string preferredId, int timeoutMs)
        {
            int waited = 0;
            ApInterface found = InterfaceLocator.FindAccessPoint(preferredId);
            while ((found == null || !found.IsUsable) && waited < timeoutMs)
            {
                System.Threading.Thread.Sleep(400);
                waited += 400;
                ApInterface candidate = InterfaceLocator.FindAccessPoint(preferredId);
                if (candidate != null && candidate.IsUsable)
                {
                    found = candidate;
                    break;
                }

                if (candidate != null)
                {
                    found = candidate;
                }
            }

            return found;
        }

        protected void NoteRestart()
        {
            _status.Restarts = _status.Restarts + 1;
        }

        protected static string Describe(Exception ex)
        {
            if (ex == null)
            {
                return "error desconocido";
            }

            string message = ex.Message;
            string inner = ex.InnerException == null ? null : ex.InnerException.Message;
            return string.IsNullOrEmpty(inner) ? message : message + " [" + inner + "]";
        }

        private static HotspotStatus Copy(HotspotStatus source)
        {
            return new HotspotStatus
            {
                Engine = source.Engine,
                State = source.State,
                Ssid = source.Ssid,
                GatewayAddress = source.GatewayAddress,
                InterfaceId = source.InterfaceId,
                InterfaceDescription = source.InterfaceDescription,
                ChannelFrequency = source.ChannelFrequency,
                LastError = source.LastError,
                StartedUtc = source.StartedUtc,
                Restarts = source.Restarts
            };
        }

        protected abstract void StartCore(HotspotConfig config);

        protected abstract void StopCore();

        /// <summary>Comprobacion viva contra el sistema operativo.</summary>
        protected abstract bool ProbeCore();
    }
}
