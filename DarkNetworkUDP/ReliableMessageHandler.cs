using System;
using System.Net;
using System.Collections.Generic;
using System.Threading;

namespace DarkNetworkUDP
{
    public class ReliableMessageHandler<T>
    {
        private class BoxedLong
        {
            public long value;
        }
        private HashSet<int> receivePartsLeft = new HashSet<int>();
        private ByteArray receivingMessage = null;
        private Queue<NetworkMessage> sendingMessages = new Queue<NetworkMessage>();
        private NetworkMessage sendingMessage = null;
        private int receivingID = 0;
        private int sendingID = 0;
        private Dictionary<int, BoxedLong> partSendTime = new Dictionary<int, BoxedLong>();
        private int partSendTotal = 0;
        private Connection<T> connection;
        private NetworkHandler<T> handler;
        public ReliableMessageHandler(Connection<T> connection, NetworkHandler<T> handler)
        {
            this.connection = connection;
            this.handler = handler;
        }

        public void Queue(NetworkMessage nm)
        {
            if (connection.destroyed)
            {
                return;
            }
            sendingMessages.Enqueue(nm);
        }

        public void Send()
        {
            if (connection.destroyed)
            {
                return;
            }
            if (sendingMessage == null && sendingMessages.Count > 0)
            {
                sendingMessage = sendingMessages.Dequeue();
                int totalMessageBytes = 12;
                if (sendingMessage.data != null && sendingMessage.data.Length > 0)
                {
                    totalMessageBytes = 12 + sendingMessage.data.Length;
                }
                partSendTotal = (totalMessageBytes + 12) / 500;
                int lastSplit = (totalMessageBytes + 12) % 500;
                if (lastSplit > 0)
                {
                    partSendTotal++;
                }
                lock (partSendTime)
                {
                    for (int i = 0; i < partSendTotal; i++)
                    {
                        BoxedLong boxedLong = Recycler<BoxedLong>.GetObject();
                        boxedLong.value = 0;
                        partSendTime.Add(i, boxedLong);
                    }
                }
            }
            if (sendingMessage != null)
            {
                if (connection.queuedOut > (64 * 1024))
                {
                    return;
                }
                long currentTime = DateTime.UtcNow.Ticks;
                long latency = connection.avgLatency;
                long minLatency = 10 * TimeSpan.TicksPerMillisecond;
                if (latency < minLatency)
                {
                    latency = minLatency;
                }
                //Transmit a piece every 1.5RTT if we haven't got an ACK for it.
                latency = (long)(latency * 1.5d);
                long sendIfUnder = currentTime - latency;
                lock (partSendTime)
                {
                    int totalLength = 12;
                    if (sendingMessage.data != null && sendingMessage.data.Length > 0)
                    {
                        totalLength = sendingMessage.data.Length + 12;
                    }
                    foreach (KeyValuePair<int, BoxedLong> kvp in partSendTime)
                    {
                        if (kvp.Value.value < sendIfUnder)
                        {
                            partSendTime[kvp.Key].value = currentTime;
                            int bytesToSend = 500;
                            if (kvp.Key == (partSendTotal - 1))
                            {
                                bytesToSend = totalLength % 500;
                            }
                            NetworkMessage nm = NetworkMessage.Create(-4, 12 + bytesToSend);
                            DarkUtils.WriteInt32ToByteArray(sendingID, nm.data.data, 0);
                            DarkUtils.WriteInt32ToByteArray(kvp.Key, nm.data.data, 4);
                            if (sendingMessage.data != null)
                            {
                                DarkUtils.WriteInt32ToByteArray(totalLength, nm.data.data, 8);
                                if (kvp.Key == 0)
                                {
                                    DarkUtils.WriteMagicHeader(nm.data.data, 12);
                                    DarkUtils.WriteInt32ToByteArray(sendingMessage.type, nm.data.data, 16);
                                    DarkUtils.WriteInt32ToByteArray(sendingMessage.data.Length, nm.data.data, 20);
                                    Array.Copy(sendingMessage.data.data, 0, nm.data.data, 24, bytesToSend - 12);
                                }
                                else
                                {
                                    Array.Copy(sendingMessage.data.data, (kvp.Key * 500) - 12, nm.data.data, 12, bytesToSend);
                                }
                            }
                            else
                            {
                                DarkUtils.WriteInt32ToByteArray(12, nm.data.data, 8);
                                DarkUtils.WriteMagicHeader(nm.data.data, 12);
                                DarkUtils.WriteInt32ToByteArray(sendingMessage.type, nm.data.data, 16);
                                DarkUtils.WriteInt32ToByteArray(0, nm.data.data, 20);
                            }
                            handler.SendMessage(nm, connection);
                            //Only queue up 64kb at a time
                            if (connection.queuedOut > (64 * 1024))
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }

        public void Handle(ByteArray data, Connection<T> connection)
        {
            if (connection.destroyed)
            {
                return;
            }
            if (data.Length < 12)
            {
                return;
            }
            int recvSendingID = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 0));
            int recvPartID = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 4));
            int recvLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 8));
            if (recvSendingID <= receivingID)
            {
                NetworkMessage nm = NetworkMessage.Create(-5, 8);
                DarkUtils.WriteInt32ToByteArray(recvSendingID, nm.data.data, 0);
                DarkUtils.WriteInt32ToByteArray(recvPartID, nm.data.data, 4);
                handler.SendMessage(nm, connection);
            }
            if (receivingMessage == null && recvSendingID == receivingID)
            {
                receivingMessage = ByteRecycler.GetObject(recvLength);
                int numberOfParts = recvLength / 500;
                if (recvLength % 500 != 0)
                {
                    numberOfParts++;
                }
                for (int i = 0; i < numberOfParts; i++)
                {
                    receivePartsLeft.Add(i);
                }
            }
            lock (receivePartsLeft)
            {
                if (receivePartsLeft.Contains(recvPartID))
                {
                    int startIndex = recvPartID * 500;
                    int bytesToCopy = recvLength - startIndex;
                    if (bytesToCopy > 500)
                    {
                        bytesToCopy = 500;
                    }
                    if (data.Length < 12 + bytesToCopy)
                    {
                        return;
                    }
                    Array.Copy(data.data, 12, receivingMessage.data, startIndex, bytesToCopy);
                    receivePartsLeft.Remove(recvPartID);
                    if (receivePartsLeft.Count == 0)
                    {
                        handler.Handle(receivingMessage.data, receivingMessage.Length, connection.remoteEndpoint);
                        ByteRecycler.ReleaseObject(receivingMessage);
                        receivingMessage = null;
                        receivingID++;
                        if (receivingID == Int32.MaxValue)
                        {
                            receivingID = 0;
                        }
                    }
                }
            }
        }

        public void HandleACK(ByteArray data, Connection<T> connection)
        {
            if (connection.destroyed)
            {
                return;
            }
            if (data.Length != 8)
            {
                return;
            }
            int recvAckReliableID = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 0));
            if (sendingID != recvAckReliableID)
            {
                return;
            }
            int recvAckPartID = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 4));
            lock (partSendTime)
            {
                if (partSendTime.ContainsKey(recvAckPartID))
                {
                    Recycler<BoxedLong>.ReleaseObject(partSendTime[recvAckPartID]);
                    partSendTime.Remove(recvAckPartID);
                }
                if (partSendTime.Count == 0)
                {
                    int usageLeft = Interlocked.Decrement(ref sendingMessage.usageCount);
                    if (usageLeft == 0)
                    {
                        if (sendingMessage.data != null)
                        {
                            ByteRecycler.ReleaseObject(sendingMessage.data);
                        }
                        Recycler<NetworkMessage>.ReleaseObject(sendingMessage);
                    }
                    sendingMessage = null;
                    sendingID++;
                    if (sendingID == Int32.MaxValue)
                    {
                        sendingID = 0;
                    }
                }
            }
        }

        public void ReleaseAllObjects()
        {
            if (sendingMessage != null)
            {
                int usageLeft = Interlocked.Decrement(ref sendingMessage.usageCount);
                if (usageLeft == 0)
                {
                    if (sendingMessage.data != null)
                    {
                        ByteRecycler.ReleaseObject(sendingMessage.data);
                    }
                    Recycler<NetworkMessage>.ReleaseObject(sendingMessage);
                }
            }
            while (sendingMessages.Count > 0)
            {
                NetworkMessage dropMessage = sendingMessages.Dequeue();
                int usageLeft = Interlocked.Decrement(ref dropMessage.usageCount);
                if (usageLeft == 0)
                {
                    if (dropMessage.data != null)
                    {
                        ByteRecycler.ReleaseObject(dropMessage.data);
                    }
                    Recycler<NetworkMessage>.ReleaseObject(dropMessage);
                }
            }
            if (receivingMessage != null)
            {
                ByteRecycler.ReleaseObject(receivingMessage);
                receivingMessage = null;
            }            
            foreach (BoxedLong bl in partSendTime.Values)
            {
                Recycler<BoxedLong>.ReleaseObject(bl);
            }
            partSendTime.Clear();
            receivePartsLeft.Clear();
        }
    }
}