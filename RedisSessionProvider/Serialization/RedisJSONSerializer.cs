namespace RedisSessionProvider.Serialization
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Data;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Web;
    using System.Web.SessionState;

    using BookSleeve;
    using Newtonsoft.Json;
    using RedisSessionProvider.Config;

    /// <summary>
    /// This serializer encodes/decodes Session values into/from JSON for Redis persistence, using
    ///     the Json.NET library. The only exceptions are for ADO.NET types (DataTable and DataSet),
    ///     which revert to using XML serialization.
    /// </summary>
    public class RedisJSONSerializer : IRedisSerializer
    {
        /// <summary>
        /// Format string used to write type information into the Redis entry before the JSON data
        /// </summary>
        protected string typeInfoPattern = "|!a_{0}_a!|";
        /// <summary>
        /// Regular expression used to extract type information from Redis entry
        /// </summary>
        protected Regex typeInfoReg = new Regex(@"\|\!a_(.*)_a\!\|", RegexOptions.Compiled);

        // ADO.NET serialization is difficult because of the recursive nature of the datastructures. In order
        //      to support DataTable and DataSet serialization, we keep track of their type names and if a
        //      Session value is one of these, we use the standard XML serializer for it instead.
        protected string DataTableTypeSerialized = typeof(DataTable).FullName;
        protected string DataSetTypeSerialized = typeof(DataSet).FullName;

        /// <summary>
        /// Deserializes the entire input of JSON-serialized values into a list of key-object pairs. This
        ///     method is not normally used in RedisSessionProvider, here only for debugging purposes.
        /// </summary>
        /// <param name="redisHashDataRaw">A dictionary of Redis Hash contents, with key being Redis key 
        ///     corresponding to Session property and value being a JSON encoded string with type info
        ///     of the original object</param>
        /// <returns>A list of key-object pairs of each entry in the input dictionary</returns>
        public virtual List<KeyValuePair<string, object>> Deserialize(Dictionary<string, string> redisHashDataRaw)
        {
            // process: for each key and value in raw data, convert byte[] field to json string and extract its type property
            //      then deserialize that type and add 

            List<KeyValuePair<string, object>> deserializedList = new List<KeyValuePair<string, object>>();

            if (redisHashDataRaw != null)
            {
                foreach (var keyFieldPair in redisHashDataRaw)
                {
                    try
                    {
                        object deserializedObj = this.DeserializeOne(keyFieldPair.Value);
                        if (deserializedObj != null)
                        {
                            deserializedList.Add(new KeyValuePair<string, object>(
                                keyFieldPair.Key,
                                deserializedObj));
                        }
                    }
                    catch (Exception e)
                    {
                        if(RedisSerializationConfig.SerializerExceptionLoggingDel != null)
                        {
                            RedisSerializationConfig.SerializerExceptionLoggingDel(e);
                        }
                    }
                }
            }

            return deserializedList;
        }

        /// <summary>
        /// Deserializes a byte array containing a utf8 encoded string with type and object information
        ///     back to the original object
        /// </summary>
        /// <param name="objRaw">A byte array containing type and object data, a utf8 encoded string</param>
        /// <returns>The original object, hopefully</returns>
        public virtual object DeserializeOne(byte[] objRaw)
        {
            return this.DeserializeOne(Encoding.UTF8.GetString(objRaw));
        }

        /// <summary>
        /// Deserializes a string containing type and object information back into the original object
        /// </summary>
        /// <param name="objRaw">A string containing type info and JSON object data</param>
        /// <returns>The original object</returns>
        public virtual object DeserializeOne(string objRaw)
        {
            Match fieldTypeMatch = this.typeInfoReg.Match(objRaw);

            if (fieldTypeMatch.Success)
            {
                // if we are deserializing a datatable, use this
                if (fieldTypeMatch.Groups[1].Value == DataTableTypeSerialized)
                {
                    DataSet desDtWrapper = new DataSet();
                    using (StringReader rdr = new StringReader(objRaw.Substring(fieldTypeMatch.Length)))
                    {
                        desDtWrapper.ReadXml(rdr);
                    }
                    return desDtWrapper.Tables[0];

                }
                // or if we are doing a dataset
                else if (fieldTypeMatch.Groups[1].Value == DataSetTypeSerialized)
                {
                    DataSet dsOut = new DataSet();
                    using (StringReader rdr = new StringReader(objRaw.Substring(fieldTypeMatch.Length)))
                    {
                        dsOut.ReadXml(rdr);
                    }
                    return dsOut;
                }
                // or for most things that are sane, use this
                else
                {
                    Type typeData = JsonConvert.DeserializeObject<Type>(fieldTypeMatch.Groups[1].Value);

                    return JsonConvert.DeserializeObject(
                        objRaw.Substring(fieldTypeMatch.Length),
                        typeData);
                }
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// See the description of this method in IRedisSerializer for a description of the logic. Given
        ///     a set of inputs from the Session, returns a dictionary of key-byte array pairs of
        ///     Session properties that have changed while serving the current request.
        /// </summary>
        /// <param name="confirmedChangedObjects">Session keys and values that have definitely changed
        ///     during the current request</param>
        /// <param name="allObjects">All Session keys and values that were accessed during the current
        ///     request</param>
        /// <param name="allObjectsOriginalState">All keys and their serialized values as retrieved from
        ///     Redis at the beginning of the current request</param>
        /// <param name="deletedObjects">All keys that were deleted during the current request</param>
        /// <returns>A dictionary of all keys and values that have changed during the current request</returns>
        public virtual Dictionary<string, byte[]> SerializeWithDirtyChecking(
            Dictionary<string, object> confirmedChangedObjects,
            Dictionary<string, object> allObjects,
            Dictionary<string, string> allObjectsOriginalState,
            List<string> deletedObjects)
        {
            Dictionary<string, byte[]> changedObjsDict;
            // first take care of the items that have definitely been changed this session by Session BaseSet, if any
            if (confirmedChangedObjects != null && confirmedChangedObjects.Count > 0)
            {
                changedObjsDict = this.Serialize(confirmedChangedObjects);
            }
            else
            {
                changedObjsDict = new Dictionary<string, byte[]>();
            }

            // now take care of looking through objects not in confirmed-changed, but which may be different because
            //      they were modified via a reference
            if (allObjects != null && allObjects.Count > 0)
            {
                foreach (var maybeChangedKeyVal in allObjects)
                {
                    try
                    {
                        // we only care if we are not deleting the key to begin with, deletes happen outside of this method
                        //      in the RedisSessionStateStoreProvider.SetAndReleaseItemExclusive
                        if (deletedObjects == null || !deletedObjects.Contains(maybeChangedKeyVal.Key))
                        {
                            // we only want to dirty-check the non-obviously-changed items
                            if (!confirmedChangedObjects.ContainsKey(maybeChangedKeyVal.Key))
                            {
                                // if the original state of the session from redis has this field
                                if (allObjectsOriginalState != null && allObjectsOriginalState.ContainsKey(maybeChangedKeyVal.Key))
                                {
                                    string unknownObjectSerializedForm = this.SerializeOne(
                                        maybeChangedKeyVal.Key,
                                        maybeChangedKeyVal.Value);

                                    // and the field has changed from its original state
                                    if (allObjectsOriginalState[maybeChangedKeyVal.Key] != unknownObjectSerializedForm)
                                    {
                                        // add it to the change dictionary
                                        changedObjsDict.Add(maybeChangedKeyVal.Key, Encoding.UTF8.GetBytes(unknownObjectSerializedForm));
                                    }
                                }
                                else if (maybeChangedKeyVal.Value != null)
                                {
                                    // we have something in the current session, it was not in the original session values from redis and
                                    //      it was not in the explicitly changed object dictionary meaning the code that marks it as a new
                                    //      addition failed to add it there, so let's take care of the add.

                                    string unknownObjectSerializedForm = this.SerializeOne(
                                        maybeChangedKeyVal.Key,
                                        maybeChangedKeyVal.Value);

                                    changedObjsDict.Add(maybeChangedKeyVal.Key, Encoding.UTF8.GetBytes(unknownObjectSerializedForm));
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
                    }
                }
            }

            return changedObjsDict;
        }

        /// <summary>
        /// Serializes the entire Session's values into a dictionary of key-byte array pairs that correspond
        ///     to the Session keys and their utf-8 encoded type and JSON data strings. This method is not
        ///     normally used by RedisSessionProvider but may be helpful for debugging.
        /// </summary>
        /// <param name="sessionItems">A dictionary containing all of the Session's keys and values</param>
        /// <returns>The serialized dictionary that would be sent to Redis</returns>
        public virtual Dictionary<string, byte[]> Serialize(Dictionary<string, object> sessionItems)
        {
            Dictionary<string, byte[]> serializedItems = new Dictionary<string, byte[]>();

            foreach (var changedItem in sessionItems)
            {
                try
                {
                    serializedItems.Add(
                        changedItem.Key,
                        Encoding.UTF8.GetBytes(
                            this.SerializeOne(changedItem.Key, changedItem.Value)));
                }
                catch (Exception e)
                {
                    if (RedisSerializationConfig.SerializerExceptionLoggingDel != null)
                    {
                        RedisSerializationConfig.SerializerExceptionLoggingDel(e);
                    }
                }
            }

            return serializedItems;
        }
        
        /// <summary>
        /// Serializes one key and object into a string containing type and JSON data
        /// </summary>
        /// <param name="key">The key of the object in the Session, does not factor into the
        ///     output except in instances of ADO.NET serialization</param>
        /// <param name="origObj">The value of the Session property</param>
        /// <returns>A string containing type information and JSON data about the object, or XML data
        ///     in the case of serialiaing ADO.NET objects. Don't store ADO.NET objects in Session if 
        ///     you can help it, but if you do we don't want to mess up your Session</returns>
        public virtual string SerializeOne(string key, object origObj)
        {
            // ServiceStack JSONSerializer incapable of serializing datatables... not that we should be storing
            //      any but Dustin code will occasionally
            if (origObj is DataTable)
            {
                DataTable dtToStore = origObj as DataTable;
                // in order to write to xml the TableName property must be set
                if (string.IsNullOrEmpty(dtToStore.TableName))
                {
                    dtToStore.TableName = key + "-session-datatable";
                }
                StringBuilder xmlSer = new StringBuilder();
                using (StringWriter xmlSw = new StringWriter(xmlSer))
                {
                    dtToStore.WriteXml(xmlSw, XmlWriteMode.WriteSchema);
                }

                return string.Format(typeInfoPattern, DataTableTypeSerialized) + xmlSer.ToString();
            }
            // the same is true of DataSet as DataTable
            else if (origObj is DataSet)
            {
                StringBuilder xmlSer = new StringBuilder();
                using (StringWriter xmlSw = new StringWriter(xmlSer))
                {
                    DataSet dsToStore = origObj as DataSet;
                    dsToStore.WriteXml(xmlSw, XmlWriteMode.WriteSchema);
                }

                return string.Format(typeInfoPattern, DataSetTypeSerialized) + xmlSer.ToString();
            }
            else
            {
                Type objType = origObj.GetType();
                string objInfo = JsonConvert.SerializeObject(origObj);
                string typeInfo = JsonConvert.SerializeObject(objType);

                return string.Format(this.typeInfoPattern, typeInfo) + objInfo;
            }
        }
    }
}