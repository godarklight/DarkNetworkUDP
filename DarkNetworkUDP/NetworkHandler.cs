using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace DarkNetworkUDP
{
    public class NetworkHandler<T>
    {
        private bool useMessagePump;
        private Func<Connection<T>, T> connectCallback;
        private Action<Connection<T>> disconnectCallback;
        private List<Guid> disconnectList = new List<Guid>();
        private Dictionary<int, NetworkCallback<T>> callbacks = new Dictionary<int, NetworkCallback<T>>();
        private List<QueuedMessage<T>> messages;
        private DarkNetwork<T> network;
        private Dictionary<Guid, Connection<T>> connections = new Dictionary<Guid, Connection<T>>();
        private Connection<T> serverConnection;

        public NetworkHandler(bool useMessagePump)
        {
            this.useMessagePump = useMessagePump;
            if (useMessagePump)
            {
                messages = new List<QueuedMessage<T>>();
            }
            callbacks.Add(-1, HandleHeartbeat);
            callbacks.Add(-2, HandleLatency);
            callbacks.Add(-3, HandleOrdered);
            callbacks.Add(-4, HandleReliable);
            callbacks.Add(-5, HandleReliableACK);
        }

        internal void SetDarkNetwork(DarkNetwork<T> network)
        {
            this.network = network;
        }

        public void RegisterCallback(int id, NetworkCallback<T> callback)
        {
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            callbacks[id] = callback;
        }

        public void RegisterConnectCallback(Func<Connection<T>, T> callback)
        {
            connectCallback = callback;
        }


        public void RegisterDisconnectCallback(Action<Connection<T>> callback)
        {
            disconnectCallback = callback;
        }

        internal void HandleRaw(byte[] data, int length, IPEndPoint endPoint)
        {
            if (length < 12)
            {
                return;
            }
            //Magic header, DARK
            if (data[0] != 68 || data[1] != 65 || data[2] != 82 || data[3] != 75)
            {
                return;
            }
            Connection<T> connection = null;
            Guid messageOwner = DarkUtils.GuidFromIPEndpoint(endPoint);
            if (!connections.ContainsKey(messageOwner))
            {
                if (connection == null)
                {
                    connection = new Connection<T>();
                    connection.handler = this;
                    connection.reliableMessageHandler = new ReliableMessageHandler<T>(connection, this);
                    if (network.clientMode && serverConnection == null)
                    {
                        serverConnection = connection;
                    }
                }
                connection.lastReceiveTime = DateTime.UtcNow.Ticks;
                connection.remoteEndpoint = endPoint;
                lock (connections)
                {
                    connections.Add(messageOwner, connection);
                }
                if (connectCallback != null)
                {
                    connection.state = connectCallback(connection);
                }
            }
            connection = connections[messageOwner];
            int messageType = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 4));
            int messageLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, 8));
            if (length < messageLength + 12)
            {
                //Malformed message
                return;
            }
            NetworkMessage nm = NetworkMessage.Create(messageType, messageLength, NetworkMessageType.UNORDERED_UNRELIABLE);
            if (nm.data != null && nm.data.Length > 0)
            {
                Array.Copy(data, 12, nm.data.data, 0, nm.data.Length);
            }
            Handle(nm, connection);
        }

        internal void HandleRawError(IPEndPoint endPoint)
        {
            Connection<T> disconnectConnection = GetConnection(endPoint);
            if (disconnectConnection != null)
            {
                HandleRawError(disconnectConnection);
            }
        }

        internal void HandleRawError(Connection<T> disconnectConnection)
        {
            //Will timeout in the heartbeat and be safely removed.
            disconnectConnection.lastReceiveTime = 0;
        }

        public void Handle(NetworkMessage nm, Connection<T> connection)
        {
            if (useMessagePump && nm.type >= 0)
            {
                lock (messages)
                {
                    QueuedMessage<T> qm = Recycler<QueuedMessage<T>>.GetObject();
                    qm.networkMessage = nm;
                    qm.connection = connection;
                    messages.Add(qm);
                }
            }
            else
            {
                HandleReal(nm, connection);
            }
        }

        private void HandleReal(NetworkMessage nm, Connection<T> connection)
        {
            connection.lastReceiveTime = DateTime.UtcNow.Ticks;
            if (callbacks.ContainsKey(nm.type))
            {
                callbacks[nm.type](nm.data, connection);
            }
            nm.Destroy();
        }

        internal void SendHeartbeat()
        {
            lock (connections)
            {
                long currentTime = DateTime.UtcNow.Ticks;
                foreach (KeyValuePair<Guid, Connection<T>> c in connections)
                {
                    if (currentTime > c.Value.lastReceiveTime + TimeSpan.TicksPerSecond * 20)
                    {
                        disconnectList.Add(c.Key);
                    }
                    if (currentTime > (c.Value.lastHeartbeatTime + TimeSpan.TicksPerSecond))
                    {
                        c.Value.lastHeartbeatTime = currentTime;
                        NetworkMessage nm = NetworkMessage.Create(-1, 8, NetworkMessageType.UNORDERED_UNRELIABLE);
                        DarkUtils.WriteInt64ToByteArray(DateTime.UtcNow.Ticks, nm.data.data, 0);
                        SendMessageWithHighPriority(nm, c.Value);
                    }
                    RateControl<T>.Update(c.Value);
                    c.Value.reliableMessageHandler.Send();
                }
                foreach (Guid disconnectConnectionGuid in disconnectList)
                {
                    Connection<T> disconnectConnection = connections[disconnectConnectionGuid];
                    if (disconnectCallback != null)
                    {
                        disconnectCallback(disconnectConnection);
                    }
                    disconnectConnection.reliableMessageHandler.ReleaseAllObjects();
                    network.ReleaseAllObjects(disconnectConnection);
                    connections.Remove(disconnectConnectionGuid);
                }
                disconnectList.Clear();
            }
        }
        private void HandleHeartbeat(ByteArray data, Connection<T> connection)
        {
            if (data.Length != 8)
            {
                return;
            }
            NetworkMessage nm = NetworkMessage.Create(-2, 8, NetworkMessageType.UNORDERED_UNRELIABLE);
            Array.Copy(data.data, 0, nm.data.data, 0, data.Length);
            SendMessageWithHighPriority(nm, connection);
        }
        private void HandleLatency(ByteArray data, Connection<T> connection)
        {
            long receiveTime = DateTime.UtcNow.Ticks;
            long sendTime = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(data.data, 0));
            //Work out this heartbeats latency
            connection.latency = receiveTime - sendTime;
            long oldLatency = connection.latencyArray[connection.latencyPointer];
            connection.latencyArray[connection.latencyPointer++] = connection.latency;
            connection.latencyTotal -= oldLatency;
            connection.latencyTotal += connection.latency;
            if (connection.latencyPointer > connection.latencyValid)
            {
                connection.latencyValid = connection.latencyPointer;
            }
            if (connection.latencyPointer >= connection.latencyArray.Length)
            {
                connection.latencyPointer = 0;
            }
            connection.avgLatency = connection.latencyTotal / connection.latencyValid;
        }

        private void HandleOrdered(ByteArray data, Connection<T> connection)
        {
            if (data.Length < 12)
            {
                return;
            }
            int orderID = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 0));
            int distance = orderID - connection.receiveOrderID;
            bool orderOK = false;
            //Normal case
            if ((distance < (Int32.MaxValue / 4)) && distance > 0)
            {
                orderOK = true;
            }
            //Wrap around case
            if (distance < -(Int32.MaxValue / 4))
            {
                orderOK = true;
            }
            if (orderOK)
            {
                connection.receiveOrderID = orderID;
                int messageType = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 4));
                int messageLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 8));
                NetworkMessage handleOrdered = NetworkMessage.Create(messageType, messageLength, NetworkMessageType.ORDERED_UNRELIABLE);
                if (messageLength > 0 && messageLength == (data.Length + 12))
                {
                    Array.Copy(data.data, 12, handleOrdered.data.data, 0, messageLength);
                }
                Handle(handleOrdered, connection);
            }
        }

        private void HandleReliable(ByteArray data, Connection<T> connection)
        {
            connection.reliableMessageHandler.Handle(data, connection);
        }

        private void HandleReliableACK(ByteArray data, Connection<T> connection)
        {
            connection.reliableMessageHandler.HandleACK(data, connection);
        }

        public void FireCallbacks()
        {
            if (!useMessagePump)
            {
                return;
            }
            lock (messages)
            {
                foreach (QueuedMessage<T> qm in messages)
                {
                    HandleReal(qm.networkMessage, qm.connection);
                    Recycler<QueuedMessage<T>>.ReleaseObject(qm);
                }
                messages.Clear();
            }
        }

        public void SendMessage(NetworkMessage nm)
        {
            if (serverConnection != null)
            {
                SendMessage(nm, serverConnection);
            }
            else
            {
                nm.Destroy();
            }
        }

        public void SendMessageToAll(NetworkMessage nm)
        {
            lock (connections)
            {
                if (connections.Count > 0)
                {
                    nm.usageCount = connections.Count;
                    foreach (KeyValuePair<Guid, Connection<T>> kvp in connections)
                    {
                        SendMessage(nm, kvp.Value);
                    }
                }
                else
                {
                    nm.Destroy();
                }
            }

        }

        public void SendMessage(NetworkMessage nm, Connection<T> c)
        {
            if (network != null && !c.destroyed)
            {
                network.SendRaw(nm, c);
            }
            else
            {
                nm.Destroy();
            }
        }

        internal void SendMessageWithHighPriority(NetworkMessage nm, Connection<T> c)
        {
            if (network != null && !c.destroyed)
            {
                network.SendRawHighPriority(nm, c);
            }
            else
            {
                nm.Destroy();
            }
        }

        public Connection<T> GetConnection(IPEndPoint endPoint)
        {
            Guid storeKey = DarkUtils.GuidFromIPEndpoint(endPoint);
            if (connections.ContainsKey(storeKey))
            {
                return connections[storeKey];
            }
            return null;
        }
    }
}
