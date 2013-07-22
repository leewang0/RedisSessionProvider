namespace Welegan.RedisSessionStoreProvider
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Web;
    using System.Web.SessionState;
    
    using ServiceStack.Text;
    using BookSleeve;

    public static class RedisSessionStateSerializer
    {
        private static string keyTypeDictRedisKey = "RedisSessionStateSerializer.KeyTypes";

        private static string keyTypeOverrideDictRedisKey = "RSSS.keyTypeOverride";

        public static ConcurrentDictionary<string, Type> KeyTypeDict;

        private static string RedisHashPrefix = null;

        private static RedisConnectionWrapper RedisConnWrap;

        private static object InitLock = new object();

        public static void Initialize(RedisConnectionWrapper redisConnWrap, string redisHashPrefix = null)
        {
            lock (InitLock)
            {
                if (KeyTypeDict == null || KeyTypeDict.Count == 0)
                {
                    RedisSessionStateSerializer.RedisHashPrefix = redisHashPrefix;
                    RedisSessionStateSerializer.RedisConnWrap = redisConnWrap;

                    RedisSessionStateSerializer.KeyTypeDict = new ConcurrentDictionary<string, Type>();

                    // try to connect to redis and pull out a pre-existing key->type dictionary
                    try
                    {
                        RedisConnection redisConn = RedisSessionStateSerializer.RedisConnWrap.GetConnection();

                        byte[] redisData = redisConn.Wait(redisConn.Strings.Get(0, RedisSessionStateSerializer.GetRedisKeyTypeDataDictKey()));

                        if(redisData != null && redisData.Length > 0)
                        {
                            string existingTypeData = Encoding.UTF8.GetString(redisData);

                            if (!string.IsNullOrEmpty(existingTypeData))
                            {
                                RedisSessionStateSerializer.KeyTypeDict =
                                    JsonSerializer.DeserializeFromString<ConcurrentDictionary<string, Type>>(existingTypeData);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // fail silently, initializing with empty type dictionary can occasionally happen
                    }
                }
            }
        }

        public static void RememberKeyTypeDict()
        {
            try
            {
                RedisConnection redisConn = RedisSessionStateSerializer.RedisConnWrap.GetConnection();

                redisConn.Strings.Set(
                    0,
                    RedisSessionStateSerializer.GetRedisKeyTypeDataDictKey(), 
                    Encoding.UTF8.GetBytes(
                        JsonSerializer.SerializeToString(RedisSessionStateSerializer.KeyTypeDict)));
            }
            catch (Exception)
            {
            }
        }

        public static string GetRedisKeyTypeDataDictKey()
        {
            return ((!string.IsNullOrEmpty(RedisSessionStateSerializer.RedisHashPrefix)) 
                        ? RedisSessionStateSerializer.RedisHashPrefix + ":" 
                        : null) 
                    + RedisSessionStateSerializer.keyTypeDictRedisKey;
        }

        public static List<KeyValuePair<string, object>> Deserialize(Dictionary<string, byte[]> redisHashDataRaw)
        {
            // the number of items we will eventually return
            int numItems = redisHashDataRaw.Count;

            // this local dictionary stored within the hash has a dictionary containing the keys that do not use the
            //      session-wide types, can often be null for sessions that are entirely homogeneous
            Dictionary<string, Type> typeOverrideDict = null;
            if (redisHashDataRaw.ContainsKey(RedisSessionStateSerializer.keyTypeOverrideDictRedisKey))
            {
                typeOverrideDict = JsonSerializer.DeserializeFromString<Dictionary<string, Type>>(
                    Encoding.UTF8.GetString(
                        redisHashDataRaw[RedisSessionStateSerializer.keyTypeOverrideDictRedisKey]));

                // one of the items is our fake type override dictionary, reduce by 1
                numItems--;
            }

            // we know how many items are in our hash, initialize list to that size
            List<KeyValuePair<string, object>> deserializedList = new List<KeyValuePair<string, object>>(numItems);

            // deserialize each one, keeping in mind that we have their type information between the overall key-type
            //      concurrentdictionary and the override dictionary we already deserialized from the redis hash
            foreach (var redisHashKeyAndField in redisHashDataRaw)
            {
                if (redisHashKeyAndField.Key != RedisSessionStateSerializer.keyTypeOverrideDictRedisKey)
                {
                    Type fieldType;

                    if (typeOverrideDict != null && typeOverrideDict.ContainsKey(redisHashKeyAndField.Key))
                    {
                        fieldType = typeOverrideDict[redisHashKeyAndField.Key];
                    }
                    else if (!RedisSessionStateSerializer.KeyTypeDict.TryGetValue(redisHashKeyAndField.Key, out fieldType))
                    {
                        // not in the overall key-type concurrentdictionary, default to object? Or maybe empty the session?
                        // TODO: figure out the best general behavior here, would clearing the session be the best?
                        fieldType = typeof(object);
                    }

                    deserializedList.Add(new KeyValuePair<string, object>(
                        redisHashKeyAndField.Key,
                        JsonSerializer.DeserializeFromString(Encoding.UTF8.GetString(redisHashKeyAndField.Value), fieldType)));
                }
            }

            return deserializedList;
        }

        public static Dictionary<string, byte[]> Serialize(Dictionary<string, object> redisSetItemsOriginal)
        {
            Dictionary<string, byte[]> serializeItems = new Dictionary<string, byte[]>(
                redisSetItemsOriginal.Count + 1);

            Dictionary<string, Type> differingTypesDict = new Dictionary<string, Type>();

            foreach (var keyValueOriginal in redisSetItemsOriginal)
            {
                // we aren't adding the override stuff yet, we need to see if it changes during this
                if (keyValueOriginal.Key != RedisSessionStateSerializer.keyTypeOverrideDictRedisKey)
                {
                    Type expectedValueType;
                    if (RedisSessionStateSerializer.KeyTypeDict.TryGetValue(keyValueOriginal.Key, out expectedValueType))
                    {
                        // we have an expected type, let's check to make sure that it is correct. If not, we should
                        //      set the override key-type dictionary
                        if (keyValueOriginal.Value.GetType() == expectedValueType)
                        {
                            differingTypesDict.Add(keyValueOriginal.Key, keyValueOriginal.Value.GetType());
                        }
                    }
                    else
                    {
                        // set expectedValueType since we use it later for serialization
                        expectedValueType = keyValueOriginal.Value.GetType();

                        // not in concurrent dict, try to add it, may fail if other thread is also doing this
                        //      which is likely on startup for the first time
                        RedisSessionStateSerializer.KeyTypeDict.TryAdd(keyValueOriginal.Key, expectedValueType);

                        // the key type dictionary has changed, update the persisting one in redis
                        RedisSessionStateSerializer.RememberKeyTypeDict();
                    }

                    // add the serialized json byte array to the list now that we've handled storing type info
                    serializeItems.Add(
                        keyValueOriginal.Key,
                        Encoding.UTF8.GetBytes(
                            JsonSerializer.SerializeToString(keyValueOriginal.Value, expectedValueType)));
                }
            }

            if (differingTypesDict.Count > 0)
            {
                serializeItems.Add(
                    RedisSessionStateSerializer.keyTypeOverrideDictRedisKey,
                    Encoding.UTF8.GetBytes(
                        JsonSerializer.SerializeToString<Dictionary<string, Type>>(differingTypesDict)));
            }

            return serializeItems;
        }
    }
}