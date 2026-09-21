// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - reparto de internet (ICS) bajo demanda.
//
// "Usar compartir conexion" es una accion aparte, nunca automatica: el software no debe
// estorbar. Al parar se desactiva SIEMPRE, para no dejar la maquina enrutando trafico
// ajena despues de cerrar.
//
// Se usa enlace tardio (COM) en vez de importar la biblioteca de tipos, porque esta app se
// compila sin Windows SDK y las interfaces de hnetcfg cambian entre versiones.
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.ServiceProcess;
using NetForge.Ui;

namespace NetForge.Core
{
    public sealed class IcsSharingResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string PublicConnection { get; set; }
        public string PrivateConnection { get; set; }

        public IcsSharingResult()
        {
            Message = string.Empty;
            PublicConnection = string.Empty;
            PrivateConnection = string.Empty;
        }
    }

    public sealed class IcsSharing
    {
        private const int SharingTypePublic = 0;
        private const int SharingTypePrivate = 1;
        private static readonly Guid NetSharingManagerClass = new Guid("5C63C1AD-3956-4FF8-8486-40034758315B");

        private readonly List<string> _enabledByUs = new List<string>();

        public bool IsSharing { get; private set; }

        public string LastMessage { get; private set; }

        public IcsSharing()
        {
            LastMessage = string.Empty;
        }

        /// <summary>
        /// Marca el adaptador del punto de acceso como conexion privada y reparte por el la
        /// conexion con salida a internet. Si no hay internet, se deja preparado el lado
        /// privado para que los clientes reciban direccion.
        /// </summary>
        public IcsSharingResult Enable(ApInterface accessPoint)
        {
            IcsSharingResult result = new IcsSharingResult();
            if (accessPoint == null || string.IsNullOrEmpty(accessPoint.Id))
            {
                result.Message = "todavia no se conoce el adaptador del punto de acceso";
                LastMessage = result.Message;
                return result;
            }

            string serviceError = EnsureServiceRunning();
            if (serviceError != null)
            {
                result.Message = serviceError;
                LastMessage = serviceError;
                return result;
            }

            object manager = null;
            try
            {
                manager = CreateManager();
                if (manager == null)
                {
                    result.Message = "el servicio de compartir conexion no expone su API en este Windows";
                    LastMessage = result.Message;
                    return result;
                }

                List<ConnectionInfo> connections = ReadConnections(manager);
                ConnectionInfo privateConnection = FindByGuid(connections, accessPoint.Id);
                if (privateConnection == null)
                {
                    result.Message = "no se encontro el adaptador del punto de acceso entre las conexiones del sistema";
                    LastMessage = result.Message;
                    return result;
                }

                ConnectionInfo publicConnection = FindPublic(connections, accessPoint.Id);

                SetSharing(manager, privateConnection, SharingTypePrivate);
                result.PrivateConnection = privateConnection.Name;
                Track(privateConnection.Name);

                if (publicConnection != null)
                {
                    SetSharing(manager, publicConnection, SharingTypePublic);
                    result.PublicConnection = publicConnection.Name;
                    Track(publicConnection.Name);
                    result.Message = "repartiendo \"" + publicConnection.Name + "\" hacia la red local";
                }
                else
                {
                    result.Message = "no hay ninguna conexion con internet que repartir: la red local funcionara igual";
                }

                IsSharing = true;
                result.Success = true;
                LastMessage = result.Message;
                return result;
            }
            catch (Exception ex)
            {
                result.Message = "no se pudo activar compartir internet: " + ex.Message;
                LastMessage = result.Message;
                Log.Write("ICS: " + ex);

                // Activacion a medias (por ejemplo, lado privado activado y lado publico
                // fallido): se deshace aqui mismo. Si se dejara asi, la aplicacion creeria
                // que no reparte nada y nadie volveria a desactivarlo nunca.
                int undone = DisableConnections();
                if (undone > 0)
                {
                    Log.Write("ICS: se deshicieron " + undone + " conexion(es) a medio activar");
                }

                return result;
            }
            finally
            {
                Release(manager);
            }
        }

        /// <summary>Devuelve el numero de conexiones que se desconectaron del reparto.</summary>
        public int Disable()
        {
            int disabled = DisableConnections();
            _enabledByUs.Clear();
            IsSharing = false;
            LastMessage = disabled == 0 ? "el reparto ya estaba desactivado" : "reparto desactivado en " + disabled + " conexion(es)";
            return disabled;
        }

        /// <summary>
        /// Desactiva el reparto en todas las conexiones que lo tuvieran activo. Se desactiva
        /// tambien lo que no activamos nosotros, a proposito: dejar la maquina enrutando
        /// trafico de terceros al cerrar es peor que el aviso en el registro.
        /// </summary>
        private int DisableConnections()
        {
            object manager = null;
            int disabled = 0;
            try
            {
                manager = CreateManager();
                if (manager == null)
                {
                    return 0;
                }

                foreach (ConnectionInfo connection in ReadConnections(manager))
                {
                    if (!connection.SharingEnabled)
                    {
                        continue;
                    }

                    if (!WasEnabledByUs(connection.Name))
                    {
                        Log.Write("ICS: se desactiva un reparto que no habia activado NetForge (\"" + connection.Name + "\")");
                    }

                    SetSharing(manager, connection, -1);
                    disabled++;
                }
            }
            catch (Exception ex)
            {
                Log.Write("ICS no se pudo desactivar: " + ex.Message);
            }
            finally
            {
                Release(manager);
            }

            return disabled;
        }

        private bool WasEnabledByUs(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (string tracked in _enabledByUs)
            {
                if (string.Equals(tracked, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void Track(string name)
        {
            if (!string.IsNullOrEmpty(name) && !_enabledByUs.Contains(name))
            {
                _enabledByUs.Add(name);
            }
        }

        private static object CreateManager()
        {
            Type type = Type.GetTypeFromProgID("HNetCfg.NetSharingManager");
            if (type == null)
            {
                type = Type.GetTypeFromCLSID(NetSharingManagerClass);
            }

            if (type == null)
            {
                return null;
            }

            return Activator.CreateInstance(type);
        }

        /// <summary>
        /// Estado del servicio de uso compartido de Windows, para poder enseñarlo en la interfaz.
        /// Es el unico servicio del sistema que esta aplicacion toca, y solo cuando el usuario
        /// marca "Compartir internet".
        /// </summary>
        public static string DescribeService()
        {
            ServiceController controller = null;
            try
            {
                controller = new ServiceController("SharedAccess");
                switch (controller.Status)
                {
                    case ServiceControllerStatus.Running:
                        return "running";
                    case ServiceControllerStatus.StartPending:
                    case ServiceControllerStatus.ContinuePending:
                        return "starting";
                    case ServiceControllerStatus.Stopped:
                        return "stopped";
                    default:
                        return "other";
                }
            }
            catch (Exception)
            {
                // En algunas ediciones de Windows el servicio ni existe.
                return "missing";
            }
            finally
            {
                if (controller != null)
                {
                    controller.Dispose();
                }
            }
        }

        /// <summary>
        /// Arranca el servicio de uso compartido y lo deja en arranque automatico. Se hace asi y
        /// no dejandolo manual porque, si el servicio no esta en marcha tras un reinicio, el
        /// reparto de internet falla sin que nadie haya tocado nada. Cambiar el tipo de arranque
        /// exige administrador, que es justo el permiso que la aplicacion ya tiene.
        /// </summary>
        public static string EnsureServiceRunning()
        {
            ServiceController controller = null;
            try
            {
                controller = new ServiceController("SharedAccess");
                if (controller.Status != ServiceControllerStatus.Running &&
                    controller.Status != ServiceControllerStatus.StartPending)
                {
                    controller.Start();
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                }
            }
            catch (Exception ex)
            {
                return "no se pudo iniciar el servicio de compartir conexion (SharedAccess): " + ex.Message;
            }
            finally
            {
                if (controller != null)
                {
                    controller.Dispose();
                }
            }

            SetServiceAutomatic();
            return null;
        }

        /// <summary>
        /// Deja el servicio en arranque automatico. Si falla no se considera un error grave: el
        /// reparto funciona en esta sesion igual, y el aviso queda en el registro.
        /// </summary>
        private static void SetServiceAutomatic()
        {
            ShellResult result = ShellRunner.Run("sc", "config SharedAccess start= auto", 15000);
            if (!result.Ok)
            {
                Log.Write("ICS: no se pudo dejar el servicio en arranque automatico (codigo " + result.ExitCode + ")");
                return;
            }

            Log.Write("ICS: servicio SharedAccess en arranque automatico");
        }

        private static List<ConnectionInfo> ReadConnections(object manager)
        {
            List<ConnectionInfo> list = new List<ConnectionInfo>();
            dynamic dynamicManager = manager;
            object collection = dynamicManager.EnumEveryConnection;
            if (collection == null)
            {
                return list;
            }

            foreach (object raw in ComEnumeration.Items(collection))
            {
                try
                {
                    dynamic props = dynamicManager.NetConnectionProps[raw];
                    dynamic configuration = dynamicManager.INetSharingConfigurationForINetConnection[raw];
                    ConnectionInfo info = new ConnectionInfo
                    {
                        Name = AsString(props.Name),
                        Guid = AsGuid(props.Guid),
                        MediaType = AsInt(props.MediaType),
                        Status = AsInt(props.Status),
                        SharingEnabled = configuration.SharingEnabled,
                        SharingType = AsInt(configuration.SharingConnectionType)
                    };
                    info.Raw = raw;
                    list.Add(info);
                }
                catch (Exception ex)
                {
                    Log.Write("ICS: conexion ignorada: " + ex.Message);
                }
            }

            return list;
        }

        private static void SetSharing(object manager, ConnectionInfo connection, int sharingType)
        {
            dynamic dynamicManager = manager;
            dynamic configuration = dynamicManager.INetSharingConfigurationForINetConnection[connection.Raw];
            if (sharingType < 0)
            {
                configuration.DisableSharing();
                connection.SharingEnabled = false;
                return;
            }

            configuration.EnableSharing(sharingType);
            connection.SharingEnabled = true;
            connection.SharingType = sharingType;
        }

        private static ConnectionInfo FindByGuid(List<ConnectionInfo> connections, string interfaceId)
        {
            Guid wanted;
            if (!TryParseGuid(interfaceId, out wanted))
            {
                return null;
            }

            foreach (ConnectionInfo connection in connections)
            {
                if (connection.Guid == wanted)
                {
                    return connection;
                }
            }

            return null;
        }

        private static ConnectionInfo FindPublic(List<ConnectionInfo> connections, string accessPointId)
        {
            // Preferencia 1: la interfaz con puerta de enlace por defecto (la que sale a internet).
            Guid accessPointGuid;
            bool hasAccessPoint = TryParseGuid(accessPointId, out accessPointGuid);
            ConnectionInfo up = null;
            ConnectionInfo physical = null;
            ConnectionInfo fallback = null;

            foreach (ApInterface adapter in InterfaceLocator.Enumerate())
            {
                if (hasAccessPoint && adapter.Id == accessPointId)
                {
                    continue;
                }

                bool gateway = HasGateway(adapter.Id);
                ConnectionInfo match = FindByGuid(connections, adapter.Id);
                if (match == null)
                {
                    continue;
                }

                if (gateway && match.Status != 0)
                {
                    if (up == null)
                    {
                        up = match;
                    }
                }
                else if (gateway)
                {
                    if (physical == null)
                    {
                        physical = match;
                    }
                }
                else if (fallback == null && match.Status != 0)
                {
                    fallback = match;
                }
            }

            return up ?? physical ?? fallback;
        }

        private static bool HasGateway(string interfaceId)
        {
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!string.Equals(adapter.Id, interfaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return adapter.GetIPProperties().GatewayAddresses.Count > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static bool TryParseGuid(string value, out Guid guid)
        {
            guid = Guid.Empty;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return Guid.TryParse(value.Trim('{', '}'), out guid);
        }

        private static Guid AsGuid(object value)
        {
            if (value is Guid)
            {
                return (Guid)value;
            }

            string text = AsString(value);
            Guid parsed;
            if (TryParseGuid(text, out parsed))
            {
                return parsed;
            }

            return Guid.Empty;
        }

        private static string AsString(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value);
        }

        private static int AsInt(object value)
        {
            try
            {
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void Release(object comObject)
        {
            if (comObject == null)
            {
                return;
            }

            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch (Exception)
            {
                // Un objeto ya liberado no es un problema.
            }
        }

        private sealed class ConnectionInfo
        {
            public string Name;
            public Guid Guid;
            public int MediaType;
            public int Status;
            public bool SharingEnabled;
            public int SharingType;
            public object Raw;
        }
    }

    /// <summary>
    /// Las colecciones COM de ICS solo exponen _NewEnum (propiedad restringida), asi que no
    /// se pueden recorrer con foreach: hay que pedir el enumerador y avanzar a mano, liberando
    /// cada elemento para no fugar referencias en una app que vive en la bandeja durante dias.
    /// </summary>
    internal static class ComEnumeration
    {
        public static IEnumerable<object> Items(object collection)
        {
            if (collection == null)
            {
                yield break;
            }

            IEnumVARIANT enumerator = null;
            try
            {
                dynamic dynamicCollection = collection;
                object raw = dynamicCollection._NewEnum;
                enumerator = raw as IEnumVARIANT;
            }
            catch (Exception)
            {
                enumerator = null;
            }

            if (enumerator == null)
            {
                enumerator = collection as IEnumVARIANT;
            }

            if (enumerator == null)
            {
                yield break;
            }

            object[] buffer = new object[1];
            while (true)
            {
                int fetched = enumerator.Next(1, buffer, IntPtr.Zero);
                if (fetched != 0 || buffer[0] == null)
                {
                    break;
                }

                yield return buffer[0];
                buffer[0] = null;
            }
        }
    }
}
