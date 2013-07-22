RedisSessionStoreProvider
=========================

A class library that will (with appropriate web.config changes) swap the persistence layer of the .NET HttpContext.Session object from the 3 standard options (InProc, StateServer or database) to a Redis process using JSON as the serialization format.
