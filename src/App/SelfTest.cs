// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - autocomprobacion (NetForge.exe /self-test).
//
// Comprueba la logica pura de la aplicacion sin tocar el sistema: nada de firewall, nada de
// levantar redes. Lo que si se prueba de verdad es lo que suele romperse: la validacion de
// la configuracion, las matematicas de subred, la jaula de rutas, el lector de formularios
// (con un caso malicioso incluido), el Reed-Solomon del QR y la maquina de estados del
// Daemon Guard contra un motor falso.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using NetForge.Core;
using NetForge.Firewall;
using NetForge.Ftp;
using NetForge.Native;
using NetForge.Net;
using NetForge.Qr;
using NetForge.Ui;
using NetForge.Web;

namespace NetForge.App
{
    internal static class SelfTest
    {
        private static readonly StringBuilder Report = new StringBuilder();
        private static int _checks;
        private static int _failures;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        internal static int Run()
        {
            try
            {
                AttachConsole(-1);
            }
            catch (Exception)
            {
                try
                {
                    AllocConsole();
                }
                catch (Exception)
                {
                    // Sin consola: el resultado queda en el fichero.
                }
            }

            Write("NetForge Studio - autocomprobacion");
            Write("==================================");
            Write("");

            Section("Configuracion de red");
            TestHotspotConfig();

            Section("Matematicas de subred");
            TestSubnetMath();

            Section("Firewall");
            TestFirewallNames();

            Section("Base de fabricantes (OUI)");
            TestOui();

            Section("Codigo QR");
            TestQr();

            Section("Reed-Solomon");
            TestReedSolomon();

            Section("Jaula de rutas y nombres");
            TestPathJail();

            Section("Subidas multipart");
            TestMultipart();

            Section("Servidor FTP (extremo a extremo)");
            TestFtpServer();
            TestFtpAnonymous();

            Section("Portal web (extremo a extremo)");
            TestPortal();

            Section("Ajustes, idioma y filtro");
            TestSettings();

            Section("Motores: arranques y paradas a medias");
            TestEngineFailureCleanup();

            Section("Radar en vivo");
            TestRadar();

            Section("Panel de control (ventana real)");
            TestUserInterface();

            Section("Daemon Guard");
            TestDaemonGuard();

            Section("Sistema");
            TestSystem();

            Write("");
            Write("==================================");
            if (_failures == 0)
            {
                Write("OK: " + _checks + " comprobaciones superadas");
            }
            else
            {
                Write("FALLO: " + _failures + " de " + _checks + " comprobaciones");
            }

            FlushToFile();
            return _failures == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ pruebas

        private static void TestHotspotConfig()
        {
            ExpectAccept("configuracion valida", "MiRed", "clave12345");
            ExpectAccept("SSID de 32 bytes exactos", new string('a', 32), "clave12345");
            ExpectAccept("clave de 63 caracteres", "MiRed", new string('k', 63));
            ExpectAccept("clave hexadecimal de 64", "MiRed", new string('a', 64));

            ExpectReject("SSID vacio", string.Empty, "clave12345");
            ExpectReject("SSID de 33 bytes", new string('a', 33), "clave12345");
            ExpectReject("SSID de 11 emojis (44 bytes UTF-8)", new string('\u2603', 11), "clave12345");
            ExpectReject("clave de 7 caracteres", "MiRed", "corta12");
            ExpectReject("clave de 64 caracteres no hexadecimales", "MiRed", new string('z', 64));
            ExpectReject("clave vacia", "MiRed", string.Empty);
            ExpectReject("comillas en el SSID", "Mi\"Red", "clave12345");
            ExpectReject("comillas en la clave", "MiRed", "cla\"ve12345");
            ExpectReject("salto de linea en el SSID", "Mi\nRed", "clave12345");
        }

        private static void TestSubnetMath()
        {
            Expect("mascara /24", InterfaceLocator.PrefixFromMask(IPAddress.Parse("255.255.255.0")) == 24);
            Expect("mascara /30", InterfaceLocator.PrefixFromMask(IPAddress.Parse("255.255.255.252")) == 30);
            Expect("mascara /16", InterfaceLocator.PrefixFromMask(IPAddress.Parse("255.255.0.0")) == 16);
            Expect("mascara /0", InterfaceLocator.PrefixFromMask(IPAddress.Parse("0.0.0.0")) == 0);
            Expect("mascara /32", InterfaceLocator.PrefixFromMask(IPAddress.Parse("255.255.255.255")) == 32);
            Expect("mascara no contigua corta en el primer hueco", InterfaceLocator.PrefixFromMask(IPAddress.Parse("255.0.255.0")) == 8);
            Expect("mascara nula usa /24", InterfaceLocator.PrefixFromMask(null) == 24);
            Expect("construccion /24", InterfaceLocator.MaskFromPrefix(24).Equals(IPAddress.Parse("255.255.255.0")));
            Expect("construccion /30", InterfaceLocator.MaskFromPrefix(30).Equals(IPAddress.Parse("255.255.255.252")));
            Expect("misma subred", InterfaceLocator.SameSubnet(IPAddress.Parse("192.168.137.1"), IPAddress.Parse("192.168.137.50"), IPAddress.Parse("255.255.255.0")));
            Expect("distinta subred", !InterfaceLocator.SameSubnet(IPAddress.Parse("192.168.137.1"), IPAddress.Parse("192.168.138.1"), IPAddress.Parse("255.255.255.0")));
            Expect("subred con IPv6 no se confunde", !InterfaceLocator.SameSubnet(IPAddress.Parse("::1"), IPAddress.Parse("192.168.137.1"), IPAddress.Parse("255.255.255.0")));

            IList<IPAddress> hosts = InterfaceLocator.HostsInSubnet(IPAddress.Parse("192.168.1.1"), IPAddress.Parse("255.255.255.252"), 100);
            Expect("subred /30 genera 2 direcciones", hosts.Count == 2);
            hosts = InterfaceLocator.HostsInSubnet(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("255.0.0.0"), 10);
            Expect("subred /8 se limita al tope indicado", hosts.Count == 10);
            hosts = InterfaceLocator.HostsInSubnet(IPAddress.Parse("192.168.1.5"), IPAddress.Parse("255.255.255.255"), 100);
            Expect("subred /32 no genera direcciones", hosts.Count == 0);
        }

        private static void TestFirewallNames()
        {
            string name = FirewallRuleManager.RuleNameFor("AA:BB:CC:DD:EE:FF");
            Expect("nombre de regla estable", name == "NetForge.Ban.AA-BB-CC-DD-EE-FF");
            Expect("nombre de regla sin caracteres raros", name.IndexOf(':') < 0 && name.Length < 200);
            Expect("nombre para MAC vacia", FirewallRuleManager.RuleNameFor(string.Empty).StartsWith("NetForge"));
        }

        private static void TestOui()
        {
            OuiLookupResult random = OuiDatabase.Lookup("02:11:22:33:44:55");
            Expect("MAC aleatorizada detectada", random.IsRandomized && random.Vendor.Length == 0);
            OuiLookupResult local = OuiDatabase.Lookup("1A:00:00:00:00:01");
            Expect("MAC de administracion local detectada", local.IsRandomized);
            OuiLookupResult intel = OuiDatabase.Lookup("48:51:C5:ED:33:11");
            Expect("fabricante encontrado para un prefijo registrado", intel.Vendor.Length > 0);
            Write("   48:51:C5 -> " + intel.Describe);
            OuiLookupResult cisco = OuiDatabase.Lookup("00:00:0C:11:22:33");
            Expect("prefijo clasico encontrado", cisco.Vendor.Length > 0);
            Write("   00:00:0C -> " + cisco.Describe);
            Expect("MAC mal formada no revienta", OuiDatabase.Lookup("no-es-una-mac").Vendor.Length == 0);
            Write("   prefijos cargados: " + OuiDatabase.VendorCount);
            if (!OuiDatabase.Available)
            {
                Write("   aviso: sin base de datos (" + OuiDatabase.LoadError + ")");
            }
        }

        private static void TestQr()
        {
            QrCode code = QrEncoder.Encode("http://192.168.137.1:8080/", QrEcc.Medium);
            Expect("tamano coherente con la version", code.Size == code.Version * 4 + 17);
            Expect("los tres patrones de posicion existen", HasFinder(code, 0, 0) && HasFinder(code, code.Size - 7, 0) && HasFinder(code, 0, code.Size - 7));
            Expect("modulo oscuro obligatorio", code[code.Size - 8, 8]);

            bool timing = true;
            for (int i = 8; i < code.Size - 8; i++)
            {
                if (code[6, i] != (i % 2 == 0) || code[i, 6] != (i % 2 == 0))
                {
                    timing = false;
                    break;
                }
            }

            Expect("patrones de sincronizacion correctos", timing);

            // Las dos copias de la informacion de formato van en posiciones distintas (una
            // alrededor de la esquina superior izquierda y otra repartida abajo y arriba a la
            // derecha), asi que se leen con la misma regla de colocacion y se comparan bit a bit.
            bool formatCopies = true;
            for (int i = 0; i < 15 && formatCopies; i++)
            {
                if (ReadFormatBit(code, i, true) != ReadFormatBit(code, i, false))
                {
                    formatCopies = false;
                }
            }

            Expect("las dos copias de informacion de formato coinciden", formatCopies);

            QrCode again = QrEncoder.Encode("http://192.168.137.1:8080/", QrEcc.Medium);
            Expect("codificacion determinista", again.Mask == code.Mask && sameMatrix(code, again));

            QrCode bigger = QrEncoder.Encode("http://192.168.137.1:8080/#NetForge-transferencia-local-de-archivos", QrEcc.Medium);
            Expect("texto mas largo sube de version", bigger.Version > code.Version);

            QrCode high = QrEncoder.Encode("http://192.168.137.1:8080/", QrEcc.High);
            Expect("mas correccion usa mas version", high.Version >= code.Version);

            bool threw = false;
            try
            {
                QrEncoder.Encode(new string('x', 400), QrEcc.High);
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            Expect("texto imposible rechazado", threw);
            Write("   version " + code.Version + " con mascara " + code.Mask + " para la URL del portal");
        }

        private static void TestReedSolomon()
        {
            byte[] generator = ReedSolomon.Generator(10);
            byte[] data = Encoding.ASCII.GetBytes("NetForge!!");
            byte[] ecc = ReedSolomon.Remainder(data, generator);

            byte[] codeword = new byte[data.Length + ecc.Length];
            Array.Copy(data, codeword, data.Length);
            Array.Copy(ecc, 0, codeword, data.Length, ecc.Length);

            // El polinomio generador tiene por raices alpha^0 .. alpha^(grado-1): si el resto
            // esta bien calculado, el codigo completo se anula en todas ellas.
            bool zero = true;
            for (int power = 0; power < generator.Length && zero; power++)
            {
                byte evaluation = EvaluateCodeword(codeword, power);
                if (evaluation != 0)
                {
                    zero = false;
                    Write("   sindrome " + power + " = " + evaluation + " (deberia ser 0)");
                }
            }

            Expect("todos los sindromes nulos (codigo de Reed-Solomon valido)", zero);

            // La comprobacion solo vale si de verdad detecta errores: se corrompe un byte y el
            // sindrome debe dejar de ser nulo.
            byte[] damaged = (byte[])codeword.Clone();
            damaged[0] ^= 0x01;
            bool detected = false;
            for (int power = 0; power < generator.Length; power++)
            {
                if (EvaluateCodeword(damaged, power) != 0)
                {
                    detected = true;
                    break;
                }
            }

            Expect("un byte corrompido si se detecta", detected);
        }

        private static void TestPathJail()
        {
            string root = CreateTempFolder("jail");
            try
            {
                File.WriteAllText(Path.Combine(root, "dentro.txt"), "ok");
                Directory.CreateDirectory(Path.Combine(root, "sub"));
                File.WriteAllText(Path.Combine(root, "sub", "anidado.txt"), "ok");

                FilePortal portal = new FilePortal(new FilePortalOptions { RootPath = root, BindAddress = IPAddress.Loopback });
                string resolved;

                Expect("archivo dentro de la carpeta", portal.TryResolve("dentro.txt", out resolved) && resolved.EndsWith("dentro.txt", StringComparison.OrdinalIgnoreCase));
                Expect("subcarpeta dentro de la carpeta", portal.TryResolve("sub/anidado.txt", out resolved) && resolved.EndsWith("anidado.txt", StringComparison.OrdinalIgnoreCase));
                Expect("subida con .. rechazada", !portal.TryResolve("../fuera.txt", out resolved));
                Expect("subida con ..\\ rechazada", !portal.TryResolve("..\\fuera.txt", out resolved));
                Expect("escape encadenado rechazado", !portal.TryResolve("sub/../../fuera.txt", out resolved));
                Expect("ruta absoluta de Windows rechazada", !portal.TryResolve("C:\\Windows\\win.ini", out resolved));
                Expect("ruta con barra inicial rechazada", !portal.TryResolve("/dentro.txt", out resolved));
                Expect("nombre vacio rechazado", !portal.TryResolve(string.Empty, out resolved));
                portal.Dispose();

                Expect("nombre con ruta se queda en el nombre", FilePortal.SanitizeName("..\\..\\evil.txt") == "evil.txt");
                Expect("nombre con dos puntos limpio", FilePortal.SanitizeName("C:evil.txt") == "evil.txt");
                Expect("nombre reservado rechazado", FilePortal.SanitizeName("..") == string.Empty);
                string unique = FilePortal.UniqueName(root, "dentro.txt");
                Expect("nombre repetido no sobrescribe", unique != "dentro.txt" && unique.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                Cleanup(root);
            }
        }

        private static void TestMultipart()
        {
            string root = CreateTempFolder("upload");
            try
            {
                const string boundary = "----NetForgeBoundary123";
                MemoryStream body = new MemoryStream();
                string text = "--" + boundary + "\r\n" +
                              "Content-Disposition: form-data; name=\"campo\"\r\n\r\n" +
                              "valor\r\n" +
                              "--" + boundary + "\r\n" +
                              "Content-Disposition: form-data; name=\"archivos\"; filename=\"notas.txt\"\r\n" +
                              "Content-Type: text/plain\r\n\r\n" +
                              "linea1\r\nlinea2\r\n--" + boundary + "-sufijo\r\nlinea3\r\n" +
                              "--" + boundary + "\r\n" +
                              "Content-Disposition: form-data; name=\"archivos\"; filename=\"..\\..\\escape.txt\"\r\n\r\n" +
                              "contenido malicioso\r\n" +
                              "--" + boundary + "--\r\n";
                byte[] payload = Encoding.UTF8.GetBytes(text);
                body.Write(payload, 0, payload.Length);
                body.Position = 0;

                List<string> saved = MultipartReader.Read(body, boundary, root);
                Expect("se guardaron los dos archivos", saved.Count == 2);

                string first = Path.Combine(root, "notas.txt");
                Expect("archivo guardado con su nombre", File.Exists(first));
                if (File.Exists(first))
                {
                    string content = File.ReadAllText(first);
                    Expect("contenido intacto (incluye texto parecido al delimitador)",
                        content == "linea1\r\nlinea2\r\n--" + boundary + "-sufijo\r\nlinea3");
                }

                Expect("nombre con .. se aplano", File.Exists(Path.Combine(root, "escape.txt")));
                Expect("no se escribio fuera de la carpeta", !File.Exists(Path.Combine(Path.GetDirectoryName(root), "escape.txt")));
            }
            finally
            {
                Cleanup(root);
            }
        }

        /// <summary>
        /// Habla FTP de verdad contra el servidor, en el puerto local, para comprobar el
        /// protocolo completo: autenticacion, modo pasivo, listado, descarga, subida, jaula de
        /// rutas en vivo y que borrar este prohibido por defecto.
        /// </summary>
        private static void TestFtpServer()
        {
            string root = CreateTempFolder("ftp");
            FtpServer server = null;
            try
            {
                File.WriteAllText(Path.Combine(root, "hola.txt"), "contenido de prueba");

                FtpOptions options = new FtpOptions
                {
                    RootPath = root,
                    BindAddress = IPAddress.Loopback,
                    Port = 0,
                    UserName = "netforge",
                    Password = "clave12345",
                    AllowUpload = true,
                    AllowDelete = false
                };

                server = new FtpServer(options);
                string error;
                if (!server.Start(out error))
                {
                    Expect("el servidor FTP arranca: " + error, false);
                    return;
                }

                Expect("el servidor FTP escucha en un puerto libre", server.Port > 0);

                using (System.Net.Sockets.TcpClient control = new System.Net.Sockets.TcpClient())
                {
                    control.Connect(IPAddress.Loopback, server.Port);
                    control.ReceiveTimeout = 15000;
                    NetworkStream stream = control.GetStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.NewLine = "\r\n";
                    writer.AutoFlush = true;

                    Expect("saludo inicial 220", Line(reader).StartsWith("220"));

                    writer.WriteLine("USER netforge");
                    Expect("usuario reconocido 331", Line(reader).StartsWith("331"));

                    writer.WriteLine("PASS incorrecta");
                    Expect("clave incorrecta rechazada 530", Line(reader).StartsWith("530"));

                    writer.WriteLine("PASS clave12345");
                    Expect("sesion iniciada 230", Line(reader).StartsWith("230"));

                    writer.WriteLine("PWD");
                    Expect("carpeta actual 257", Line(reader).StartsWith("257"));

                    writer.WriteLine("RETR ../fuera.txt");
                    Expect("la jaula de rutas rechaza .. en vivo", Line(reader).StartsWith("550"));

                    string listing;
                    string preliminary;
                    string final;
                    PassiveTransfer(reader, writer, "LIST", null, out listing, out preliminary, out final, out _);
                    Expect("LIST responde 150 y 226", preliminary.StartsWith("150") && final.StartsWith("226"));
                    Expect("el listado incluye el archivo", listing.Contains("hola.txt"));

                    byte[] downloaded;
                    string ignored;
                    PassiveTransfer(reader, writer, "RETR hola.txt", null, out ignored, out preliminary, out final, out downloaded);
                    Expect("descarga integra", Encoding.UTF8.GetString(downloaded) == "contenido de prueba");

                    byte[] payload = Encoding.UTF8.GetBytes("archivo subido desde el movil");
                    PassiveTransfer(reader, writer, "STOR subido.txt", payload, out ignored, out preliminary, out final, out _);
                    Expect("subida confirmada", final.StartsWith("226"));
                    Expect("el archivo subido existe en disco",
                        File.Exists(Path.Combine(root, "subido.txt")) &&
                        File.ReadAllText(Path.Combine(root, "subido.txt")) == "archivo subido desde el movil");

                    writer.WriteLine("SIZE hola.txt");
                    string size = Line(reader);
                    long realSize = new FileInfo(Path.Combine(root, "hola.txt")).Length;
                    Expect("SIZE devuelve el tamano real",
                        size.StartsWith("213") && size.Contains(realSize.ToString(CultureInfo.InvariantCulture)));

                    writer.WriteLine("DELE hola.txt");
                    Expect("borrar esta prohibido por defecto", Line(reader).StartsWith("550"));
                    Expect("el archivo sigue en su sitio", File.Exists(Path.Combine(root, "hola.txt")));

                    writer.WriteLine("QUIT");
                    Expect("despedida 221", Line(reader).StartsWith("221"));
                }

                Expect("el contador de bytes crece", server.BytesTransferred > 0);
            }
            finally
            {
                if (server != null)
                {
                    server.Dispose();
                }

                Cleanup(root);
            }
        }

        /// <summary>
        /// FTP en modo anonimo: con el nombre de usuario vacio en los ajustes, el servidor
        /// tiene que dejar entrar sin clave y seguir enjaulado. Es la opcion para redes locales
        /// de confianza, y si se rompiera el telefono se quedaria fuera sin saber por que.
        /// </summary>
        private static void TestFtpAnonymous()
        {
            string root = CreateTempFolder("ftp-anon");
            FtpServer server = null;
            try
            {
                File.WriteAllText(Path.Combine(root, "libre.txt"), "acceso anonimo");

                FtpOptions options = new FtpOptions
                {
                    RootPath = root,
                    BindAddress = IPAddress.Loopback,
                    Port = 0,
                    UserName = "anonymous",
                    Password = "lo-que-sea",
                    AllowAnonymous = true
                };

                server = new FtpServer(options);
                string error;
                if (!server.Start(out error))
                {
                    Expect("el servidor FTP anonimo arranca: " + error, false);
                    return;
                }

                using (System.Net.Sockets.TcpClient control = new System.Net.Sockets.TcpClient())
                {
                    control.Connect(IPAddress.Loopback, server.Port);
                    control.ReceiveTimeout = 15000;
                    NetworkStream stream = control.GetStream();
                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                    StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.NewLine = "\r\n";
                    writer.AutoFlush = true;

                    Expect("saludo inicial 220 (anonimo)", Line(reader).StartsWith("220"));

                    writer.WriteLine("USER anonymous");
                    Expect("anonimo entra sin clave 230", Line(reader).StartsWith("230"));

                    writer.WriteLine("PWD");
                    Expect("sesion anonima util 257", Line(reader).StartsWith("257"));

                    byte[] downloaded;
                    string listing;
                    string ignored;
                    string preliminary;
                    string final;
                    PassiveTransfer(reader, writer, "LIST", null, out listing, out preliminary, out final, out _);
                    Expect("el anonimo lista", final.StartsWith("226") && listing.Contains("libre.txt"));

                    PassiveTransfer(reader, writer, "RETR libre.txt", null, out ignored, out preliminary, out final, out downloaded);
                    Expect("el anonimo descarga integra", Encoding.UTF8.GetString(downloaded) == "acceso anonimo");

                    writer.WriteLine("QUIT");
                    Expect("despedida 221 (anonimo)", Line(reader).StartsWith("221"));
                }
            }
            finally
            {
                if (server != null)
                {
                    server.Dispose();
                }

                Cleanup(root);
            }
        }

        /// <summary>
        /// Comprueba el portal web contra el servidor real: listado, descarga, peticion con
        /// Range (imprescindible para ver video) y subida de un formulario multipart.
        /// Si el equipo no permite escuchar en la direccion local sin permisos de
        /// administrador, se informa y se omite en vez de declarar un fallo falso.
        /// </summary>
        private static void TestPortal()
        {
            string root = CreateTempFolder("portal");
            FilePortal portal = null;
            try
            {
                File.WriteAllText(Path.Combine(root, "notas.txt"), "0123456789");

                int port = 18080 + (new Random().Next(0, 400));
                portal = new FilePortal(new FilePortalOptions
                {
                    RootPath = root,
                    BindAddress = IPAddress.Loopback,
                    Port = port,
                    AllowUpload = true,
                    AllowDelete = false
                });

                string error;
                if (!portal.Start(out error))
                {
                    Write("   omitido: no se puede escuchar en local sin permisos (" + error + ")");
                    return;
                }

                string baseUrl = "http://127.0.0.1:" + portal.Port + "/";
                Expect("el portal arranca", portal.IsRunning);

                string index = HttpText(baseUrl, "GET", null, null);
                Expect("la pagina lista el archivo", index.Contains("notas.txt"));
                Expect("la pagina no filtra el nombre del portal", index.Contains("<title>"));

                byte[] file = HttpBytes(baseUrl + "f/notas.txt", "GET", null, null, out _);
                Expect("descarga por HTTP exacta", Encoding.UTF8.GetString(file) == "0123456789");

                Dictionary<string, string> rangeHeaders = new Dictionary<string, string>();
                rangeHeaders["Range"] = "bytes=2-4";
                int status;
                byte[] partial = HttpBytes(baseUrl + "f/notas.txt", "GET", null, rangeHeaders, out status);
                Expect("peticion con Range devuelve 206", status == 206);
                Expect("el trozo pedido es correcto", Encoding.UTF8.GetString(partial) == "234");

                const string boundary = "----NetForgeTest";
                byte[] body = Encoding.UTF8.GetBytes(
                    "--" + boundary + "\r\n" +
                    "Content-Disposition: form-data; name=\"archivos\"; filename=\"movil.txt\"\r\n" +
                    "Content-Type: text/plain\r\n\r\n" +
                    "subido por HTTP\r\n" +
                    "--" + boundary + "--\r\n");
                Dictionary<string, string> uploadHeaders = new Dictionary<string, string>();
                uploadHeaders["Content-Type"] = "multipart/form-data; boundary=" + boundary;
                HttpBytes(baseUrl + "subir", "POST", body, uploadHeaders, out _);
                Expect("subida multipart por HTTP",
                    File.Exists(Path.Combine(root, "movil.txt")) &&
                    File.ReadAllText(Path.Combine(root, "movil.txt")) == "subido por HTTP");

                byte[] outside = HttpBytes(baseUrl + "f/..%2F..%2Fescape.txt", "GET", null, null, out status);
                Expect("el portal no deja salir de la carpeta", status == 404);
                Expect("la respuesta de error no revienta", outside != null);
            }
            finally
            {
                if (portal != null)
                {
                    portal.Dispose();
                }

                Cleanup(root);
            }
        }

        private static void TestDaemonGuard()
        {
            FakeEngine engine = new FakeEngine();
            MachineProfile profile = new MachineProfile { HasInternet = false };
            HotspotController controller = new HotspotController(profile, new IHotspotEngine[] { engine });
            HotspotConfig config = new HotspotConfig { Ssid = "NetForge", Passphrase = "clave12345" };

            Expect("la red arranca con el motor inyectado", controller.Start(config) && controller.IsRunning);
            Expect("el motor arranco una vez", engine.StartCount == 1);

            using (DaemonGuard guard = new DaemonGuard(controller))
            {
                guard.IntervalMs = 150;
                guard.MaxConsecutiveFailures = 2;
                int gaveUp = 0;
                int resurrected = 0;
                guard.GaveUp += delegate { gaveUp++; };
                guard.Resurrected += delegate { resurrected++; };
                guard.Arm();

                // 1) Caida simulada: el guard debe levantarla.
                engine.Alive = false;
                Expect("el guard detecta la caida y restaura", WaitFor(delegate { return resurrected >= 1; }, 6000));
                Expect("el motor volvio a arrancar", engine.StartCount >= 2);
                Expect("el guard sigue armado", guard.Armed);

                // 2) Fallos repetidos: debe rendirse con aviso, sin bucle infinito.
                engine.FailNextStart = true;
                engine.Alive = false;
                Expect("el guard se rinde tras varios fallos", WaitFor(delegate { return gaveUp >= 1; }, 20000));

                // 3) Detencion manual: el guard no puede resucitar lo que se apago a proposito.
                engine.FailNextStart = false;
                int before = guard.TotalResurrections;
                controller.Stop();
                guard.Disarm();
                System.Threading.Thread.Sleep(600);
                Expect("una red detenida a mano no se resucita", controller.IsRunning == false && guard.TotalResurrections == before);
            }

            controller.Dispose();
        }

        /// <summary>
        /// Construye la ventana de verdad y la pinta, en un hilo STA, para detectar lo que
        /// ningun analisis estatico ve: una excepcion al montar los controles, una pestana
        /// vacia o una ventana que se dibuja en blanco.
        ///
        /// Se ejecuta sobre el contexto real (bandeja, preferencias, sondeo de capacidades),
        /// pero sin tocar el sistema: no arranca la red ni los servidores de archivos. La
        /// ventana aparece un instante durante la comprobacion y se destruye al terminar.
        /// </summary>
        /// <summary>
        /// Los ajustes se guardan en un fichero de texto que el usuario puede editar a mano, asi
        /// que lo importante no es solo que se guarden: es que un valor imposible escrito a mano
        /// no pueda dejar la aplicacion auditando la red cada cero segundos ni escuchando en un
        /// puerto reservado. Se prueba con la carpeta redirigida a un temporal para no tocar las
        /// preferencias reales de quien ejecuta la comprobacion.
        /// </summary>
        private static void TestSettings()
        {
            string folder = CreateTempFolder("ajustes");
            string previous = AppSettings.OverrideStoreFolder;
            string previousLanguage = Strings.Language;
            try
            {
                AppSettings.OverrideStoreFolder = folder;

                AppSettings saved = new AppSettings();
                saved.Language = "en";
                saved.PreferTwoGhz = false;
                saved.RadarSeconds = 7;
                saved.GuardSeconds = 4;
                saved.PortalPort = 9000;
                saved.FtpPort = 2121;
                saved.Engine = EnginePreference.OffGrid;
                saved.Ssid = "Rede De Prueba";
                saved.Save();

                Expect("los ajustes se guardan en un fichero", File.Exists(AppSettings.StorePath));

                AppSettings loaded = AppSettings.Load();
                Expect("el idioma vuelve tal cual", loaded.Language == "en");
                Expect("la banda vuelve tal cual", !loaded.PreferTwoGhz);
                Expect("el intervalo del radar vuelve", loaded.RadarSeconds == 7);
                Expect("el intervalo del vigilante vuelve", loaded.GuardSeconds == 4);
                Expect("el puerto del portal vuelve", loaded.PortalPort == 9000);
                Expect("el puerto del ftp vuelve", loaded.FtpPort == 2121);
                Expect("el motor elegido vuelve", loaded.Engine == EnginePreference.OffGrid);
                Expect("el nombre de la red vuelve", loaded.Ssid == "Rede De Prueba");

                // Valores absurdos escritos a mano: se sanean al cargar, no al fallar.
                File.WriteAllText(AppSettings.StorePath,
                    "ssid=\nftpuser=\nlanguage=kl\nradar=0\nguard=9999\nportalport=80\nftpport=70000\nengine=42\nband=5\n",
                    Encoding.UTF8);
                AppSettings wild = AppSettings.Load();
                Expect("un SSID vacio se sustituye por el de fabrica", wild.Ssid.Length > 0);
                Expect("un usuario vacio significa FTP anonimo", wild.FtpUser.Length == 0);
                Expect("un usuario con caracteres prohibidos se limpia", AppSettings.SanitizeUserName("pe pe").Length > 0);
                Expect("un idioma inventado pasa a automatico", wild.Language.Length == 0);
                Expect("el radar no puede ir a cero", wild.RadarSeconds == AppSettings.MinRadarSeconds);
                Expect("ni pasarse de lento", wild.GuardSeconds == AppSettings.MaxGuardSeconds);
                Expect("un puerto reservado se sube al minimo", wild.PortalPort == AppSettings.MinPort);
                Expect("un puerto imposible se baja al maximo", wild.FtpPort == AppSettings.MaxPort);
                Expect("un motor inventado vuelve a automatico", wild.Engine == EnginePreference.Auto);
                Expect("la banda de 5 GHz se respeta", !wild.PreferTwoGhz);

                // El idioma de la interfaz tiene que poder forzarse y volver a automatico.
                Strings.ApplyLanguage("en");
                Expect("forzando ingles, los textos salen en ingles", Strings.T("tab.clients") == "Clients");
                Strings.ApplyLanguage("es");
                Expect("forzando espanol, los textos salen en espanol", Strings.T("tab.clients") == "Clientes");
                Strings.ApplyLanguage(string.Empty);
                Expect("sin forzar nada, se sigue el idioma de Windows",
                    Strings.T("tab.clients") == (Strings.IsSpanish ? "Clientes" : "Clients"));

                // Filtro de la lista de clientes: se busca por lo que el usuario ve.
                LanClient client = new LanClient();
                client.Mac = "48:51:C5:ED:33:11";
                client.Address = IPAddress.Parse("192.168.137.20");
                client.HostName = "portatil-salon";
                client.Vendor = "Intel Corporate";

                Expect("sin filtro se ve todo", MainForm.MatchesFilter(client, string.Empty));
                Expect("filtra por IP", MainForm.MatchesFilter(client, "137.20"));
                Expect("filtra por MAC ignorando mayusculas", MainForm.MatchesFilter(client, "48:51:c5"));
                Expect("filtra por fabricante", MainForm.MatchesFilter(client, "intel"));
                Expect("filtra por nombre del equipo", MainForm.MatchesFilter(client, "salon"));
                Expect("y descarta lo que no coincide", !MainForm.MatchesFilter(client, "raspberry"));
                Expect("un cliente nulo no rompe el filtro", !MainForm.MatchesFilter(null, "algo"));
            }
            finally
            {
                AppSettings.OverrideStoreFolder = previous;
                Strings.ApplyLanguage(previousLanguage);
                Cleanup(folder);
            }
        }

        /// <summary>
        /// Camino que casi nunca se prueba y que deja el peor rastro: un motor que falla
        /// DESPUES de haber encendido la radio, y un motor al que no se le confirma la parada.
        /// En el primer caso el controlador probaria el siguiente motor con el anterior
        /// emitiendo; en el segundo, la app diria que la red esta apagada cuando no lo esta.
        /// </summary>
        private static void TestEngineFailureCleanup()
        {
            MachineProfile profile = new MachineProfile { HasInternet = false };
            HotspotConfig config = new HotspotConfig { Ssid = "NetForge", Passphrase = "clave12345" };

            FakeEngine half = new FakeEngine();
            half.PartialStart = true;
            using (HotspotController controller = new HotspotController(profile, new IHotspotEngine[] { half }))
            {
                Expect("un arranque a medias no se da por bueno", !controller.Start(config));
                Expect("el motor a medias se deshace (no queda la radio emitiendo)", half.StopCount == 1 && !half.Alive);
                Expect("el error dice que fue lo que fallo",
                    controller.LastError.Length > 0 && controller.LastError.Contains("fallo simulado a medias"));
            }

            FakeEngine stubborn = new FakeEngine();
            using (stubborn)
            {
                stubborn.Start(config);
                Expect("el motor falso arranca", stubborn.Alive);

                stubborn.FailStop = true;
                stubborn.Stop();
                HotspotStatus live = stubborn.GetStatus();
                Expect("una parada no confirmada no se declara apagada", live.State != EngineState.Idle);
                Expect("y la verdad sigue estando en el estado (sigue emitiendo)", stubborn.Alive);
                Expect("el motivo queda registrado", live.LastError.Length > 0);

                stubborn.FailStop = false;
                stubborn.Stop();
                Expect("al segundo intento si se apaga", !stubborn.Alive && stubborn.GetStatus().State == EngineState.Idle);
            }
        }

        private static void TestRadar()
        {
            // El radar se prueba con proveedores inyectados y sin gestor de firewall: no lee
            // la tabla ARP de este equipo, no sondea la red y no puede aplicar ningun bloqueo
            // real. Lo que se comprueba es su maquina de estados: temporizador, identidad por
            // MAC, enriquecimiento con la base de fabricantes y parada limpia.
            List<EngineClient> reported = new List<EngineClient>();
            EngineClient known = new EngineClient();
            known.Mac = "48:51:C5:ED:33:11";
            known.HostNames = new List<string>();
            known.HostNames.Add("portatil-de-prueba");
            reported.Add(known);

            EngineClient anonymous = new EngineClient();
            anonymous.Mac = "02:11:22:33:44:55";
            reported.Add(anonymous);

            EngineClient junk = new EngineClient();
            junk.Mac = string.Empty;
            reported.Add(junk);

            int enforced = 0;
            FirewallBanList bans = new FirewallBanList(null);
            RadarService radar = new RadarService(
                delegate { return null; },
                delegate { return reported; },
                bans,
                delegate (string mac, IPAddress address) { enforced++; });

            try
            {
                radar.IntervalMs = 60;
                radar.Start();
                Expect("el radar arranca", WaitFor(delegate { return radar.ScanCycles >= 2; }, 5000));

                IList<LanClient> snapshot = radar.Snapshot();
                bool sawKnown = false;
                bool sawAnonymous = false;
                bool allConfirmed = true;
                foreach (LanClient client in snapshot)
                {
                    if (client.Mac == known.Mac)
                    {
                        sawKnown = true;
                        Expect("el nombre que reporta el motor llega al cliente", client.HostName == "portatil-de-prueba");
                        Expect("el fabricante se resuelve con la base OUI",
                            client.Vendor.Length > 0 && client.Vendor == OuiDatabase.Lookup(client.Mac).Vendor);
                    }

                    if (client.Mac == anonymous.Mac)
                    {
                        sawAnonymous = true;
                        Expect("una MAC aleatorizada se marca como tal", client.VendorIsRandomized);
                    }

                    allConfirmed = allConfirmed && client.ConfirmedByEngine;
                }

                Expect("el radar ve los equipos que reporta el motor", sawKnown && sawAnonymous);
                Expect("el cliente sin MAC no se inventa", snapshot.Count == 2);
                Expect("marca los equipos como confirmados por el motor", allConfirmed);
                Expect("no bloquea a nadie por su cuenta", enforced == 0);
                Expect("nadie aparece baneado sin estar en la lista", !bans.IsBanned(known.Mac));

                // Un equipo recien visto no puede salir ya en la lista como si llevara minutos.
                Expect("la primera vez visto y la ultima coinciden al aparecer",
                    Math.Abs((snapshot[0].FirstSeenUtc - snapshot[0].LastSeenUtc).TotalSeconds) < 5);

                radar.Stop();
                int cycles = radar.ScanCycles;
                System.Threading.Thread.Sleep(300);
                Expect("parar el radar detiene la auditoria", radar.ScanCycles == cycles);

                radar.Dispose();
                radar.Start();
                System.Threading.Thread.Sleep(200);
                Expect("un radar liberado no vuelve a arrancar", radar.ScanCycles == cycles);
            }
            finally
            {
                radar.Dispose();
            }
        }

        private static void TestUserInterface()
        {
            // La ventana real se monta sobre el contexto real, que lee las preferencias del
            // usuario. Leer esta bien; escribir no: esta comprobacion vigila que el fichero de
            // preferencias de verdad no cambie al construir y destruir la interfaz. Se detecto
            // asi un fallo real, porque pintar los controles disparaba sus manejadores y aquellos
            // guardaban los ajustes cada vez que la ventana se refrescaba.
            string realStore = AppSettings.StorePath;
            bool existedBefore = File.Exists(realStore);
            DateTime stampBefore = existedBefore ? File.GetLastWriteTimeUtc(realStore) : DateTime.MinValue;

            // La ventana existe y sus controles tienen nombre. Se comprueba por nombre y no por
            // tipo, porque una casilla olvidada en una pestana es exactamente el tipo de fallo
            // que no se ve en el codigo y si en la pantalla.
            string[] mandatoryControls = new string[]
            {
                "_filesAuto", "_notify", "_notifyJoin", "_ftpUser",
                "_autostartState", "_repairAutostart", "_clientMenu"
            };
            int missingControls = 0;
            foreach (string controlName in mandatoryControls)
            {
                if (typeof(MainForm).GetField(controlName, BindingFlags.Instance | BindingFlags.NonPublic) == null)
                {
                    missingControls++;
                    Write("   falta el control " + controlName);
                }
            }

            Expect("la ventana tiene los controles nuevos de ajustes y clientes", missingControls == 0);

            // El cazador de textos sin traducir tiene que distinguir una clave de un texto real:
            // si confundiera una direccion IP o una ruta con una clave, la prueba daria falsos fallos.
            Expect("el detector de textos sin traducir reconoce una clave", LooksLikeAnUntranslatedKey("files.copy"));
            Expect("y no confunde una direccion con una clave", !LooksLikeAnUntranslatedKey("ftp://192.168.137.1:2121"));
            Expect("ni una ruta de Windows", !LooksLikeAnUntranslatedKey("C:\\Users\\alguien\\settings.txt"));
            Expect("ni un texto normal", !LooksLikeAnUntranslatedKey("Crear red"));
            Expect("el detector de espanol reconoce una frase olvidada", LooksSpanish("no se pudo generar el codigo"));
            Expect("y no confunde el ingles", !LooksSpanish("Create network"));

            // La interfaz se monta en los dos idiomas: el ingles no se usa en esta maquina, y
            // una traduccion rota solo se veria en el equipo de un usuario que no es el autor.
            foreach (string culture in new string[] { "es-ES", "en-US" })
            {
                string detail = string.Empty;
                int tabs = 0;
                int blankPages = 0;
                int fewestColours = int.MaxValue;
                bool ok = false;

                System.Threading.Thread thread = new System.Threading.Thread(delegate ()
                {
                    try
                    {
                        ok = RenderMainWindow(out detail, out tabs, out blankPages, out fewestColours, culture);
                    }
                    catch (Exception ex)
                    {
                        detail = ex.GetType().Name + ": " + ex.Message;
                    }
                }, 8 * 1024 * 1024);

                thread.IsBackground = true;
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();

                if (!thread.Join(TimeSpan.FromSeconds(90)))
                {
                    Expect("la ventana se construye sin colgarse (" + culture + ")", false);
                    continue;
                }

                Expect("la ventana se construye y se destruye sin excepciones (" + culture + ")", ok);
                if (!ok)
                {
                    Write("   detalle: " + detail);
                    continue;
                }

                Expect("hay seis pestanas (" + culture + ")", tabs == 6);
                Expect("ninguna pestana se queda en blanco (" + culture + ")", blankPages == 0);
                Write("   pestanas pintadas de verdad: " + tabs + " de 6 (minimo " + fewestColours + " colores distintos por pestana)");
            }

            bool existsAfter = File.Exists(realStore);
            DateTime stampAfter = existsAfter ? File.GetLastWriteTimeUtc(realStore) : DateTime.MinValue;
            Expect("la comprobacion no crea el fichero de preferencias del usuario", existsAfter == existedBefore);
            Expect("ni modifica el que ya existe", stampAfter == stampBefore);
        }

        /// <summary>
        /// Se ejecuta dentro del hilo STA. Devuelve false (con el detalle) si la ventana no
        /// llego a existir. El recuento de excepciones lo hace el llamador con Expect.
        /// </summary>
        private static bool RenderMainWindow(out string detail, out int tabCount, out int blankPages, out int fewestColours, string culture)
        {
            detail = string.Empty;
            tabCount = 0;
            blankPages = 0;
            fewestColours = int.MaxValue;
            int untranslated = 0;
            int inspectedColumns = 0;
            int inspectedControls = 0;

            string firstTabTitle = null;
            string languageCode = culture.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
            CultureInfo previous = System.Threading.Thread.CurrentThread.CurrentUICulture;
            CultureInfo previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);

            System.Threading.EventWaitHandle signal =
                new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset);
            NetForgeContext context = null;
            try
            {
                context = new NetForgeContext(false, signal);

                System.Windows.Forms.Form form = null;
                foreach (System.Windows.Forms.Form open in System.Windows.Forms.Application.OpenForms)
                {
                    if (open is MainForm)
                    {
                        form = open;
                        break;
                    }
                }

                if (form == null)
                {
                    detail = "la ventana no quedo registrada en Application.OpenForms";
                    return false;
                }

                Expect("la ventana tiene un tamano utilizable", form.Width >= 800 && form.Height >= 500);
                Expect("la ventana tiene titulo", !string.IsNullOrEmpty(form.Text));

                System.Windows.Forms.TabControl pages = null;
                foreach (System.Windows.Forms.Control control in Descendants(form))
                {
                    System.Windows.Forms.TabControl candidate = control as System.Windows.Forms.TabControl;
                    if (candidate != null)
                    {
                        pages = candidate;
                        break;
                    }
                }

                if (pages == null)
                {
                    detail = "la ventana no tiene pestanas";
                    return false;
                }

                tabCount = pages.TabPages.Count;
                firstTabTitle = pages.TabPages.Count > 0 ? pages.TabPages[0].Text : null;
                foreach (System.Windows.Forms.TabPage page in pages.TabPages)
                {
                    pages.SelectedTab = page;
                    page.PerformLayout();
                    System.Windows.Forms.Application.DoEvents();

                    int visible = 0;
                    foreach (System.Windows.Forms.Control child in Descendants(page))
                    {
                        if (child.Visible && child.Width > 0 && child.Height > 0)
                        {
                            visible++;
                        }
                    }

                    if (visible < 3)
                    {
                        blankPages++;
                        Write("   pestana casi vacia: '" + page.Text + "' con " + visible + " controles");
                    }

                    // Ningun texto visible puede quedarse sin traducir: si una clave no esta en
                    // la tabla, Strings.T devuelve la clave tal cual y el usuario ve "tab.clients".
                    foreach (System.Windows.Forms.Control child in Descendants(page))
                    {
                        if (!child.Visible)
                        {
                            continue;
                        }

                        inspectedControls++;
                        string caption = child.Text;
                        if (LooksLikeAnUntranslatedKey(caption))
                        {
                            untranslated++;
                            Write("   texto sin traducir: '" + caption + "' en " + child.GetType().Name);
                        }

                        // En ingles no puede quedar texto escrito a mano en espanol. Los cuadros
                        // de texto se excluyen a proposito: ahi van rutas, direcciones y el
                        // informe de diagnostico, que es un volcado tecnico en espanol por
                        // diseno (se copia en un informe de error).
                        bool editable = child is System.Windows.Forms.TextBoxBase;
                        if (languageCode == "en" && !editable && LooksSpanish(caption))
                        {
                            untranslated++;
                            Write("   texto en espanol dentro de la version inglesa: '" + caption + "' en " + child.GetType().Name);
                        }

                        System.Windows.Forms.DataGridView grid = child as System.Windows.Forms.DataGridView;
                        if (grid != null)
                        {
                            foreach (System.Windows.Forms.DataGridViewColumn column in grid.Columns)
                            {
                                inspectedColumns++;
                                if (string.IsNullOrEmpty(column.HeaderText) || LooksLikeAnUntranslatedKey(column.HeaderText))
                                {
                                    untranslated++;
                                    Write("   columna sin titulo traducido: '" + column.HeaderText + "'");
                                }
                                else if (languageCode == "en" && LooksSpanish(column.HeaderText))
                                {
                                    untranslated++;
                                    Write("   columna en espanol dentro de la version inglesa: '" + column.HeaderText + "'");
                                }
                            }
                        }
                    }

                    string rendered;
                    int colours = CountRenderedColours(form, out rendered, page.Text, culture);
                    if (!string.IsNullOrEmpty(rendered))
                    {
                        detail = rendered;
                        blankPages++;
                        continue;
                    }

                    if (colours < fewestColours)
                    {
                        fewestColours = colours;
                    }

                    CaptureScrolledBottom(form, page, culture);

                    // Menos de ocho tonos distintos significa que la pestana salio en blanco:
                    // ni texto, ni tabla, ni botones llegaron al mapa de bits.
                    if (colours < 8)
                    {
                        blankPages++;
                        Write("   pestana pintada casi en blanco: '" + page.Text + "' con " + colours + " colores");
                    }
                }

                // Modo Fantasma: la interfaz se destruye y el contexto sigue vivo. Se comprueba
                // aqui porque es logica de la ventana, no del motor de red.
                bool visibleBefore = context.WindowVisible;
                context.CollapseWindow();
                System.Windows.Forms.Application.DoEvents();
                Expect("la ventana estaba visible antes de plegarse", visibleBefore);
                Expect("tras el Modo Fantasma no queda interfaz viva", !context.WindowVisible);

                context.CollapseWindow();
                Expect("plegar dos veces seguidas no rompe nada", !context.WindowVisible);

                Expect("ningun texto se queda sin traducir", untranslated == 0);
                Expect("se revisaron los textos de la ventana y sus columnas", inspectedControls >= 25 && inspectedColumns >= 11);

                // Que la ventana este en el idioma que toca, no solo que sus textos no sean claves.
                // Se guarda la referencia antes de plegarla: al destruir la ventana sus controles
                // desaparecen, pero las paginas que ya tenemos en la mano conservan su titulo.
                string expectedFirstTab = culture.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? "Clientes" : "Clients";
                string actualFirstTab = firstTabTitle ?? "(sin pestanas)";
                Expect("la ventana habla el idioma elegido (" + culture + ")", actualFirstTab == expectedFirstTab);
                if (actualFirstTab != expectedFirstTab)
                {
                    Write("   esperado '" + expectedFirstTab + "', leido '" + actualFirstTab + "'");
                }

                context.ShowWindow();
                System.Windows.Forms.Application.DoEvents();
                Expect("doble clic en la bandeja devuelve la interfaz", context.WindowVisible);

                context.ExitApp();
                Expect("cerrar la aplicacion sin red activa no lanza errores", true);
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (context != null)
                    {
                        context.Dispose();
                    }
                }
                catch (Exception)
                {
                    // La limpieza no debe enmascarar el resultado de la prueba.
                }

                signal.Close();
                System.Threading.Thread.CurrentThread.CurrentUICulture = previous;
                System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture;
            }
        }

