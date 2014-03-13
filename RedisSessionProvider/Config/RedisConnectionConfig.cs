namespace RedisSessionProvider.Config
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Web;

    public static class RedisConnectionConfig
    {
        /// <summary>
        /// Gets or sets a delegate method which takes in an HttpContext and returns the necessary
        ///     hostname and port information for the appropriate redis server that contains the
        ///     Redis Session data for the httprequest-httpresponse in the HttpContext parameter
        /// </summary>
        public static Func<HttpContextBase, RedisConnectionParameters> GetRedisServerAddress = null;

        /// <summary>
        /// Gets or sets the function to call for logging how many commands were sent to redis. The first
        ///     parameter is the connection name and the second parameter is the number of commands.
        /// </summary>
        public static Action<string, int> LogRedisCommandsSentDel { get; set; }

        /// <summary>
        /// Gets or sets the function to call for logging how many commands were received from redis 
        ///     The first parameter is the connection name and the second parameter is the number of 
        ///     commands.
        /// </summary>
        public static Action<string, int> LogRedisCommandsReceivedDel { get; set; }

        /// <summary>
        /// Gets or sets a function to call every time data is pulled from Redis, where the first
        ///     parameter is the connection name and the second parameter is the size in bytes
        ///     of the data retrieved.
        /// </summary>
        public static Action<string, int> LogRedisSessionSize { get; set; }
    }
}
