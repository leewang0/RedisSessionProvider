namespace SampleUsageMVCApp.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Web;
    using System.Web.Mvc;

    using Models;
    using Newtonsoft.Json;

    public class HomeController : Controller
    {
        //
        // GET: /Home/
        public ActionResult Index()
        {
            this.ResetCounts();

            return View("TestView", this.GetModel());
        }

        public ActionResult IncrementCounts()
        {
            TestPageModel mdl = this.GetModel();

            return Content(
                string.Format(
                    "{{ \"count\": {0}, \"safeCount\": {1}, \"listCount\": {2} }}",
                    mdl.Count,
                    mdl.SafeCount,
                    mdl.ListCount), 
                "application/json");
        }

        private void ResetCounts()
        {
            this.Session["count"] = 0;
            this.Session["safeCount"] = new SafelyIncrementableIntHolder();
            this.Session["listCount"] = new LockedList();
        }

        private TestPageModel GetModel()
        {
            this.Session["count"] = (int)this.Session["count"] + 1;


            SafelyIncrementableIntHolder safeInt = this.Session["safeCount"] as SafelyIncrementableIntHolder;
            int safeResult = safeInt.Increment();

            LockedList sessList = this.Session["listCount"] as LockedList;
            
            sessList.Add(0);

            
            TestPageModel mdl = new TestPageModel
            {
                Count = (int)this.Session["count"],
                SafeCount = safeResult,
                ListCount = sessList.Count
            };

            return mdl;
        }

        [JsonConverter(typeof(CustomSafeIntConverter))]
        class SafelyIncrementableIntHolder
        {
            public SafelyIncrementableIntHolder()
                : this(0)
            {
            }

            public SafelyIncrementableIntHolder(int startVal)
            {
                this.internalVal = startVal;
            }

            public int Increment()
            {
                return Interlocked.Increment(ref this.internalVal);
            }

            private int internalVal;

            public int Value 
            { 
                get 
                {
                    return internalVal;
                }
            }
        }

        public class CustomSafeIntConverter : JsonConverter
        {
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                SafelyIncrementableIntHolder result = null;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.Integer)
                    {
                        result = new SafelyIncrementableIntHolder((int)(long)reader.Value);
                    }
                }

                return result;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                SafelyIncrementableIntHolder safeInt = value as SafelyIncrementableIntHolder;
                if(safeInt != null)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("value");
                    writer.WriteValue(safeInt.Value);
                    writer.WriteEndObject();
                }
            }
            
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(SafelyIncrementableIntHolder);
            }
        }


        class LockedList : List<byte>
        {
            // we don't care that this object is private and readonly, since it has a
            //      default value then the empty constructor that json.net calls
            //      when deserializing will cause this new object to be made each time
            //      or if multiple threads are currently using one session, they will all
            //      share one instance of LockedList, so they should have a 
            //      session-specific lockedObj
            public readonly object lockedObj = new object();

            public new void Add(byte val)
            {
                lock(lockedObj)
                {
                    base.Add(val);
                }
            }
        }
    }
}
