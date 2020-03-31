using System;
using System.Net;
using System.Text;
using DarkNetworkUDP;
using System.Threading;

namespace DarkNetworkTest
{
    class ClientTest
    {
        //128MB
        private byte[] randomBytes = new byte[5 * 1024 * 1024 - 12];
        private int freeID = 1;
        private int relayCount = 0;
        private bool running = true;
        public void Run()
        {
            Random r = new Random();
            r.NextBytes(randomBytes);
            //ByteRecycler must be allowed to allocate the size of the biggest message
            ByteRecycler.AddPoolSize(128 * 1024 * 1024);
            NetworkHandler<StateObject> handler = new NetworkHandler<StateObject>(false);
            handler.RegisterConnectCallback(Connected);
            handler.RegisterDisconnectCallback(Disconnected);
            handler.RegisterCallback(0, GotMessage);
            handler.RegisterCallback(1, ReliableReceive);
            DarkNetwork<StateObject> dn = new DarkNetwork<StateObject>();
            IPAddress netAddr = IPAddress.Parse("2403:5800:9100:5b00:76da:38ff:fea3:9dbe");
            dn.SetupClient(new IPEndPoint(netAddr, 12345), handler);
            Thread.Sleep(1000);
            NetworkMessage nmbig = NetworkMessage.Create(1, randomBytes.Length, NetworkMessageType.UNORDERED_RELIABLE);
            Array.Copy(randomBytes, 0, nmbig.data.data, 0, randomBytes.Length);
            handler.SendMessage(nmbig);
            int messageID = 0;
            while (running)
            {
                NetworkMessage nm = NetworkMessage.Create(0, 2048, NetworkMessageType.UNORDERED_UNRELIABLE);
                //nm.ordered = true;
                byte[] sendBytes = Encoding.UTF8.GetBytes("Message " + messageID++);
                Array.Copy(sendBytes, 0, nm.data.data, 0, sendBytes.Length);
                nm.data.size = sendBytes.Length;
                handler.SendMessage(nm);
                //Recycler<NetworkMessage>.GarbageCollect(500, 1000);
                //ByteRecycler.GarbageCollect(2048, 500, 1000);
                //ByteRecycler.GarbageCollect(128 * 1024 * 1024, 2, 4);
                //PrintRecyclerStats();
                Thread.Sleep(1000);
            }
            dn.Shutdown();
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
            running = false;
            Console.WriteLine(connection.state.id + " disconnected.");
        }

        private void GotMessage(ByteArray message, Connection<StateObject> connection)
        {
            string messageData = Encoding.UTF8.GetString(message.data, 0, message.Length);
            Console.WriteLine("Got message: " + messageData + " on connection " + connection.state.id);
            Decimal latencyMS = Math.Round(connection.GetLatency() / (Decimal)TimeSpan.TicksPerMillisecond, 2);
            Console.WriteLine("Latency: " + connection.GetLatency() + ", ms: " + latencyMS);
            Console.WriteLine("Speed: " + connection.GetSpeed());
        }

        private void ReliableReceive(ByteArray message, Connection<StateObject> connection)
        {
            bool matches = true;
            for (int i = 0; i < message.Length; i++)
            {
                if (message.data[i] != randomBytes[i])
                {
                    matches = false;
                    break;
                }
            }
            Console.WriteLine("Reliable matches: " + matches);
            if (matches && relayCount < 10)
            {
                relayCount++;
                NetworkMessage bigmessage = NetworkMessage.Create(1, randomBytes.Length, NetworkMessageType.UNORDERED_RELIABLE);
                Array.Copy(randomBytes, 0, bigmessage.data.data, 0, randomBytes.Length);
                connection.handler.SendMessage(bigmessage, connection);
            }
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

