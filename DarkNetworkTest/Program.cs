using System;
using DarkNetworkUDP;

namespace DarkNetworkTest
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--client")
            {
                ClientTest ct = new ClientTest();
                ct.Run();
            }
            else
            {
                ServerTest st = new ServerTest();
                st.Run();
            }
        }
    }
}
