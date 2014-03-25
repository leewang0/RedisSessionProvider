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

    using StackExchange.Redis;
    using RedisSessionProvider.Config;

    public sealed class RedisConnectionWrapper
    {
        private static Dictionary<string, ConnectionMultiplexer> RedisConnections =
            new Dictionary<string, ConnectionMultiplexer>();
        private static Dictionary<string, long> RedisStats =
            new Dictionary<string, long>();
        
        private static System.Timers.Timer connMessagesSentTimer;
        
        private static object RedisCreateLock = new object();

        static RedisConnectionWrapper()
        {
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
        /// Method that returns a StackExchange.Redis.IDatabase object with ip and port number matching
        ///     what was passed into the constructor for this instance of RedisConnectionWrapper
        /// </summary>
        /// <returns>An open and callable RedisConnection object, shared with other threads in this
        /// application domain that also called for a connection to the specified ip and port</returns>
        public IDatabase GetConnection()
        {
            string connKey = this.RedisConnIdFromAddressAndPort;

            if(!RedisConnectionWrapper.RedisConnections.ContainsKey(connKey))
            {
                lock(RedisConnectionWrapper.RedisCreateLock)
                {
                    if(!RedisConnectionWrapper.RedisConnections.ContainsKey(connKey))
                    {
                        ConfigurationOptions connectOpts = ConfigurationOptions.Parse(
                            this.redisConnParams.ServerAddress + ":" + this.redisConnParams.ServerPort);

                        // just default this value for now
                        connectOpts.KeepAlive = 5;

                        if (!string.IsNullOrEmpty(this.redisConnParams.Password))
                        {
                            connectOpts.Password = this.redisConnParams.Password;
                        }
                        if (!string.IsNullOrEmpty(this.redisConnParams.ServerVersion))
                        {
                            connectOpts.DefaultVersion = new Version(this.redisConnParams.ServerVersion);
                        }
                        if (this.redisConnParams.UseProxy != Proxy.None)
                        {
                            // thanks marc gravell
                            connectOpts.Proxy = this.redisConnParams.UseProxy;
                        }

                        RedisConnectionWrapper.RedisConnections.Add(
                            connKey,
                            ConnectionMultiplexer.Connect(
                                connectOpts));
                    }
                }
            }

            return RedisConnectionWrapper.RedisConnections[connKey].GetDatabase();
        }

        /// <summary>
        /// Gets a string uniquely identifying the connection from hostname and port number
        /// </summary>
        internal string RedisConnIdFromAddressAndPort
        {
            get
            {
                return string.Format(
                    "{0}_%_{1}",
                    this.redisConnParams.ServerAddress,
                    this.redisConnParams.ServerPort);
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
            bool logCount = RedisConnectionConfig.LogConnectionActionsCountDel != null;

            if (logCount)
            {
                foreach (string connName in RedisConnectionWrapper.RedisConnections.Keys.ToList())
                {
                    try
                    {
                        ConnectionMultiplexer conn;
                        if (RedisConnectionWrapper.RedisConnections.TryGetValue(connName, out conn))
                        {
                            long priorPeriodCount = 0;
                            if (RedisConnectionWrapper.RedisStats.ContainsKey(connName))
                            {
                                priorPeriodCount = RedisConnectionWrapper.RedisStats[connName];
                            }

                            ServerCounters counts = conn.GetCounters();
                            long curCount = counts.Interactive.OperationCount;

                            // log the sent commands
                            RedisConnectionConfig.LogConnectionActionsCountDel(
                                connName, 
                                curCount - priorPeriodCount);

                            RedisConnectionWrapper.RedisStats[connName] = curCount;
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