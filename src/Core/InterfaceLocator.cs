// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - localizacion del adaptador del punto de acceso. Windows crea un
// adaptador virtual distinto en cada motor y su nombre esta traducido, asi que la
// busqueda nunca depende del texto visible: se compara la instantanea de interfaces
// antes y despues de levantar la red, y se priorizan los patrones conocidos.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NetForge.Core
{
    public sealed class ApInterface
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public IPAddress Address { get; set; }
        public IPAddress Mask { get; set; }
        public int PrefixLength { get; set; }
        public int Index { get; set; }

        public bool IsUsable
        {
            get { return Address != null && Address.AddressFamily == AddressFamily.InterNetwork && !Address.Equals(IPAddress.Any); }
        }

        public override string ToString()
        {
            return Description + " @ " + (Address == null ? "sin IP" : Address.ToString()) + " (indice " + Index + ")";
        }
    }

    public static class InterfaceLocator
    {
        private static readonly string[] AccessPointPatterns = new string[]
        {
            "Wi-Fi Direct",
            "WiFi Direct",
            "Hosted Network",
            "Red hospedada",
            "Virtual Wireless",
            "Virtual Wi-Fi",
            "Microsoft Wi-Fi"
        };

        public static IDictionary<string, ApInterface> Snapshot()
        {
            Dictionary<string, ApInterface> map = new Dictionary<string, ApInterface>(StringComparer.OrdinalIgnoreCase);
            foreach (ApInterface item in Enumerate())
            {
                map[item.Id] = item;
            }

            return map;
        }

        public static IList<ApInterface> Enumerate()
        {
            List<ApInterface> list = new List<ApInterface>();
            NetworkInterface[] adapters;
            try
            {
                adapters = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (Exception)
            {
                return list;
            }

            foreach (NetworkInterface adapter in adapters)
            {
                ApInterface item = Describe(adapter);
                if (item != null)
                {
                    list.Add(item);
                }
            }

            return list;
        }

        /// <summary>
        /// Encuentra el adaptador del punto de acceso: primero por patron conocido, despues
        /// por cualquier interfaz con direccion privada que haya aparecido. Devolver null no
        /// es un fallo: significa que hay que reintentar cuando Windows termine de crearlo.
        /// </summary>
        public static ApInterface FindAccessPoint(string preferredId)
        {
            IList<ApInterface> all = Enumerate();

            if (!string.IsNullOrEmpty(preferredId))
            {
                foreach (ApInterface candidate in all)
                {
                    if (string.Equals(candidate.Id, preferredId, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            ApInterface byPattern = null;
            ApInterface byAddress = null;
            foreach (ApInterface candidate in all)
            {
                if (!candidate.IsUsable)
                {
                    continue;
                }

                if (MatchesAccessPointPattern(candidate))
                {
                    if (byPattern == null || PreferBetter(byPattern, candidate))
                    {
                        byPattern = candidate;
                    }
                }
                else if (IsPrivate(candidate.Address))
                {
                    if (byAddress == null || PreferBetter(byAddress, candidate))
                    {
                        byAddress = candidate;
                    }
                }
            }

            return byPattern ?? byAddress;
        }

        /// <summary>Compara la instantanea previa con el estado actual (util para diagnosticos).</summary>
        public static ApInterface FindAppeared(IDictionary<string, ApInterface> before)
        {
            if (before == null)
            {
                return null;
            }

            foreach (ApInterface candidate in Enumerate())
            {
                if (!candidate.IsUsable)
                {
                    continue;
                }

                ApInterface previous;
                if (!before.TryGetValue(candidate.Id, out previous))
                {
                    return candidate;
                }

                if (previous.Address == null && candidate.Address != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        public static bool IsPrivate(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            byte[] octets = address.GetAddressBytes();
            if (octets[0] == 10)
            {
                return true;
            }

            if (octets[0] == 192 && octets[1] == 168)
            {
                return true;
            }

            if (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// .NET Framework no expone la longitud de prefijo en IPv4InterfaceProperties (eso
        /// llego en .NET Core), asi que se cuenta desde la mascara.
        /// </summary>
        public static int PrefixFromMask(IPAddress mask)
        {
            if (mask == null)
            {
                return 24;
            }

            byte[] bytes = mask.GetAddressBytes();
            if (bytes.Length != 4)
            {
                return 24;
            }

            int bits = 0;
            bool sawZero = false;
            for (int i = 0; i < 4; i++)
            {
                for (int bit = 7; bit >= 0; bit--)
                {
                    bool set = (bytes[i] & (1 << bit)) != 0;
                    if (set && sawZero)
                    {
                        // Mascara no contigua: se corta donde deja de ser valida.
                        return bits;
                    }

                    if (set)
                    {
                        bits++;
                    }
                    else
                    {
                        sawZero = true;
                    }
                }
            }

            return bits;
        }

        public static IPAddress MaskFromPrefix(int prefixLength)
        {
            if (prefixLength < 0 || prefixLength > 32)
            {
                return IPAddress.Any;
            }

            uint mask = prefixLength == 0 ? 0u : 0xFFFFFFFFu << (32 - prefixLength);
            byte[] bytes = BitConverter.GetBytes(mask);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return new IPAddress(bytes);
        }

        /// <summary>Valor numerico de una direccion IPv4 en orden de red (big-endian).</summary>
        public static uint NetworkValue(byte[] octets)
        {
            return ((uint)octets[0] << 24) | ((uint)octets[1] << 16) | ((uint)octets[2] << 8) | octets[3];
        }

        /// <summary>Direccion IPv4 a partir de un valor en orden de red.</summary>
        public static byte[] NetworkBytes(uint value)
        {
            return new byte[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            };
        }

        public static bool SameSubnet(IPAddress a, IPAddress b, IPAddress mask)
        {
            if (a == null || b == null || mask == null)
            {
                return false;
            }

            byte[] left = a.GetAddressBytes();
            byte[] right = b.GetAddressBytes();
            byte[] bits = mask.GetAddressBytes();
            if (left.Length != 4 || right.Length != 4 || bits.Length != 4)
            {
                return false;
            }

            for (int i = 0; i < 4; i++)
            {
                if ((left[i] & bits[i]) != (right[i] & bits[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Todas las direcciones utilizables de la subred, acotada para no reventar en /8.</summary>
        public static IList<IPAddress> HostsInSubnet(IPAddress address, IPAddress mask, int maximum)
        {
            List<IPAddress> hosts = new List<IPAddress>();
            if (address == null || mask == null)
            {
                return hosts;
            }

            byte[] net = address.GetAddressBytes();
            byte[] bits = mask.GetAddressBytes();
            if (net.Length != 4 || bits.Length != 4)
            {
                return hosts;
            }

            // Importante: BitConverter no sirve aqui. Lee los bytes en el orden de la maquina
            // (little-endian en Windows), asi que una direccion como 192.168.1.1 se
            // convertiria en 1.1.168.192 y el calculo de subred seria basura. Se usa
            // aritmetica explicita en orden de red.
            uint network = NetworkValue(net);
            uint maskValue = NetworkValue(bits);
            uint first = (network & maskValue) + 1;
            uint last = (network | ~maskValue) - 1;

            for (uint current = first; current <= last && hosts.Count < maximum; current++)
            {
                if (current == 0xFFFFFFFF)
                {
                    break;
                }

                hosts.Add(new IPAddress(NetworkBytes(current)));
            }

            return hosts;
        }

        private static ApInterface Describe(NetworkInterface adapter)
        {
            try
            {
                IPInterfaceProperties properties = adapter.GetIPProperties();
                IPv4InterfaceProperties v4 = null;
                try
                {
                    v4 = properties.GetIPv4Properties();
                }
                catch (NetworkInformationException)
                {
                    // Interfaces sin IPv4 (por ejemplo Teredo) simplemente no sirven aqui.
                }

                IPAddress address = null;
                IPAddress mask = null;
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        address = unicast.Address;
                        mask = unicast.IPv4Mask;
                        break;
                    }
                }

                if (v4 == null && address == null)
                {
                    return null;
                }

                int prefix = mask == null ? 24 : PrefixFromMask(mask);
                return new ApInterface
                {
                    Id = adapter.Id,
                    Name = adapter.Name,
                    Description = adapter.Description,
                    Address = address,
                    Mask = mask ?? MaskFromPrefix(prefix),
                    PrefixLength = prefix,
                    Index = v4 == null ? 0 : v4.Index
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool MatchesAccessPointPattern(ApInterface candidate)
        {
            string description = candidate.Description ?? string.Empty;
            string name = candidate.Name ?? string.Empty;
            foreach (string pattern in AccessPointPatterns)
            {
                if (description.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PreferBetter(ApInterface current, ApInterface candidate)
        {
            // Prefiere una direccion ya asignada y un adaptador que no sea la tarjeta fisica.
            if (current.IsUsable && !candidate.IsUsable)
            {
                return false;
            }

            return !current.IsUsable && candidate.IsUsable;
        }
    }
}
