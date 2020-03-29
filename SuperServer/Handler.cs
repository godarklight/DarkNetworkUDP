using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SuperServer
{
    public class Handler
    {
        private Action<byte[], int, IPEndPoint> send;
        private byte[] byte2 = new byte[2];
        private byte[] byte4 = new byte[4];
        private byte[] byte16 = new byte[16];
        private Dictionary<Guid, ServerData> serverDatas = new Dictionary<Guid, ServerData>();
        public void SetSendMethod(Action<byte[], int, IPEndPoint> sendMethod)
        {
            this.send = sendMethod;
        }
        public void Handle(byte[] data, int length, IPEndPoint endPoint)
        {
            if (length < 23)
            {
                return;
            }
            string magic = Encoding.UTF8.GetString(data, 0, 6);
            if (magic != "DarkSS")
            {
                return;
            }
            int processPointer = 6;
            Array.Copy(data, processPointer, byte16, 0, 16);
            Guid parseGuid = new Guid(byte16);
            processPointer += 16;
            byte mode = data[processPointer++];
            if (mode == 0)
            {
                ServerData serverData = null;
                if (serverDatas.ContainsKey(parseGuid))
                {
                    serverData = serverDatas[parseGuid];
                }
                if (serverData == null)
                {
                    serverData = new ServerData();
                    serverDatas.Add(parseGuid, serverData);
                }
                if (endPoint.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (serverData.v4addr == null)
                    {
                        serverData.v4addr = endPoint.Address;
                    }
                    if (!serverData.v4addr.Equals(endPoint.Address))
                    {
                        SendStore(data, false, endPoint);
                        return;
                    }
                }
                if (endPoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    if (serverData.v6addr == null)
                    {
                        serverData.v6addr = endPoint.Address;
                    }
                    if (!serverData.v6addr.Equals(endPoint.Address))
                    {
                        SendStore(data, false, endPoint);
                        return;
                    }
                }
                serverData.v4.Clear();
                serverData.v6.Clear();
                //Processing store message
                if (processPointer == length)
                {
                    return;
                }
                byte numberOfV4 = data[processPointer++];
                for (int i = 0; i < numberOfV4; i++)
                {
                    if (length < processPointer + 6)
                    {
                        return;
                    }
                    Array.Copy(data, processPointer, byte4, 0, 4);
                    processPointer += 4;
                    Array.Copy(data, processPointer, byte2, 0, 2);
                    processPointer += 2;
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(byte2);
                    }
                    ushort newPort = BitConverter.ToUInt16(byte2);
                    IPAddress newAddr = new IPAddress(byte4);
                    IPEndPoint newEndpoint = new IPEndPoint(newAddr, newPort);
                    serverData.v4.Add(newEndpoint);
                }
                if (processPointer == length)
                {
                    return;
                }
                byte numberOfV6 = data[processPointer++];
                for (int i = 0; i < numberOfV6; i++)
                {
                    if (length < processPointer + 18)
                    {
                        return;
                    }
                    Array.Copy(data, processPointer, byte16, 0, 16);
                    processPointer += 16;
                    Array.Copy(data, processPointer, byte2, 0, 2);
                    processPointer += 2;
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(byte2);
                    }
                    ushort newPort = BitConverter.ToUInt16(byte2);
                    IPAddress newAddr = new IPAddress(byte16);
                    IPEndPoint newEndpoint = new IPEndPoint(newAddr, newPort);
                    serverData.v6.Add(newEndpoint);
                }
                Console.WriteLine("Server " + parseGuid + " updated from " + endPoint);
                SendStore(data, true, endPoint);
            }
            if (mode == 1)
            {
                SendGet(data, parseGuid, endPoint);
            }
        }

        public void SendStore(byte[] data, bool result, IPEndPoint endPoint)
        {
            data[24] = 0;
            if (result)
            {
                data[24] = 1;
            }
            send(data, 24, endPoint);
        }
        public void SendGet(byte[] data, Guid parseGuid, IPEndPoint endPoint)
        {
            data[23] = 0;
            data[24] = 0;
            ServerData serverData = null;
            if (serverDatas.ContainsKey(parseGuid))
            {
                serverData = serverDatas[parseGuid];
            }
            if (serverData == null)
            {
                send(data, 25, endPoint);
                return;
            }
            int processPointer = 23;
            data[processPointer++] = (byte)serverData.v4.Count;
            for (int i = 0; i < serverData.v4.Count; i++)
            {
                IPEndPoint serverEndpoint = serverData.v4[i];
                serverEndpoint.Address.TryWriteBytes(byte4, out int _);
                Array.Copy(byte4, 0, data, processPointer, 4);
                processPointer += 4;
                ushort readPort = (ushort)serverEndpoint.Port;
                byte[] portBytes = BitConverter.GetBytes(readPort);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(portBytes);
                }
                Array.Copy(portBytes, 0, data, processPointer, 2);
                processPointer += 2;
            }
            data[processPointer++] = (byte)serverData.v6.Count;
            for (int i = 0; i < serverData.v6.Count; i++)
            {
                IPEndPoint serverEndpoint = serverData.v6[i];
                serverEndpoint.Address.TryWriteBytes(byte16, out int _);
                Array.Copy(byte16, 0, data, processPointer, 16);
                processPointer += 16;
                ushort readPort = (ushort)serverEndpoint.Port;
                byte[] portBytes = BitConverter.GetBytes(readPort);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(portBytes);
                }
                Array.Copy(portBytes, 0, data, processPointer, 2);
                processPointer += 2;
            }
            send(data, processPointer, endPoint);
        }
    }
}