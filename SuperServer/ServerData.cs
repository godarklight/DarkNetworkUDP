using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace SuperServer
{
    public class ServerData
    {
        public IPAddress v4addr = null;
        public IPAddress v6addr = null;
        public List<IPEndPoint> v4 = new List<IPEndPoint>();
        public List<IPEndPoint> v6 = new List<IPEndPoint>();
        public long lastUpdate;
    }
}