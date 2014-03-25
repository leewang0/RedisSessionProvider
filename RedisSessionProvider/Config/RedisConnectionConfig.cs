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
        static RedisConnectionConfig()
        {
            RedisConnectionConfig.MaxSessionByteSize = 30000;
            RedisConnectionConfig.RedisSessionSizeExceededHandler = RedisConnectionConfig.ClearRedisItems;
        }

        /// <summary>
        /// Gets or sets a delegate method which takes in an HttpContext and returns the necessary
        ///     hostname and port information for the appropriate redis server that contains the
        ///     Redis Session data for the httprequest-httpresponse in the HttpContext parameter
        /// </summary>
        public static Func<HttpContextBase, RedisConnectionParameters> GetRedisServerAddress = null;

        /// <summary>
        /// Gets or sets a logging delegate that takes as input the server ip and port of the connection used as
        ///     a string and the number of total redis messages to it as a long
        /// </summary>
        public static Action<string, long> LogConnectionActionsCountDel { get; set; }
        
        /// <summary>
        /// Gets or sets a function to call every time data is pulled from Redis, where the first
        ///     parameter is the connection name and the second parameter is the size in bytes
        ///     of the data retrieved.
        /// </summary>
        public static Action<string, int> LogRedisSessionSize { get; set; }

        /// <summary>
        /// Gets or sets the delegate that handles when the Session goes over the max allowed size. Defaults
        ///     to clearing the items.
        /// </summary>
        public static Action<RedisSessionStateItemCollection, int> RedisSessionSizeExceededHandler { get; set; }

        private static void ClearRedisItems(RedisSessionStateItemCollection items, int size)
        {
            items.Clear();
        }

        /// <summary>
        /// Gets or sets the maximum supported session size, in bytes. Defaults to 30000, or 30kb
        /// </summary>
        public static int MaxSessionByteSize { get; set; }

        #region Deprecated
        [Obsolete("Due to a change in Redis clients starting in v1.2.0, RedisSessionProvider now only" +
            " logs total actions taken (both sends and receives) via the LogConnectionActionsCountDel property")]
        public static Action<string, int> LogRedisCommandsSentDel
        {
            get
            {
                return null;
            }
            set
            {
            }
        }

        [Obsolete("Due to a change in Redis clients starting in v1.2.0, RedisSessionProvider now only" +
            " logs total actions taken (both sends and receives) via the LogConnectionActionsCountDel property")]
        public static Action<string, int> LogRedisCommandsReceivedDel
        {
            get
            {
                return null;
            }
            set
            {
            }
        }
        #endregion
    }
}
