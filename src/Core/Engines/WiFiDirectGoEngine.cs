// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - motor Wi-Fi Direct en modo grupo autonomo (GO).
//
// Es el motor que hace posible el "Off-Grid": Windows no exige internet para publicar un
// grupo autonomo, y con LegacySettings el grupo se anuncia como un SSID normal, asi que
// cualquier telefono lo ve como una red Wi-Fi corriente. El propio GO reparte direcciones,
// por lo que la intranet funciona sin compartir conexion.
using System;
using System.Threading;
using NetForge.Native;
using Windows.Devices.WiFiDirect;
using Windows.Foundation;
using Windows.Security.Credentials;

namespace NetForge.Core.Engines
{
    public sealed class WiFiDirectGoEngine : HotspotEngineBase
    {
        private readonly MachineProfile _profile;
        private WiFiDirectAdvertisementPublisher _publisher;
        private string _lastStatusChange = string.Empty;

        public WiFiDirectGoEngine(MachineProfile profile)
        {
            _profile = profile;
        }

        public override EngineKind Kind
        {
            get { return EngineKind.WiFiDirectGo; }
        }

        public override string DisplayName
        {
            get { return "Wi-Fi Direct (grupo autonomo)"; }
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

                if (_profile.WiFiDirectPublisher == Capability.No)
                {
                    return "el sistema no expone Wi-Fi Direct";
                }

                if (_profile.RadioOn == Capability.No)
                {
                    return "la radio Wi-Fi esta apagada";
                }

                return string.Empty;
            }
        }

        protected override void StartCore(HotspotConfig config)
        {
            WiFiDirectAdvertisementPublisher publisher = new WiFiDirectAdvertisementPublisher();
            publisher.Advertisement.IsAutonomousGroupOwnerEnabled = true;
            publisher.Advertisement.ListenStateDiscoverability = WiFiDirectAdvertisementListenStateDiscoverability.None;

            WiFiDirectLegacySettings legacy = publisher.Advertisement.LegacySettings;
            if (legacy != null)
            {
                // Passphrase es una PasswordCredential, no una cadena: el SSID viaja como
                // nombre de usuario y la clave como contrasena.
                PasswordCredential credential = new PasswordCredential();
                credential.UserName = config.Ssid;
                credential.Password = config.Passphrase;
                legacy.Ssid = config.Ssid;
                legacy.Passphrase = credential;
                legacy.IsEnabled = true;
            }

            publisher.StatusChanged += OnStatusChanged;
            publisher.Start();
            _publisher = publisher;

            // Start() es asincrono de facto: el adaptador aparece cuando el driver confirma.
            int waited = 0;
            while (publisher.Status != WiFiDirectAdvertisementPublisherStatus.Started && waited < 10000)
            {
                if (publisher.Status == WiFiDirectAdvertisementPublisherStatus.Aborted)
                {
                    throw new InvalidOperationException("el driver aborto la publicacion del grupo" +
                        (string.IsNullOrEmpty(_lastStatusChange) ? string.Empty : " (" + _lastStatusChange + ")"));
                }

                Thread.Sleep(250);
                waited += 250;
            }

            if (publisher.Status != WiFiDirectAdvertisementPublisherStatus.Started)
            {
                throw new InvalidOperationException("el grupo no alcanzo el estado Started en 10 segundos (estado actual: " + publisher.Status + ")");
            }
        }

        protected override void StopCore()
        {
            if (_publisher == null)
            {
                return;
            }

            try
            {
                _publisher.StatusChanged -= OnStatusChanged;
                _publisher.Stop();
            }
            finally
            {
                _publisher = null;
            }
        }

        protected override bool ProbeCore()
        {
            if (_publisher == null)
            {
                return false;
            }

            return _publisher.Status == WiFiDirectAdvertisementPublisherStatus.Started;
        }

        private void OnStatusChanged(WiFiDirectAdvertisementPublisher sender, WiFiDirectAdvertisementPublisherStatusChangedEventArgs args)
        {
            _lastStatusChange = args.Status + (args.Error == WiFiDirectError.Success ? string.Empty : " / " + args.Error);
        }
    }
}
