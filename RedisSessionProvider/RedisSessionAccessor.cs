namespace RedisSessionProvider
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Web;

    using Config;

    public class RedisSessionAccessor : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the RedisSessionAccessor class, which provides access to a
        ///     local Redis items collection outside of the standard ASP.NET pipeline Session hooks
        /// </summary>
        /// <param name="context">The context of the current request</param>
        public RedisSessionAccessor(HttpContextBase context)
        {
            this.RequestContext = context;
            this.SharedSessions = new LocalSharedSessionDictionary();

            // if we have the session ID
            if (this.RequestContext.Request.Cookies[RedisSessionConfig.SessionHttpCookieName] != null)
            {
                this.SessionRedisHashKey = RedisSessionStateStoreProvider.RedisHashIdFromSessionId(
                    this.RequestContext,
                    this.RequestContext.Request.Cookies[RedisSessionConfig.SessionHttpCookieName].Value);
            }

            if(!string.IsNullOrEmpty(this.SessionRedisHashKey))
            {
                this.Session = this.SharedSessions.GetSessionForBeginRequest(
                    this.SessionRedisHashKey,
                    (string redisKey) =>
                    {
                        return RedisSessionStateStoreProvider.GetItemFromRedis(
                            redisKey,
                            this.RequestContext,
                            RedisSessionConfig.SessionTimeout);
                    });
            }
        }

        /// <summary>
        /// Gets a Session item collection outside of the normal ASP.NET pipeline, but will serialize back to
        ///     Redis on Dispose of RedisSessionAccessor object
        /// </summary>
        public RedisSessionStateItemCollection Session { get; protected set; }

        /// <summary>
        /// Gets or sets the context of the current web request
        /// </summary>
        protected HttpContextBase RequestContext { get; set; }

        /// <summary>
        /// Gets or sets a collection that handles all RedisSessionStateItemCollection objecst used by
        ///     RedisSessionProvider
        /// </summary>
        protected LocalSharedSessionDictionary SharedSessions { get; set; }

        /// <summary>
        /// Gets or sets the string key in Redis holding the Session data
        /// </summary>
        protected string SessionRedisHashKey { get; set; }

        #region IDisposable Members

        public void Dispose()
        {
            // record with local shared session storage that we are done with the session so it gets
            //      cleared out sooner, but we already have a reference to the item collection so
            //      no need for the return value from this method
            this.SharedSessions.GetSessionForEndRequest(this.SessionRedisHashKey);

            RedisSessionStateStoreProvider.SerializeToRedis(
                this.RequestContext,
                this.Session,
                this.SessionRedisHashKey,
                RedisSessionConfig.SessionTimeout);
        }

        #endregion
    }
}
