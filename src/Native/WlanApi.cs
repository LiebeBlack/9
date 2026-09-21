// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - interop de wlanapi.dll. Todo el sondeo es estructural: nunca se
// interpreta texto localizado de netsh, porque eso se rompe en cualquier Windows
// que no este en ingles.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NetForge.Native
{
    public enum WlanInterfaceState
    {
        NotReady = 0,
        Connected = 1,
        AdHocNetworkFormed = 2,
        Disconnecting = 3,
        Disconnected = 4,
        Associating = 5,
        Discovering = 6,
        Authenticating = 7
    }

    public enum WlanHostedNetworkState
    {
        Unavailable = 0,
        Idle = 1,
        Active = 2
    }

    public sealed class WlanInterfaceInfo
    {
        public Guid Guid { get; set; }
        public string Description { get; set; }
        public WlanInterfaceState State { get; set; }
    }

    public sealed class HostedNetworkInfo
    {
        public bool Supported { get; set; }
        public bool Enabled { get; set; }
        public WlanHostedNetworkState State { get; set; }
        public string Bssid { get; set; }
        public int ChannelFrequency { get; set; }
        public int PeerCount { get; set; }
        public string Reason { get; set; }
    }

    public static class WlanApi
    {
        private const uint ClientVersion = 2;
        private const int ErrorSuccess = 0;
        private const int ErrorNotSupported = 50;
        private const uint WlanHostedNetworkOpcodeEnable = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct WlanInterfaceInfoRaw
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public int isState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WlanHostedNetworkStatusRaw
        {
            public int HostedNetworkState;
            public Guid IPDeviceID;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] wlanHostedNetworkBSSID;
            public int dot11PhyType;
            public uint ulChannelFrequency;
            public uint dwNumberOfPeers;
        }

        private delegate void WlanNotificationCallback(IntPtr data, IntPtr context);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern int WlanOpenHandle(uint clientVersion, IntPtr clientContext, out uint negotiatedVersion, out IntPtr clientHandle);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr clientContext);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern int WlanHostedNetworkQueryStatus(IntPtr clientHandle, ref Guid interfaceGuid, out IntPtr status);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern int WlanHostedNetworkQueryProperty(IntPtr clientHandle, ref Guid interfaceGuid, uint opcode, out uint dataSize, out IntPtr data, out int opcodeType);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr memory);

        /// <summary>Enumera las interfaces Wi-Fi presentes. Vacio si no hay ninguna tarjeta.</summary>
        public static IList<WlanInterfaceInfo> EnumerateInterfaces()
        {
            List<WlanInterfaceInfo> result = new List<WlanInterfaceInfo>();
            IntPtr handle;
            uint negotiated;
            int code = WlanOpenHandle(ClientVersion, IntPtr.Zero, out negotiated, out handle);
            if (code != ErrorSuccess)
            {
                return result;
            }

            try
            {
                IntPtr listPtr;
                if (WlanEnumInterfaces(handle, IntPtr.Zero, out listPtr) != ErrorSuccess || listPtr == IntPtr.Zero)
                {
                    return result;
                }

                try
                {
                    int count = Marshal.ReadInt32(listPtr);
                    int size = Marshal.SizeOf(typeof(WlanInterfaceInfoRaw));
                    IntPtr cursor = new IntPtr(listPtr.ToInt64() + 8);
                    for (int i = 0; i < count; i++)
                    {
                        WlanInterfaceInfoRaw raw = (WlanInterfaceInfoRaw)Marshal.PtrToStructure(cursor, typeof(WlanInterfaceInfoRaw));
                        result.Add(new WlanInterfaceInfo
                        {
                            Guid = raw.InterfaceGuid,
                            Description = raw.strInterfaceDescription,
                            State = (WlanInterfaceState)raw.isState
                        });
                        cursor = new IntPtr(cursor.ToInt64() + size);
                    }
                }
                finally
                {
                    WlanFreeMemory(listPtr);
                }
            }
            finally
            {
                WlanCloseHandle(handle, IntPtr.Zero);
            }

            return result;
        }

        /// <summary>
        /// Estado de la red hospedada (motor legacy). Devuelve Supported=false cuando el
        /// driver no implementa Soft AP, que es exactamente el caso de muchos adaptadores
        /// modernos: no es un error, es una capacidad ausente.
        /// </summary>
        public static HostedNetworkInfo QueryHostedNetwork(Guid interfaceGuid)
        {
            HostedNetworkInfo info = new HostedNetworkInfo { State = WlanHostedNetworkState.Unavailable, Reason = string.Empty };
            IntPtr handle;
            uint negotiated;
            int code = WlanOpenHandle(ClientVersion, IntPtr.Zero, out negotiated, out handle);
            if (code != ErrorSuccess)
            {
                info.Reason = "wlanapi no disponible (codigo " + code + ")";
                return info;
            }

            try
            {
                uint size;
                IntPtr data;
                int opcodeType;
                Guid guid = interfaceGuid;
                int propCode = WlanHostedNetworkQueryProperty(handle, ref guid, WlanHostedNetworkOpcodeEnable, out size, out data, out opcodeType);
                if (propCode == ErrorSuccess && data != IntPtr.Zero)
                {
                    try
                    {
                        info.Enabled = Marshal.ReadInt32(data) != 0;
                        info.Supported = true;
                    }
                    finally
                    {
                        WlanFreeMemory(data);
                    }
                }
                else if (propCode == ErrorNotSupported)
                {
                    info.Reason = "el driver no implementa red hospedada (Soft AP)";
                    return info;
                }

                IntPtr statusPtr;
                int statusCode = WlanHostedNetworkQueryStatus(handle, ref guid, out statusPtr);
                if (statusCode == ErrorSuccess && statusPtr != IntPtr.Zero)
                {
                    try
                    {
                        WlanHostedNetworkStatusRaw raw = (WlanHostedNetworkStatusRaw)Marshal.PtrToStructure(statusPtr, typeof(WlanHostedNetworkStatusRaw));
                        info.State = (WlanHostedNetworkState)raw.HostedNetworkState;
                        info.ChannelFrequency = (int)raw.ulChannelFrequency;
                        info.PeerCount = (int)raw.dwNumberOfPeers;
                        if (raw.wlanHostedNetworkBSSID != null && raw.wlanHostedNetworkBSSID.Length == 6)
                        {
                            info.Bssid = NativeFormat.MacToString(raw.wlanHostedNetworkBSSID);
                        }
                        if (info.State != WlanHostedNetworkState.Unavailable)
                        {
                            info.Supported = true;
                        }
                        else if (string.IsNullOrEmpty(info.Reason))
                        {
                            info.Reason = "la red hospedada no esta disponible en este adaptador";
                        }
                    }
                    finally
                    {
                        WlanFreeMemory(statusPtr);
                    }
                }
                else if (statusCode == ErrorNotSupported)
                {
                    info.Reason = "el driver no implementa red hospedada (Soft AP)";
                }
                else if (statusCode != ErrorSuccess)
                {
                    info.Reason = "consulta de estado fallida (codigo " + statusCode + ")";
                }
            }
            finally
            {
                WlanCloseHandle(handle, IntPtr.Zero);
            }

            return info;
        }
    }

    public static class NativeFormat
    {
        public static string MacToString(byte[] mac)
        {
            if (mac == null || mac.Length < 6)
            {
                return string.Empty;
            }

            return string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
        }

        public static string MacToString(ulong macLow6)
        {
            byte[] bytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                bytes[i] = (byte)((macLow6 >> (8 * (5 - i))) & 0xFF);
            }

            return MacToString(bytes);
        }
    }
}
