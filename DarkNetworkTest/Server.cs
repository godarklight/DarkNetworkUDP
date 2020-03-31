using System;
using System.Net;
using System.Text;
using DarkNetworkUDP;
using System.Threading;

namespace DarkNetworkTest
{
    class ServerTest
    {
        private int freeID = 1;
        public void Run()
        {
            //ByteRecycler must be allowed to allocate the size of the biggest message
            ByteRecycler.AddPoolSize(128 * 1024 * 1024);
            NetworkHandler<StateObject> handler = new NetworkHandler<StateObject>(false);
            handler.RegisterConnectCallback(Connected);
            handler.RegisterDisconnectCallback(Disconnected);
            handler.RegisterCallback(0, GotMessage);
            handler.RegisterCallback(1, RelayReliable);
            DarkNetwork<StateObject> dn = new DarkNetwork<StateObject>();
            dn.SetupServer(new IPEndPoint(IPAddress.IPv6Any, 12345), handler);
            int messageID = 0;
            while (true)
            {
                NetworkMessage nm = NetworkMessage.Create(0, 2048, NetworkMessageType.ORDERED_UNRELIABLE);
                byte[] sendBytes = Encoding.UTF8.GetBytes("Message " + messageID++);
                Array.Copy(sendBytes, 0, nm.data.data, 0, sendBytes.Length);
                nm.data.size = sendBytes.Length;
                handler.SendMessageToAll(nm);
                //Recycler<NetworkMessage>.GarbageCollect(500, 1000);
                //ByteRecycler.GarbageCollect(2048, 500, 1000);
                //ByteRecycler.GarbageCollect(128 * 1024 * 1024, 2, 4);
                Thread.Sleep(1000);
            }
        }

        private StateObject Connected(Connection<StateObject> connection)
        {
            StateObject newObject = new StateObject();
            newObject.id = freeID++;
            Console.WriteLine(newObject.id + " connected");
            return newObject;
        }

        private void Disconnected(Connection<StateObject> connection)
        {
            Console.WriteLine(connection.state.id + " disconnected.");
            PrintRecyclerStats();
        }
        private void GotMessage(ByteArray message, Connection<StateObject> connection)
        {
            string messageData = Encoding.UTF8.GetString(message.data, 0, message.Length);
            Decimal latencyMS = Math.Round(connection.GetLatency() / (Decimal)TimeSpan.TicksPerMillisecond, 2);
            Console.WriteLine("Latency: " + connection.GetLatency() + ", ms: " + latencyMS);
            Console.WriteLine("Speed: " + connection.GetSpeed());
            //PrintRecyclerStats();
        }


        private void RelayReliable(ByteArray message, Connection<StateObject> connection)
        {
            Console.WriteLine("Relaying reliable message");
            NetworkMessage nm = NetworkMessage.Create(1, message.Length, NetworkMessageType.ORDERED_RELIABLE);
            Array.Copy(message.data, 0, nm.data.data, 0, message.Length);
            connection.handler.SendMessage(nm, connection);
        }

        private void PrintRecyclerStats()
        {
            Console.WriteLine();
            Console.WriteLine("Recycler statistics: ");
            int rFree = Recycler<NetworkMessage>.GetPoolFreeCount();
            int rTotal = Recycler<NetworkMessage>.GetPoolCount();
            Console.WriteLine("NetworkMessage: " + rFree + "/" + rTotal);
            rFree = ByteRecycler.GetPoolFreeCount(2048);
            rTotal = ByteRecycler.GetPoolCount(2048);
            Console.WriteLine("ByteArray 2048: " + rFree + "/" + rTotal);
            rFree = ByteRecycler.GetPoolFreeCount(128 * 1024 * 1024);
            rTotal = ByteRecycler.GetPoolCount(128 * 1024 * 1024);
            Console.WriteLine("ByteArray 128M: " + rFree + "/" + rTotal);
            Console.WriteLine("Total memory: " + GC.GetTotalMemory(false) / 1024);
        }
    }
}

