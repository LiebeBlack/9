// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - interop de iphlpapi/psapi/ntdll.
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace NetForge.Native
{
    public enum ArpEntryType
    {
        Other = 1,
        Invalid = 2,
        Dynamic = 3,
        Static = 4
    }

    public sealed class ArpEntry
    {
        public uint InterfaceIndex { get; set; }
        public IPAddress Address { get; set; }
        public string Mac { get; set; }
        public ArpEntryType Type { get; set; }
    }

    public static class IpHelper
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetIpNetTable(IntPtr ipNetTable, ref int size, bool order);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int SendARP(uint destination, uint source, byte[] macAddress, ref uint macAddressLength);

        /// <summary>
        /// Tabla ARP del sistema. Es la fuente barata y real de quien esta en la red:
        /// no hace falta ping ni espera, solo leer lo que el stack ya sabe.
        /// </summary>
        public static IList<ArpEntry> GetArpTable()
        {
            List<ArpEntry> entries = new List<ArpEntry>();
            int size = 0;
            GetIpNetTable(IntPtr.Zero, ref size, false);
            if (size <= 0)
            {
                return entries;
            }

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (GetIpNetTable(buffer, ref size, false) != 0)
                {
                    return entries;
                }

                int count = Marshal.ReadInt32(buffer);
                int rowSize = Marshal.SizeOf(typeof(MibIpNetRow));
                IntPtr cursor = new IntPtr(buffer.ToInt64() + 4);
                for (int i = 0; i < count; i++)
                {
                    MibIpNetRow row = (MibIpNetRow)Marshal.PtrToStructure(cursor, typeof(MibIpNetRow));
                    cursor = new IntPtr(cursor.ToInt64() + rowSize);

                    byte[] raw = row.bPhysAddr;
                    if (raw == null || row.dwPhysAddrLen == 0 || row.dwPhysAddrLen > 6)
                    {
                        continue;
                    }

                    byte[] mac = new byte[row.dwPhysAddrLen];
                    Array.Copy(raw, mac, mac.Length);
                    // dwAddr llega en el orden de bytes de la red; se reconstruye la direccion
                    // desde esos bytes en lugar de pasar el entero, que IPAddress interpretaria
                    // al reves (192.168.1.1 se convertiria en 1.1.168.192).
                    entries.Add(new ArpEntry
                    {
                        InterfaceIndex = row.dwIndex,
                        Address = new IPAddress(BitConverter.GetBytes(row.dwAddr)),
                        Mac = NativeFormat.MacToString(mac),
                        Type = (ArpEntryType)row.dwType
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return entries;
        }

        /// <summary>
        /// Sonda activa: fuerza al stack a resolver una IP concreta. Necesario porque una
        /// entrada ARP incompleta (00:00:00:00:00:00) significa "aun no hay trafico".
        ///
        /// SendARP espera direcciones en orden de red (el mismo orden de bytes que la
        /// direccion IP), por lo que BitConverter.ToUInt32 es aqui lo correcto.
        /// </summary>
        public static string ResolveMac(IPAddress address, uint sourceInterfaceIp)
        {
            byte[] mac = new byte[6];
            uint length = 6;
            byte[] octets = address.GetAddressBytes();
            if (octets.Length != 4)
            {
                return string.Empty;
            }

            uint destination = BitConverter.ToUInt32(octets, 0);
            if (SendARP(destination, sourceInterfaceIp, mac, ref length) != 0 || length == 0)
            {
                return string.Empty;
            }

            byte[] trimmed = new byte[length];
            Array.Copy(mac, trimmed, (int)length);
            return NativeFormat.MacToString(trimmed);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibIpNetRow
        {
            public uint dwIndex;
            public uint dwPhysAddrLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] bPhysAddr;
            public uint dwAddr;
            public uint dwType;
        }
    }

    public static class PsApi
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr process);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// Devuelve al sistema las paginas de trabajo. En Modo Fantasma es lo que baja el
        /// consumo tras destruir la interfaz.
        /// </summary>
        public static bool TrimWorkingSet()
        {
            try
            {
                return EmptyWorkingSet(GetCurrentProcess());
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static class OsInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct OsVersionInfoEx
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public ushort wServicePackMajor;
            public ushort wServicePackMinor;
            public ushort wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
        private static extern int RtlGetVersion(ref OsVersionInfoEx versionInfo);

        /// <summary>
        /// RtlGetVersion es la unica fuente fiable: Environment.OSVersion miente y reporta
        /// 6.2 cuando el manifiesto no declara las versiones soportadas.
        /// </summary>
        public static Version GetRealVersion()
        {
            OsVersionInfoEx info = new OsVersionInfoEx();
            info.dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(OsVersionInfoEx));
            try
            {
                if (RtlGetVersion(ref info) == 0)
                {
                    return new Version((int)info.dwMajorVersion, (int)info.dwMinorVersion, (int)info.dwBuildNumber);
                }
            }
            catch (Exception)
            {
                // Sin ntdll (imposible en Windows real) se cae al dato de .NET.
            }

            return Environment.OSVersion.Version;
        }

        /// <summary>Windows 10 1903 (build 18362) es el suelo garantizado del proyecto.</summary>
        public static bool IsSupported()
        {
            Version v = GetRealVersion();
            return v.Major > 10 || (v.Major == 10 && v.Build >= 18362);
        }

        public static string Describe()
        {
            return "Windows NT " + GetRealVersion().ToString() + " (" + Environment.MachineName + ")";
        }
    }
}
