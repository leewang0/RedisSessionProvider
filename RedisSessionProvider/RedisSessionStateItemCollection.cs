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
    public class RedisSessionStateItemCollection : NameObjectCollectionBase, ISessionStateItemCollection, ICollection, IEnumerable
    {
        /// <summary>
        /// Gets or sets a dictionary of keys that have changed during the course of this session, and what
        ///     the changed action was (either del or set in redis terms). We use this as opposed to two lists
        ///     to allow overwriting of actions based on key e.g. modifying then deleting would end up being 
        ///     just a delete in this scheme. 
        /// </summary>
        public Dictionary<string, ActionAndValue> ChangedKeysDict { get; set; }

        /// <summary>
        /// Gets or sets a dictionary of keys and serialized values as they came from Redis, so we can
        ///     lazily deserialize keys that we need later on, as well as compare what has changed afterwards
        /// </summary>
        public Dictionary<string, string> SerializedRawData { get; set; }

        /// <summary>
        /// Gets or sets a hashset of keys that have been looked at during this object's lifetime
        /// </summary>
        protected HashSet<string> AccessedKeys { get; set; }

        /// <summary>
        /// Instantiates a new instance of the RedisSessionStateItemCollection class, with no values
        /// </summary>
        public RedisSessionStateItemCollection()
            : this(null)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the RedisSessionStateItemCollection class, with data from
        ///     Redis
        /// </summary>
        /// <param name="redisHashData">A dictionary of keys to byte array values from Redis. The
        ///     byte array should be the output of the SerializeOne method from the configured 
        ///     IRedisSerializer in RedisSerializationConfig</param>
        public RedisSessionStateItemCollection(Dictionary<string, byte[]> redisHashData)
            : base()
        {
            this.SerializedRawData = new Dictionary<string, string>();
            if (redisHashData != null)
            {
                int byteDataTotal = 0;
                foreach (var sessDataEntry in redisHashData)
                {
                    this.SerializedRawData.Add(
                        sessDataEntry.Key,
                        Encoding.UTF8.GetString(sessDataEntry.Value));
                    base.BaseAdd(sessDataEntry.Key, new NotYetDeserializedPlaceholderValue());

                    byteDataTotal += sessDataEntry.Value.Length;
                }
                
            }
            this.ChangedKeysDict = new Dictionary<string, ActionAndValue>();
            this.AccessedKeys = new HashSet<string>();
        }

        #region ISessionStateItemCollection Members

        /// <summary>
        /// Clears the Session of all values, deleting all keys
        /// </summary>
        public void Clear()
        {
            // since we stuff base with all of the keys in the constructor, this bool is correct even if we havent deserialized yet
            if (base.BaseHasKeys())
            {
                this.Dirty = true;

                // insert all keys as Del actions into change dict
                foreach (string key in base.Keys)
                {
                    this.AddOrSetItemAction(key, new DeleteValue());
                }
            }

            // clear internal raw values as well
            SerializedRawData.Clear();
            base.BaseClear();
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
                // since we stuff in constructor, this is correct
                return base.Keys;
            }
        }

        /// <summary>
        /// Removes a key from the Session
        /// </summary>
        /// <param name="name">The Session key to remove</param>
        public void Remove(string name)
        {
            if (base.BaseGet(name) != null)
            {
                this.Dirty = true;

                this.AddOrSetItemAction(name, new DeleteValue());
            }

            if (SerializedRawData != null && SerializedRawData.ContainsKey(name))
            {
                SerializedRawData.Remove(name);
            }
            base.BaseRemove(name);
        }

        /// <summary>
        /// Removes an item from the Session based on its position
        /// </summary>
        /// <param name="index">The index of the object to remove</param>
        public void RemoveAt(int index)
        {
            if (index < base.Keys.Count)
            {
                if (base.BaseGet(index) != null)
                {
                    this.Dirty = true;

                    this.AddOrSetItemAction(base.Keys[index], new DeleteValue());
                }

                if (SerializedRawData != null && SerializedRawData.ContainsKey(base.Keys[index]))
                {
                    SerializedRawData.Remove(base.Keys[index]);
                }
                base.BaseRemoveAt(index);
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
                return MemoizedDeserializeGet(base.Keys[index]);
            }
            set
            {
                if (index < base.Keys.Count)
                {
                    BaseSetWithDeserialize(base.Keys[index], value);
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
                return MemoizedDeserializeGet(name);
            }
            set
            {
                BaseSetWithDeserialize(name, value);
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

        #endregion

        /// <summary>
        /// Returns a dictionary of keys and values which have been accessed (deserialized) during the 
        ///     current request.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, object> GetAccessedReferenceTypes()
        {
            Dictionary<string, object> usedObjs = new Dictionary<string, object>(this.AccessedKeys.Count);

            foreach (var key in this.AccessedKeys)
            {
                object val = base.BaseGet(key);
                // we don't want values that were not ever deserialized, and we don't want values that
                //      are value-types, meaning they cannot be easily modified by reference. The reason 
                //      is that any modifications via direct assignment to the collection are already
                //      handled by the change dictionary, and this method is used to get all collection
                //      items that were possibly changed. Unfortunately, for version 1 of 
                //      RedisSessionProvider, that may be overly ambitious so we will just return all
                //      accessed keys and values for now.

                // if it's null, it means that it was either set to null so it should be in the changed
                //      objects dictionary as a delete, or that it was accessed without being
                //      initialized, in which case we don't need to return it because it shouldn't be
                //      persisted to redis anyways
                if (val != null)
                {
                    if (!(val is NotYetDeserializedPlaceholderValue))
                    {
                        usedObjs.Add(key, val);
                    }
                }
            }

            return usedObjs;
        }

        /// <summary>
        /// A key has been set, or removed. We need to store that so we know what we don't need to 
        ///     dirty-check at the end of the current request.
        /// </summary>
        /// <param name="key">The Session key name that has been changed</param>
        /// <param name="itemAct">The type of change applied to the key, either DeleteValue or 
        ///     SetValue</param>
        protected void AddOrSetItemAction(string key, ActionAndValue itemAct)
        {
            if (this.ChangedKeysDict.ContainsKey(key))
            {
                this.ChangedKeysDict[key] = itemAct;
            }
            else
            {
                this.ChangedKeysDict.Add(key, itemAct);
            }
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

            object storedObj = base.BaseGet(key);
            bool failedDeserialize = false;
            if (storedObj is NotYetDeserializedPlaceholderValue)
            {
                try
                {
                    storedObj = RedisSerializationConfig.SessionDataSerializer.DeserializeOne(SerializedRawData[key]);

                    // if we can't deserialize, storedObj will still be the placeholder and in that case it's
                    //      as if the DeserializeOne method error'ed, so mark it as failed to deserialize and clear
                    //      session
                    if (storedObj is NotYetDeserializedPlaceholderValue)
                    {
                        failedDeserialize = true;
                        base.BaseSet(key, null);
                    }
                    else
                    {
                        base.BaseSet(key, storedObj);
                    }
                }
                catch (Exception e)
                {
                    failedDeserialize = true;

                    if (RedisSerializationConfig.SerializerExceptionLoggingDel != null)
                    {
                        RedisSerializationConfig.SerializerExceptionLoggingDel(e);
                    }
                }

                if (failedDeserialize)
                {
                    // uh oh, just clear everything. We could just remove this one key, but
                    //      safer to just nuke the whole session and force user to autologin 
                    //      or log in again. Especially since right now we store the Dustin-era
                    //      user object in the session, as well as the DHStructure.DomainModel user
                    //      object and if either is present the user is still considered logged in
                    //      which could cause issues if one is removed due to failure to deserialize
                    //      but not the other, and they would be in an undefined logged in state.
                    this.Clear();

                    storedObj = null;
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
            // check if new value equal to old, if so, we need to add it to the change dictionary. Note
            //      that this probably should be a .Equals check, but that requires more testing
            if (this.MemoizedDeserializeGet(key) != value)
            {
                // if we are trying to set something to null, consider it a delete
                if (value == null)
                {
                    this.AddOrSetItemAction(key, new DeleteValue());
                    base.BaseRemove(key);
                }
                else
                {
                    this.AddOrSetItemAction(key, new SetValue() { Value = value });

                    // update to new value
                    base.BaseSet(key, value);
                }
            }
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