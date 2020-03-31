using System;
using System.Net;
using System.Collections.Generic;
using System.Threading;

namespace DarkNetworkUDP
{
    public class ReliableMessageHandler<T>
    {
        private Dictionary<int, ReliableMessageReceiveTracking> receivingMessages = new Dictionary<int, ReliableMessageReceiveTracking>();
        private Dictionary<int, ReliableMessageSendTracking<T>> sendingMessages = new Dictionary<int, ReliableMessageSendTracking<T>>();
        private int unorderedReceiveID = 0;
        private int orderedReceiveID = 0;
        //We start at ID 1 because 0 and -0 turns out to be the same number
        private int unorderedSendingID = 1;
        private int orderedSendingID = 1;
        //Message queue for ordered reliable messages
        private Dictionary<int, NetworkMessage> orderedHandleMessages = new Dictionary<int, NetworkMessage>();
        private int orderedHandleID = 0;
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
            lock (sendingMessages)
            {
                ReliableMessageSendTracking<T> rmst = ReliableMessageSendTracking<T>.Create();
                rmst.Setup(nm);
                if (nm.sendType == NetworkMessageType.UNORDERED_RELIABLE)
                {
                    sendingMessages.Add(unorderedSendingID++, rmst);
                }
                if (nm.sendType == NetworkMessageType.ORDERED_RELIABLE)
                {
                    sendingMessages.Add(-(orderedSendingID++), rmst);
                }
            }
        }

        public void Send()
        {
            if (connection.destroyed)
            {
                return;
            }

            int removeID = -1;
            foreach (KeyValuePair<int, ReliableMessageSendTracking<T>> kvp in sendingMessages)
            {
                while (connection.queuedOut < 64 * 1024)
                {
                    NetworkMessage nm = kvp.Value.GetMessage(kvp.Key, connection);
                    if (nm != null)
                    {
                        connection.handler.SendMessage(nm, connection);
                    }
                    else
                    {
                        if (kvp.Value.finished)
                        {
                            removeID = kvp.Key;
                        }
                        break;
                    }
                }
                if (connection.queuedOut >= 64 * 1024)
                {
                    break;
                }
            }
            if (removeID != -1)
            {
                lock (sendingMessages)
                {
                    sendingMessages.Remove(removeID);
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
            //Always send an ACK back immediately
            NetworkMessage nm = NetworkMessage.Create(-5, 8, NetworkMessageType.UNORDERED_UNRELIABLE);
            Array.Copy(data.data, 0, nm.data.data, 0, 8);
            handler.SendMessage(nm, connection);

            ReliableMessageReceiveTracking rmrt = null;
            if (receivingMessages.ContainsKey(recvSendingID))
            {
                rmrt = receivingMessages[recvSendingID];
            }
            //Received either a new chunk or a duplicate, if the messageID is higher than what we have received, it's new.
            if (rmrt == null)
            {
                if (recvSendingID > 0)
                {
                    int distance = recvSendingID - unorderedReceiveID;
                    //A message in the past (doesn't detect wrap around)
                    bool fromThePast = distance < 0;
                    //A future message received before we have wrapped around
                    bool massivelyInPast = -distance > (Int32.MaxValue / 4);
                    //A past message received when we have wrapped around
                    bool massivelyInFuture = distance > (Int32.MaxValue / 4);
                    if (fromThePast && !massivelyInPast || massivelyInFuture)
                    {
                        return;
                    }
                    while (recvSendingID != unorderedReceiveID)
                    {
                        if (unorderedReceiveID == Int32.MaxValue)
                        {
                            unorderedReceiveID = 0;
                        }
                        unorderedReceiveID++;
                        rmrt = ReliableMessageReceiveTracking.Create();
                        receivingMessages.Add(unorderedReceiveID, rmrt);
                    }
                }
                else
                {
                    int distance = -recvSendingID - orderedReceiveID;
                    //A message in the past (doesn't detect wrap around)
                    bool fromThePast = distance < 0;
                    //A future message received before we have wrapped around
                    bool massivelyInPast = -distance > (Int32.MaxValue / 4);
                    //A past message received when we have wrapped around
                    bool massivelyInFuture = distance > (Int32.MaxValue / 4);
                    if (fromThePast && !massivelyInPast || massivelyInFuture)
                    {
                        return;
                    }
                    while (-recvSendingID != orderedReceiveID)
                    {
                        if (orderedReceiveID == Int32.MaxValue)
                        {
                            orderedReceiveID = 0;
                        }
                        orderedReceiveID++;
                        rmrt = ReliableMessageReceiveTracking.Create();
                        lock (receivingMessages)
                        {
                            receivingMessages.Add(-orderedReceiveID, rmrt);
                        }
                    }
                }
                rmrt = receivingMessages[recvSendingID];
            }

            //Fist setup if needed
            if (rmrt.networkMessage == null)
            {
                if (recvSendingID > 0)
                {
                    rmrt.Setup(recvLength, NetworkMessageType.UNORDERED_RELIABLE);
                }
                else
                {
                    rmrt.Setup(recvLength, NetworkMessageType.ORDERED_RELIABLE);
                }
            }

            //Handle incoming data
            rmrt.Handle(recvPartID, recvLength, data);

            //We have all the parts
            if (rmrt.receivePartsLeft == 0)
            {
                if (recvPartID > 0)
                {
                    handler.Handle(rmrt.networkMessage, connection);
                }
                else
                {
                    //This message is received in order
                    if (-recvSendingID == orderedHandleID)
                    {
                        orderedHandleID++;
                        handler.Handle(rmrt.networkMessage, connection);
                    }
                    else
                    {
                        //This message is received out of order and we need to hold onto it
                        orderedHandleMessages.Add(-recvSendingID, rmrt.networkMessage);
                    }
                    //If a message fills the missing hole this can play out.
                    while (orderedHandleMessages.ContainsKey(orderedHandleID))
                    {
                        NetworkMessage handleMessage = orderedHandleMessages[orderedHandleID];
                        orderedHandleMessages.Remove(orderedHandleID);
                        handler.Handle(handleMessage, connection);
                        orderedHandleID++;
                    }
                }
                rmrt.Destroy();
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
            ReliableMessageSendTracking<T> rmst = null;
            if (sendingMessages.ContainsKey(recvAckReliableID))
            {
                rmst = sendingMessages[recvAckReliableID];
            }
            int recvAckPartID = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 4));
            if (rmst != null)
            {
                rmst.ReceiveACK(recvAckPartID);
                //Increase the speed because we got an ACK
                connection.speed += 500;
                if (connection.speed > Connection<T>.MAX_SPEED)
                {
                    connection.speed = Connection<T>.MAX_SPEED;
                }
                if (rmst.finished)
                {
                    lock (sendingMessages)
                    {
                        sendingMessages.Remove(recvAckReliableID);
                    }
                    rmst.Destroy();
                }
            }
        }

        public void ReleaseAllObjects()
        {
            lock (receivingMessages)
            {
                foreach (KeyValuePair<int, ReliableMessageReceiveTracking> kvp in receivingMessages)
                {
                    kvp.Value.Destroy();
                }
                receivingMessages.Clear();
            }
            lock (sendingMessages)
            {
                foreach (KeyValuePair<int, ReliableMessageSendTracking<T>> kvp in sendingMessages)
                {
                    kvp.Value.Destroy();
                }
                sendingMessages.Clear();
            }
            unorderedReceiveID = 0;
            orderedReceiveID = 0;
            unorderedSendingID = 1;
            orderedSendingID = 1;
        }
    }
}