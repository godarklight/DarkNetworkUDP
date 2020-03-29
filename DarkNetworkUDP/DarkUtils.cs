using System;
using System.Net;

namespace DarkNetworkUDP
{
    public static class DarkUtils
    {
        private static byte[] byte16 = new byte[16];
        private static byte[] byte4 = new byte[4];
        public static Guid GuidFromIPEndpoint(IPEndPoint endpoint)
        {
            Guid retVal = Guid.Empty;
            lock (byte16)
            {
#if NETFRAMEWORK
                endpoint.Address.GetAddressBytes().CopyTo(byte16, 0);
#else
                endpoint.Address.TryWriteBytes(byte16, out int _);
#endif
                byte16[0] = (byte)(byte16[0] ^ (endpoint.Port >> 8) & 0xFF);
                byte16[1] = (byte)(byte16[1] ^ (endpoint.Port & 0xFF));
                retVal = new Guid(byte16);
            }
            return retVal;
        }
        public static void WriteMagicHeader(byte[] data, int index)
        {
            data[index] = 68;
            data[index + 1] = 65; 
            data[index + 2] = 82;
            data[index + 3] = 75;
        }
        public static void WriteInt32ToByteArray(int number, byte[] data, int index)
        {
            uint unumber = (uint)number;
            data[index] = (byte)((unumber >> 24) & 0xFF);
            data[index + 1] = (byte)((unumber >> 16) & 0xFF);
            data[index + 2] = (byte)((unumber >> 8) & 0xFF);
            data[index + 3] = (byte)((unumber) & 0xFF);
        }

        public static void WriteInt64ToByteArray(long number, byte[] data, int index)
        {
            ulong unumber = (ulong)number;
            data[index] = (byte)((unumber >> 56) & 0xFF);
            data[index + 1] = (byte)((unumber >> 48) & 0xFF);
            data[index + 2] = (byte)((unumber >> 40) & 0xFF);
            data[index + 3] = (byte)((unumber >> 32) & 0xFF);
            data[index + 4] = (byte)((unumber >> 24) & 0xFF);
            data[index + 5] = (byte)((unumber >> 16) & 0xFF);
            data[index + 6] = (byte)((unumber >> 8) & 0xFF);
            data[index + 7] = (byte)((unumber) & 0xFF);
        }
    }
}