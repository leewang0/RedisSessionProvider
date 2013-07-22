namespace Welegan.RedisSessionStoreProvider
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Configuration;
    using System.Linq;
    using System.Text;
    using System.Web;    
    using System.Web.Configuration;
    using System.Web.Hosting;
    using System.Web.SessionState;
    using System.Web.UI;

    using BookSleeve;
    using NLog;
    using ServiceStack.Text;

    public class RedisSessionStateStoreProvider : SessionStateStoreProviderBase
    {
        private static Logger redisSessLogger = LogManager.GetCurrentClassLogger();

        // note that redisConn is not static. This is not a problem, look at the implementation of RedisConnectionWrapper
        protected RedisConnectionWrapper redisConnWrap;

        protected virtual string RedisServerAddress { get; set; }

        protected virtual int RedisServerPort { get; set; }

        protected virtual string RedisHashPrefix { get; set; }

        protected virtual SessionStateSection sessionConfig { get; set; }

        protected virtual int SessionTimeoutInSeconds { get; set; }
        
        public override void Initialize(string name, NameValueCollection config)
        {
            redisSessLogger.Debug("Initialize method called");

            // config will contain attribute values from the provider tag in the web.config
            if (string.IsNullOrEmpty(config["description"]))
            {
                config.Remove("description");
                config.Add("description", "Redis Session State Store Provider");
            }

            if (string.IsNullOrEmpty(name))
            {
                name = "RedisSessionStateStore";
            }

            // the base Provider class is a .NET pattern that allows for configurable persistence layers or "providers".
            //      might as well initialize the base with the description attribute and application name
            base.Initialize(name, config);

            // Get <sessionState> configuration element.
            Configuration cfg = WebConfigurationManager.OpenWebConfiguration(HostingEnvironment.ApplicationVirtualPath);
            this.sessionConfig = (SessionStateSection)cfg.GetSection("system.web/sessionState");

            this.SessionTimeoutInSeconds = Convert.ToInt32(this.sessionConfig.Timeout.TotalSeconds);

            this.RedisServerAddress = config["serverAddress"];

            int portNum;
            if (int.TryParse(config["serverPort"], out portNum))
            {
                this.RedisServerPort = portNum;
            }
            else
            {
                this.RedisServerPort = 2000; // some default, if someone forgets to pass in serverPort configuration attribute
            }

            if (string.IsNullOrEmpty(this.RedisServerAddress))
            {
                throw new ConfigurationException("RedisSessionStateStoreProvider has no valid Redis server address");
            }
            else
            {
                // note that redisConn is not static. This is not a problem, look at the implementation of RedisConnectionWrapper
                this.redisConnWrap = new RedisConnectionWrapper(this.RedisServerAddress, this.RedisServerPort);
            }

            if (!string.IsNullOrEmpty(config["hashPrefix"]))
            {
                this.RedisHashPrefix = config["hashPrefix"];
            }

            RedisSessionStateSerializer.Initialize(this.redisConnWrap, this.RedisHashPrefix);
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
        {
            return new SessionStateStoreData(
                new RedisSessionStateItemCollection(),
                SessionStateUtility.GetSessionStaticObjects(context),
                timeout);
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            // Redis has no way to create an empty hash, so do nothing
        }

        public override void Dispose()
        {
            redisSessLogger.Debug("Dispose method called");
        }

        public override void EndRequest(HttpContext context)
        {
            // do nothing
        }

        public override void InitializeRequest(HttpContext context)
        {
            // do nothing
        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            // we do not accept callback methods on session expiration for now
            return false;
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            // do nothing, we are allowing parallel accesses to redis so locking never happens
        }

        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            locked = false;
            lockAge = new TimeSpan(0, 0, 0);
            lockId = null;
            actions = SessionStateActions.None;

            Dictionary<string, byte[]> redisSessData = this.RedisConn.Wait(this.RedisConn.Hashes.GetAll(0, this.RedisHashIdFromSessionId(id)));

            // if the data we got back from Redis for this session was populated, we should reset its expiration to the appropriate time.
            if (redisSessData != null && redisSessData.Count > 0)
            {
                this.RedisConn.Keys.Expire(0, this.RedisHashIdFromSessionId(id), this.SessionTimeoutInSeconds);
            }

            return new SessionStateStoreData(
                new RedisSessionStateItemCollection(redisSessData),
                SessionStateUtility.GetSessionStaticObjects(context),
                this.SessionTimeoutInSeconds * 60);
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            return GetItem(context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            this.RedisConn.Keys.Remove(0, this.RedisHashIdFromSessionId(id));
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            // note that we would put the following line here:
            //      this.RedisConn.Keys.Expire(0, this.RedisHashIdFromSessionId(id), this.SessionTimeoutInSeconds);
            //      except that this gets run on every single request, even for static images depending on the
            //      "runAllManagedModulesForAllRequests" option which is common with MVC sites.
        }

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            RedisSessionStateItemCollection redisItems = item.Items as RedisSessionStateItemCollection;

            if (redisItems != null && redisItems.ChangedKeysDict != null && redisItems.ChangedKeysDict.Count > 0)
            {
                Dictionary<string, object> setItems = new Dictionary<string,object>();
                List<string> delItems = new List<string>();

                foreach(KeyValuePair<string, RedisSessionStateItemCollection.ActionAndValue> actItem in redisItems.ChangedKeysDict)
                {
                    if(actItem.Value is RedisSessionStateItemCollection.DeleteValue)
                    {
                        delItems.Add(actItem.Key);
                    }
                    else if(actItem.Value is RedisSessionStateItemCollection.SetValue)
                    {
                        setItems.Add(
                            actItem.Key, 
                            actItem.Value.Value);
                    }
                    else
                    {
                        throw new ArgumentException("Unknown changed item type from RedisSessionStateItemCollection.ChangedKeysDict");
                    }
                }

                string redishHashId = this.RedisHashIdFromSessionId(id);

                if(setItems.Count > 0)
                {
                    this.RedisConn.Hashes.Set(
                        0,
                        redishHashId,
                        RedisSessionStateSerializer.Serialize(setItems));
                }
                if (delItems.Count > 0)
                {
                    this.RedisConn.Hashes.Remove(
                        0,
                        redishHashId,
                        delItems.ToArray());
                }
            }
        }

        



        protected virtual RedisConnection RedisConn
        { 
            get 
            {
                return this.redisConnWrap.GetConnection();
            } 
        }

        protected virtual string RedisHashIdFromSessionId(string sessId)
        {
            return string.Format(
                "{0}{1}", 
                string.IsNullOrEmpty(this.RedisHashPrefix) ? "" : this.RedisHashPrefix + ":", 
                sessId);
        }
    }
}