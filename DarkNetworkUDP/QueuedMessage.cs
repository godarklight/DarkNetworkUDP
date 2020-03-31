namespace DarkNetworkUDP
{
    public class QueuedMessage<T>
    {
        public NetworkMessage networkMessage;
        public Connection<T> connection;
    }
}