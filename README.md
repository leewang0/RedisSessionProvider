RedisSessionProvider
=========================

## Key features:

* .NET 4.5 library for storing Session data in Redis
* stores .NET web Sessions as [Redis hashes](http://redis.io/commands#hash), with each Session key translating to a key in the Redish hash
* only performs SET or DEL operations on hash keys if the value has changed since it was retrieved from Redis
* batches all SET and DEL operations when the Session is released (at the end of the request pipeline)
* uses the [Booksleeve](https://code.google.com/p/booksleeve/) redis client, an awaitable library for asynchronous communication with Redis
* many configurable options for the particulars of using your redis instance(s), see the options section below
* JSON serialization format for easy reading of Session contents on the redis end, using Json.NET
* already production-tested on a website that serves 25 million page visits a day

## High-Level overview:

This library exposes a custom [SessionStateStoreProviderBase](http://msdn.microsoft.com/en-us/library/ms178587.aspx), a 
Microsoft-created class within the System.Web.SessionState namespace that exists solely for the purpose of changing
how your Session data is persisted. Because the .NET Session Module is not modified aside from this class, you should
feel more confident that your website's pre-existing use of the Session keyword within webforms and MVC controllers
will not exhibit any strange behavior by dropping this layer in. 

In addition, because Session data is persisted over the network to Redis nodes of your choosing, you will be able to
truly load-balance your web application without any IP-affinity or concern that taking webserver nodes out of your 
cluster will harm your existing user Sessions. Storing Session data in Redis gives you the out-of-process advantages
of the [StateServer mession mode](http://msdn.microsoft.com/en-us/library/ms178586.ASPX), which keeps Sessions alive
during application restarts, as well as the true-load-balancing behavior of the SQLServer mode without the overhead
of maintaining a Session database in SQL Server.

## Setting up RedisSessionProvider

This assumes you have running (Redis server)[http://redis.io/] that is at an address accessible by your web machines.
RedisSessionProvider has been production tested and running since 2013 on multiple webservers hitting Redis 2.6.14
instances.

### Modifying the web.config

Your sessionState element must be changed to mark RedisSessionProvider as the provider of the Session data. This can be
done with the following modifications:

	<sessionState 
		mode="Custom" 
		customProvider="RedisSessionProvider">
		<providers>
			<add name="RedisSessionProvider" type="RedisSessionProvider.RedisSessionStateStoreProvider, RedisSessionProvider" />
		</providers>
	</sessionState>

### Configuring your specifics

The sessionState element only provides the entrypoint from your application into the RedisSessionProvider code. In
order for RedisSessionProvider to know where to direct requests to Redis, you must set the following properties:

	using RedisSessionProvider.Config;
	...
	RedisConnectionConfig.GetRedisServerAddress = (HttpContextBase context) => {
		return new RedisConnectionParameters(){
			ServerAddress = "Your redis server IP or hostname",
			ServerPort = 1234, // the port your redis server is listening on, defaults to 6379
			Password = "12345", // if your redis server is password protected, set this, otherwise leave null
			ServerVersion = "2.6.14" // sometimes necessary, defaults to 2.6.14
		};
	};

Eeewww, why does it take a lambda function as opposed to a configuration class? It takes a lambda in case you want to
load-balance across multiple Redis instances, using the context as input. This way, you can dynamically choose your 
Redis server to suit your needs. If you only have one server, a simple function like the example will suffice.

At this point, your application should work, but there are additional configuration options should you need them.

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
RedisSessionProvider.Serialization.RedisJSONSerializer. RedisSessionProvider is under active development and 
alternative serializers will be made available as they are written. The method signatures in IRedisSerializer
contain detailed descriptions of each method's purpose if you do choose to roll your own.

	RedisSerializationConfig.SerializerExceptionLoggingDel

This logging lambda function is used within try-catch blocks of the RedisJSONSerializer so you can get 
detailed exception messages if anything is going wrong at the serialization level. Hopefully (and likely) you 
won't need it. Personally, I had to log all serializations once upon a time because of a legacy code block
storing DataTable instances in Session. Don't do that, they aren't meant to be serialized easily, though
we do currently have a workaround in place to handle it within RedisJSONSerializer. But much gross.

#### RedisSessionConfig additional properties

	RedisSessionConfig.SessionExceptionLoggingDel

This lambda function is used in the top-level RedisSessionProvider.RedisSessionStateStoreProvider class to
log all exceptions that slip through after serialization, or for other causes. If something is just not 
working, adding this method may help you pinpoint the issue.

	RedisSessionConfig.RedisKeyFromSessionIdDel

If you share your Redis server with other users or applications, it may be that the default behavior of using
the auto-generated .NET session id as the Redis key name is insufficient. Setting this lambda function will
allow you to have fine-grained control over how an HttpContext and given session id are translated into a
Redis key name to store the hash of Session values.
