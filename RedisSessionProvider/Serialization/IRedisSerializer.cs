namespace RedisSessionProvider.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// An interface containing the methods used by the RedisSessionProvider to convert objects to strings
    ///     or byte arrays, to be written to Redis. Currently, RedisSessionProvider only calls DeserializeOne
    ///     and SerializeWithDirtyChecking methods, though implementing the other methods will be helpful
    ///     during debugging. To set your own custom Serializer for RedisSessionProvider, set the 
    ///     RedisSessionProvider.Config.RedisSerializationConfig.SessionDataSerializer property to an 
    ///     instance of your Serializer class.
    /// </summary>
    public interface IRedisSerializer
    {
        /// <summary>
        /// Not used within RedisSessionProvider in favor of lazy deserialization on a per-key basis, but
        ///     this method deserializes the entire contents of a Redis session all at once
        /// </summary>
        /// <param name="redisHashDataRaw">The key-value pairs directly from Redis</param>
        /// <returns>A list of name-object pairs</returns>
        List<KeyValuePair<string, object>> Deserialize(Dictionary<string, string> redisHashDataRaw);

        /// <summary>
        /// Deserializes one byte array into the corresponding, correctly typed object
        /// </summary>
        /// <param name="objRaw">A byte array of the object data</param>
        /// <returns>The deserialized object, or null if the byte array is empty</returns>
        object DeserializeOne(byte[] objRaw);

        /// <summary>
        /// Deserializes one string into the corresponding, correctly typed object
        /// </summary>
        /// <param name="objRaw">A string of the object data</param>
        /// <returns>The Deserialized object, or null if the string is null or empty</returns>
        object DeserializeOne(string objRaw);

        /// <summary>
        /// This method is not used by RedisSessionProvider, but its purpose should it be called is to take
        ///     the entire Session contents and return their serialized, byte array values.
        /// </summary>
        /// <param name="redisSetItemsOriginal">The entire contents of the current Session</param>
        /// <returns>A dictionary of name to serialized byte array values corresponding to the input
        ///     redisSetItemsOriginal</returns>
        Dictionary<string, byte[]> Serialize(Dictionary<string, object> redisSetItemsOriginal);

        /// <summary>
        /// This method serializes one key-object pair into a string. SerializeWithDirtyChecking can
        ///     call this, or not, depends on your implementation. For ease of debugging, however,
        ///     implementing this method is highly recommended.
        /// </summary>
        /// <param name="key">The string key of the Session property, may factor into your serializer, 
        ///     may not</param>
        /// <param name="origObj">The value of the Session property</param>
        /// <returns>The serialized origObj data as a string</returns>
        string SerializeOne(string key, object origObj);
    }
}
