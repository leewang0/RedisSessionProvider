RedisSessionProvider
=========================

RedisSessionProvider is a class library that changes the behavior of ASP.NET's Session property 
to efficiently persist data to a [Redis](http://redis.io) server.

To install via NuGet, please go to the NuGet 
[RedisSessionProvider package](https://www.nuget.org/packages/RedisSessionProvider) page

## Why a non-locking session is important

In case you don't know, the default ASP.NET Session State behavior has a [major flaw](http://goo.gl/YEOVJV). Perhaps
it made sense in the early 2000's when AJAX was rare, but **locking requests based on Session ID** does not 
sound like a very good idea to me. 

I.E. If you have multiple requests with the same session Id at the same time, the first will run and the others will 
check in 0.5 second intervals until the first request is complete, then the next one will go, and so on. This is 
great for maintaining correctness of the Session, but horrible for app responsiveness when there's a 
lot of client to server requests happening at once.

RedisSessionProvider never locks the Session. In fact it goes to great lengths to ensure that you
can have as broken a Session as you want. Starting in Version 1.1, however, RedisSessionProvider includes behavior
to mitigate the impact of this breaking change from the default .NET Session implementation. Specifically, in V1.0
RedisSessionProvider returned a distinct `Dictionary<string, object>` instance for each request thread. This
would be fine if each request only operated on separate Session keys, since only the changed objects would be
written back to Redis. However, if two simultaneous requests changed the same key, the second one to finish would
be what was ultimately persisted in Redis. To counter this behavior for reference types, we added a middle layer in
V1.1 that returns the same `ConcurrentDictionary<string, object>` instance to all requests that ask for the same Session.
The end result is that multiple requests that do the following:

    static object locker = new object();
    ...
    List&lt;string&gt; sessionList = Session["myList"] as List&lt;string&gt;;    
    lock(locker) {
        sessionList.Add("some string");
    }

Will be correctly persisted to Redis starting in V1.1, where three simultaneous requests calling the .Add will 
result in three list entries, as opposed to 1 in V1.0. Unfortunately, the call

    int x = (int)Session["myInt"];
    Session["myInt"] = x + 1;

...will still result in non-deterministic behavior, where the final value of x may 1, 2 or 3 higher with three
simultaneous requests. With default ASP.NET Session behavior, because only one request is allowed to run at
a time, the additions will happen exclusively of each other (although with higher latency from the perspective
of the client), and thus be correct. Of course, you could get around this using RedisSessionProvider by only
storing reference types in the Session, and providing thread-safe add calls to any ints by using the 
[System.Threading.Interlocked](http://msdn.microsoft.com/en-us/library/system.threading.interlocked.aspx) 
class, but to get correct behavior with a multi-thread accessible Session you will always have to be conscious
of the highly parallel nature of serving your pages. If speed is your primary concern, and you want to remove
the bottleneck of Session locking, then RedisSessionProvider is for you. If you are writing an app that does
not need that, then use one of the other Redis providers out there.
[RedisSessionStateStore](https://github.com/TheCloudlessSky/Harbour.RedisSessionStateStore)
seems to be the most popular.

## Key features:

* .NET 4.5 library for storing Session data in Redis
* ASP.NET web Sessions are stored as [Redis hashes](http://redis.io/commands#hash), with each `Session["someKey"]`
persisting to a field named `"someKey"` in the Redis hash
* only performs SET or DEL operations back to Redis if the value has changed
* batches all SET and DEL operations when the Session is released at the end of the request
* uses the [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) redis client
* many configurable options for the particulars of using your redis instance(s), see the options section below
* JSON serialization format for human-readable Session contents, using Json.NET
* configuration options for writing your own serializers
* production-tested on [DateHookup](http://www.datehookup.com) a website that serves 25 million page visits (and
many more total web requests) a day

## High-Level overview:

RedisSessionProvider is intended for new ASP.NET projects or existing sites running on .NET 4.5 that want to store
user sessions in Redis as opposed to the built-in options. Some very important behaviors may differ, see the disclaimer 
above. This library exposes a custom 
[SessionStateStoreProviderBase](http://msdn.microsoft.com/en-us/library/ms178587.aspx), a 
class within the System.Web.SessionState namespace that exists solely for the purpose of changing
how your Session data is persisted.

Storing session data in Redis gives you the out-of-process advantages
of the [StateServer session mode](http://msdn.microsoft.com/en-us/library/ms178586.ASPX), which keeps Sessions alive
during application restarts, as well as the load-balancing behavior of the SQLServer mode without the overhead
of maintaining a session-specific database in SQL Server. Please keep in mind, however, that you should still have
IP-affinity or "sticky" load balancing in order to take advantage of the shared local cache portion of 
RedisSessionProvider, which allows for safer multi-threaded Session access (again, see disclaimer above).

## Setting up RedisSessionProvider

This assumes you have a running [Redis server](http://redis.io/) that is at an address accessible by your web machines.
RedisSessionProvider has been actively used on [DateHookup](http://www.datehookup.com) since March 2013 on multiple 
webservers hitting Redis 2.6.14 instances, but has not been tested with earlier or later versions.

### Modifying the web.config

Your sessionState element must be changed to mark RedisSessionProvider as the provider of the Session data. This can be
done with the following modifications:

	<sessionState 
		mode="Custom" 
		customProvider="RedisSessionProvider">
		<providers>
			<add 
				name="RedisSessionProvider" 
				type="RedisSessionProvider.RedisSessionStateStoreProvider, RedisSessionProvider" />
		</providers>
	</sessionState>

### Configuring your specifics

The sessionState element only provides the entrypoint from your application into the RedisSessionProvider code. In
order for RedisSessionProvider to know where to direct requests to Redis, you must set the following properties once
in your application (Global.asax application_start is a good place for that):

	using RedisSessionProvider.Config;
	using StackExchange.Redis

	// your application class name may differ but the name or base class should not matter for our purposes
	public class MvcApplication : System.Web.HttpApplication
	{
		public static ConfigurationOptions redisConfigOpts { get; set; }

		...

		protected void Application_Start()
		{
			// assign your local Redis instance address, can be static
			Application.redisConfigOpts = ConfigurationOptions.Parse("{ip}:{port}");
	
			// pass it to RedisSessionProvider configuration class
			RedisConnectionConfig.GetSERedisServerConfig = (HttpContextBase context) => {
				return new KeyValuePair<string,ConfigurationOptions>(
					"DefaultConnection",				// if you use multiple configuration objects, please make the keys unique
					Application.redisConfigOpts);
			};
		}
	}

Prior to V1.2.1, we used a separate configuration class than StackExchange.Redis. The delegate example for that is:

	using RedisSessionProvider.Config;
	...
	RedisConnectionConfig.GetRedisServerAddress = (HttpContextBase context) => {
		return new RedisConnectionParameters(){
			ServerAddress = "Your redis server IP or hostname",
			ServerPort = 1234, // the port your redis server is listening on, defaults to 6379
			Password = "12345", // if your redis server is password protected, set this, otherwise leave null
			ServerVersion = "2.6.14", // sometimes necessary, defaults to 2.6.14
			UseProxy = Proxy.None     // can specify TwemProxy as a proxy layer
		};
	};

Why does it take a lambda function? It takes a lambda in case you want to load-balance across multiple Redis 
instances, using the context as input. This way, you can dynamically choose your Redis server to suit your needs. 
If you only have one server, a simple function like the example will suffice. RedisSessionProvider may provide an 
IOC-based configuration method in future versions, but for now it's a static property. If you use multiple
ConfigurationOptions objects, please make sure each one is returned with a unique key since the connections that
are made with them are stored in a Dictionary. Different ConfigurationOptions with the same key will always
use the first ConfigurationOption to be called.

In V1.2 of the NuGet package, a proxy option was added to the connection parameters that allows you to specify 
[TwemProxy](https://github.com/twitter/twemproxy/blob/master/notes/redis.md) compatible behavior from 
StackExchange.Redis. By specifying this, you will have a reduced command set available, allowing RedisSessionProvider 
to talk to a TwemProxy endpoint.

At this point, your application should work, but there are additional configuration options should you need them.

### Use with WebAPI, ServiceStack or other non-standard pipelines

Starting in V1.2.2, RedisSessionProvider exposes an IDisposable class called RedisSessionAccessor. The best explanation
for its use is this short example:

    public class APIController ...
	{
		// getting an HttpContext in WebAPI is weird
		public HttpContextBase MyHttpContextBase
        {
            get
            {
                object context;
                if (this.Request != null && this.Request.Properties.TryGetValue(
                    "MS_HttpContext", out context))
                {
                    return ((HttpContextWrapper)context);
                }
                else if (HttpContext.Current != null)
                {
                    return new HttpContextWrapper(HttpContext.Current);
                }

                return null;
            }
        }

		// some public WebAPI method
		public SomeObject PostMethod(SomeParams parmesan)
		{
			using(var sessAcc = new RedisSessionAccessor(
				new HttpContextWrapper(MyHttpContextBase)))
			{
				// within this using block, you now have access to the same Session as web pages, at the
				//		cost of possibly reading from Redis in the constructor of RedisSessionAccessor.
				//		Keep in mind that RedisSessionProvider shares one instance of the Session 
				//		collection between all currently serving requests, so if you happen to have another
				//		request going on at the same instant as this constructor, it is more or less free.
				var aha = sessAcc.Session["currentUser"]

				// now here, at the } where the dispose happens, the RedisSessionAccessor will write
				//		the changes back to Redis, just like in a normal ASP.NET Session module
				//		SetAndReleaseItemExclusive event
			}
		}
	}

There are plenty of flame wars to be had over whether or not this violates REST-ful webservice design practices.
I could care less, the accessor is just there for convenience if you need it. The `Session` property that it
exposes inherits from `SessionStateBase`, but it does not implement all of its methods. See the 
[FakeHttpSessionState class definition](https://github.com/welegan/RedisSessionProvider/blob/master/RedisSessionProvider/RedisSessionAccessor.cs)
for details.

### More configuration options

Within the RedisSessionProvider.Config namespace, there are two other classes in addition to RedisConnectionConfig
that you may find useful for your specific needs.

#### RedisConnectionConfig additional properties

	RedisConnectionConfig.LogRedisCommandsSentDel 

A lambda function you can specify which will log every 30 seconds the number of Redis commands sent

	RedisConnectionConfig.LogRedisCommandsReceivedDel 

A lambda function you can specify which will log every 30 seconds the number of Redis replies received

#### RedisSerializationConfig additional properties
	
	RedisSerializationConfig.SessionDataSerializer

This is an instance of a class of type IRedisSerializer, which you can replace with your own implementation if
you have a need to serialize to something other than JSON within Redis. The default implementation is called
[RedisSessionProvider.Serialization.RedisJSONSerializer](https://github.com/welegan/RedisSessionProvider/blob/master/RedisSessionProvider/Serialization/RedisJSONSerializer.cs).
Alternative serializers will be made available as they 
are written. The method signatures in IRedisSerializer contain detailed descriptions of each method's purpose 
if you do choose to roll your own. Please consider adding a pull request if you do.

	RedisSerializationConfig.SerializerExceptionLoggingDel

This logging lambda function is used within try-catch blocks of the RedisJSONSerializer so you can get 
detailed exception messages if anything is going wrong at the serialization level. Hopefully (and likely) you 
won't need it. Personally, I had to log all serializations once upon a time because of a legacy code block
storing DataTable instances in Session. Don't do that, they aren't meant to be serialized easily, though
we do have a workaround in place to handle it within RedisJSONSerializer. But please don't do it.

#### RedisSessionConfig additional properties

	RedisSessionConfig.SessionExceptionLoggingDel

This lambda function is used in the top-level RedisSessionProvider.RedisSessionStateStoreProvider class to
log all exceptions that bubble up through all the other classes. If something is just not 
working, adding this method may help you pinpoint the issue.

	RedisSessionConfig.RedisKeyFromSessionIdDel

If you share your Redis server with other users or applications, it may be that the default behavior of using
the auto-generated .NET session id as the Redis key name is insufficient. Setting this lambda function will
allow you to have fine-grained control over how an HttpContext and given session id are translated into a
Redis key name to store the hash of Session values.
