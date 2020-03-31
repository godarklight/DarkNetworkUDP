using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace DarkNetworkUDP
{
    public class DarkNetwork<T>
    {
        internal bool clientMode;
        private bool running;
        private NetworkHandler<T> handler;
        private byte[] sendBuffer = new byte[2048];
        private byte[] receiveBuffer = new byte[2048];
        private Dictionary<Connection<T>, Queue<NetworkMessage>> sendMessages = new Dictionary<Connection<T>, Queue<NetworkMessage>>();
        Socket socket;
        private Thread receiveThread;
        private Thread sendThread;
        private AutoResetEvent sendEvent = new AutoResetEvent(false);
        public void SetupServer(IPEndPoint listenAddress, NetworkHandler<T> handler)
        {
            if (running)
            {
                return;
            }
            Setup(listenAddress, handler);
        }

        public void SetupClient(IPEndPoint[] connectAddresses, NetworkHandler<T> handler)
        {
            if (running)
            {
                return;
            }
            IPEndPoint any = new IPEndPoint(IPAddress.IPv6Any, 0);
            Setup(any, handler);
            foreach (IPEndPoint connectAddress in connectAddresses)
            {
                SendInitialHeartbeat(connectAddress);
            }
            clientMode = true;
        }

        public void SetupClient(IPEndPoint connectAddress, NetworkHandler<T> handler)
        {
            if (running)
            {
                return;
            }
            IPEndPoint any = new IPEndPoint(IPAddress.IPv6Any, 0);
            Setup(any, handler);
            SendInitialHeartbeat(connectAddress);
            clientMode = true;
        }

        private void Setup(IPEndPoint bindAddress, NetworkHandler<T> handler)
        {
            ByteRecycler.AddPoolSize(2048);
            this.handler = handler;
            handler.SetDarkNetwork(this);
            socket = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
#if NETFRAMEWORK
            try
            {
                socket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, 0);
            }
            catch (Exception e)
            {
                //We don't care.
            }
#else
            if (bindAddress.AddressFamily == AddressFamily.InterNetworkV6 && bindAddress.Equals(IPAddress.IPv6Any))
            {
                socket.DualMode = true;
            }
#endif
            socket.Bind(bindAddress);
            running = true;
            sendThread = new Thread(new ThreadStart(SendLoop));
            sendThread.Name = "DarkNetworkUDP Send Thread";
            sendThread.Start();
            receiveThread = new Thread(new ThreadStart(ReceiveLoop));
            receiveThread.Name = "DarkNetworkUDP Receieve Thread";
            receiveThread.Start();
        }

        private void ReceiveLoop()
        {
            IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
            if (socket.LocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                any = new IPEndPoint(IPAddress.IPv6Any, 0);
            }
            while (running)
            {
                EndPoint recvAddr = any;
                while (socket.Poll(10000, SelectMode.SelectRead))
                {
                    int bytesRead = socket.ReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ref recvAddr);
                    handler.HandleRaw(receiveBuffer, bytesRead, recvAddr as IPEndPoint);
                }
            }
            sendThread.Join();
            socket.Close();
            socket = null;
        }

        private void SendLoop()
        {
            while (running)
            {
                sendEvent.WaitOne(10);
                lock (sendMessages)
                {
                    foreach (KeyValuePair<Connection<T>, Queue<NetworkMessage>> kvp in sendMessages)
                    {
                        Connection<T> c = kvp.Key;
                        Queue<NetworkMessage> sendMessageQueue = kvp.Value;
                        if (c.lastTokensTime == 0)
                        {
                            c.tokens = Connection<T>.TOKENS_MAX;
                            c.lastTokensTime = DateTime.UtcNow.Ticks;
                        }
                        long ticksElapsed = DateTime.UtcNow.Ticks - c.lastTokensTime;
                        c.lastTokensTime = DateTime.UtcNow.Ticks;
                        c.tokens += (ticksElapsed * c.speed) / TimeSpan.TicksPerSecond;
                        if (c.tokens > Connection<T>.TOKENS_MAX)
                        {
                            c.tokens = Connection<T>.TOKENS_MAX;
                        }
                        while (sendMessageQueue.Count > 0)
                        {
                            //Speed control
                            NetworkMessage peekMessage = sendMessageQueue.Peek();
                            if (!peekMessage.IsReliable() && peekMessage.data != null)
                            {
                                if (peekMessage.data.Length < c.tokens)
                                {
                                    c.tokens -= peekMessage.data.Length;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            //Actual send
                            NetworkMessage nm = sendMessageQueue.Dequeue();
                            if (nm.data != null)
                            {
                                c.queuedOut -= nm.data.Length;
                            }
                            int sendBytes = 0;
                            if (!nm.IsReliable())
                            {
                                sendBytes = WriteRawMessageToBuffer(nm);
                            }
                            NetworkMessage queueMessage = nm;
                            if (queueMessage.IsReliable())
                            {
                                c.reliableMessageHandler.Queue(nm);
                                c.reliableMessageHandler.Send();
                                continue;
                            }
                            if (queueMessage.sendType == NetworkMessageType.ORDERED_UNRELIABLE)
                            {
                                nm = NetworkMessage.Create(-3, sendBytes + 4, NetworkMessageType.UNORDERED_UNRELIABLE);
                                DarkUtils.WriteInt32ToByteArray(c.sendOrderID, nm.data.data, 0);
                                Array.Copy(sendBuffer, 0, nm.data.data, 4, sendBytes);
                                sendBytes = WriteRawMessageToBuffer(nm);
                                c.sendOrderID++;
                                if (c.sendOrderID == Int32.MaxValue)
                                {
                                    c.sendOrderID = 0;
                                }
                            }
                            int bytesSent = socket.SendTo(sendBuffer, 0, sendBytes, SocketFlags.None, c.remoteEndpoint);
                            if (bytesSent != sendBytes)
                            {
                                throw new Exception("Failed to send complete message!");
                            }
                            if (queueMessage.sendType == NetworkMessageType.ORDERED_UNRELIABLE)
                            {
                                ByteRecycler.ReleaseObject(nm.data);
                                Recycler<NetworkMessage>.ReleaseObject(nm);
                                nm = queueMessage;
                            }
                            int usageLeft = Interlocked.Decrement(ref nm.usageCount);
                            if (usageLeft == 0)
                            {
                                if (nm.data != null)
                                {
                                    ByteRecycler.ReleaseObject(nm.data);
                                }
                                Recycler<NetworkMessage>.ReleaseObject(nm);
                            }
                        }
                    }
                }
                handler.SendHeartbeat();
            }
        }

        byte[] initialHeartBeat = new byte[20];
        private void SendInitialHeartbeat(IPEndPoint endPoint)
        {
            DarkUtils.WriteMagicHeader(initialHeartBeat, 0);
            DarkUtils.WriteInt32ToByteArray(-1, initialHeartBeat, 4);
            DarkUtils.WriteInt32ToByteArray(8, initialHeartBeat, 8);
            DarkUtils.WriteInt64ToByteArray(DateTime.UtcNow.Ticks, initialHeartBeat, 12);
            //Send 4 times.
            for (int i = 0; i < 4; i++)
            {
                socket.SendTo(initialHeartBeat, endPoint);
            }
        }

        private int WriteRawMessageToBuffer(NetworkMessage nm)
        {
            DarkUtils.WriteMagicHeader(sendBuffer, 0);
            DarkUtils.WriteInt32ToByteArray(nm.type, sendBuffer, 4);
            if (nm.data == null || nm.data.Length == 0)
            {
                DarkUtils.WriteInt32ToByteArray(0, sendBuffer, 8);
                return 12;
            }
            DarkUtils.WriteInt32ToByteArray(nm.data.Length, sendBuffer, 8);
            Array.Copy(nm.data.data, 0, sendBuffer, 12, nm.data.Length);
            return 12 + nm.data.Length;
        }

        public void Shutdown()
        {
            running = false;
        }

        public void SendRaw(NetworkMessage networkMessage, Connection<T> connection)
        {
            lock (sendMessages)
            {
                if (!sendMessages.ContainsKey(connection))
                {
                    sendMessages.Add(connection, new Queue<NetworkMessage>());
                }
                sendMessages[connection].Enqueue(networkMessage);
                if (networkMessage.data != null)
                {
                    connection.queuedOut += networkMessage.data.Length;
                }
                sendEvent.Set();
            }
        }

        public void ReleaseAllObjects(Connection<T> connection)
        {
            lock (sendMessages)
            {
                if (sendMessages.ContainsKey(connection))
                {
                    Queue<NetworkMessage> dropMessages = sendMessages[connection];
                    while (dropMessages.Count > 0)
                    {
                        NetworkMessage dropMessage = dropMessages.Dequeue();
                        int usageLeft = Interlocked.Decrement(ref dropMessage.usageCount);
                        if (usageLeft == 0)
                        {
                            if (dropMessage.data != null)
                            {
                                ByteRecycler.ReleaseObject(dropMessage.data);
                            }
                            dropMessage.data = null;
                            Recycler<NetworkMessage>.ReleaseObject(dropMessage);
                        }
                    }
                    sendMessages.Remove(connection);
                }
            }
        }
    }
}
