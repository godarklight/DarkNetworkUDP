using System;

namespace DarkNetworkUDP
{
    public static class RateControl<T>
    {
        public static void Update(Connection<T> connection)
        {
            //Rate control adjustment
            long currentTime = DateTime.UtcNow.Ticks;
            if (currentTime > connection.lastRateControlChange + connection.latency)
            {
                connection.lastRateControlChange = currentTime;
                connection.speed -= connection.dataLoss;
                connection.dataLoss = 0;
                long growValue = 5000;
                if (growValue > connection.dataSent)
                {
                    growValue = connection.dataSent;
                }
                connection.dataSent = 0;
                connection.speed += growValue;
                if (connection.speed < connection.minSpeed)
                {
                    connection.speed = connection.minSpeed;
                }
                if (connection.speed > connection.maxSpeed)
                {
                    connection.speed = connection.maxSpeed;
                }
            }
            //Send tokens
            if (connection.lastTokensTime == 0)
            {
                connection.tokens = Connection<T>.TOKENS_MAX;
                connection.lastTokensTime = currentTime;
            }
            long ticksElapsed = currentTime - connection.lastTokensTime;
            connection.lastTokensTime = currentTime;
            connection.tokens += (ticksElapsed * connection.speed) / TimeSpan.TicksPerSecond;
            if (connection.tokens > Connection<T>.TOKENS_MAX)
            {
                connection.tokens = Connection<T>.TOKENS_MAX;
            }
        }
    }
}