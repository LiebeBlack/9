// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - entrada de la autocomprobacion.
//
// Existe como punto de entrada aparte porque el ejecutable principal pide permisos de
// administrador en su manifiesto (requireAdministrator) y Windows se niega a iniciarlo sin
// el aviso de UAC. Un ejecutable sin privilegios permite comprobar el proyecto en cualquier
// equipo y en integracion continua sin interaccion humana.
using System;
using System.Windows.Forms;

namespace NetForge
{
    internal static class SelfTestEntry
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // La aplicacion de verdad arranca con los estilos visuales de Windows y con el
            // dibujado de texto clasico. La comprobacion hace lo mismo para que las capturas de
            // revision de la interfaz (NETFORGE_UI_DUMP) ensenen lo que ve el usuario y no una
            // variante que solo existe dentro del test.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            return App.SelfTest.Run();
        }
    }
}
