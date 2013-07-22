namespace Welegan.RedisSessionStoreProvider
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Web;

    using BookSleeve;

    public class RedisConnectionWrapper
    {
        private static ConcurrentDictionary<string, RedisConnection> RedisConnections = new ConcurrentDictionary<string, RedisConnection>();

        private static object RedisOpenLock = new object();

        public RedisConnectionWrapper(string serverAddress, int serverPort)
        {
            this.ServerAddress = serverAddress;
            this.ServerPort = serverPort;
        }

        protected string ServerAddress { get; set; }

        protected int ServerPort { get; set; }

        public RedisConnection GetConnection()
        {
            RedisConnection conn = RedisConnectionWrapper.RedisConnections.GetOrAdd(
                this.RedisConnIdFromAddressAndPort,
                new RedisConnection(this.ServerAddress, this.ServerPort));

            // if for some reason the connection is no longer there. This would happen if null came from the concurrentdictionary
            if (conn == null)
            {
                // try to update it, we know that the update will work because the key is guaranteed to exist by this point in the method.
                //      That is, assuming we never perform a remove on the dictionary elsewhere.
                RedisConnectionWrapper.RedisConnections.TryUpdate(
                    this.RedisConnIdFromAddressAndPort, 
                    new RedisConnection(this.ServerAddress, this.ServerPort), // update to this value
                    null); // but only if the current value at the key is equal to null
            }

            // conn is guaranteed not null, now we need to check to make sure it is open
            if(!(conn.State == RedisConnectionBase.ConnectionState.Open || 
                conn.State == RedisConnectionBase.ConnectionState.Opening))
            {
                lock (RedisConnectionWrapper.RedisOpenLock)
                {
                    if (!(conn.State == RedisConnectionBase.ConnectionState.Open ||
                        conn.State == RedisConnectionBase.ConnectionState.Opening))
                    {
                        conn = new RedisConnection(this.ServerAddress, this.ServerPort);
                        conn.Wait(conn.Open());
                    }
                }
            }

            return conn;
        }

        private string RedisConnIdFromAddressAndPort
        {
            get
            {
                return string.Format("{0}_%_{1}", this.ServerAddress, this.ServerPort);
            }
        }
    }
}