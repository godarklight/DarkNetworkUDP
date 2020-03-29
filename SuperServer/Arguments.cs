using System;

namespace SuperServer
{
    public class Arguments
    {
        public readonly bool test;
        public readonly bool ok;
        public readonly string address;
        public readonly int port;
        public readonly bool help;
        public Arguments(string[] args)
        {
            string nextArg = null;
            foreach (string str in args)
            {
                if (nextArg != null)
                {
                    switch (nextArg)
                    {
                        case "address":
                            address = str;
                            break;
                        case "port":
                            if (Int32.TryParse(str, out int setPort))
                            {
                                port = setPort;
                                ok = true;
                            }
                            break;
                    }
                    nextArg = null;
                }
                if (str == "-p" || str == "--port")
                {
                    nextArg = "port";
                }
                if (str == "-a" || str == "--address")
                {
                    nextArg = "port";
                }
                if (str == "-h" || str == "--help")
                {
                    help = true;
                }
                if (str == "--test")
                {
                    test = true;
                }
            }
        }
    }
}
