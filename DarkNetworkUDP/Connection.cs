using System;
using System.Collections.Generic;
using System.Net;

namespace DarkNetworkUDP
{
    public class Connection<T>
    {
        internal bool destroyed = false;
        //Minimum send rate
        public const long MIN_SPEED = 64 * 1024;
        //Maximum send rate
        public const long MAX_SPEED = 20 * 1024 * 1024;
        //Current send rate
        internal long speed = 64 * 1024;

        //Amount of data we can send
        internal long tokens = 0;
        //Maximum amount of data to burst
        public const long TOKENS_MAX = 1024 * 1024;
        internal long lastTokensTime = 0;
        internal int queuedOut = 0;
        internal int lostData = 0;
        //Heartbeat is 1 per second.
        internal long[] latencyArray = new long[60];
        internal long latencyTotal = 0;
        internal int latencyPointer = 0;
        internal int latencyValid = 0;
        internal int sendOrderID = 0;
        internal int receiveOrderID = 0;
        internal ReliableMessageHandler<T> reliableMessageHandler;
        internal long avgLatency;
        internal long latency;
        public long lastHeartbeatTime;
        public long lastReceiveTime;
        public IPEndPoint remoteEndpoint;
        public T state;
        public NetworkHandler<T> handler;

        /// <summary>
        /// Returns bytes queued to send
        /// </summary>
        public int GetQueuedOut()
        {
            return queuedOut;
        }

        /// <summary>
        /// Returns current latency in ticks
        /// </summary>
        public long GetLatency()
        {
            return latency;
        }

        /// <summary>
        /// Returns average latency in ticks
        /// </summary>
        public long GetAverageLatency()
        {
            return avgLatency;
        }

        /// <summary>
        /// Returns send speed in bytes/second
        /// </summary>
        public long GetSpeed()
        {
            return speed;
        }
    }
}