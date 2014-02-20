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
        /// Determines what objects have changed in the local Session during the current request. The 
        ///     inputs to this method have several specific characteristics, with each one representing 
        ///     a class of objects from the local Session that were changed in a particular manner. Read 
        ///     each parameter description for details.
        /// </summary>
        /// <param name="confirmedChangedObjects">A dictionary of name-object pairs that contain all
        ///     objects which were set in the Session during the serving of the current request. The return 
        ///     value will be a superset of the serialized values of this dictionary.</param>
        /// <param name="allObjects">A dictionary of name-object pairs that contain all objects which
        ///     were ACCESSED from the Session during this request</param>
        /// <param name="allObjectsOriginalState">A dictionary of name-string pairs that is exactly
        ///     how the data looked in Redis BEFORE any changes in local Session. Generally, you should
        ///     compare all serialized strings of objects from allObjects with their corresponding string
        ///     in this dictionary, and add any strings that differ to the returned dictionary.
        /// 
        /// Why? Consider: Session["a"] = new List<string> {}; will correctly be in confirmedChangedObjects
        ///     But Session["a"].Add("foo") will not because the reference did not change, even though the 
        ///     contents at the reference did. The only way to generally check if the property is 
        ///     dirty is to compare the serialized version of property "a" now with the value of "a"
        ///     before the request happened. There are shortcuts to doing this comparison, for example
        ///     if "a" key already exists in confirmedChangedObjects or deletedObjects this comparison is
        ///     not necessary. In all other cases, you must check in your Serializer implementation or
        ///     your Session properties may not be correct when modified by reference. However, since the 
        ///     allObjects parameter only contains Session properties that were accessed during the current 
        ///     request, you will only incur this penalty for each reference type actually used.</param>
        /// <param name="deletedObjects">A list of property names that were deleted (either Session.Remove 
        ///     or set to null) during the current request. This is an input to the method as an 
        ///     optimization so you don't have to compare serialized forms. Removal from Redis happens 
        ///     outside this method.</param>
        /// <returns>Do NOT modify Redis in this method, since its purpose is merely to decide what keys 
        ///     have changed and to provide their serialized values to the 
        ///     RedisSessionProvider.RedisSessionStateStoreProvider class. The return value should be a
        ///     dictionary of key to serialized byte array pairs that will be set in Redis by the
        ///     class mentioned above, no deletes should be in the returned dictionary.</returns>
        Dictionary<string, byte[]> SerializeWithDirtyChecking(
            Dictionary<string, object> confirmedChangedObjects,
            Dictionary<string, object> allObjects,
            Dictionary<string, string> allObjectsOriginalState,
            List<string> deletedObjects);

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
