// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - identificacion de fabricante sin internet.
//
// El radar necesita responder "que telefono es este" sin salir a la red, asi que la base
// de datos del IEEE viaja comprimida dentro del ejecutable y se consulta por busqueda
// binaria en memoria. Se admiten los tres registros (24, 28 y 36 bits) porque un prefijo
// de 24 bits mal atribuye los equipos modernos, que usan prefijos mas largos.
//
// Aviso importante y honesto: los sistemas moviles modernos aleatorizan la MAC por
// privacidad. Cuando el bit de administracion local esta activo, el fabricante NO existe
// y decir lo contrario seria inventarse un dato.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace NetForge.Net
{
    public sealed class OuiLookupResult
    {
        public string Vendor { get; set; }
        public bool IsRandomized { get; set; }
        public int MatchedBits { get; set; }

        public OuiLookupResult()
        {
            Vendor = string.Empty;
        }

        public string Describe
        {
            get
            {
                if (IsRandomized)
                {
                    return "MAC aleatoria (privacidad)";
                }

                if (string.IsNullOrEmpty(Vendor))
                {
                    return "fabricante desconocido";
                }

                return Vendor;
            }
        }
    }

    public static class OuiDatabase
    {
        private const string ResourceName = "NetForge.Oui.bin.gz";
        private const int HeaderSize = 20;
        private const int NameSlot = 32;

        // Cada registro guarda un prefijo de 64 bits con los bits significativos arriba y el
        // nombre en ASCII rellenado a 32 bytes. Tamano fijo para poder buscar en binario.
        private const int RecordSize = 8 + NameSlot;

        private static readonly object Sync = new object();
        private static byte[] _data;
        private static int _count24;
        private static int _count28;
        private static int _count36;
        private static bool _loaded;
        private static string _loadError = string.Empty;

        public static bool Available
        {
            get
            {
                EnsureLoaded();
                return _data != null;
            }
        }

        public static string LoadError
        {
            get
            {
                EnsureLoaded();
                return _loadError;
            }
        }

        public static OuiLookupResult Lookup(string mac)
        {
            OuiLookupResult result = new OuiLookupResult();
            byte[] octets = ParseMac(mac);
            if (octets == null)
            {
                return result;
            }

            result.IsRandomized = (octets[0] & 0x02) != 0;
            if (result.IsRandomized)
            {
                return result;
            }

            EnsureLoaded();
            if (_data == null)
            {
                return result;
            }

            ulong value = 0;
            for (int i = 0; i < 6; i++)
            {
                value = (value << 8) | octets[i];
            }

            // Se prueba primero el prefijo mas especifico: 36, luego 28 y por ultimo 24.
            if (TryFind(36, value, _count36, HeaderSize + _count24 * RecordSize + _count28 * RecordSize, result))
            {
                return result;
            }

            if (TryFind(28, value, _count28, HeaderSize + _count24 * RecordSize, result))
            {
                return result;
            }

            if (TryFind(24, value, _count24, HeaderSize, result))
            {
                return result;
            }

            return result;
        }

        public static int VendorCount
        {
            get
            {
                EnsureLoaded();
                return _count24 + _count28 + _count36;
            }
        }

        private static bool TryFind(int bits, ulong value, int count, int blockOffset, OuiLookupResult result)
        {
            if (count <= 0)
            {
                return false;
            }

            int shift = 64 - bits;
            ulong wanted = (value >> (48 - bits)) << shift;
            int low = 0;
            int high = count - 1;

            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                int offset = blockOffset + middle * RecordSize;
                ulong prefix = BitConverter.ToUInt64(_data, offset);
                if (wanted < prefix)
                {
                    high = middle - 1;
                }
                else if (wanted > prefix)
                {
                    low = middle + 1;
                }
                else
                {
                    result.Vendor = ReadName(offset + 8);
                    result.MatchedBits = bits;
                    return true;
                }
            }

            return false;
        }

        private static string ReadName(int offset)
        {
            int length = 0;
            while (length < NameSlot && _data[offset + length] != 0)
            {
                length++;
            }

            return Encoding.ASCII.GetString(_data, offset, length);
        }

        private static byte[] ParseMac(string mac)
        {
            if (string.IsNullOrEmpty(mac))
            {
                return null;
            }

            string cleaned = mac.Replace("-", string.Empty).Replace(":", string.Empty).Replace(".", string.Empty).Trim();
            if (cleaned.Length != 12)
            {
                return null;
            }

            byte[] octets = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                string pair = cleaned.Substring(i * 2, 2);
                int parsed;
                if (!int.TryParse(pair, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out parsed))
                {
                    return null;
                }

                octets[i] = (byte)parsed;
            }

            return octets;
        }

        private static void EnsureLoaded()
        {
            lock (Sync)
            {
                if (_loaded)
                {
                    return;
                }

                _loaded = true;
                try
                {
                    Assembly assembly = Assembly.GetExecutingAssembly();
                    using (Stream raw = assembly.GetManifestResourceStream(ResourceName))
                    {
                        if (raw == null)
                        {
                            _loadError = "el recurso " + ResourceName + " no esta incrustado";
                            return;
                        }

                        using (GZipStream gzip = new GZipStream(raw, CompressionMode.Decompress))
                        using (MemoryStream buffer = new MemoryStream())
                        {
                            byte[] chunk = new byte[64 * 1024];
                            int read;
                            while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
                            {
                                buffer.Write(chunk, 0, read);
                            }

                            _data = buffer.ToArray();
                        }
                    }

                    if (_data.Length < HeaderSize)
                    {
                        _data = null;
                        _loadError = "la base de datos esta truncada";
                        return;
                    }

                    string magic = Encoding.ASCII.GetString(_data, 0, 4);
                    byte version = _data[4];
                    if (magic != "NOUI" || version != 1)
                    {
                        _data = null;
                        _loadError = "formato de base de datos no reconocido";
                        return;
                    }

                    _count24 = BitConverter.ToInt32(_data, 8);
                    _count28 = BitConverter.ToInt32(_data, 12);
                    _count36 = BitConverter.ToInt32(_data, 16);
                    int expected = HeaderSize + (_count24 + _count28 + _count36) * RecordSize;
                    if (_data.Length < expected)
                    {
                        int actual = _data.Length;
                        _count24 = 0;
                        _count28 = 0;
                        _count36 = 0;
                        _data = null;
                        _loadError = "la base de datos esta incompleta (" + actual + " de " + expected + " bytes)";
                    }
                }
                catch (Exception ex)
                {
                    _data = null;
                    _loadError = ex.Message;
                }
            }
        }
    }
}
