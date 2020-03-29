using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;

//Connects to localhost and reports a message, then processes that message received.

namespace SuperServer
{
    public class ServerTest
    {
        public void Run(int port)
        {
            //Buffers we need
            byte[] byte2 = new byte[2];
            byte[] byte4 = new byte[4];
            byte[] byte16 = new byte[16];
            byte[] buffer = new byte[2048];
            byte[] buffer2 = new byte[2048];
            //Setup socket
            Socket clientSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            clientSocket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.IPv6Loopback, port);
            IPEndPoint any = new IPEndPoint(IPAddress.IPv6Any, 0);
            ushort ourPort = (ushort)(clientSocket.LocalEndPoint as IPEndPoint).Port;
            byte[] ourPortBytes = BitConverter.GetBytes(ourPort);
            for (int loopCount = 0; loopCount < 100000; loopCount++)
            {
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(ourPortBytes);
                }
                //Write magic
                Encoding.UTF8.GetBytes("DarkSS", 0, 6, buffer, 0);
                int processesBytes = 6;
                //Write Guid
                Guid randomGuid = Guid.NewGuid();
                randomGuid.TryWriteBytes(byte16);
                Array.Copy(byte16, 0, buffer, processesBytes, 16);
                processesBytes += 16;
                //Write Mode
                buffer[processesBytes++] = 0;
                //Write IPv4 count            
                buffer[processesBytes++] = 1;
                //Write IPv4 blocks
                IPAddress.Loopback.TryWriteBytes(byte4, out int _);
                Array.Copy(byte4, 0, buffer, processesBytes, 4);
                processesBytes += 4;
                Array.Copy(ourPortBytes, 0, buffer, processesBytes, 2);
                processesBytes += 2;
                //Write IPv6 count
                buffer[processesBytes++] = 1;
                //Write IPv6 blocks
                IPAddress.IPv6Loopback.TryWriteBytes(byte16, out int _);
                Array.Copy(byte16, 0, buffer, processesBytes, 16);
                processesBytes += 16;
                Array.Copy(ourPortBytes, 0, buffer, processesBytes, 2);
                processesBytes += 2;
                //Send store message
                clientSocket.SendTo(buffer, 0, processesBytes, SocketFlags.None, serverAddr);
                //Get message
                int bytesRead = clientSocket.Receive(buffer2);
                Console.WriteLine("Received " + bytesRead + " bytes, store message");
                if (buffer2[25] == 1)
                {
                    Console.WriteLine("Stored correctly");
                }
                Array.Clear(buffer, 25, buffer.Length - 25);
                Array.Clear(buffer2, 0, buffer2.Length);
                buffer[22] = 1;
                clientSocket.SendTo(buffer, 0, 23, SocketFlags.None, serverAddr);
                int bytesRead2 = clientSocket.Receive(buffer2);
                Console.WriteLine("Received " + bytesRead2 + " bytes, get message");
                processesBytes = 23;
                int numberOfV4 = buffer2[processesBytes++];
                for (int i = 0; i < numberOfV4; i++)
                {
                    Array.Copy(buffer2, processesBytes, byte4, 0, 4);
                    processesBytes += 4;
                    Array.Copy(buffer2, processesBytes, byte2, 0, 2);
                    processesBytes += 2;
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(byte2);
                    }
                    ushort portNumber = BitConverter.ToUInt16(byte2);
                    IPEndPoint endpoint = new IPEndPoint(new IPAddress(byte4), portNumber);
                    Console.WriteLine("Got IPv4 endpoint: " + endpoint);
                }
                int numberOfV6 = buffer2[processesBytes++];
                for (int i = 0; i < numberOfV6; i++)
                {
                    Array.Copy(buffer2, processesBytes, byte16, 0, 16);
                    processesBytes += 16;
                    Array.Copy(buffer2, processesBytes, byte2, 0, 2);
                    processesBytes += 2;
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(byte2);
                    }
                    ushort portNumber = BitConverter.ToUInt16(byte2);
                    IPEndPoint endpoint = new IPEndPoint(new IPAddress(byte16), portNumber);
                    Console.WriteLine("Got IPv6 endpoint: " + endpoint);
                }
            }
        }
    }
}