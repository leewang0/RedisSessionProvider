namespace SampleUsageMVCApp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web;
    using System.Web.Http;
    using System.Web.Mvc;
    using System.Web.Routing;

    using NLog;
    using StackExchange.Redis;
    using RedisSessionProvider.Config;    

    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801
    public class MvcApplication : System.Web.HttpApplication
    {
        private static Logger globLog;

        protected void Application_Start()
        {
            MvcApplication.globLog = LogManager.GetCurrentClassLogger();

            AreaRegistration.RegisterAllAreas();

            WebApiConfig.Register(GlobalConfiguration.Configuration);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            // assign your local testing Redis instance address
            RedisConnectionConfig.GetRedisServerAddress = (HttpContextBase context) =>
            {
                return new RedisConnectionParameters()
                {
                    ServerAddress = "10.224.61.240",
                    //ServerPort = 1000,            // raw
                    ServerPort = 22122,             // TwemProxy
                    UseProxy = Proxy.Twemproxy,     // more TwemProxy
                    ServerVersion = "2.6.14"                    
                };
            };

            RedisSessionConfig.SessionExceptionLoggingDel = (Exception e) => 
            {
                MvcApplication.globLog.LogException(
                    LogLevel.Error,
                    "Unhandled RedisSessionProvider exception",
                    e);
            };

            RedisConnectionConfig.LogConnectionActionsCountDel = (string name, long count) =>
            {
                MvcApplication.globLog.Debug(
                    "Redis connection {0} had {1} operations",
                    name,
                    count);
            };
        }
    }
}