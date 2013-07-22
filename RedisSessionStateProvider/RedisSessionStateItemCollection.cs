namespace Welegan.RedisSessionStoreProvider
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
    using System.Web;
    using System.Web.SessionState;

    using BookSleeve;
    using ServiceStack.Text;

    public class RedisSessionStateItemCollection : NameObjectCollectionBase, ISessionStateItemCollection, ICollection, IEnumerable
    {
        public string RedisHashId { get; set; }

        public RedisConnectionWrapper RedisConnWrap { get; set; }
        
        /// <summary>
        /// Gets or sets a dictionary of keys that have changed during the course of this session, and what
        ///     the changed action was (either del or set in redis terms). We use this as opposed to two lists
        ///     to allow overwriting of actions based on key e.g. modifying then deleting would end up being 
        ///     just a delete in this scheme. TODO: add logic for add-then-delete resulting in nothing
        /// </summary>
        public Dictionary<string, ActionAndValue> ChangedKeysDict { get; set; }

        public RedisSessionStateItemCollection()
            : this(null)
        {
        }

        public RedisSessionStateItemCollection(Dictionary<string, byte[]> redisHashData)
            : base()
        {
            if (redisHashData != null && redisHashData.Count > 0)
            {
                foreach (KeyValuePair<string, object> itemData in RedisSessionStateSerializer.Deserialize(redisHashData))
                {
                    base.BaseAdd(itemData.Key, itemData.Value);
                }
            }

            ChangedKeysDict = new Dictionary<string, ActionAndValue>();
        }

        #region ISessionStateItemCollection Members

        public void Clear()
        {
            if(base.BaseHasKeys())
            {
                this.Dirty = true;

                // insert all keys as Del actions into change dict
                foreach (string key in base.Keys)
                {
                    this.AddOrSetItemAction(key, new DeleteValue());
                }
            }

            base.BaseClear();
        }

        public bool Dirty { get; set; }

        public NameObjectCollectionBase.KeysCollection Keys
        {
            get { return base.Keys; }
        }

        public void Remove(string name)
        {
            if (base.BaseGet(name) != null)
            {
                this.Dirty = true;

                this.AddOrSetItemAction(name, new DeleteValue());
            }

            base.BaseRemove(name);
        }

        public void RemoveAt(int index)
        {
            if (base.BaseGet(index) != null)
            {
                this.Dirty = true;

                this.AddOrSetItemAction(base.Keys[index], new DeleteValue());
            }

            base.BaseRemoveAt(index);
        }

        public object this[int index]
        {
            get
            {
                return base.BaseGet(index);
            }
            set
            {
                this.Dirty = true;

                if (base.BaseGet(base.Keys[index]) != value)
                {
                    this.AddOrSetItemAction(base.Keys[index], new SetValue() { Value = value });
                }

                base.BaseSet(index, value);
            }
        }

        public object this[string name]
        {
            get
            {
                return base.BaseGet(name);
            }
            set
            {
                this.Dirty = true;

                if (base.BaseGet(name) != value)
                {
                    this.AddOrSetItemAction(name, new SetValue() { Value = value });
                }

                base.BaseSet(name, value);
            }
        }

        #endregion

        #region ICollection Members

        public void CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
        }
        
        public bool IsSynchronized
        {
            get { return false; }
        }

        public object SyncRoot
        {
            get { throw new NotImplementedException(); }
        }

        #endregion
        
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

        public abstract class ActionAndValue
        {
            public object Value { get; set; }
        }

        public class DeleteValue : ActionAndValue
        {
        }

        public class SetValue : ActionAndValue
        {
        }
    }
}