        /// <summary>
        /// Pinta la ventana a un mapa de bits y cuenta colores distintos: una ventana que
        /// devuelve un unico color es una ventana que no ha dibujado nada. De paso detecta
        /// que el propio dibujado no lance excepciones.
        /// </summary>
        private static int CountRenderedColours(System.Windows.Forms.Form form, out string error, string pageName, string culture)
        {
            error = string.Empty;
            try
            {
                int width = form.Width;
                int height = form.Height;
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(width, height))
                {
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, width, height));
                    SaveForReview(bitmap, pageName, culture);
                    List<int> colours = new List<int>();
                    for (int y = 0; y < height; y += 11)
                    {
                        for (int x = 0; x < width; x += 11)
                        {
                            int colour = bitmap.GetPixel(x, y).ToArgb();
                            if (!colours.Contains(colour))
                            {
                                colours.Add(colour);
                            }

                            if (colours.Count > 32)
                            {
                                return colours.Count;
                            }
                        }
                    }

                    return colours.Count;
                }
            }
            catch (Exception ex)
            {
                error = "no se pudo pintar la ventana: " + ex.Message;
                return 0;
            }
        }

        /// <summary>
        /// Guarda el mapa de bits de una pestana para poder mirarla con los ojos, no solo contar
        /// colores. Solo se activa si se define NETFORGE_UI_DUMP con una carpeta, para que la
        /// comprobacion normal no escriba nada: sirve para revisar el diseno de la interfaz real
        /// sin abrir la aplicacion (que pide administrador y no se puede automatizar).
        /// </summary>
        /// <summary>
        /// Segunda captura de la pestana con el panel desplazable llevado al fondo. Las pestanas
        /// altas (Ajustes) tienen contenido bajo el pliegue que la captura normal no ve: asi se
        /// revisa tambien lo que solo aparece al hacer scroll. No abre scroll si la pestana cabe.
        /// </summary>
        private static void CaptureScrolledBottom(System.Windows.Forms.Form form, System.Windows.Forms.TabPage page, string culture)
        {
            System.Windows.Forms.Panel scroll = null;
            foreach (System.Windows.Forms.Control child in page.Controls)
            {
                scroll = child as System.Windows.Forms.Panel;
                if (scroll != null && scroll.AutoScroll)
                {
                    break;
                }
            }

            if (scroll == null || !scroll.VerticalScroll.Visible)
            {
                return;
            }

            try
            {
                scroll.AutoScrollPosition = new System.Drawing.Point(0, scroll.VerticalScroll.Maximum);
                System.Windows.Forms.Application.DoEvents();
                page.PerformLayout();

                int width = form.Width;
                int height = form.Height;
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(width, height))
                {
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, width, height));
                    SaveForReview(bitmap, page.Text + "-scroll", culture);
                }

                scroll.AutoScrollPosition = new System.Drawing.Point(0, 0);
                System.Windows.Forms.Application.DoEvents();
            }
            catch
            {
                // La captura de revision nunca puede romper la comprobacion.
            }
        }

        private static void SaveForReview(System.Drawing.Bitmap bitmap, string pageName, string culture)
        {
            string folder = Environment.GetEnvironmentVariable("NETFORGE_UI_DUMP");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            try
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string safe = new string(Array.FindAll(pageName.ToCharArray(), char.IsLetterOrDigit));
                string path = Path.Combine(folder, culture + "-" + safe + ".png");
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Write("   no se pudo guardar la captura de la interfaz: " + ex.Message);
            }
        }

        /// <summary>
        /// Caza un texto escrito a mano en espanol dentro de la version inglesa. Los controles
        /// que pasan por la tabla de textos no pueden fallar aqui, asi que cualquier coincidencia
        /// es una cadena puesta a mano que se olvido traducir.
        /// </summary>
        private static bool LooksSpanish(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string lowered = text.ToLowerInvariant();
            string[] senales = new string[]
            {
                "apagado", "dispositivos", "conexiones", "administrador", "no se pudo",
                "detenid", "sin permisos", "iniciar servidores", "crear red", "baneado",
                "bloqueado", "ajustes", "permisos"
            };

            foreach (string senal in senales)
            {
                if (lowered.IndexOf(senal, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Heuristica para cazar una clave sin traducir: las claves son palabras en minusculas
        /// separadas por puntos ("files.copy"), mientras que los textos reales llevan mayusculas,
        /// espacios, cifras o simbolos. Se exige que todo el texto sea de ese tipo para no
        /// confundir una direccion, una ruta o un nombre de fichero.
        /// </summary>
        private static bool LooksLikeAnUntranslatedKey(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 40 || text.IndexOf('.') <= 0)
            {
                return false;
            }

            foreach (char c in text)
            {
                bool allowed = (c >= 'a' && c <= 'z') || c == '.';
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Recorre un arbol de controles de arriba abajo, sin recursividad profunda.</summary>
        private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control root)
        {
            Stack<System.Windows.Forms.Control> pending = new Stack<System.Windows.Forms.Control>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                System.Windows.Forms.Control current = pending.Pop();
                foreach (System.Windows.Forms.Control child in current.Controls)
                {
                    yield return child;
                    pending.Push(child);
                }
            }
        }

        private static void TestSystem()
        {
            Version version = OsInfo.GetRealVersion();
            Expect("version de Windows creible", version.Major >= 6);
            Write("   " + OsInfo.Describe() + " (soportado: " + (OsInfo.IsSupported() ? "si" : "no") + ")");

            try
            {
                IList<ArpEntry> entries = IpHelper.GetArpTable();
                Expect("tabla ARP legible", entries != null);
                Write("   entradas ARP: " + entries.Count);

                // Contraste con la herramienta del sistema: si la conversion de direcciones
                // estuviera al reves, las IP no coincidirian. Es la red de seguridad contra el
                // error clasico de orden de bytes.
                ShellResult arp = ShellRunner.Run("arp", "-a", 15000);
                if (arp.Ok)
                {
                    int matched = 0;
                    int total = 0;
                    foreach (ArpEntry entry in entries)
                    {
                        if (entry.Address == null || entry.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        if (entry.Address.Equals(IPAddress.Any) || entry.Address.Equals(IPAddress.Broadcast))
                        {
                            continue;
                        }

                        total++;
                        if (arp.StdOut.IndexOf(entry.Address.ToString(), StringComparison.Ordinal) >= 0)
                        {
                            matched++;
                        }
                    }

                    if (total > 0)
                    {
                        Expect("las direcciones ARP coinciden con las del sistema", matched == total);
                        Write("   coincidencias ARP: " + matched + " de " + total);
                    }
                }
            }
            catch (Exception ex)
            {
                Expect("tabla ARP legible (" + ex.Message + ")", false);
            }

            ShellResult result = ShellRunner.Run("cmd", "/c exit 3", 10000);
            Expect("el lanzador propaga el codigo de salida", result.ExitCode == 3);
            Expect("el lanzador detecta el exito", !result.Ok);

            string first = AppSettings.GeneratePassphrase();
            string second = AppSettings.GeneratePassphrase();
            Expect("clave generada con longitud util", first.Length >= 12);
            Expect("dos claves generadas son distintas", first != second);

            try
            {
                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
                Expect("interfaces de red enumerables", adapters != null);
            }
            catch (Exception ex)
            {
                Expect("interfaces de red enumerables (" + ex.Message + ")", false);
            }
        }

        // ------------------------------------------------------------------ utilidades

        private sealed class FakeEngine : HotspotEngineBase
        {
            public bool Alive;
            public bool FailNextStart;
            public bool PartialStart;
            public bool FailStop;
            public int StartCount;
            public int StopCount;

            /// <summary>Sin hardware no hay adaptador que esperar: las pruebas no pueden tardar 8 s por arranque.</summary>
            protected override int AccessPointTimeoutMs
            {
                get { return 0; }
            }

            public override EngineKind Kind
            {
                get { return EngineKind.WiFiDirectGo; }
            }

            public override string DisplayName
            {
                get { return "Motor falso de prueba"; }
            }

            protected override void StartCore(HotspotConfig config)
            {
                if (PartialStart)
                {
                    // Arranque a medias: la radio queda publicando y despues falla.
                    StartCount++;
                    Alive = true;
                    throw new InvalidOperationException("fallo simulado a medias");
                }

                if (FailNextStart)
                {
                    throw new InvalidOperationException("fallo simulado");
                }

                StartCount++;
                Alive = true;
            }

            protected override void StopCore()
            {
                StopCount++;
                if (FailStop)
                {
                    throw new InvalidOperationException("la parada no se pudo confirmar");
                }

                Alive = false;
            }

            protected override bool ProbeCore()
            {
                return Alive;
            }
        }

        private static string Line(StreamReader reader)
        {
            string line = reader.ReadLine();
            return line ?? string.Empty;
        }

        /// <summary>Ejecuta un comando FTP por el canal pasivo y devuelve los datos recibidos.</summary>
        private static void PassiveTransfer(StreamReader reader, StreamWriter writer, string command, byte[] payload,
            out string result, out string preliminary, out string final, out byte[] bytes)
        {
            preliminary = string.Empty;
            final = string.Empty;
            result = string.Empty;
            bytes = new byte[0];

            writer.WriteLine("PASV");
            string passive = Line(reader);
            int dataPort = ParsePassivePort(passive);
            if (dataPort <= 0)
            {
                return;
            }

            writer.WriteLine(command);
            preliminary = Line(reader);

            using (System.Net.Sockets.TcpClient data = new System.Net.Sockets.TcpClient())
            {
                data.Connect(IPAddress.Loopback, dataPort);
                data.ReceiveTimeout = 15000;
                NetworkStream stream = data.GetStream();
                if (payload != null)
                {
                    stream.Write(payload, 0, payload.Length);
                }
                else
                {
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        byte[] chunk = new byte[4096];
                        int read;
                        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            buffer.Write(chunk, 0, read);
                        }

                        bytes = buffer.ToArray();
                    }
                }

                if (payload == null)
                {
                    result = Encoding.UTF8.GetString(bytes);
                }
            }

            final = Line(reader);
        }

        private static int ParsePassivePort(string reply)
        {
            int open = reply.IndexOf('(');
            int close = reply.IndexOf(')', open + 1);
            if (open < 0 || close < 0)
            {
                return 0;
            }

            string[] parts = reply.Substring(open + 1, close - open - 1).Split(',');
            if (parts.Length != 6)
            {
                return 0;
            }

            int high;
            int low;
            if (!int.TryParse(parts[4], out high) || !int.TryParse(parts[5], out low))
            {
                return 0;
            }

            return high * 256 + low;
        }

        private static string HttpText(string url, string method, byte[] body, Dictionary<string, string> headers)
        {
            return Encoding.UTF8.GetString(HttpBytes(url, method, body, headers, out _));
        }

        private static byte[] HttpBytes(string url, string method, byte[] body, Dictionary<string, string> headers, out int status)
        {
            status = 0;
            try
            {
                System.Net.HttpWebRequest request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                request.Method = method;
                request.Timeout = 15000;
                request.AllowAutoRedirect = false;
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> header in headers)
                    {
                        if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            request.ContentType = header.Value;
                        }
                        else if (string.Equals(header.Key, "Range", StringComparison.OrdinalIgnoreCase))
                        {
                            // 'Range' es un encabezado restringido: hay que usar la API, no el indice.
                            string spec = header.Value.StartsWith("bytes=") ? header.Value.Substring(6) : header.Value;
                            int dash = spec.IndexOf('-');
                            long from = long.Parse(spec.Substring(0, dash), CultureInfo.InvariantCulture);
                            string to = spec.Substring(dash + 1);
                            if (to.Length == 0)
                            {
                                request.AddRange(from);
                            }
                            else
                            {
                                request.AddRange(from, long.Parse(to, CultureInfo.InvariantCulture));
                            }
                        }
                        else
                        {
                            request.Headers[header.Key] = header.Value;
                        }
                    }
                }

                if (body != null)
                {
                    request.ContentLength = body.Length;
                    using (Stream stream = request.GetRequestStream())
                    {
                        stream.Write(body, 0, body.Length);
                    }
                }

                using (System.Net.HttpWebResponse response = (System.Net.HttpWebResponse)request.GetResponse())
                {
                    status = (int)response.StatusCode;
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        using (Stream stream = response.GetResponseStream())
                        {
                            byte[] chunk = new byte[4096];
                            int read;
                            while (stream != null && (read = stream.Read(chunk, 0, chunk.Length)) > 0)
                            {
                                buffer.Write(chunk, 0, read);
                            }
                        }

                        return buffer.ToArray();
                    }
                }
            }
            catch (System.Net.WebException ex)
            {
                if (ex.Response is System.Net.HttpWebResponse)
                {
                    using (System.Net.HttpWebResponse response = (System.Net.HttpWebResponse)ex.Response)
                    {
                        status = (int)response.StatusCode;
                    }
                }

                return new byte[0];
            }
        }

        private static byte EvaluateCodeword(byte[] codeword, int power)
        {
            byte alpha = 1;
            for (int i = 0; i < power; i++)
            {
                alpha = ReedSolomon.Multiply(alpha, 0x02);
            }

            byte accumulator = 0;
            foreach (byte value in codeword)
            {
                accumulator = (byte)(ReedSolomon.Multiply(accumulator, alpha) ^ value);
            }

            return accumulator;
        }

        /// <summary>
        /// Lee un bit de la informacion de formato aplicando la misma regla de colocacion que
        /// el codificador: primera copia alrededor de la esquina superior izquierda, segunda
        /// repartida entre la columna 8 inferior y la fila 8 derecha.
        /// </summary>
        private static bool ReadFormatBit(QrCode code, int index, bool firstCopy)
        {
            int size = code.Size;
            if (firstCopy)
            {
                if (index <= 5)
                {
                    return code[index, 8];
                }

                if (index == 6)
                {
                    return code[7, 8];
                }

                if (index == 7)
                {
                    return code[8, 8];
                }

                if (index == 8)
                {
                    return code[8, 7];
                }

                return code[8, 14 - index];
            }

            if (index <= 7)
            {
                return code[8, size - 1 - index];
            }

            return code[size - 15 + index, 8];
        }

        private static bool HasFinder(QrCode code, int left, int top)
        {
            for (int y = 0; y < 7; y++)
            {
                for (int x = 0; x < 7; x++)
                {
                    int distance = Math.Max(Math.Abs(x - 3), Math.Abs(y - 3));
                    bool expected = distance != 2;
                    if (code[top + y, left + x] != expected)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool sameMatrix(QrCode left, QrCode right)
        {
            if (left.Size != right.Size)
            {
                return false;
            }

            for (int y = 0; y < left.Size; y++)
            {
                for (int x = 0; x < left.Size; x++)
                {
                    if (left[y, x] != right[y, x])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                {
                    return true;
                }

                System.Threading.Thread.Sleep(100);
            }

            return condition();
        }

        private static string CreateTempFolder(string tag)
        {
            string path = Path.Combine(Path.GetTempPath(), "NetForgeSelfTest-" + tag + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Cleanup(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception)
            {
                // Una carpeta temporal que no se puede borrar no es un fallo de la app.
            }
        }

        private static void Expect(string name, bool condition)
        {
            _checks++;
            if (condition)
            {
                Write("ok     " + name);
            }
            else
            {
                _failures++;
                Write("FALLO  " + name);
            }
        }

        private static void ExpectAccept(string name, string ssid, string passphrase)
        {
            bool accepted = true;
            try
            {
                new HotspotConfig { Ssid = ssid, Passphrase = passphrase }.Validate();
            }
            catch (Exception)
            {
                accepted = false;
            }

            Expect(name, accepted);
        }

        private static void ExpectReject(string name, string ssid, string passphrase)
        {
            bool rejected = false;
            try
            {
                new HotspotConfig { Ssid = ssid, Passphrase = passphrase }.Validate();
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Expect(name, rejected);
        }

        private static void Section(string title)
        {
            Write("");
            Write("[" + title + "]");
        }

        private static void Write(string line)
        {
            Report.AppendLine(line);
            try
            {
                Console.WriteLine(line);
            }
            catch (Exception)
            {
                // Sin consola disponible: queda el fichero.
            }
        }

        private static void FlushToFile()
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "netforge-selftest.txt");
                File.WriteAllText(path, Report.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Si no se puede escribir el informe, el codigo de salida sigue siendo valido.
            }
        }
    }
}
