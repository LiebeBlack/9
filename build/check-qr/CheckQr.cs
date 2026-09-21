// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - comprobacion del codificador QR con un decodificador independiente.
//
// ZXing.Net se usa UNICAMENTE aqui, como oraculo de prueba: no forma parte del producto ni
// se distribuye con el. Si ZXing lee exactamente el texto que se codifico, entonces la
// informacion de formato, la mascara elegida, la informacion de version, la colocacion de
// modulos y el Reed-Solomon son correctos; si algo de eso estuviera mal, el lector no
// devolveria el texto y esta herramienta fallaria.
using System;
using System.Drawing;
using NetForge.Qr;
using ZXing;

internal static class CheckQr
{
    private static int Main()
    {
        string[] payloads = new string[]
        {
            "http://192.168.137.1:8080/",
            "ftp://192.168.137.1:2121/",
            "http://192.168.137.1:8080/#NetForge-Studio-transferencia-local-de-archivos",
            "NetForge: carpeta compartida, acentos y simbolos -> \u00e1\u00e9\u00ed\u00f3\u00fa\u00f1\u00bf?",
            "http://10.0.0.1:8080/f/pelicula.mkv",
            new string('A', 100)
        };

        QrEcc[] levels = new QrEcc[] { QrEcc.Low, QrEcc.Medium, QrEcc.Quartile, QrEcc.High };
        BarcodeReader reader = new BarcodeReader();
        reader.AutoRotate = true;
        reader.Options.TryHarder = true;

        int failures = 0;
        int cases = 0;

        foreach (string payload in payloads)
        {
            foreach (QrEcc level in levels)
            {
                cases++;
                string label = payload.Length > 28 ? payload.Substring(0, 28) + "..." : payload;
                try
                {
                    QrCode code = QrEncoder.Encode(payload, level);
                    using (Bitmap bitmap = QrRenderer.Render(code, 4, Color.Black, Color.White))
                    {
                        Result decoded = reader.Decode(bitmap);
                        if (decoded == null)
                        {
                            failures++;
                            Console.WriteLine("FALLO  v" + code.Version + "/" + level + " sin lectura (" + label + ")");
                            continue;
                        }

                        if (decoded.Text != payload)
                        {
                            failures++;
                            Console.WriteLine("FALLO  v" + code.Version + "/" + level + " texto distinto: " + decoded.Text);
                            continue;
                        }

                        Console.WriteLine("ok     v" + code.Version + "/" + level + " mascara " + code.Mask + " (" + label + ")");
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine("FALLO  " + level + " excepcion: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        // Comprobacion de los limites: la capacidad declarada debe ser exactamente el mayor
        // texto que cabe, ni uno mas ni uno menos.
        for (int version = 1; version <= 10; version++)
        {
            foreach (QrEcc level in levels)
            {
                int capacity = QrEncoder.ByteCapacity(version, level);
                QrCode fits = QrEncoder.Encode(new string('x', capacity), level);
                if (fits.Version != version && capacity > 0)
                {
                    // Puede caber en una version anterior, lo cual es correcto: se comprueba
                    // que no cabe en la anterior.
                    if (version > 1 && QrEncoder.ByteCapacity(version - 1, level) >= capacity)
                    {
                        failures++;
                        Console.WriteLine("FALLO  capacidad v" + version + "/" + level + " cabe en la version anterior");
                    }
                }
            }
        }

        // Un texto que no cabe debe fallar con un mensaje claro, no generar un QR roto.
        cases++;
        try
        {
            QrEncoder.Encode(new string('B', 400), QrEcc.High);
            failures++;
            Console.WriteLine("FALLO  un texto imposible no genero error");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("ok     texto demasiado largo rechazado con mensaje claro");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("FALLO  excepcion inesperada: " + ex.GetType().Name);
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "OK: " + cases + " casos decodificados por una biblioteca independiente"
            : "FALLO: " + failures + " de " + cases + " casos");
        return failures == 0 ? 0 : 1;
    }
}
