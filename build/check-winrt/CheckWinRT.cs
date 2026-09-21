// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - comprobacion de que las API modernas de Windows (WinRT) son
// alcanzables desde .NET Framework 4.8 sin Windows SDK. Forma parte del build:
// si falla, el motor moderno no puede compilarse en esta maquina.
using System;
using System.Collections.Generic;
using NetForge.Native;
using Windows.Devices.Radios;
using Windows.Devices.WiFiDirect;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

internal static class CheckWinRT
{
    private static void Line(string name, string value)
    {
        Console.WriteLine("  " + name.PadRight(34) + ": " + value);
    }

    private static int Main()
    {
        int problems = 0;
        Console.WriteLine("CheckWinRT - proyeccion WinRT sobre .NET Framework " + Environment.Version);

        try
        {
            IReadOnlyList<Radio> radios = WinRtAsync.Wait(Radio.GetRadiosAsync());
            Line("radios encontradas", radios == null ? "null" : radios.Count.ToString());
            if (radios != null)
            {
                foreach (Radio radio in radios)
                {
                    Line("  radio " + radio.Kind, radio.State + " (" + radio.Name + ")");
                }
            }
        }
        catch (Exception ex)
        {
            problems++;
            Line("Radio.GetRadiosAsync", ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            WiFiDirectAdvertisementPublisher publisher = new WiFiDirectAdvertisementPublisher();
            Line("WiFiDirectPublisher", "construido, estado=" + publisher.Status);
        }
        catch (Exception ex)
        {
            problems++;
            Line("WiFiDirectPublisher", ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            ConnectionProfile profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile == null)
            {
                Line("ConnectionProfile", "sin perfil de internet (posible modo off-grid)");
            }
            else
            {
                Line("ConnectionProfile", profile.ProfileName);
                NetworkOperatorTetheringManager manager =
                    NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
                Line("TetheringOperationalState", manager.TetheringOperationalState.ToString());
            }
        }
        catch (Exception ex)
        {
            Line("TetheringManager", ex.GetType().Name + ": " + ex.Message + " (no fatal sin perfil activo)");
        }

        // Diagnostico de nombres reales: la metadata dividida no siempre expone los nombres
        // que documenta Microsoft, asi que se preguntan por reflexion (sin ejecutar nada).
        try
        {
            foreach (System.Reflection.MethodInfo method in typeof(NetworkOperatorTetheringManager).GetMethods())
            {
                if (method.Name != "StartTetheringAsync" && method.Name != "StopTetheringAsync")
                {
                    continue;
                }

                Line(method.Name, method.ReturnType.FullName);
                foreach (Type arg in method.ReturnType.GetGenericArguments())
                {
                    Line("  resultado", arg.FullName);
                }
            }

            System.Reflection.PropertyInfo[] props =
                typeof(ConnectionProfile).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (System.Reflection.PropertyInfo prop in props)
            {
                if (prop.Name.IndexOf("Id", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prop.Name.IndexOf("Profile", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Line("  ConnectionProfile." + prop.Name, prop.PropertyType.Name);
                }
            }
        }
        catch (Exception ex)
        {
            Line("reflexion", ex.GetType().Name + ": " + ex.Message);
        }

        Console.WriteLine(problems == 0 ? "OK: WinRT alcanzable" : "FALLO: " + problems + " problema(s)");
        return problems == 0 ? 0 : 1;
    }
}
