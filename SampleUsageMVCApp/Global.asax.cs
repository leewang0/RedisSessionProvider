namespace SampleUsageMVCApp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web;
    using System.Web.Http;
    using System.Web.Mvc;
    using System.Web.Routing;

    using RedisSessionProvider.Config;

    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
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
                    ServerPort = 22122,
                    ServerVersion = "2.6.14"
                };
            };
        }
    }
}