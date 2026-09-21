// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - motor moderno: API de punto de acceso movil de Windows 10/11.
//
// Es el motor preferido cuando SI hay internet, porque es el unico que Windows sabe
// enrutar y compartir de forma nativa. Tambien es el unico que puede enumerar sus propios
// clientes, lo que da un radar exacto (MAC confirmada por el sistema, no deducida del ARP).
using System;
using System.Collections.Generic;
using NetForge.Native;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace NetForge.Core.Engines
{
    public sealed class MobileHotspotEngine : HotspotEngineBase, IClientReportingEngine
    {
        private readonly MachineProfile _profile;
        private NetworkOperatorTetheringManager _manager;
        private string _profileUsed = string.Empty;

        public MobileHotspotEngine(MachineProfile profile)
        {
            _profile = profile;
        }

        public override EngineKind Kind
        {
            get { return EngineKind.MobileHotspot; }
        }

        public override string DisplayName
        {
            get { return "Punto de acceso movil de Windows"; }
        }

        public override string BehaviourNote
        {
            get { return "Comparte internet de forma nativa cuando el equipo tiene conexion."; }
        }

        public override string UnavailableReason
        {
            get
            {
                if (_profile == null)
                {
                    return string.Empty;
                }

                if (_profile.RadioPresent == Capability.No)
                {
                    return "no hay radio Wi-Fi en este equipo";
                }

                if (_profile.TetheringApi == Capability.No)
                {
                    return "el sistema indica que este adaptador no admite punto de acceso";
                }

                if (!_profile.HasWifiAdapter)
                {
                    return "no hay adaptador Wi-Fi";
                }

                return string.Empty;
            }
        }

        public IList<EngineClient> GetClients()
        {
            List<EngineClient> clients = new List<EngineClient>();
            NetworkOperatorTetheringManager manager = _manager;
            if (manager == null)
            {
                return clients;
            }

            try
            {
                IReadOnlyList<NetworkOperatorTetheringClient> reported = manager.GetTetheringClients();
                if (reported == null)
                {
                    return clients;
                }

                foreach (NetworkOperatorTetheringClient client in reported)
                {
                    EngineClient item = new EngineClient { Mac = client.MacAddress ?? string.Empty };
                    if (client.HostNames != null)
                    {
                        foreach (HostName name in client.HostNames)
                        {
                            item.HostNames.Add(name.CanonicalName);
                        }
                    }

                    clients.Add(item);
                }
            }
            catch (Exception)
            {
                // Si el sistema no puede enumerar, el radar sigue con ARP.
            }

            return clients;
        }

        protected override void StartCore(HotspotConfig config)
        {
            ConnectionProfile profile = PickProfile();
            if (profile == null)
            {
                throw new InvalidOperationException("Windows no tiene ningun perfil de conexion guardado; sin perfil no puede crearse el punto de acceso (usa el motor Wi-Fi Direct en su lugar).");
            }

            _profileUsed = profile.ProfileName;
            NetworkOperatorTetheringManager manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);

            NetworkOperatorTetheringAccessPointConfiguration apc = manager.GetCurrentAccessPointConfiguration();
            apc.Ssid = config.Ssid;
            apc.Passphrase = config.Passphrase;
            try
            {
                apc.Band = config.PreferTwoGhz ? TetheringWiFiBand.TwoPointFourGigahertz : TetheringWiFiBand.FiveGigahertz;
            }
            catch (Exception)
            {
                // No todos los adaptadores eligen banda: se deja la automatica.
            }

            WinRtAsync.Wait(manager.ConfigureAccessPointAsync(apc), 20000);

            // El tipo real en la metadata de Windows es NetworkOperatorTetheringOperationResult
            // (la documentacion lo llama TetheringOperationResult): se comprobo por reflexion.
            NetworkOperatorTetheringOperationResult startResult = WinRtAsync.Wait(manager.StartTetheringAsync(), 30000);
            if (startResult == null || startResult.Status != TetheringOperationStatus.Success)
            {
                string detail = startResult == null ? "sin resultado" : startResult.Status + " " + startResult.AdditionalErrorMessage;
                throw new InvalidOperationException("Windows rechazo activar el punto de acceso (" + detail + ").");
            }

            _manager = manager;
        }

        protected override void StopCore()
        {
            NetworkOperatorTetheringManager manager = _manager;
            if (manager == null)
            {
                return;
            }

            try
            {
                NetworkOperatorTetheringOperationResult stopResult = WinRtAsync.Wait(manager.StopTetheringAsync(), 20000);
                if (stopResult != null && stopResult.Status != TetheringOperationStatus.Success)
                {
                    throw new InvalidOperationException("Windows no confirmo la parada: " + stopResult.Status);
                }
            }
            catch (Exception ex)
            {
                // El gestor se conserva a proposito si la parada no se confirmo: asi el radar
                // de estado sigue viendo la red encendida (y se puede volver a intentar), en
                // vez de mentir diciendo que ya esta apagada.
                throw new InvalidOperationException("no se pudo detener el punto de acceso: " + ex.Message, ex);
            }

            _manager = null;
        }

        protected override bool ProbeCore()
        {
            NetworkOperatorTetheringManager manager = _manager;
            if (manager == null)
            {
                return false;
            }

            return manager.TetheringOperationalState == TetheringOperationalState.On;
        }

        /// <summary>
        /// Windows exige un perfil guardado para construir el gestor. Si no hay conexion a
        /// internet se usa cualquier perfil Wi-Fi guardado; eso es precisamente lo que
        /// permite levantar la red sin estar conectado.
        /// </summary>
        private static ConnectionProfile PickProfile()
        {
            try
            {
                ConnectionProfile online = NetworkInformation.GetInternetConnectionProfile();
                if (online != null)
                {
                    return online;
                }

                ConnectionProfile fallback = null;
                IReadOnlyList<ConnectionProfile> profiles = NetworkInformation.GetConnectionProfiles();
                if (profiles == null)
                {
                    return null;
                }

                foreach (ConnectionProfile candidate in profiles)
                {
                    if (fallback == null)
                    {
                        fallback = candidate;
                    }

                    if (candidate.IsWlanConnectionProfile)
                    {
                        return candidate;
                    }
                }

                return fallback;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
