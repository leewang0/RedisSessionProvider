namespace RedisSessionProvider
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.SessionState;

    using AsyncBridge;
    using BookSleeve;
    using RedisSessionProvider.Config;
    using RedisSessionProvider.Serialization;

    /// <summary>
    /// This class holds the Session's items during the serving of a web request. It lazily deserializes items as they
    ///     are accessed by the web layer, and holds on to which keys have been added modified or deleted directly. These
    ///     various collections are then examined after the request is done being served to determine which keys have
    ///     changed and then re-populated in Redis.
    /// </summary>
    public class RedisSessionStateItemCollection : ISessionStateItemCollection, ICollection, IEnumerable
    {
        /// <summary>
        /// Gets or sets a dictionary of keys that have changed during the course of this session, and what
        ///     the changed action was (either del or set in redis terms). We use this as opposed to two lists
        ///     to allow overwriting of actions based on key e.g. modifying then deleting would end up being 
        ///     just a delete in this scheme. 
        /// </summary>
        public ConcurrentDictionary<string, ActionAndValue> ChangedKeysDict { get; set; }

        /// <summary>
        /// Gets or sets a dictionary of keys and serialized values as they came from Redis, so we can
        ///     lazily deserialize keys that we need later on, as well as compare what has changed afterwards
        /// </summary>
        public ConcurrentDictionary<string, string> SerializedRawData { get; set; }

        /// <summary>
        /// Gets or sets the dictionary that actually stores all of the items in the Session
        /// </summary>
        protected ConcurrentDictionary<string, object> Items { get; set; }

        /// <summary>
        /// Gets or sets a hashset of keys that have been looked at during this object's lifetime. This is a
        ///     standard HashSet, not from the System.Collections.Concurrent namespace because we only ever
        ///     call Add or Contains on it. In the case of Add/Add from two threads, the result is the same, a
        ///     key will be there. In the case of Add/Contains from two threads, with the Contains going first,
        ///     the extra key will be skipped, which is bad. However, the thread that called Add must itself
        ///     eventually call Contains at the end of its web request to see if it needs to propogate back
        ///     to Redis, so the accessed key will be inspected for changes eventually anyways. Contains/Contains
        ///     will be fine as well, since neither will modify the collection. Thus, we (with great risk)
        ///     stick to the non-thread-safe implementation, though this can easily be swapped out for a
        ///     ConcurrentDictionary<string, byte> if need be.
        /// </summary>
        protected HashSet<string> AccessedKeys { get; set; }

        /// <summary>
        /// All keys in the Items collection, so we can guarantee an ordering
        /// </summary>
        protected List<string> ItemKeys { get; set; }

        /// <summary>
        /// An semaphore whose "read" is allowing access to the objects in the session (either get or set) 
        ///     but whose "write" is the copying of the state of those objects in the session to a temporary
        ///     List whose enumerator is returned when the SessionModule is ready to write to Redis. This
        ///     ensures that any thread can use the ConcurrentDictionary's locking to access objects in 
        ///     session, but will wait when any of them are ready to write to redis. Changes the data in
        ///     SerializedRawData, which becomes a state for what was last written or read from Redis since
        ///     this class is SHARED by all threads accessing a particular user session.
        /// </summary>
        private readonly ReaderWriterLockSlim enumLock = new ReaderWriterLockSlim();

        /// <summary>
        /// The serializer from RedisSerializationConfig to use
        /// </summary>
        private IRedisSerializer cereal;


        /// <summary>
        /// Instantiates a new instance of the RedisSessionStateItemCollection class, with no values
        /// </summary>
        public RedisSessionStateItemCollection()
            : this(null, null)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the RedisSessionStateItemCollection class, with data from
        ///     Redis
        /// </summary>
        /// <param name="redisHashData">A dictionary of keys to byte array values from Redis. The
        ///     byte array should be the output of the SerializeOne method from the configured 
        ///     IRedisSerializer in RedisSerializationConfig</param>
        public RedisSessionStateItemCollection(Dictionary<string, byte[]> redisHashData, string redisConnName)
        {
            int byteDataTotal = 0;
            int concLevel = RedisSessionConfig.SessionAccessConcurrencyLevel;
            if (concLevel < 1)
            {
                concLevel = 1;
            }

            int numItems = 0;
            if (redisHashData != null)
            {
                numItems = redisHashData.Count;
            }

            this.Items = new ConcurrentDictionary<string, object>(concLevel, numItems);
            this.ItemKeys = new List<string>(numItems);
            this.SerializedRawData = new ConcurrentDictionary<string, string>();
            if (redisHashData != null)
            {
                foreach (var sessDataEntry in redisHashData)
                {
                    if (this.SerializedRawData.TryAdd(
                        sessDataEntry.Key,
                        Encoding.UTF8.GetString(sessDataEntry.Value))

                        && this.Items.TryAdd(
                        sessDataEntry.Key, 
                        new NotYetDeserializedPlaceholderValue()))
                    {
                        ItemKeys.Add(sessDataEntry.Key);
                    }

                    byteDataTotal += sessDataEntry.Value.Length;
                }
            }

            this.ChangedKeysDict = new ConcurrentDictionary<string, ActionAndValue>();
            this.AccessedKeys = new HashSet<string>();

            if (byteDataTotal != 0 && !string.IsNullOrEmpty(redisConnName) &&
                RedisConnectionConfig.LogRedisSessionSize != null)
            {
                RedisConnectionConfig.LogRedisSessionSize(redisConnName, byteDataTotal);
            }

            this.cereal = RedisSerializationConfig.SessionDataSerializer;

            if(byteDataTotal > RedisConnectionConfig.MaxSessionByteSize)
            {
                RedisConnectionConfig.RedisSessionSizeExceededHandler(this, byteDataTotal);
            }
        }


        #region ISessionStateItemCollection Members

        /// <summary>
        /// Clears the Session of all values, deleting all keys. 
        /// </summary>
        public void Clear()
        {
            if (this.enumLock.TryEnterReadLock(50))
            {
                try
                {
                    string[] keyArr = new string[this.ItemKeys.Count];
                    this.ItemKeys.CopyTo(keyArr);

                    // insert all keys as Del actions into change dict
                    foreach (string key in keyArr)
                    {
                        object removed;
                        if (this.Items.TryRemove(key, out removed))
                        {
                            this.AddOrSetItemAction(key, new DeleteValue());
                        }
                    }

                    // clear internal raw values as well since the delete actions will cause the serializer to
                    //      ignore original session values, so no need to hold on to them
                    SerializedRawData.Clear();
                }
                finally
                {
                    this.enumLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether or not the collection has changed. This implementation always returns
        ///     true because we do a dirty-check comparison in 
        ///     RedisSessionProvider.RedisSessionStateStoreProvider.SetAndReleaseItemExclusive method, which
        ///     only runs when this is true. This behavior may change in later versions of 
        ///     RedisSessionProvider
        /// </summary>
        public bool Dirty
        {
            get
            {
                return true;
            }
            set
            {
                // do nothing
            }
        }

        /// <summary>
        /// Gets the non-null keys present in the Session
        /// </summary>
        public NameObjectCollectionBase.KeysCollection Keys
        {
            get
            {
                // KeysCollection has no available constructor, so make a fake one... sigh. Look
                //      into making a subclass of KeysCollection with an internal constructor
                NameValueCollection fakeColl = new NameValueCollection();

                if (this.enumLock.TryEnterReadLock(50))
                {
                    try
                    {
                        string[] keyArr = new string[this.ItemKeys.Count];
                        this.ItemKeys.CopyTo(keyArr);

                        foreach (string key in keyArr)
                        {
                            fakeColl.Add(key, "a");
                        }
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        this.enumLock.ExitReadLock();
                    }
                }

                return fakeColl.Keys;
            }
        }

        /// <summary>
        /// Removes a key from the Session
        /// </summary>
        /// <param name="name">The Session key to remove</param>
        public void Remove(string name)
        {
            if (this.enumLock.TryEnterReadLock(50))
            {
                try
                {
                    object removed;
                    if (this.Items.TryRemove(name, out removed))
                    {
                        this.AddOrSetItemAction(name, new DeleteValue());
                    }

                    if (SerializedRawData != null && SerializedRawData.ContainsKey(name))
                    {
                        string serRemoved;
                        SerializedRawData.TryRemove(name, out serRemoved);
                    }
                }
                finally
                {
                    this.enumLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Removes an item from the Session based on its position
        /// </summary>
        /// <param name="index">The index of the object to remove</param>
        public void RemoveAt(int index)
        {
            if (this.enumLock.TryEnterReadLock(50))
            {
                try
                {
                    if (index < this.ItemKeys.Count)
                    {
                        string removeKey = this.ItemKeys[index];
                        object removedItem;

                        if (this.Items.TryRemove(removeKey, out removedItem))
                        {
                            this.AddOrSetItemAction(removeKey, new DeleteValue());
                        }

                        if (SerializedRawData != null)
                        {
                            string removed;
                            SerializedRawData.TryRemove(removeKey, out removed);
                        }
                    }
                }
                finally
                {
                    this.enumLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets or sets an item in the Session at an index
        /// </summary>
        /// <param name="index">The index of the object to get or set</param>
        /// <returns>The object at the index in the Session</returns>
        public object this[int index]
        {
            get
            {
                if (this.enumLock.TryEnterReadLock(50))
                {
                    try
                    {
                        if (index < this.ItemKeys.Count)
                        {
                            // read / write locks within this method, be careful
                            return MemoizedDeserializeGet(this.ItemKeys[index]);
                        }
                    }
                    finally
                    {
                        this.enumLock.ExitReadLock();
                    }
                }

                return null;
            }
            set
            {
                if (this.enumLock.TryEnterReadLock(50))
                {
                    try
                    {
                        if (index < this.ItemKeys.Count)
                        {
                            BaseSetWithDeserialize(this.ItemKeys[index], value);
                        }
                    }
                    finally
                    {
                        this.enumLock.ExitReadLock();
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets an item in the Session based on its key name. If no item exists,
        ///     returns null.
        /// </summary>
        /// <param name="name">The key name of the item in the Session</param>
        /// <returns>The object corresponding to the key name or null if none exist</returns>
        public object this[string name]
        {
            get
            {
                if (this.enumLock.TryEnterReadLock(50))
                {
                    try
                    {
                        // read / write locks within this method, be careful
                        return MemoizedDeserializeGet(name);
                    }
                    finally
                    {
                        this.enumLock.ExitReadLock();
                    }
                }

                return null;
            }
            set
            {
                if (this.enumLock.TryEnterReadLock(50))
                {
                    try
                    {
                        BaseSetWithDeserialize(name, value);
                    }
                    finally
                    {
                        this.enumLock.ExitReadLock();
                    }
                }
            }
        }

        #endregion

        #region ICollection Members

        /// <summary>
        /// Copies the entire Session to an array. This is not currently implemented and will 
        ///     throw an exception.
        /// </summary>
        /// <param name="array">The destination array object to hold the Session's values</param>
        /// <param name="index">The index at which to begin copying items</param>
        public void CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Always returns false
        /// </summary>
        public bool IsSynchronized
        {
            get { return false; }
        }

        /// <summary>
        /// Throws a notimplemented exception when the get accessor is called.
        /// </summary>
        public object SyncRoot
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Gets the number of items in the collection
        /// </summary>
        public int Count
        {
            get
            {
                if (this.enumLock.TryEnterReadLock(50))
                {
                    try
                    {
                        return this.Items.Count;
                    }
                    finally
                    {
                        this.enumLock.ExitReadLock();
                    }
                }

                return 0;
            }
        }

        #endregion

        #region IEnumerable Members

        public IEnumerator GetEnumerator()
        {
            if (this.enumLock.TryEnterReadLock(50))
            {
                try
                {
                    return this.Items.GetEnumerator();
                }
                finally
                {
                    this.enumLock.ExitReadLock();
                }
            }

            return new ConcurrentDictionary<string, object>().GetEnumerator();
        }

        #endregion

        
        /// <summary>
        /// A key has been set, or removed. We need to store that so we know what we don't need to 
        ///     dirty-check at the end of the current request.
        /// </summary>
        /// <param name="key">The Session key name that has been changed</param>
        /// <param name="itemAct">The type of change applied to the key, either DeleteValue or 
        ///     SetValue</param>
        protected void AddOrSetItemAction(string key, ActionAndValue itemAct)
        {
            this.ChangedKeysDict.AddOrUpdate(
                key,
                itemAct,
                (k, orig) => {
                    // override old value
                    return itemAct;
                });
        }

        /// <summary>
        /// If the key has been deserialized already, return the current object value we have on hand 
        ///     for it. If it has not, then deserialize it from initial Redis input and add it to the base
        ///     collection.
        /// </summary>
        /// <param name="key">The desired Session key name</param>
        /// <returns>The deserialized object at the key, or null if it does not exist.</returns>
        protected object MemoizedDeserializeGet(string key)
        {
            // Record that we are accessing this key
            this.AccessedKeys.Add(key);

            object storedObj = null;
            if (this.Items.TryGetValue(key, out storedObj))
            {
                if (storedObj is NotYetDeserializedPlaceholderValue)
                {
                    string serializedData;
                    if(SerializedRawData.TryGetValue(key, out serializedData))
                    {
                        try
                        {
                            var placeholderReference = storedObj;
                            storedObj = this.cereal.DeserializeOne(serializedData);

                            // if we can't deserialize, storedObj will still be the placeholder and in that case it's
                            //      as if the DeserializeOne method error'ed, so mark it as failed to deserialize
                            if (storedObj is NotYetDeserializedPlaceholderValue)
                            {
                                storedObj = null;
                            }

                            // Try to update the key to the deserialized object. If it fails, check to make sure that its
                            //      not because it was already deserialized in another thread
                            if(!this.Items.TryUpdate(key, storedObj, placeholderReference))
                            {
                                // the update failed, this could be because the comparison value was different for the
                                //      concurrentDictionary, so lets try fetching the value again
                                if (this.Items.TryGetValue(key, out storedObj))
                                {
                                    // if it is not a placeholder, return the re-fetched object from the Items collection
                                    if (!(storedObj is NotYetDeserializedPlaceholderValue))
                                    {
                                        return storedObj;
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            if (RedisSerializationConfig.SerializerExceptionLoggingDel != null)
                            {
                                RedisSerializationConfig.SerializerExceptionLoggingDel(e);
                            }

                            storedObj = null;
                        }
                    }
                }
            }

            return storedObj;
        }

        /// <summary>
        /// Sets a key to a value if it differs from the current value. If the value we are setting is
        ///     null, call remove on it instead because there is no point storing an empty key into 
        ///     Redis. If it is being set, record that into the ChangedKeysDict.
        /// </summary>
        /// <param name="key">The key name to set</param>
        /// <param name="value">The value to assign to it</param>
        protected void BaseSetWithDeserialize(string key, object value)
        {
            // if we are trying to set something to null, consider it a delete
            if (value == null)
            {
                object removedObj;
                if (this.Items.TryRemove(key, out removedObj))
                {
                    this.AddOrSetItemAction(key, new DeleteValue());
                }
            }
            else
            {
                object addedOrUpdated = this.Items.AddOrUpdate(
                    key,
                    value,
                    (kee, oldVal) => {
                        return value;
                    });

                if (addedOrUpdated == value) 
                {
                    this.AddOrSetItemAction(key, new SetValue() { Value = value });
                }
            }
        }


        /// <summary>
        /// Returns a class that allows iteration over the objects in the Session collection that have changed
        ///     since being pulled from Redis. Each key is checked for being all of the following: key was 
        ///     accessed, key was not removed or set to null, if the key holds the same object reference as it
        ///     did when pulled from redis, then the current object at the reference address is serialized and
        ///     the serialized string is compared with the serialized string that was originally pulled from
        ///     Redis. The last condition is to ensure Dirty checking is correct, i.e. 
        ///     ((List<string>)Session["a"]).Add("a");
        ///     does not change what Session["a"] refers to, but it does change the object and we need to write
        ///     that back to Redis at the end.
        /// </summary>
        /// <returns>an IEnumerator that allows iteration over the changed elements of the Session.</returns>
        public IEnumerable<KeyValuePair<string, object>> GetChangedObjectsEnumerator()
        {
            List<KeyValuePair<string, object>> changedObjs = new List<KeyValuePair<string, object>>();

            if (this.enumLock.TryEnterWriteLock(50))
            {
                try
                {
                    // for items that have definitely changed (ints, strings, value types and
                    //      reference types whose refs have changed), add to the resulting list
                    //      and reset their serialized raw data
                    foreach (KeyValuePair<string, ActionAndValue> changeData in ChangedKeysDict)
                    {
                        if (changeData.Value is SetValue)
                        {
                            changedObjs.Add(
                                new KeyValuePair<string, object>(
                                    changeData.Key,
                                    changeData.Value.Value));
                                    
                            // now set the SerializedRawData of the key to what we are returning to the
                            //      Session provider, which will write it to Redis so the next thread won't
                            //      think the obj has changed when it tries to write
                            string oldSerVal;
                            if (this.SerializedRawData.TryGetValue(changeData.Key, out oldSerVal))
                            {
                                string newSerVal = this.cereal.SerializeOne(changeData.Key, this.Items[changeData.Key]);
                                if (oldSerVal != newSerVal)
                                {
                                    this.SerializedRawData.TryUpdate(
                                        changeData.Key,
                                        newSerVal,
                                        oldSerVal);
                                }
                            }
                        }
                        if (changeData.Value is DeleteValue)
                        {
                            // null means delete to the serializer, perhaps change this in the future
                            changedObjs.Add(
                                new KeyValuePair<string, object>(
                                    changeData.Key,
                                    null));

                            // remove what SerializedRawData thinks is the original Redis state for the key,
                            //      because it will be removed by the Session provider once it reads from the
                            //      enumerator we are returning
                            string remSerVal;
                            this.SerializedRawData.TryRemove(changeData.Key, out remSerVal);
                        }
                    }

                    // check each key that was accessed for changes
                    foreach (string usedKey in this.AccessedKeys)
                    {
                        // only check keys that are not already in the changedObjs list
                        bool alreadyAdded = false;
                        foreach (KeyValuePair<string, object> markedItem in changedObjs)
                        {
                            if (markedItem.Key == usedKey)
                            {
                                alreadyAdded = true;
                                break;
                            }
                        }

                        if (!alreadyAdded)
                        {
                            object usedItem;
                            if (this.Items.TryGetValue(usedKey, out usedItem))
                            {
                                string serVal = this.cereal.SerializeOne(usedKey, usedItem);
                                string origSerVal;
                                if (this.SerializedRawData.TryGetValue(usedKey, out origSerVal))
                                {
                                    if (serVal != origSerVal)
                                    {
                                        // object's value has changed, add to output list
                                        changedObjs.Add(new KeyValuePair<string, object>(usedKey, usedItem));

                                        // and reset the original state of it to what it is now
                                        this.SerializedRawData.TryUpdate(
                                            usedKey,
                                            serVal,
                                            origSerVal);
                                    }
                                }
                            }
                        }
                    }

                    // reset the ChangedKeysDict for each item we are returning in the enumerator, since they
                    //      will be written to Redis
                    foreach (KeyValuePair<string, object> changeItem in changedObjs)
                    {
                        ActionAndValue removeVal;
                        this.ChangedKeysDict.TryRemove(changeItem.Key, out removeVal);
                    }
                }
                finally
                {
                    this.enumLock.ExitWriteLock();
                }
            }

            return changedObjs;
        }


        /// <summary>
        /// This class is inserted as the value for each key initially, and is removed when the
        ///     key is accessed (and then deserialized) for the first time by the application. 
        /// </summary>
        private class NotYetDeserializedPlaceholderValue
        {
        }

        /// <summary>
        /// Base class wrapping a value that provides a descriptor of what happened to the value
        ///     during the current request Session (either delete or set).
        /// </summary>
        public abstract class ActionAndValue
        {
            public object Value { get; set; }
        }

        /// <summary>
        /// Class that wraps a value that was deleted during the current request
        /// </summary>
        public class DeleteValue : ActionAndValue
        {
        }

        /// <summary>
        /// Class that wraps a value that was modified during the current request
        /// </summary>
        public class SetValue : ActionAndValue
        {
        }
    }
}