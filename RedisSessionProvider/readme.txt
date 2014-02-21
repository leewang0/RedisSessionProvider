RedisSessionProvider
============================

Full documentation can be found at https://github.com/welegan/RedisSessionProvider

If you are using the NuGet package manager v2.6 and up, installing this NuGet package has already modified your 
web.config's sessionState element to have the appropriate settings to hook into RedisSessionProvider. If the it has
not, please modify your web.config as follows: 

<configuration>
  <system.web>
    <sessionState 
      mode="Custom"
      customProvider="RedisSessionStateStore">      
      <providers>
        <add 
          name="RedisSessionProvider" 
          type="RedisSessionProvider.RedisSessionStateStoreProvider, RedisSessionProvider" />
      </providers>
    </sessionState>
  </system.web>
</configuration>

The last step in order for RedisSessionProvider to work is adding the following line to your application startup, 
typically located within your global.asax file's Application_Start method:

using RedisSessionProvider.Config;

RedisConnectionConfig.GetRedisServerAddress = (HttpContextBase context) => {
    return new RedisConnectionParameters(){
        ServerAddress = "Your redis server IP or hostname",
        ServerPort = 1234, // the port your redis server is listening on, defaults to 6379
        Password = "12345", // if your redis server is password protected, set this, otherwise leave null
        ServerVersion = "2.6.14" // sometimes necessary, defaults to 2.6.14
    };
};

After that, you should be ready to go. If you do not have your Redis instance set up yet, you can turn off 
RedisSessionProvider by changing the "mode" and "customProvider" attributes of your sessionState web.config element.