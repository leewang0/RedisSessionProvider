namespace RedisSessionProvider.Config
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    using StackExchange.Redis;

    public class RedisConnectionParameters
    {
        public RedisConnectionParameters()
        {
            this.ServerAddress = null;
            this.ServerPort = 6379;
            this.Password = null;
            this.ServerVersion = "2.6.14";
            this.UseProxy = Proxy.None;
        }

        /// <summary>
        /// Gets or sets the ip address or hostname of the redis server
        /// </summary>
        public string ServerAddress { get; set; }

        /// <summary>
        /// Gets or sets the port the redis server is listening on, defaults to 6379
        /// </summary>
        public int ServerPort { get; set; }

        /// <summary>
        /// Gets or sets the password to use when connecting, default is null which indicates no password
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets the redis server version, defaults to 2.6.14
        /// </summary>
        public string ServerVersion { get; set; }

        /// <summary>
        /// Gets or sets the proxy behavior to use, currently, StackExchange.Redis has an option for
        ///     TwemProxy-compatible behavior
        /// </summary>
        public Proxy UseProxy { get; set; }
    }
}
