namespace DarkNetworkUDP
{
    public delegate void NetworkCallback<T>(ByteArray data, Connection<T> stateObject);
}