// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - perfil de la maquina. Es la respuesta honesta a "que puede hacer
// este equipo": nada se asume, todo se sondea, y lo que no se puede determinar se marca
// como Unknown en lugar de suponerse.
using System;
using System.Collections.Generic;
using System.Text;

namespace NetForge.Core
{
    public enum Capability
    {
        Unknown = 0,
        Yes = 1,
        No = 2
    }

    public sealed class MachineProfile
    {
        public Version WindowsVersion { get; set; }
        public bool WindowsSupported { get; set; }
        public bool Elevated { get; set; }
        public bool WlanServicePresent { get; set; }
        public bool HasWifiAdapter { get; set; }
        public string WifiAdapterDescription { get; set; }
        public Guid WifiInterfaceGuid { get; set; }
        public Capability RadioPresent { get; set; }
        public Capability RadioOn { get; set; }
        public Capability WiFiDirectPublisher { get; set; }
        public Capability HostedNetwork { get; set; }
        public Capability TetheringApi { get; set; }
        public Capability InternetSharingService { get; set; }
        public bool HasInternet { get; set; }
        public string PublicInterface { get; set; }
        public int ArpEntries { get; set; }
        public IList<string> Notes { get; set; }
        public IList<string> Adapters { get; set; }

        public MachineProfile()
        {
            Notes = new List<string>();
            Adapters = new List<string>();
            WindowsVersion = new Version(0, 0);
            WifiAdapterDescription = string.Empty;
            PublicInterface = string.Empty;
        }

        public void Note(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Notes.Add(text);
            }
        }

        public static string Label(Capability value)
        {
            switch (value)
            {
                case Capability.Yes:
                    return "si";
                case Capability.No:
                    return "no";
                default:
                    return "sin determinar";
            }
        }

        /// <summary>Informe copiable pensado para adjuntar a un issue de GitHub.</summary>
        public string ToReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NetForge Studio - diagnostico del equipo");
            sb.AppendLine("--------------------------------------");
            sb.AppendLine("Windows          : " + WindowsVersion + (WindowsSupported ? " (soportado)" : " (por debajo del minimo: 10.0.18362)"));
            sb.AppendLine("Proceso elevado  : " + (Elevated ? "si" : "no"));
            sb.AppendLine("Adaptador Wi-Fi  : " + (HasWifiAdapter ? WifiAdapterDescription : "ninguno detectado"));
            sb.AppendLine("Servicio WLAN    : " + (WlanServicePresent ? "presente" : "ausente"));
            sb.AppendLine();
            sb.AppendLine("Capacidades");
            sb.AppendLine("  Radio Wi-Fi                 : " + Label(RadioPresent));
            sb.AppendLine("  Radio encendida             : " + Label(RadioOn));
            sb.AppendLine("  Wi-Fi Direct (GO)           : " + Label(WiFiDirectPublisher));
            sb.AppendLine("  Red hospedada (Soft AP)     : " + Label(HostedNetwork));
            sb.AppendLine("  API de punto de acceso      : " + Label(TetheringApi));
            sb.AppendLine("  Servicio de compartir (ICS) : " + Label(InternetSharingService));
            sb.AppendLine();
            sb.AppendLine("Red");
            sb.AppendLine("  Conectividad a internet : " + (HasInternet ? "si" : "no (modo off-grid)"));
            sb.AppendLine("  Interfaz publica        : " + (string.IsNullOrEmpty(PublicInterface) ? "(ninguna)" : PublicInterface));
            sb.AppendLine("  Entradas ARP conocidas  : " + ArpEntries);
            sb.AppendLine();
            sb.AppendLine("Interfaces");
            foreach (string adapter in Adapters)
            {
                sb.AppendLine("  " + adapter);
            }

            if (Notes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Notas");
                foreach (string note in Notes)
                {
                    sb.AppendLine("  - " + note);
                }
            }

            return sb.ToString();
        }
    }
}
