// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - entrada de la autocomprobacion.
//
// Existe como punto de entrada aparte porque el ejecutable principal pide permisos de
// administrador en su manifiesto (requireAdministrator) y Windows se niega a iniciarlo sin
// el aviso de UAC. Un ejecutable sin privilegios permite comprobar el proyecto en cualquier
// equipo y en integracion continua sin interaccion humana.
using System;

namespace NetForge
{
    internal static class SelfTestEntry
    {
        private static int Main(string[] args)
        {
            return App.SelfTest.Run();
        }
    }
}
