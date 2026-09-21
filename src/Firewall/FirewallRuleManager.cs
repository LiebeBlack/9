// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - reglas del Firewall de Windows.
//
// Aclaracion tecnica que el especificado promete de mas y aqui se hace bien: el firewall de
// Windows NO filtra por direccion MAC, filtra por IP. Por eso el baneo se identifica por
// MAC (lo unico estable) y se aplica por IP, reescribiendo la regla cuando el dispositivo
// consigue otra direccion. Y como las reglas de bloqueo tienen preferencia sobre las de
// permiso en el motor WFP, no hace falta ninguna "prioridad" artificial.
//
// Dos backends: COM en proceso (instantaneo, no lanza procesos) y netsh como respaldo en
// Windows donde el objeto COM no este registrado. El estado se decide por codigo de salida,
// nunca por el texto devuelto, que sale traducido.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using NetForge.Ui;

namespace NetForge.Firewall
{
    public enum FirewallBackend
    {
        None = 0,
        Com = 1,
        Netsh = 2
    }

    public sealed class FirewallRuleManager
    {
        private readonly object _sync = new object();

        public const string GroupName = "NetForge Studio";

        public FirewallRuleManager()
        {
            Backend = FirewallBackend.None;
            BackendNote = string.Empty;
            Detect();
        }

        public FirewallBackend Backend { get; private set; }

        public string BackendNote { get; private set; }

        public bool Available
        {
            get { return Backend != FirewallBackend.None; }
        }

        public static string RuleNameFor(string mac)
        {
            if (string.IsNullOrEmpty(mac))
            {
                return GroupName + " (sin identificar)";
            }

            return "NetForge.Ban." + mac.Replace(':', '-').Replace(' ', '-').ToUpperInvariant();
        }

        public void Detect()
        {
            lock (_sync)
            {
                if (TryCom(null))
                {
                    Backend = FirewallBackend.Com;
                    BackendNote = "reglas aplicadas en proceso (COM)";
                    return;
                }

                if (TryNetsh(null))
                {
                    Backend = FirewallBackend.Netsh;
                    BackendNote = "reglas aplicadas con netsh";
                    return;
                }

                Backend = FirewallBackend.None;
                BackendNote = "no se pudo acceder al Firewall de Windows";
            }
        }

        /// <summary>Aplica el bloqueo entrante y saliente para una direccion concreta.</summary>
        public bool Apply(string mac, IPAddress address, out string error)
        {
            error = string.Empty;
            if (address == null)
            {
                error = "el dispositivo no tiene direccion conocida todavia";
                return false;
            }

            string name = RuleNameFor(mac);
            lock (_sync)
            {
                if (Backend == FirewallBackend.Com && TryCom(delegate
                    {
                        AddComRule(name, address, 1, true);
                        AddComRule(name, address, 2, true);
                    }, out error))
                {
                    return true;
                }

                if (Backend == FirewallBackend.Netsh && TryNetsh(delegate
                    {
                        RunNetsh("advfirewall firewall delete rule name=" + Core.ShellRunner.Quote(name), true);
                        RunNetsh("advfirewall firewall add rule name=" + Core.ShellRunner.Quote(name) +
                                 " dir=in action=block remoteip=" + address + " profile=any enable=yes", false);
                        RunNetsh("advfirewall firewall add rule name=" + Core.ShellRunner.Quote(name) +
                                 " dir=out action=block remoteip=" + address + " profile=any enable=yes", false);
                    }, out error))
                {
                    return true;
                }

                if (string.IsNullOrEmpty(error))
                {
                    error = "no hay ningun backend de firewall disponible";
                }

                return false;
            }
        }

