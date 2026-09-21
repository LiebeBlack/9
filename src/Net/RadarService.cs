// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - Radar en vivo.
//
// Audita la red local cada pocos segundos en un hilo de fondo y publica una lista de
// clientes. La fuente principal es la tabla ARP del sistema (baratísima: es lo que el stack
// ya sabe, no hace falta escanear con ping), complementada por lo que el propio motor de la
// red pueda confirmar.
//
// Decisiones que importan:
//  - La identidad es la MAC, asi que un cliente que cambia de IP sigue siendo el mismo.
//  - Solo se sondean con SendARP los equipos ya vistos (responden al instante); sondear
//    direcciones libres bloquearia segundos por cada una.
//  - En Modo Fantasma la interfaz no existe: se calcula todo igual y el que observa decide
//    si dibuja. Nunca se toca un control desde este hilo.
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using NetForge.Core;
using NetForge.Ui;

namespace NetForge.Net
{
    public sealed class RadarEventArgs : EventArgs
    {
        public RadarEventArgs(IList<LanClient> clients, ApInterface accessPoint)
        {
            Clients = clients;
            AccessPoint = accessPoint;
        }

        public IList<LanClient> Clients { get; private set; }

        public ApInterface AccessPoint { get; private set; }
    }

    /// <summary>
    /// Un dispositivo que el radar no habia visto nunca acaba de entrar en la red. Se avisa
    /// una sola vez por MAC: es lo que hace util tener la ventana cerrada (Modo Fantasma).
    /// </summary>
    public sealed class ClientJoinedEventArgs : EventArgs
    {
        public ClientJoinedEventArgs(LanClient client)
        {
            Client = client;
        }

        public LanClient Client { get; private set; }
    }

    public sealed class RadarService : IDisposable
    {
        private readonly Func<ApInterface> _accessPointProvider;
        private readonly Func<IList<EngineClient>> _engineClientProvider;
        private readonly Firewall.FirewallBanList _banList;
        private readonly Action<string, IPAddress> _banEnforcer;
        private readonly object _sync = new object();
        private readonly IDictionary<string, LanClient> _known = new Dictionary<string, LanClient>(StringComparer.OrdinalIgnoreCase);

        private Timer _timer;
        private int _busy;
        private int _running;
        private bool _disposed;
        private DateTime _lastActiveProbeUtc = DateTime.MinValue;

        public RadarService(
            Func<ApInterface> accessPointProvider,
            Func<IList<EngineClient>> engineClientProvider,
            Firewall.FirewallBanList banList,
            Action<string, IPAddress> banEnforcer)
        {
            _accessPointProvider = accessPointProvider;
            _engineClientProvider = engineClientProvider;
            _banList = banList;
            _banEnforcer = banEnforcer;
            IntervalMs = 3000;
            ScanCycles = 0;
        }

        public event EventHandler<RadarEventArgs> Updated;

        public event EventHandler<ClientJoinedEventArgs> ClientJoined;

        public int IntervalMs { get; set; }

        public int ScanCycles { get; private set; }

        public string LastError { get; private set; }

        public void Start()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1 || _disposed)
            {
                return;
            }

