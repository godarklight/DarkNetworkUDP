using System;

namespace DarkNetworkUDP
{
    public class NetworkMessage
    {
        public Guid owner;
        public int type;
        public ByteArray data;
        public bool ordered;
        public bool reliable;
        public int usageCount;

        public static NetworkMessage Create(int type, int size)
        {
            NetworkMessage retVal = Recycler<NetworkMessage>.GetObject();
            retVal.owner = Guid.Empty;
            retVal.type = type;
            retVal.data = null;
            if (size > 0)
            {
                retVal.data = ByteRecycler.GetObject(size);
            }
            retVal.ordered = false;
            retVal.reliable = false;
            retVal.usageCount = 1;
            return retVal;
        }
    }
}