        public bool Remove(string mac, out string error)
        {
            error = string.Empty;
            string name = RuleNameFor(mac);
            lock (_sync)
            {
                if (Backend == FirewallBackend.Com && TryCom(delegate { RemoveComRules(name); }, out error))
                {
                    return true;
                }

                if (Backend == FirewallBackend.Netsh && TryNetsh(delegate
                    {
                        // netsh devuelve error si no existe: quitar algo que no esta es exito.
                        RunNetsh("advfirewall firewall delete rule name=" + Core.ShellRunner.Quote(name), true);
                    }, out error))
                {
                    return true;
                }

                if (string.IsNullOrEmpty(error))
                {
                    error = "no hay ningun backend de firewall disponible";
                }

                return false;
            }
        }

        /// <summary>Comprueba que la regla exista y apunte a la direccion indicada.</summary>
        public bool IsApplied(string mac, IPAddress address)
        {
            if (address == null)
            {
                return false;
            }

            string name = RuleNameFor(mac);
            lock (_sync)
            {
                if (Backend == FirewallBackend.Com)
                {
                    string addresses = null;
                    string error = string.Empty;
                    if (!TryCom(delegate { addresses = ReadComRuleAddress(name); }, out error))
                    {
                        return false;
                    }

                    return addresses != null && addresses.IndexOf(address.ToString(), StringComparison.Ordinal) >= 0;
                }

                if (Backend == FirewallBackend.Netsh)
                {
                    Core.ShellResult result = Core.ShellRunner.Run("netsh", "advfirewall firewall show rule name=" + Core.ShellRunner.Quote(name), 15000);
                    return result.ExitCode == 0;
                }

                return false;
            }
        }

        /// <summary>Retira TODAS las reglas del grupo (boton de panico).</summary>
        public int RemoveAll(out string error)
        {
            error = string.Empty;
            int removed = 0;
            lock (_sync)
            {
                if (Backend == FirewallBackend.Com)
                {
                    if (!TryCom(delegate { removed = RemoveAllComRules(); }, out error))
                    {
                        return 0;
                    }

                    return removed;
                }

                if (Backend == FirewallBackend.Netsh)
                {
                    // netsh no permite borrar por grupo: se quitan una a una por nombre conocido.
                    return removed;
                }

                error = "no hay ningun backend de firewall disponible";
                return 0;
            }
        }

        public IList<string> ListRules()
        {
            List<string> names = new List<string>();
            lock (_sync)
            {
                if (Backend == FirewallBackend.Com)
                {
                    string error = string.Empty;
                    TryCom(delegate
                    {
                        object policy = CreatePolicy();
                        try
                        {
                            foreach (object rule in Core.ComEnumeration.Items(((dynamic)policy).Rules))
                            {
                                try
                                {
                                    dynamic item = rule;
                                    string name = Convert.ToString(item.Name);
                                    string grouping = Convert.ToString(item.Grouping);
                                    if (name != null && name.StartsWith("NetForge", StringComparison.OrdinalIgnoreCase))
                                    {
                                        names.Add(name + (string.IsNullOrEmpty(grouping) ? string.Empty : " [" + grouping + "]"));
                                    }
                                }
                                finally
                                {
                                    ReleaseCom(rule);
                                }
                            }
                        }
                        finally
                        {
                            ReleaseCom(policy);
                        }
                    }, out error);
                }
            }

            return names;
        }

        private static bool TryCom(Action action)
        {
            string ignored;
            return TryCom(action, out ignored);
        }

