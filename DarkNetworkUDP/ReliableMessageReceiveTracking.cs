using System;
using System.Collections.Generic;
using System.Net;

namespace DarkNetworkUDP
{
    public class ReliableMessageReceiveTracking
    {
        public NetworkMessage networkMessage;
        public bool[] receiveParts;
        public int receivePartsLength = 0;
        public int receivePartsLeft = 0;

        public static ReliableMessageReceiveTracking Create()
        {
            ReliableMessageReceiveTracking rmrt = Recycler<ReliableMessageReceiveTracking>.GetObject();
            rmrt.receivePartsLength = 0;
            rmrt.receivePartsLeft = 0;
            rmrt.networkMessage = null;
            return rmrt;
        }

        public void Destroy()
        {
            networkMessage = null;
            Recycler<ReliableMessageReceiveTracking>.ReleaseObject(this);
        }

        public void Setup(int length, NetworkMessageType sendType)
        {
            this.networkMessage = NetworkMessage.Create(-4, length, sendType);
            //Number of chunks needed
            receivePartsLength = (length + 4) / 500;
            if ((length + 4) % 500 > 0)
            {
                receivePartsLength++;
            }
            //Check the send array can fit the chunks
            if (receiveParts == null || receiveParts.Length < receivePartsLength)
            {
                int createLength = 128;
                while (receivePartsLength > createLength)
                {
                    createLength = createLength * 4;
                }
                receiveParts = new bool[createLength];
            }
            receivePartsLeft = receivePartsLength;
            for (int i = 0; i < receivePartsLength; i++)
            {
                receiveParts[i] = false;
            }
        }

        public void Handle(int partID, int recvLength, ByteArray data)
        {
            if (partID > receivePartsLength)
            {
                throw new Exception("This shouldn't happen");
            }
            bool storedAlready = receiveParts[partID];
            if (!storedAlready)
            {
                int bytesToCopy = 500;
                //First part has the message ID
                int startModify = 0;
                if (partID == 0)
                {
                    //The first message steals 4 bytes for the message type
                    startModify = 4;
                    networkMessage.type = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data.data, 12));
                }
                //Last part is a smaller copy.
                if (partID == (receivePartsLength - 1))
                {
                    bytesToCopy = recvLength % 500;
                }                
                if (bytesToCopy > 0)
                {
                    Array.Copy(data.data, 12 + startModify, networkMessage.data.data, partID * 500 - (4 - startModify), bytesToCopy - startModify);
                }
                receiveParts[partID] = true;
                receivePartsLeft--;
            }
        }
    }
}