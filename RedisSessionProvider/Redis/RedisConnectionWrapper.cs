namespace RedisSessionProvider.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Configuration;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Timers;
    using System.Web;

    using BookSleeve;
    using RedisSessionProvider.Config;

    public sealed class RedisConnectionWrapper
    {
        private static ConcurrentDictionary<string, RedisConnection> RedisConnections = new ConcurrentDictionary<string, RedisConnection>();
        private static Dictionary<string, Tuple<int, int>> RedisStats = new Dictionary<string, Tuple<int, int>>();

        private static System.Timers.Timer connRefreshTimer;

        private static System.Timers.Timer connMessagesSentTimer;

        private static object RedisOpenLock = new object();

        private static object RedisCreateLock = new object();

        static RedisConnectionWrapper()
        {
            connRefreshTimer = new System.Timers.Timer(10800000);
            connRefreshTimer.Elapsed += RedisConnectionWrapper.KillAllConnections;
            connRefreshTimer.Start();

            connMessagesSentTimer = new System.Timers.Timer(30000);
            connMessagesSentTimer.Elapsed += RedisConnectionWrapper.GetConnectionsMessagesSent;
            connMessagesSentTimer.Start();
        }

        /// <summary>
        /// Gets or sets the parameters to use when connecting to a redis server
        /// </summary>
        private RedisConnectionParameters redisConnParams;

        /// <summary>
        /// Initializes a new instance of the RedisConnectionWrapper class, which contains methods for accessing
        ///     a static concurrentdictionary of already created and open RedisConnection instances
        /// </summary>
        /// <param name="serverAddress">The ip address of the redis instance</param>
        /// <param name="serverPort">The port number of the redis instance</param>
        public RedisConnectionWrapper(string srvAddr, int srvPort)
        {
            this.redisConnParams = new RedisConnectionParameters() { 
                ServerAddress = srvAddr,
                ServerPort = srvPort
            };
        }
        
        /// <summary>
        /// Initializes a new instance of the RedisConnectionWrapper class, which contains methods for accessing
        ///     a static concurrentdictionary of already created and open redisconnection instances
        /// </summary>
        /// <param name="redisParams">A configuration class containing the redis server hostname and port number</param>
        public RedisConnectionWrapper(RedisConnectionParameters redisParams)
        {
            if (redisParams == null)
            {
                throw new ConfigurationErrorsException(
                    "RedisConnectionWrapper cannot be initialized with null RedisConnectionParameters property");
            }

            this.redisConnParams = redisParams;
        }

        /// <summary>
        /// Method that returns a Booksleeve RedisConnection object with ip and port number matching
        ///     what was passed into the constructor for this instance of RedisConnectionWrapper
        /// </summary>
        /// <returns>An open and callable RedisConnection object, shared with other threads in this
        /// application domain that also called for a connection to the specified ip and port</returns>
        public RedisConnection GetConnection()
        {
            RedisConnection conn = RedisConnectionWrapper.RedisConnections.GetOrAdd(
                this.RedisConnIdFromAddressAndPort,
                this.ConnFromParameters());

            // if for some reason the connection is no longer there. This would happen if null came from the concurrentdictionary
            if (conn == null ||
                conn.State == RedisConnectionBase.ConnectionState.Closed ||
                conn.State == RedisConnectionBase.ConnectionState.Closing)
            {
                lock (RedisConnectionWrapper.RedisCreateLock)
                {
                    if (conn == null ||
                        conn.State == RedisConnectionBase.ConnectionState.Closed ||
                        conn.State == RedisConnectionBase.ConnectionState.Closing)
                    {
                        conn = this.ConnFromParameters();

                        RedisConnectionWrapper.RedisConnections.AddOrUpdate(
                            this.RedisConnIdFromAddressAndPort,
                            conn,
                            (keyName, existingVal) => conn);
                    }
                }
            }

            // conn is guaranteed not null, now we need to check to make sure it is open
            if (!(conn.State == RedisConnectionBase.ConnectionState.Open ||
                conn.State == RedisConnectionBase.ConnectionState.Opening))
            {
                lock (RedisConnectionWrapper.RedisOpenLock)
                {
                    if (!(conn.State == RedisConnectionBase.ConnectionState.Open ||
                        conn.State == RedisConnectionBase.ConnectionState.Opening))
                    {
                        if (!string.IsNullOrEmpty(this.redisConnParams.ServerVersion))
                        {
                            // required for twemproxy (a twitter-created layer for redis sharding) to work
                            conn.SetServerVersion(new Version(this.redisConnParams.ServerVersion), ServerType.Master);
                        }

                        conn.SetKeepAlive(5);

                        conn.Open();
                    }
                }
            }

            return conn;
        }

        private string RedisConnIdFromAddressAndPort
        {
            get
            {
                return string.Format(
                    "{0}_%_{1}",
                    this.redisConnParams.ServerAddress,
                    this.redisConnParams.ServerPort);
            }
        }

        private RedisConnection ConnFromParameters()
        {
            if (!string.IsNullOrEmpty(this.redisConnParams.Password))
            {
                return new RedisConnection(
                    this.redisConnParams.ServerAddress,
                    this.redisConnParams.ServerPort,
                    password: this.redisConnParams.Password,
                    syncTimeout: 2000);
            }

            return new RedisConnection(
                this.redisConnParams.ServerAddress, 
                this.redisConnParams.ServerPort,
                syncTimeout: 2000);
        }

        /// <summary>
        /// Method that removes all stored RedisConnections from the RedisConnectionWrapper's static concurrentdictionary of
        ///     previously requested connections
        /// </summary>
        public static void KillAllConnections(object sender, ElapsedEventArgs e)
        {
            try
            {
                List<RedisConnection> oldRedisConnections = new List<RedisConnection>(RedisConnectionWrapper.RedisConnections.Count);
                foreach (var nameConn in RedisConnectionWrapper.RedisConnections.Keys)
                {
                    RedisConnection removedRedisConn;
                    if (RedisConnectionWrapper.RedisConnections.TryRemove(nameConn, out removedRedisConn))
                    {
                        oldRedisConnections.Add(removedRedisConn);
                        // also reset the stats
                        RedisConnectionWrapper.RedisStats.Remove(nameConn);
                    }
                }

                // we removed all of the references to the redis connections from the dictionary, meaning all new requests
                //      will go to new connection instances. Wait for the current requests depending on this connection to finish
                Thread.Sleep(5000); // 5 seconds should be ample time to complete all requests

                // dispose of all of the now hopefully idle removed RedisConnection objects
                foreach (RedisConnection redisConn in oldRedisConnections)
                {
                    try
                    {
                        redisConn.Close(true);
                        redisConn.Dispose();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Gets the number of redis commands sent and received, and sets the count to 0 so the next time
        ///     we will not see double counts
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public static void GetConnectionsMessagesSent(object sender, ElapsedEventArgs e)
        {
            bool logSent = RedisConnectionConfig.LogRedisCommandsSentDel != null;
            bool logRcved = RedisConnectionConfig.LogRedisCommandsReceivedDel != null;

            if (logSent || logRcved)
            {
                foreach (string connName in RedisConnectionWrapper.RedisConnections.Keys)
                {
                    try
                    {
                        RedisConnection conn;
                        if (RedisConnectionWrapper.RedisConnections.TryGetValue(connName, out conn))
                        {
                            int priorPeriodSent = 0;
                            int priorPeriodRcved = 0;
                            if (RedisConnectionWrapper.RedisStats.ContainsKey(connName))
                            {
                                priorPeriodSent = RedisConnectionWrapper.RedisStats[connName].Item1;
                                priorPeriodRcved = RedisConnectionWrapper.RedisStats[connName].Item2;
                            }

                            Counters counts = conn.GetCounters(false);
                            if (logSent)
                            {
                                // log the sent commands
                                RedisConnectionConfig.LogRedisCommandsSentDel(
                                    connName, 
                                    counts.MessagesSent - priorPeriodSent);
                            }
                            if (logRcved)
                            {
                                // log the received commands
                                RedisConnectionConfig.LogRedisCommandsReceivedDel(
                                    connName, 
                                    counts.MessagesReceived - priorPeriodRcved);
                            }

                            RedisConnectionWrapper.RedisStats[connName] =
                                new Tuple<int, int>(counts.MessagesSent, counts.MessagesReceived);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
    }
}