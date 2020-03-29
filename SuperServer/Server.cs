using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SuperServer
{
    public class Server
    {
        private Handler handler;
        private Socket serverSocket;
        private byte[] buffer = new byte[2048];
        public Server(Handler handler)
        {
            this.handler = handler;
            this.handler.SetSendMethod(Send);
        }

        public ExitReason Run(string addr, int port)
        {
            ExitReason exitReason = ExitReason.NORMAL;
            if (addr == null)
            {
                addr = "::";
            }
            if (port < 0 || port > 65535)
            {
                return ExitReason.PORT;
            }
            if (IPAddress.TryParse(addr, out IPAddress ipAddr))
            {
                IPEndPoint bindEndpoint = new IPEndPoint(ipAddr, port);
                serverSocket = new Socket(bindEndpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                if (bindEndpoint.Address.Equals(IPAddress.IPv6Any))
                {
                    serverSocket.DualMode = true;
                }
                try
                {
                    serverSocket.Bind(bindEndpoint);
                }
                catch (SocketException e)
                {
                    switch (e.SocketErrorCode)
                    {
                        case SocketError.AccessDenied:
                            return ExitReason.DENIED;
                        case SocketError.AddressAlreadyInUse:
                            return ExitReason.INUSE;
                    }
                }
                IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
                if (bindEndpoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    any = new IPEndPoint(IPAddress.IPv6Any, 0);
                }
                while (Program.running)
                {
                    EndPoint recvFrom = any;
                    if (serverSocket.Poll(1024, SelectMode.SelectRead))
                    {
                        int bytesRead = serverSocket.ReceiveFrom(buffer, ref recvFrom);
                        if (bytesRead == 0)
                        {
                            continue;
                        }
                        handler.Handle(buffer, bytesRead, recvFrom as IPEndPoint);
                    }
                }
            }
            else
            {
                return ExitReason.ADDR;
            }
            return exitReason;
        }

        public void Send(byte[] data, int length, IPEndPoint endPoint)
        {
            try
            {
                serverSocket.SendTo(data, 0, length, SocketFlags.None, endPoint);
            }
            catch
            {
                //We don't care.
            }
        }
    }
}
