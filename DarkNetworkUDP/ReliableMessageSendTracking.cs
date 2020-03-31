using System;
using System.Collections.Generic;

namespace DarkNetworkUDP
{
    public class ReliableMessageSendTracking<T>
    {
        public bool finished = false;
        public NetworkMessage networkMessage;
        public long[] sendParts;
        public int sendPartsLength = 0;
        public int sendPartsLeft = 0;
        public int nextSendPart = 0;

        public static ReliableMessageSendTracking<T> Create()
        {
            ReliableMessageSendTracking<T> rmst = Recycler<ReliableMessageSendTracking<T>>.GetObject();
            rmst.sendPartsLength = 0;
            rmst.sendPartsLeft = 0;
            rmst.nextSendPart = 0;
            rmst.finished = false;
            return rmst;
        }

        public void Destroy()
        {
            if (networkMessage != null)
            {
                networkMessage.Destroy();
            }
            networkMessage = null;
            Recycler<ReliableMessageSendTracking<T>>.ReleaseObject(this);
        }

        public void Setup(NetworkMessage nm)
        {
            this.networkMessage = nm;
            //Header part
            int newMessageLengthBytes = 4;
            //Data part
            if (nm.data != null)
            {
                newMessageLengthBytes += nm.data.size;
            }
            //Number of chunks needed
            sendPartsLength = newMessageLengthBytes / 500;
            if (newMessageLengthBytes % 500 > 0)
            {
                sendPartsLength++;
            }
            //Check the send array can fit the chunks
            if (sendParts == null || sendParts.Length < sendPartsLength)
            {
                int createLength = 128;
                while (sendPartsLength > createLength)
                {
                    createLength = createLength * 4;
                }
                sendParts = new long[createLength];
            }
            sendPartsLeft = sendPartsLength;
            for (int i = 0; i < sendPartsLength; i++)
            {
                sendParts[i] = 0;
            }
        }

        public NetworkMessage GetMessage(int id, Connection<T> connection)
        {
            if (finished)
            {
                return null;
            }
            int checkedThisRound = 0;
            //Always assume at least 10ms lag
            long latency = connection.latency;
            if (latency < 10 * TimeSpan.TicksPerMillisecond)
            {
                latency = 10 * TimeSpan.TicksPerMillisecond;
            }
            //Resend message if it has been 2 RTT's
            long checkTime = DateTime.UtcNow.Ticks - (latency * 2);
            bool found = false;
            while (!found)
            {
                if (sendParts[nextSendPart] != -1 && checkTime > sendParts[nextSendPart])
                {
                    found = true;
                }
                else
                {
                    nextSendPart++;
                    if (nextSendPart == sendPartsLength)
                    {
                        nextSendPart = 0;
                    }
                    checkedThisRound++;
                    if (checkedThisRound == sendPartsLength)
                    {
                        //We checked to see if we could send any chunks and didn't find any.
                        break;
                    }
                }
            }
            if (found)
            {
                int totalSize = 4;
                if (networkMessage.data != null)
                {
                    totalSize += networkMessage.data.Length;
                }
                int thisSendSize = 500;
                if (nextSendPart == (sendPartsLength - 1))
                {
                    thisSendSize = totalSize % 500;
                }
                //This is a retransmit, count the lost data
                if (sendParts[nextSendPart] != 0)
                {
                    connection.lostData += thisSendSize;
                    //If we have lost 64kb of data let's slow down our send speed
                    if (connection.lostData > 64 * 1024)
                    {
                        connection.lostData = 0;
                        connection.speed = connection.speed / 2;
                        if (connection.speed < Connection<T>.MIN_SPEED)
                        {
                            connection.speed = Connection<T>.MIN_SPEED;
                        }
                    }
                }
                NetworkMessage sendMessage = NetworkMessage.Create(-4, 12 + thisSendSize, NetworkMessageType.UNORDERED_UNRELIABLE);
                DarkUtils.WriteInt32ToByteArray(id, sendMessage.data.data, 0);
                DarkUtils.WriteInt32ToByteArray(nextSendPart, sendMessage.data.data, 4);
                //Don't include the header in the message length, we know it's there
                DarkUtils.WriteInt32ToByteArray(totalSize - 4, sendMessage.data.data, 8);
                if (nextSendPart == 0)
                {
                    DarkUtils.WriteInt32ToByteArray(networkMessage.type, sendMessage.data.data, 12);
                    if (networkMessage.data != null && networkMessage.data.Length > 0)
                    {
                        Array.Copy(networkMessage.data.data, 0, sendMessage.data.data, 16, thisSendSize - 4);
                    }
                }
                else
                {
                    Array.Copy(networkMessage.data.data, (nextSendPart * 500) - 4, sendMessage.data.data, 12, thisSendSize);
                }
                sendParts[nextSendPart] = DateTime.UtcNow.Ticks;
                nextSendPart++;
                if (nextSendPart == sendPartsLength)
                {
                    nextSendPart = 0;
                }
                return sendMessage;
            }
            return null;
        }

        public void ReceiveACK(int part)
        {
            long oldTime = sendParts[part];
            if (oldTime != -1)
            {
                sendParts[part] = -1;
                sendPartsLeft--;
                if (sendPartsLeft == 0)
                {
                    finished = true;
                }
            }
        }
    }
}