// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - modelo de un dispositivo conectado.
//
// La identidad del cliente es la MAC, nunca la IP: es lo unico que sobrevive a una
// renovacion de concesion DHCP, y es justo lo que permite que un baneo siga siendo valido
// cuando el intruso reinicia el telefono para conseguir otra direccion.
using System;
using System.Net;

namespace NetForge.Net
{
    public sealed class LanClient
    {
        public string Mac { get; set; }
        public IPAddress Address { get; set; }
        public string Vendor { get; set; }
        public bool VendorIsRandomized { get; set; }
        public string HostName { get; set; }
        public bool IsBanned { get; set; }
        public bool ConfirmedByEngine { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public LanClient()
        {
            Mac = string.Empty;
            Vendor = string.Empty;
            HostName = string.Empty;
            FirstSeenUtc = DateTime.UtcNow;
            LastSeenUtc = DateTime.UtcNow;
        }

        public string AddressText
        {
            get { return Address == null ? "-" : Address.ToString(); }
        }

        public string VendorText
        {
            get
            {
                if (VendorIsRandomized)
                {
                    return "MAC aleatoria (privacidad)";
                }

                return string.IsNullOrEmpty(Vendor) ? "desconocido" : Vendor;
            }
        }

        public string PresenceText
        {
            get
            {
                TimeSpan seconds = DateTime.UtcNow - LastSeenUtc;
                if (seconds.TotalSeconds < 15)
                {
                    return "en linea";
                }

                if (seconds.TotalMinutes < 2)
                {
                    return "hace " + (int)seconds.TotalSeconds + " s";
                }

                return "hace " + (int)seconds.TotalMinutes + " min";
            }
        }

        public string SourceText
        {
            get { return ConfirmedByEngine ? "confirmado por el sistema" : "detectado por ARP"; }
        }

        public override string ToString()
        {
            return Mac + " / " + AddressText + " / " + VendorText;
        }
    }
}
