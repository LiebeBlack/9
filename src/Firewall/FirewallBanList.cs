// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - lista negra (El Martillo) y gestor de indultos.
//
// La lista se guarda en disco y se identifica por MAC: si el intruso reinicia el telefono
// para pedir otra direccion, sigue baneado, porque el radar reescribe la regla con la IP
// nueva en cuanto lo vuelve a ver. Este es el detalle que hace que el baneo sea real y no
// un parche de un solo uso.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using NetForge.Ui;

namespace NetForge.Firewall
{
    public sealed class BannedDevice
    {
        public string Mac { get; set; }
        public IPAddress Address { get; set; }
        public string Reason { get; set; }
        public string Vendor { get; set; }
        public DateTime BannedUtc { get; set; }

        public BannedDevice()
        {
            Mac = string.Empty;
            Reason = string.Empty;
            Vendor = string.Empty;
            BannedUtc = DateTime.UtcNow;
        }

        public string AddressText
        {
            get { return Address == null ? "-" : Address.ToString(); }
        }

        public string BannedText
        {
            get { return BannedUtc.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture); }
        }

        public override string ToString()
        {
            return Mac + " (" + AddressText + ")";
        }
    }

    public sealed class FirewallBanList
    {
        private const string FileName = "bans.tsv";
        private readonly FirewallRuleManager _firewall;
        private readonly object _sync = new object();
        private readonly List<BannedDevice> _devices = new List<BannedDevice>();

        public FirewallBanList(FirewallRuleManager firewall)
        {
            _firewall = firewall;
            Load();
        }

        public event EventHandler Changed;

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _devices.Count;
                }
            }
        }

        public IList<BannedDevice> Devices()
        {
            lock (_sync)
            {
                return new List<BannedDevice>(_devices);
            }
        }

        public bool IsBanned(string mac)
        {
            if (string.IsNullOrEmpty(mac))
            {
                return false;
            }

            lock (_sync)
            {
                foreach (BannedDevice device in _devices)
                {
                    if (string.Equals(device.Mac, mac, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public BannedDevice Find(string mac)
        {
            if (string.IsNullOrEmpty(mac))
            {
                return null;
            }

            lock (_sync)
            {
                foreach (BannedDevice device in _devices)
                {
                    if (string.Equals(device.Mac, mac, StringComparison.OrdinalIgnoreCase))
                    {
                        return device;
                    }
                }
            }

            return null;
        }

        /// <summary>Aplica el bloqueo real y solo lo registra si el firewall lo acepto.</summary>
        public bool Ban(string mac, IPAddress address, string reason, string vendor, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(mac))
            {
                error = "no se sabe la MAC del dispositivo";
                return false;
            }

            if (_firewall == null || !_firewall.Available)
            {
                error = "el Firewall de Windows no esta accesible: " + (_firewall == null ? "sin gestor" : _firewall.BackendNote);
                return false;
            }

            if (address == null)
            {
                error = "el dispositivo no tiene direccion IP conocida: vuelve a intentarlo cuando el radar lo vea";
                return false;
            }

            if (!_firewall.Apply(mac, address, out error))
            {
                return false;
            }

            lock (_sync)
            {
                BannedDevice existing = null;
                foreach (BannedDevice device in _devices)
                {
                    if (string.Equals(device.Mac, mac, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = device;
                        break;
                    }
                }

                if (existing == null)
                {
                    _devices.Add(new BannedDevice
                    {
                        Mac = mac,
                        Address = address,
                        Reason = reason,
                        Vendor = vendor,
                        BannedUtc = DateTime.UtcNow
                    });
                }
                else
                {
                    existing.Address = address;
                    existing.Reason = reason;
                    if (!string.IsNullOrEmpty(vendor))
                    {
                        existing.Vendor = vendor;
                    }
                }

                Save();
            }

            Log.Write("Martillo: bloqueado " + mac + " en " + address + (string.IsNullOrEmpty(reason) ? string.Empty : " (" + reason + ")"));
            Raise();
            return true;
        }

        public bool Pardon(string mac, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(mac))
            {
                error = "MAC vacia";
                return false;
            }

            if (_firewall != null && _firewall.Available && !_firewall.Remove(mac, out error))
            {
                return false;
            }

            lock (_sync)
            {
                for (int i = _devices.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_devices[i].Mac, mac, StringComparison.OrdinalIgnoreCase))
                    {
                        _devices.RemoveAt(i);
                    }
                }

                Save();
            }

            Log.Write("Indulto: " + mac + " puede volver a la red");
            Raise();
            return true;
        }

        /// <summary>Retira todos los bloqueos y limpia cualquier regla huerfana del grupo.</summary>
        public int PardonAll(out string error)
        {
            int count = 0;
            error = string.Empty;
            lock (_sync)
            {
                List<BannedDevice> copy = new List<BannedDevice>(_devices);
                foreach (BannedDevice device in copy)
                {
                    string currentError;
                    if (Pardon(device.Mac, out currentError))
                    {
                        count++;
                    }
                    else if (string.IsNullOrEmpty(error))
                    {
                        error = currentError;
                    }
                }

                _devices.Clear();
                Save();
            }

            return count;
        }

        /// <summary>
        /// Llamado por el radar en cada pasada: si un baneado consiguio otra IP, la regla se
        /// reescribe; si alguien borro la regla a mano, se vuelve a crear.
        /// </summary>
        public void Enforce(string mac, IPAddress address)
        {
            if (string.IsNullOrEmpty(mac) || address == null || _firewall == null || !_firewall.Available)
            {
                return;
            }

            BannedDevice device = Find(mac);
            if (device == null)
            {
                return;
            }

            bool sameAddress = device.Address != null && device.Address.Equals(address);
            if (sameAddress && _firewall.IsApplied(mac, address))
            {
                return;
            }

            string error;
            if (_firewall.Apply(mac, address, out error))
            {
                lock (_sync)
                {
                    device.Address = address;
                    Save();
                }

                Log.Write("Martillo: bloqueo reaplicado a " + mac + " en " + address +
                          (sameAddress ? " (la regla habia desaparecido)" : " (cambio de direccion)"));
                Raise();
            }
            else
            {
                Log.Write("Martillo: no se pudo reaplicar el bloqueo de " + mac + ": " + error);
            }
        }

        public void Reconcile()
        {
            lock (_sync)
            {
                foreach (BannedDevice device in _devices)
                {
                    if (device.Address == null)
                    {
                        continue;
                    }

                    if (!_firewall.IsApplied(device.Mac, device.Address))
                    {
                        string error;
                        if (_firewall.Apply(device.Mac, device.Address, out error))
                        {
                            Log.Write("Martillo: regla restaurada para " + device.Mac + " en " + device.Address);
                        }
                        else
                        {
                            Log.Write("Martillo: el firewall rechazo restaurar " + device.Mac + ": " + error);
                        }
                    }
                }
            }
        }

        public string StorePath
        {
            get
            {
                string directory = DirectoryPath();
                return directory == null ? string.Empty : Path.Combine(directory, FileName);
            }
        }

        private void Raise()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception)
                {
                    // El observador no puede romper la lista.
                }
            }
        }

        private static string DirectoryPath()
        {
            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(root))
                {
                    return null;
                }

                string directory = Path.Combine(root, "NetForge");
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return directory;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void Load()
        {
            try
            {
                string path = StorePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string[] parts = line.Split('\t');
                    if (parts.Length < 2 || string.IsNullOrEmpty(parts[0]))
                    {
                        continue;
                    }

                    BannedDevice device = new BannedDevice { Mac = parts[0].Trim() };
                    IPAddress address;
                    if (parts.Length > 1 && IPAddress.TryParse(parts[1].Trim(), out address))
                    {
                        device.Address = address;
                    }

                    long ticks;
                    if (parts.Length > 2 && long.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) && ticks > 0)
                    {
                        device.BannedUtc = new DateTime(ticks, DateTimeKind.Utc);
                    }

                    device.Reason = parts.Length > 3 ? parts[3] : string.Empty;
                    device.Vendor = parts.Length > 4 ? parts[4] : string.Empty;
                    _devices.Add(device);
                }

                Log.Write("Lista negra cargada: " + _devices.Count + " dispositivo(s)");
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo leer la lista negra: " + ex.Message);
            }
        }

        private void Save()
        {
            try
            {
                string path = StorePath;
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# NetForge Studio - dispositivos bloqueados (identidad por MAC)");
                foreach (BannedDevice device in _devices)
                {
                    sb.AppendLine(string.Join("\t", new string[]
                    {
                        Clean(device.Mac),
                        device.Address == null ? string.Empty : device.Address.ToString(),
                        device.BannedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                        Clean(device.Reason),
                        Clean(device.Vendor)
                    }));
                }

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Write("No se pudo guardar la lista negra: " + ex.Message);
            }
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
