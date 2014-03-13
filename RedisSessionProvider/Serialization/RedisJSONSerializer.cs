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
        /// Converts the input dictionary into a dictionary of key to serialized string values
        /// </summary>
        /// <param name="sessionItems">A dictionary some or all of the Session's keys and values</param>
        /// <returns>The serialized version of the input dictionary members</returns>
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