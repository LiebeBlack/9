// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - motor legacy: red hospedada (Soft AP) de Windows 7/8/10 antiguo.
//
// Se maneja con netsh porque es exactamente lo que pide el especificado ("comandos de
// kernel directo" para adaptadores antiguos), pero la DECISION nunca depende del texto de
// netsh, que sale traducido: se decide por codigo de salida y se confirma el estado real
// leyendo la red hospedada desde wlanapi.
using System;
using NetForge.Native;

namespace NetForge.Core.Engines
{
    public sealed class LegacyHostedNetworkEngine : HotspotEngineBase
    {
        private readonly MachineProfile _profile;
        private Guid _interfaceGuid;
        private string _lastDiagnostic = string.Empty;

        public LegacyHostedNetworkEngine(MachineProfile profile)
        {
            _profile = profile;
        }

        public override EngineKind Kind
        {
            get { return EngineKind.LegacyHostedNetwork; }
        }

        public override string DisplayName
        {
            get { return "Red hospedada (Soft AP legacy)"; }
        }

        public override string BehaviourNote
        {
            get
            {
                return "Windows no reparte direcciones por si sola en este modo: hasta que no se " +
                       "active compartir conexion, los clientes se quedan sin IP util.";
            }
        }

        public override string UnavailableReason
        {
            get
            {
                if (_profile == null)
                {
                    return string.Empty;
                }

                if (!_profile.HasWifiAdapter)
                {
                    return "no hay adaptador Wi-Fi";
                }

                if (_profile.HostedNetwork == Capability.No)
                {
                    return "el driver de este adaptador no implementa red hospedada (Soft AP)";
                }

                if (_profile.HostedNetwork == Capability.Unknown)
                {
                    return "no se pudo determinar si el adaptador admite red hospedada";
                }

                return string.Empty;
            }
        }

        public string LastDiagnostic
        {
            get { return _lastDiagnostic; }
        }

        protected override void StartCore(HotspotConfig config)
        {
            _interfaceGuid = _profile == null ? Guid.Empty : _profile.WifiInterfaceGuid;

            string setCommand = "wlan set hostednetwork mode=allow ssid=" + ShellRunner.Quote(config.Ssid) +
                                " key=" + ShellRunner.Quote(config.Passphrase) + " keyUsage=persistent";
            ShellResult configure = ShellRunner.Run("netsh", setCommand, 20000);
            _lastDiagnostic = configure.Combined;
            if (!configure.Ok)
            {
                throw new InvalidOperationException("netsh no pudo configurar la red hospedada (codigo " + configure.ExitCode + "). " + Short(configure.Combined));
            }

            ShellResult start = ShellRunner.Run("netsh", "wlan start hostednetwork", 30000);
            _lastDiagnostic = start.Combined;
            HostedNetworkInfo info = WlanApi.QueryHostedNetwork(_interfaceGuid);
            if (!start.Ok && info.State != WlanHostedNetworkState.Active)
            {
                throw new InvalidOperationException("netsh no pudo arrancar la red hospedada (codigo " + start.ExitCode + "). " + Short(start.Combined));
            }
        }

        protected override void StopCore()
        {
            ShellResult stop = ShellRunner.Run("netsh", "wlan stop hostednetwork", 20000);
            _lastDiagnostic = stop.Combined;
        }

        protected override bool ProbeCore()
        {
            Guid guid = _interfaceGuid;
            if (guid == Guid.Empty && _profile != null)
            {
                guid = _profile.WifiInterfaceGuid;
            }

            if (guid == Guid.Empty)
            {
                return false;
            }

            HostedNetworkInfo info = WlanApi.QueryHostedNetwork(guid);
            return info.State == WlanHostedNetworkState.Active;
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string single = text.Replace(Environment.NewLine, " ").Replace("\n", " ").Replace("\r", " ").Trim();
            return single.Length > 240 ? single.Substring(0, 240) + "..." : single;
        }
    }
}
