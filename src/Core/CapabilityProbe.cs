// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - sondeo de capacidades. Es el corazon de la universalidad: la app no
// lleva una lista de "adaptadores compatibles" ni asume nada de la maquina donde corre.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.ServiceProcess;
using NetForge.Native;
using Windows.Devices.Radios;
using Windows.Devices.WiFiDirect;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace NetForge.Core
{
    public static class CapabilityProbe
    {
        /// <summary>Perfil cacheado: el sondeo toca servicios y hardware, no se repite por gusto.</summary>
        private static MachineProfile _cached;

        public static MachineProfile Current()
        {
            MachineProfile profile = _cached;
            if (profile == null)
            {
                return Probe();
            }

            return profile;
        }

        public static MachineProfile Probe()
        {
            MachineProfile profile = new MachineProfile();
            profile.WindowsVersion = OsInfo.GetRealVersion();
            profile.WindowsSupported = OsInfo.IsSupported();
            profile.Elevated = IsElevated();

            if (!profile.WindowsSupported)
            {
                profile.Note("Windows " + profile.WindowsVersion + " esta por debajo del minimo garantizado (10.0.18362): la app intentara funcionar, pero sin garantia.");
            }

            ProbeWlan(profile);
            ProbeRadio(profile);
            ProbeWiFiDirect(profile);
            ProbeTethering(profile);
            ProbeSharing(profile);
            ProbeNetwork(profile);

            _cached = profile;
            return profile;
        }

        public static void Invalidate()
        {
            _cached = null;
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void ProbeWlan(MachineProfile profile)
        {
            IList<WlanInterfaceInfo> interfaces = WlanApi.EnumerateInterfaces();
            profile.HasWifiAdapter = interfaces.Count > 0;
            if (profile.HasWifiAdapter)
            {
                WlanInterfaceInfo first = interfaces[0];
                profile.WifiAdapterDescription = first.Description;
                profile.WifiInterfaceGuid = first.Guid;
            }
            else
            {
                profile.Note("wlanapi no enumera ninguna interfaz: o no hay tarjeta Wi-Fi, o el servicio WLAN esta detenido.");
            }

            profile.WlanServicePresent = ServiceExists("WlanSvc");

            if (profile.HasWifiAdapter)
            {
                HostedNetworkInfo hosted = WlanApi.QueryHostedNetwork(profile.WifiInterfaceGuid);
                profile.HostedNetwork = hosted.Supported ? Capability.Yes : Capability.No;
                if (!string.IsNullOrEmpty(hosted.Reason))
                {
                    profile.Note("Red hospedada: " + hosted.Reason + ".");
                }
            }
            else
            {
                profile.HostedNetwork = Capability.No;
            }
        }

        private static void ProbeRadio(MachineProfile profile)
        {
            try
            {
                IReadOnlyList<Radio> radios = WinRtAsync.Wait(Radio.GetRadiosAsync(), 8000);
                bool present = false;
                bool on = false;
                if (radios != null)
                {
                    foreach (Radio radio in radios)
                    {
                        if (radio.Kind == RadioKind.WiFi)
                        {
                            present = true;
                            on = radio.State == RadioState.On;
                        }
                    }
                }

                profile.RadioPresent = present ? Capability.Yes : Capability.No;
                profile.RadioOn = present ? (on ? Capability.Yes : Capability.No) : Capability.No;
            }
            catch (Exception ex)
            {
                profile.RadioPresent = Capability.Unknown;
                profile.RadioOn = Capability.Unknown;
                profile.Note("No se pudo consultar el estado de la radio: " + ex.Message);
            }
        }

        private static void ProbeWiFiDirect(MachineProfile profile)
        {
            try
            {
                WiFiDirectAdvertisementPublisher publisher = new WiFiDirectAdvertisementPublisher();
                profile.WiFiDirectPublisher = publisher == null ? Capability.No : Capability.Yes;
            }
            catch (Exception ex)
            {
                profile.WiFiDirectPublisher = Capability.No;
                profile.Note("Wi-Fi Direct no disponible: " + ex.Message);
            }
        }

        private static void ProbeTethering(MachineProfile profile)
        {
            try
            {
                ConnectionProfile connection = NetworkInformation.GetInternetConnectionProfile();
                ConnectionProfile target = connection;
                if (target == null)
                {
                    IReadOnlyList<ConnectionProfile> profiles = NetworkInformation.GetConnectionProfiles();
                    if (profiles != null)
                    {
                        foreach (ConnectionProfile candidate in profiles)
                        {
                            if (candidate.IsWlanConnectionProfile)
                            {
                                target = candidate;
                                break;
                            }
                        }
                    }
                }

                if (target == null)
                {
                    profile.TetheringApi = Capability.Unknown;
                    profile.Note("Sin perfiles de conexion guardados: la capacidad de punto de acceso se comprobara al arrancar.");
                    return;
                }

                // No se usa GetTetheringCapability(idDeCuenta): ConnectionProfile no expone
                // ProfileId en la metadata actual de Windows. Crear el gestor es la
                // comprobacion honesta: si el sistema no puede, lanza.
                NetworkOperatorTetheringManager manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(target);
                if (manager == null)
                {
                    profile.TetheringApi = Capability.No;
                    profile.Note("El sistema no pudo crear el gestor de punto de acceso.");
                }
                else
                {
                    profile.TetheringApi = Capability.Yes;
                    profile.Note("Punto de acceso: estado actual " + manager.TetheringOperationalState + ".");
                }
            }
            catch (Exception ex)
            {
                profile.TetheringApi = Capability.Unknown;
                profile.Note("No se pudo consultar la capacidad de punto de acceso: " + ex.Message);
            }
        }

        private static void ProbeSharing(MachineProfile profile)
        {
            profile.InternetSharingService = ServiceExists("SharedAccess") ? Capability.Yes : Capability.No;
            if (profile.InternetSharingService == Capability.No)
            {
                profile.Note("El servicio de uso compartido de conexion (SharedAccess) no existe en este Windows.");
            }
        }

        private static void ProbeNetwork(MachineProfile profile)
        {
            try
            {
                profile.ArpEntries = IpHelper.GetArpTable().Count;
            }
            catch (Exception)
            {
                profile.ArpEntries = 0;
            }

            try
            {
                ConnectionProfile connection = NetworkInformation.GetInternetConnectionProfile();
                if (connection != null)
                {
                    NetworkConnectivityLevel level = connection.GetNetworkConnectivityLevel();
                    profile.HasInternet = level == NetworkConnectivityLevel.InternetAccess;
                    profile.PublicInterface = connection.ProfileName +
                        (level == NetworkConnectivityLevel.InternetAccess ? " (internet)" : " (" + level + ")");
                }
                else
                {
                    profile.HasInternet = false;
                }
            }
            catch (Exception)
            {
                profile.HasInternet = false;
            }

            foreach (ApInterface adapter in InterfaceLocator.Enumerate())
            {
                bool isAp = adapter.Description != null &&
                            adapter.Description.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0;
                profile.Adapters.Add(string.Format("{0} [{1}] {2} (indice {3}{4})",
                    adapter.Address == null ? "sin IP" : adapter.Address.ToString(),
                    adapter.PrefixLength,
                    adapter.Description,
                    adapter.Index,
                    isAp ? ", virtual" : string.Empty));
            }
        }

        private static bool ServiceExists(string name)
        {
            ServiceController controller = null;
            try
            {
                controller = new ServiceController(name);
                ServiceControllerStatus status = controller.Status;
                return status == ServiceControllerStatus.Running || status == ServiceControllerStatus.Stopped ||
                       status == ServiceControllerStatus.StartPending || status == ServiceControllerStatus.StopPending;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (controller != null)
                {
                    controller.Dispose();
                }
            }
        }
    }
}
