namespace RedisSessionProvider.Config
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Web;

    /// <summary>
    /// This class contains settings for how the classes that hold the Session behave, after data
    ///     is retrieved from Redis but before it is written back.
    /// </summary>
    public static class RedisSessionConfig
    {
        static RedisSessionConfig()
        {
            RedisSessionConfig.SessionAccessConcurrencyLevel = 1;
        }

        /// <summary>
        /// A delegate that is called when RedisSessionProvider.RedisSessionStateStoreProvider encounters
        ///     an error during the retrieval or setting of a Session
        /// </summary>
        public static Action<Exception> SessionExceptionLoggingDel { get; set; }

        /// <summary>
        /// A delegate that returns a Redis keyname given an HttpContext and the Session Id cookie value.
        ///     If this is null, the Session Id value will be used directly as the Redis keyname. This may
        ///     be fine if your Redis server is specifically used only for web Sessions within one app.
        /// </summary>
        public static Func<HttpContextBase, string, string> RedisKeyFromSessionIdDel { get; set; }

        /// <summary>
        /// Gets or sets the expected number of threads that will simultaneously try to access a session,
        ///     defaults to 1
        /// </summary>
        public static int SessionAccessConcurrencyLevel { get; set; }
    }
}
