RedisSessionStoreProvider
=========================

A class library that will (with appropriate web.config changes) swap the persistence layer of the .NET HttpContext.Session object from one of the 3 standard options (InProc, StateServer or database) to a Redis process using JSON as the serialization format.

This implementation of the SessionStateProviderBase abstract class uses the Booksleeve redis client and the ServiceStack.Text JSONSerializers for improved performance. Both libraries are well-documented and come from very reputable sources. In addition, there is a reference to NLog that is trivial to remove if you do not want any logging at all (there's very little as it is since I wrote this over a few days).

It is worth noting that the RedisSessionStateProvider performs no locking of the hash on requests, so if two requests come in simultaneously there is no guarantee of consistency if both want to store to the same key. In practice, I feel as though redis is quick enough and Session variables are static enough that this should not be an issue for most uses.

Key features:

* only performs SET or DEL operations if the value has changed since it was retrieved from redis
* batches all SET and DEL operations when the Session is released (at the end of the request pipeline)
* configurable options for the ip and port number of your redis instance, as well as key-prefix in case you have multiple applications all using the same redis instance.
* JSON serialization format for easy reading of Session contents on the redis end, provided by the very fast ServiceStack.Text.JSONSerializer
* type-safe objects upon deserialization even though we are using JSON to store object data
