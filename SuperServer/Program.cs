using System;
using System.Collections.Generic;
using System.Threading;

namespace SuperServer
{
    public static class Program
    {
        public static bool running = true;
        public static void Main(string[] args)
        {
            Arguments passed_args = new Arguments(args);
            bool display_help = passed_args.help;
            if (passed_args.ok)
            {
                if (!passed_args.test)
                {
                    Handler h = new Handler();
                    Console.CancelKeyPress += (object _, ConsoleCancelEventArgs __) => { running = false; };
                    Server s = new Server(h);
                    ExitReason exitReason = s.Run(passed_args.address, passed_args.port);
                    switch (exitReason)
                    {
                        case ExitReason.ADDR:
                            Console.WriteLine("Address: " + passed_args.address + " is invalid.");
                            break;
                        case ExitReason.PORT:
                            Console.WriteLine("Port: " + passed_args.port + " is invalid.");
                            break;
                        case ExitReason.INUSE:
                            Console.WriteLine("Address/port already in use.");
                            break;
                        case ExitReason.DENIED:
                            Console.WriteLine("Address/port denied.");
                            break;
                    }
                }
                else
                {
                    ServerTest st = new ServerTest();
                    st.Run(passed_args.port);
                }
            }
            else
            {
                display_help = true;
            }
            if (display_help)
            {
                Console.WriteLine("Usage: -a|--address set the servers bind address. Defaults to IPV6Any (dual stack)");
                Console.WriteLine("Usage: -p|--port set the servers bind port. Will not run unless set.");
                Console.WriteLine("-h|--help display this help text");
            }
        }
    }
}