            _timer = new Timer(OnTimer, null, 500, IntervalMs);
            Log.Write("Radar: auditoria cada " + IntervalMs + " ms");
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _running, 0) == 0)
            {
                return;
            }

            Timer timer = _timer;
            _timer = null;
            if (timer != null)
            {
                timer.Dispose();
            }
        }

        /// <summary>Copia segura de la ultima lista conocida (para la interfaz o para exportar).</summary>
        public IList<LanClient> Snapshot()
        {
            lock (_sync)
            {
                List<LanClient> copy = new List<LanClient>(_known.Count);
                foreach (KeyValuePair<string, LanClient> pair in _known)
                {
                    copy.Add(pair.Value);
                }

                return copy;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            Stop();
        }

        private void OnTimer(object state)
        {
            // El temporizador ya esta liberado o el radar parado: una pasada en vuelo no debe
            // publicar una lista de clientes despues de que se haya pedido parar.
            if (_disposed || Thread.VolatileRead(ref _running) == 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                return;
            }

            try
            {
                Scan();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Write("Radar: " + ex);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private void Scan()
        {
            ApInterface accessPoint = _accessPointProvider == null ? null : _accessPointProvider();
            DateTime now = DateTime.UtcNow;
            List<LanClient> snapshot = new List<LanClient>();

            Dictionary<string, IPAddress> byMac = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> hostNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> confirmed = new List<string>();
            List<LanClient> joined = new List<LanClient>();

            if (accessPoint != null && accessPoint.Index > 0)
            {
                IList<Native.ArpEntry> entries = Native.IpHelper.GetArpTable();
                Dictionary<string, string> incomplete = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (Native.ArpEntry entry in entries)
                {
                    if (entry.InterfaceIndex != accessPoint.Index)
                    {
                        continue;
                    }

                    if (!InterfaceLocator.SameSubnet(entry.Address, accessPoint.Address, accessPoint.Mask))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(entry.Mac) || entry.Mac == "00:00:00:00:00:00")
                    {
                        incomplete[entry.Address.ToString()] = entry.Mac;
                        continue;
                    }

                    if (entry.Address.Equals(accessPoint.Address))
                    {
                        continue;
                    }

                    byMac[entry.Mac] = entry.Address;
                }

                // Refresco activo: solo equipos ya conocidos, cuyo ARP responde en milisegundos.
                if ((now - _lastActiveProbeUtc).TotalSeconds >= 15)
                {
                    _lastActiveProbeUtc = now;
                    ProbeKnownHosts(accessPoint, byMac, incomplete);
                }
            }

            if (_engineClientProvider != null)
            {
                try
                {
                    IList<EngineClient> reported = _engineClientProvider();
                    if (reported != null)
                    {
                        foreach (EngineClient client in reported)
                        {
                            if (string.IsNullOrEmpty(client.Mac))
                            {
                                continue;
                            }

                            confirmed.Add(client.Mac);
                            if (client.HostNames.Count > 0)
                            {
                                hostNames[client.Mac] = client.HostNames[0];
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Radar: el motor no pudo enumerar clientes: " + ex.Message);
                }
            }

            lock (_sync)
            {
                // Los equipos que ya estaban y siguen en ARP se actualizan; los que
                // desaparecieron se conservan un ciclo mas, para no parpadear en la lista.
                foreach (KeyValuePair<string, IPAddress> pair in byMac)
                {
                    LanClient client = Touch(pair.Key, now, joined);
                    client.Address = pair.Value;
                }

                foreach (string mac in confirmed)
                {
                    LanClient client = Touch(mac, now, joined);
                    client.ConfirmedByEngine = true;
                }

                Prune(now);

                foreach (KeyValuePair<string, LanClient> pair in _known)
                {
                    LanClient client = pair.Value;
                    string name;
                    if (hostNames.TryGetValue(client.Mac, out name))
                    {
                        client.HostName = name;
                    }

                    OuiLookupResult vendor = OuiDatabase.Lookup(client.Mac);
                    client.Vendor = vendor.Vendor;
                    client.VendorIsRandomized = vendor.IsRandomized;
                    client.IsBanned = _banList != null && _banList.IsBanned(client.Mac);
                    snapshot.Add(client);
                }
            }

            snapshot.Sort(CompareClients);

            // Fuera del lock: aplicar el baneo a la IP actual del intruso.
            if (_banEnforcer != null)
            {
                foreach (LanClient client in snapshot)
                {
                    if (client.IsBanned && client.Address != null)
                    {
                        try
                        {
                            _banEnforcer(client.Mac, client.Address);
                        }
                        catch (Exception ex)
                        {
                            Log.Write("Radar: no se pudo mantener el bloqueo de " + client.Mac + ": " + ex.Message);
                        }
                    }
                }
            }

            if (_disposed)
            {
                return;
            }

            ScanCycles++;

            EventHandler<RadarEventArgs> handler = Updated;
            if (handler != null)
            {
                try
                {
                    handler(this, new RadarEventArgs(snapshot, accessPoint));
                }
                catch (Exception ex)
                {
                    Log.Write("Radar: el observador fallo: " + ex.Message);
                }
            }

            // Fuera del lock y de la lista grande: quien escucha puede tardar (una notificacion
            // de Windows) y el radar no se puede quedar esperando.
            EventHandler<ClientJoinedEventArgs> joinedHandler = ClientJoined;
            if (joinedHandler != null && joined.Count > 0)
            {
                foreach (LanClient client in joined)
                {
                    try
                    {
                        joinedHandler(this, new ClientJoinedEventArgs(client));
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Radar: el observador de entradas fallo: " + ex.Message);
                    }
                }
            }
        }

        private void ProbeKnownHosts(ApInterface accessPoint, Dictionary<string, IPAddress> byMac, Dictionary<string, string> incomplete)
        {
            uint sourceIp = 0;
            if (accessPoint.Address != null)
            {
                byte[] octets = accessPoint.Address.GetAddressBytes();
                if (octets.Length == 4)
                {
                    sourceIp = BitConverter.ToUInt32(octets, 0);
                }
            }

            int budget = 16;
            foreach (KeyValuePair<string, string> pair in incomplete)
            {
                if (budget-- <= 0)
                {
                    return;
                }

                if (!byMac.ContainsKey(pair.Key))
                {
                    IPAddress address;
                    if (!IPAddress.TryParse(pair.Key, out address))
                    {
                        continue;
                    }

                    string mac = Native.IpHelper.ResolveMac(address, sourceIp);
                    if (!string.IsNullOrEmpty(mac) && mac != "00:00:00:00:00:00")
                    {
                        byMac[mac] = address;
                    }
                }
            }
        }

        private LanClient Touch(string mac, DateTime now, List<LanClient> joined)
        {
            LanClient client;
            if (!_known.TryGetValue(mac, out client))
            {
                client = new LanClient { Mac = mac, FirstSeenUtc = now, LastSeenUtc = now };
                _known[mac] = client;
                if (joined != null)
                {
                    joined.Add(client);
                }
            }

            client.LastSeenUtc = now;
            return client;
        }

        private void Prune(DateTime now)
        {
            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, LanClient> pair in _known)
            {
                // Se mantiene el historial 5 minutos: un telefono en suspension no es un
                // intruso desaparecido, y borrarlo obligaria a re-banearlo.
                if ((now - pair.Value.LastSeenUtc).TotalSeconds > 300)
                {
                    stale.Add(pair.Key);
                }
            }

            foreach (string mac in stale)
            {
                _known.Remove(mac);
            }
        }

        private static int CompareClients(LanClient left, LanClient right)
        {
            byte[] a = left.Address == null ? new byte[4] : left.Address.GetAddressBytes();
            byte[] b = right.Address == null ? new byte[4] : right.Address.GetAddressBytes();
            for (int i = 0; i < 4 && i < a.Length && i < b.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return a[i] < b[i] ? -1 : 1;
                }
            }

            return string.CompareOrdinal(left.Mac, right.Mac);
        }
    }
}