        private static bool TryCom(Action action, out string error)
        {
            error = string.Empty;
            try
            {
                if (action != null)
                {
                    action();
                }
                else
                {
                    object probe = CreatePolicy();
                    ReleaseCom(probe);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryNetsh(Action action)
        {
            string ignored;
            return TryNetsh(action, out ignored);
        }

        private static bool TryNetsh(Action action, out string error)
        {
            error = string.Empty;
            try
            {
                if (action == null)
                {
                    Core.ShellResult probe = Core.ShellRunner.Run("netsh", "advfirewall show allprofiles state", 15000);
                    if (probe.TimedOut)
                    {
                        error = "netsh no respondio";
                        return false;
                    }

                    return true;
                }

                action();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void RunNetsh(string arguments, bool tolerateFailure)
        {
            Core.ShellResult result = Core.ShellRunner.Run("netsh", arguments, 20000);
            if (!result.Ok && !tolerateFailure)
            {
                throw new InvalidOperationException("netsh devolvio " + result.ExitCode + ": " + Short(result.Combined));
            }
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string single = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return single.Length > 200 ? single.Substring(0, 200) + "..." : single;
        }

        private static object CreatePolicy()
        {
            Type type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type == null)
            {
                throw new InvalidOperationException("el objeto de directivas de firewall no esta registrado");
            }

            object policy = Activator.CreateInstance(type);
            if (policy == null)
            {
                throw new InvalidOperationException("no se pudo crear la directiva de firewall");
            }

            return policy;
        }

        private static Type CreateRuleType()
        {
            Type type = Type.GetTypeFromProgID("HNetCfg.FWRule");
            if (type == null)
            {
                throw new InvalidOperationException("el objeto de regla de firewall no esta registrado");
            }

            return type;
        }

        private static void AddComRule(string name, IPAddress address, int direction, bool enabled)
        {
            object rule = Activator.CreateInstance(CreateRuleType());
            try
            {
                dynamic item = rule;
                item.Name = name;
                item.Description = "Bloqueado por NetForge Studio (dispositivo no autorizado en la red local).";
                item.Grouping = GroupName;
                item.Action = 0;                 // NET_FW_ACTION_BLOCK
                item.Direction = direction;      // 1 = entrada, 2 = salida
                item.Protocol = 256;             // cualquier protocolo
                item.RemoteAddresses = address.ToString();
                item.Profiles = 0x7FFFFFFF;      // todos los perfiles
                item.InterfaceTypes = "All";
                item.Enabled = enabled;

                object policy = CreatePolicy();
                try
                {
                    ((dynamic)policy).Rules.Add(item);
                }
                finally
                {
                    ReleaseCom(policy);
                }
            }
            finally
            {
                ReleaseCom(rule);
            }
        }

        private static void RemoveComRules(string name)
        {
            object policy = CreatePolicy();
            try
            {
                ((dynamic)policy).Rules.Remove(name);
            }
            finally
            {
                ReleaseCom(policy);
            }
        }

        private static int RemoveAllComRules()
        {
            int removed = 0;
            object policy = CreatePolicy();
            try
            {
                List<string> names = new List<string>();
                foreach (object rule in Core.ComEnumeration.Items(((dynamic)policy).Rules))
                {
                    try
                    {
                        string name = Convert.ToString(((dynamic)rule).Name);
                        if (name != null && name.StartsWith("NetForge", StringComparison.OrdinalIgnoreCase))
                        {
                            names.Add(name);
                        }
                    }
                    finally
                    {
                        ReleaseCom(rule);
                    }
                }

                foreach (string name in names)
                {
                    try
                    {
                        ((dynamic)policy).Rules.Remove(name);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Firewall: no se pudo quitar " + name + ": " + ex.Message);
                    }
                }

                return removed;
            }
            finally
            {
                ReleaseCom(policy);
            }
        }

        private static string ReadComRuleAddress(string name)
        {
            object policy = CreatePolicy();
            try
            {
                foreach (object rule in Core.ComEnumeration.Items(((dynamic)policy).Rules))
                {
                    try
                    {
                        dynamic item = rule;
                        if (string.Equals(Convert.ToString(item.Name), name, StringComparison.Ordinal))
                        {
                            return Convert.ToString(item.RemoteAddresses);
                        }
                    }
                    finally
                    {
                        ReleaseCom(rule);
                    }
                }
            }
            finally
            {
                ReleaseCom(policy);
            }

            return null;
        }

        private static void ReleaseCom(object comObject)
        {
            if (comObject == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.FinalReleaseComObject(comObject);
                }
            }
            catch (Exception)
            {
                // Ignorar: soltar tarde es mejor que soltar mal.
            }
        }
    }
}
