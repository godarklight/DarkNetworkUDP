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
        private Queue<QueuedMessage<T>> sendHighPriorityMessages = new Queue<QueuedMessage<T>>();
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
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = socket.ReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ref recvAddr);
                    }
                    catch
                    {
                        handler.HandleRawError((IPEndPoint)recvAddr);
                    }
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
                sendEvent.WaitOne(50);
                bool sending = true;
                while (sending)
                {
                    sending = false;
                    lock (sendHighPriorityMessages)
                    {
                        while (sendHighPriorityMessages.Count > 0)
                        {
                            QueuedMessage<T> qm = sendHighPriorityMessages.Dequeue();
                            if (qm.networkMessage.data != null)
                            {
                                qm.connection.queuedOut -= qm.networkMessage.data.Length;
                            }
                            ActualSendMessage(qm.networkMessage, qm.connection);
                            Recycler<QueuedMessage<T>>.ReleaseObject(qm);
                            sending = true;
                        }
                    }
                    lock (sendMessages)
                    {
                        foreach (KeyValuePair<Connection<T>, Queue<NetworkMessage>> kvp in sendMessages)
                        {
                            Connection<T> connection = kvp.Key;
                            Queue<NetworkMessage> sendQueue = kvp.Value;
                            while (sendQueue.Count > 0)
                            {
                                if (sendHighPriorityMessages.Count > 0)
                                {
                                    break;
                                }
                                NetworkMessage peekMessage = sendQueue.Peek();
                                //Reliable messages are broken up and do not create actual sends.
                                if (peekMessage.IsReliable())
                                {
                                    NetworkMessage reliableMessage = sendQueue.Dequeue();
                                    if (reliableMessage.data != null)
                                    {
                                        connection.queuedOut -= reliableMessage.data.Length;
                                    }
                                    connection.reliableMessageHandler.Queue(reliableMessage);
                                    connection.reliableMessageHandler.Send();
                                    continue;
                                }
                                else
                                {
                                    if (peekMessage.data != null)
                                    {
                                        if (peekMessage.data.Length > connection.tokens)
                                        {
                                            //Not enough tokens to send
                                            break;
                                        }
                                    }
                                    NetworkMessage sendMessage = sendQueue.Dequeue();
                                    if (sendMessage.data != null)
                                    {
                                        connection.queuedOut -= sendMessage.data.Length;
                                    }
                                    ActualSendMessage(sendMessage, connection);
                                    sending = true;
                                }
                            }
                            if (sendHighPriorityMessages.Count > 0)
                            {
                                break;
                            }
                        }
                    }
                }
                handler.SendHeartbeat();
            }
        }

        private NetworkMessage ConvertOrderedUnreliableMessage(NetworkMessage input, Connection<T> connection)
        {
            if (input.sendType != NetworkMessageType.ORDERED_UNRELIABLE)
            {
                return input;
            }
            int newSize = 12;
            if (input.data != null)
            {
                newSize += input.data.Length;
            }
            NetworkMessage converted = NetworkMessage.Create(-3, newSize, NetworkMessageType.UNORDERED_UNRELIABLE);
            DarkUtils.WriteInt32ToByteArray(connection.sendOrderID++, converted.data.data, 0);
            if (connection.sendOrderID == Int32.MaxValue)
            {
                connection.sendOrderID = 0;
            }
            DarkUtils.WriteInt32ToByteArray(input.type, converted.data.data, 4);
            DarkUtils.WriteInt32ToByteArray(newSize - 8, converted.data.data, 8);
            if (input.data != null)
            {
                Array.Copy(input.data.data, 0, converted.data.data, 12, input.data.Length);
            }
            input.Destroy();
            return input;
        }

        private void ActualSendMessage(NetworkMessage sendMessage, Connection<T> connection)
        {
            if (sendMessage.data != null)
            {
                connection.tokens -= sendMessage.data.Length;
            }
            NetworkMessage converted = ConvertOrderedUnreliableMessage(sendMessage, connection);
            if (sendMessage.type == -4)
            {
                connection.reliableMessageHandler.RealSendPart(sendMessage);
            }
            int sendBytes = WriteRawMessageToBuffer(sendMessage);
            sendMessage.Destroy();
            try
            {
                int bytesSent = socket.SendTo(sendBuffer, 0, sendBytes, SocketFlags.None, connection.remoteEndpoint);
            }
            catch
            {
                handler.HandleRawError(connection);
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

        public void SendRawHighPriority(NetworkMessage networkMessage, Connection<T> connection)
        {
            lock (sendHighPriorityMessages)
            {
                QueuedMessage<T> qm = Recycler<QueuedMessage<T>>.GetObject();
                qm.networkMessage = networkMessage;
                qm.connection = connection;
                sendHighPriorityMessages.Enqueue(qm);
                if (networkMessage.data != null)
                {
                    connection.queuedOut += networkMessage.data.Length;
                }
                sendEvent.Set();
            }
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
                        dropMessage.Destroy();
                    }
                    sendMessages.Remove(connection);
                }
            }
        }

        public int GetSendCount()
        {
            int retVal = 0;
            lock (sendMessages)
            {
                foreach (KeyValuePair<Connection<T>, Queue<NetworkMessage>> thing in sendMessages)
                {
                    retVal += thing.Value.Count;
                }
            }
            return retVal;
        }
    }
}
