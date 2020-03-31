using System;
using System.Threading;

namespace DarkNetworkUDP
{
    public class NetworkMessage
    {
        public int type;
        public ByteArray data;
        public int usageCount;
        public NetworkMessageType sendType;

        public static NetworkMessage Create(int type, int size, NetworkMessageType sendType)
        {
            NetworkMessage retVal = Recycler<NetworkMessage>.GetObject();
            retVal.type = type;
            retVal.sendType = sendType;
            retVal.data = null;
            if (size > 0)
            {
                retVal.data = ByteRecycler.GetObject(size);
            }
            retVal.usageCount = 1;
            return retVal;
        }

        public void Destroy()
        {
            int usageLeft = Interlocked.Decrement(ref usageCount);
            if (usageLeft == 0)
            {
                if (data != null)
                {
                    ByteRecycler.ReleaseObject(data);
                }
                Recycler<NetworkMessage>.ReleaseObject(this);
            }
        }

        public NetworkMessage Copy()
        {
            NetworkMessage retVal = null;
            if (data != null)
            {
                retVal = NetworkMessage.Create(type, data.size, sendType);
                Array.Copy(data.data, 0, retVal.data.data, 0, data.Length);
            }
            else
            {
                retVal = NetworkMessage.Create(type, 0, sendType);
            }
            return retVal;
        }

        public bool IsOrdered()
        {
            return (sendType == NetworkMessageType.ORDERED_RELIABLE || sendType == NetworkMessageType.ORDERED_UNRELIABLE);
        }

        public bool IsReliable()
        {
            return (sendType == NetworkMessageType.ORDERED_RELIABLE || sendType == NetworkMessageType.UNORDERED_RELIABLE);
        }
    }
}