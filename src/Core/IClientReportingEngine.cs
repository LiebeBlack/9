// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - capacidad opcional de los motores: algunos pueden enumerar sus
// propios clientes. Cuando existe, el radar la usa para confirmar y enriquecer lo que ve
// en la tabla ARP; cuando no, el radar funciona igual solo con ARP.
using System.Collections.Generic;

namespace NetForge.Core
{
    public sealed class EngineClient
    {
        public string Mac { get; set; }
        public IList<string> HostNames { get; set; }
        public IList<string> Addresses { get; set; }

        public EngineClient()
        {
            Mac = string.Empty;
            HostNames = new List<string>();
            Addresses = new List<string>();
        }
    }

    public interface IClientReportingEngine
    {
        IList<EngineClient> GetClients();
    }
}
