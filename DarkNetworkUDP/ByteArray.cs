namespace DarkNetworkUDP
{
    public class ByteArray
    {
        public int size;
        public readonly byte[] data;

        public ByteArray(int size)
        {
            data = new byte[size];
            size = 0;
        }

        public int Length
        {
            get
            {
                return size;
            }
        }
    }
}