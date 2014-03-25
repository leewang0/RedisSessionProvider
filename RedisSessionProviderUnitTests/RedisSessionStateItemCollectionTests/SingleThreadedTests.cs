namespace RedisSessionProviderUnitTests.RedisSessionStateItemCollectionTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using NUnit.Framework;
    using Moq;
    using RedisSessionProvider;
    using RedisSessionProvider.Serialization;
    
    [TestFixture]
    public class SingleThreadedTests
    {
        private RedisSessionStateItemCollection items;

        private RedisJSONSerializer srsly;
        
        [SetUp]
        public void OnBeforeTestExecute()
        {
            srsly = new RedisJSONSerializer();

            this.items = new RedisSessionStateItemCollection(
                new Dictionary<string, byte[]> { 
                    { "a", Encoding.UTF8.GetBytes(srsly.SerializeOne("a", "x")) },
                    { "b", Encoding.UTF8.GetBytes(srsly.SerializeOne("b", "y")) },
                    { "c", Encoding.UTF8.GetBytes(srsly.SerializeOne("c", "z")) }
                },
                "fakeCollection");
        }

        [Test]
        public void RedisItemsCollectionConstructorTest()
        {
            Assert.AreEqual("x", (string)this.items["a"]);
            Assert.AreEqual("y", (string)this.items["b"]);
            Assert.AreEqual("z", (string)this.items["c"]);
        }

        [Test]
        public void RedisItemsCollectionAddTest()
        {
            this.items["something"] = "a thing";
            this.items["foo"] = "bar";
            this.items["lucas"] = "uses venmo";

            Assert.AreEqual("a thing", this.items["something"]);
            Assert.AreEqual("bar", this.items["foo"]);
            Assert.AreEqual("uses venmo", this.items["lucas"]);

            Assert.Throws<NotImplementedException>(
                () => {
                    var x = this.items[3];
                });
        }

        [Test]
        public void RedisItemsCollectionRemoveTest()
        {
            Assert.AreEqual("x", (string)this.items["a"]);
            Assert.AreEqual("y", (string)this.items["b"]);
            Assert.AreEqual("z", (string)this.items["c"]);

            this.items.Remove("a");

            Assert.IsNull(this.items["a"]);

            this.items["something"] = "a thing";
            this.items["foo"] = "bar";
            this.items["lucas"] = "uses venmo";
            
            Assert.AreEqual("y", (string)this.items["b"]);
            Assert.AreEqual("z", (string)this.items["c"]);
            Assert.AreEqual("a thing", this.items["something"]);
            Assert.AreEqual("bar", this.items["foo"]);
            Assert.AreEqual("uses venmo", this.items["lucas"]);

            this.items.Remove("foo");

            Assert.IsNull(this.items["foo"]);

            Assert.AreEqual("y", (string)this.items["b"]);
            Assert.AreEqual("z", (string)this.items["c"]);
            Assert.AreEqual("a thing", this.items["something"]);
            Assert.AreEqual("uses venmo", this.items["lucas"]);

            Assert.AreEqual(4, this.items.Count);

            this.items["empty"] = null;

            Assert.AreEqual(4, this.items.Count);
        }

        [Test]
        public void RedisItemsEnumeratorTest()
        {
            foreach(KeyValuePair<string, object> val in this.items)
            {
                Assert.Contains(val.Value, new string[] { "x", "y", "z" });
            }

            this.items["something"] = "a thing";
            this.items["foo"] = "bar";
            this.items["lucas"] = "uses venmo";

            foreach (KeyValuePair<string, object> val in this.items)
            {
                Assert.Contains(val.Value, new string[] { "x", "y", "z", "a thing", "bar", "uses venmo" });
            }
        }

        [Test]
        public void RedisItemsChangedObjsEnumeratorTest()
        {
            List<KeyValuePair<string, string>> changedObjs = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> val in this.items.GetChangedObjectsEnumerator())
            {
                changedObjs.Add(val);
            }

            Assert.AreEqual(0, changedObjs.Count);

            this.items["something"] = "a thing";
            this.items["foo"] = "bar";
            this.items["lucas"] = "uses venmo";

            foreach (KeyValuePair<string, string> val in this.items.GetChangedObjectsEnumerator())
            {
                changedObjs.Add(val);
            }

            Assert.AreEqual(3, changedObjs.Count);
            
            foreach(KeyValuePair<string, string> val in changedObjs)
            {
                Assert.Contains(
                    this.srsly.DeserializeOne(val.Value), 
                    new string[] { "a thing", "bar", "uses venmo" });
            }

            this.items["a"] = "not x";

            changedObjs.Clear();
            foreach (KeyValuePair<string, string> val in this.items.GetChangedObjectsEnumerator())
            {
                changedObjs.Add(val);
            }

            // since we got all the new changed objects in the previous call to GetChangedObjectsEnumerator,
            //      this call should only return "a", "not x"
            Assert.AreEqual(1, changedObjs.Count);
            Assert.AreEqual(changedObjs[0].Key, "a");
            Assert.AreEqual( 
                this.srsly.DeserializeOne(changedObjs[0].Value), 
                "not x");
        }

        [Test]
        public void AddRemoveAndGetEnumeratorTest()
        {
            // test that nothing has changed since the stuff we added was in the constructor,
            //      so in real usage that would be coming from Redis and thus be the default state
            int numChanged = 0;
            foreach (KeyValuePair<string, string> val in this.items.GetChangedObjectsEnumerator())
            {
                numChanged++;
            }

            Assert.AreEqual(0, numChanged);

            // test assigning a value to something new
            this.items["a"] = "not x";

            numChanged = 0;
            foreach (KeyValuePair<string, string> val in this.items.GetChangedObjectsEnumerator())
            {
                numChanged++;
                Assert.AreEqual(
                    val.Key, 
                    "a");
                Assert.AreEqual(
                    this.srsly.DeserializeOne(val.Value), 
                    "not x");
            }

            Assert.AreEqual(1, numChanged);

            // test assigning a value to something else then assigning it back. In such a case,
            //      we don't want it to come back from the enumerator because it has not really
            //      changed from the initial state so why bother redis about it?
            this.items["a"] = "x";
            this.items["a"] = "not x";

            numChanged = 0;
            foreach (KeyValuePair<string, string> val in this.items.GetChangedObjectsEnumerator())
            {
                numChanged++;
            }

            Assert.AreEqual(0, numChanged);

            // test creating a new value and then removing it will do nothing
            this.items["new"] = "m";
            this.items["new"] = null;

            numChanged = 0;
            foreach (KeyValuePair<string, string> val in this.items.GetChangedObjectsEnumerator())
            {
                numChanged++;
            }

            Assert.AreEqual(0, numChanged);

            // test creating a new value, then getting the changed objects enumerator (which resets
            //      the initial state) then removing the new value then getting the changed objects
            //      again results in two enumerators that return 1 element each

            this.items["new"] = "m";
            numChanged = 0;
            foreach (KeyValuePair<string, string> val in this.items.GetChangedObjectsEnumerator())
            {
                numChanged++;
                Assert.AreEqual(
                    val.Key, 
                    "new");
                Assert.AreEqual(
                    this.srsly.DeserializeOne(val.Value), 
                    "m");
            }

            Assert.AreEqual(1, numChanged);

            this.items["new"] = null;

            numChanged = 0;
            foreach (KeyValuePair<string, string> val in this.items.GetChangedObjectsEnumerator())
            {
                numChanged++;
                Assert.AreEqual(
                    val.Key, 
                    "new");
                Assert.IsNull(val.Value);
            }

            Assert.AreEqual(1, numChanged);

            

        }

        [Test]
        public void AddRemoveAndDirtyCheckReferenceTypesTest()
        {
            // add a list to session
            this.items["refType"] = new List<string>();
            // get a reference to it
            List<string> myList = this.items["refType"] as List<string>;

            // alternatively, make a list
            List<int> myOtherList = new List<int>();
            // add it to session
            this.items["otherRefType"] = myOtherList;

            bool listCameBack = false;
            bool otherListCameBack = false;
            // test that these come back from the enumerator
            foreach(KeyValuePair<string, string> changed in this.items.GetChangedObjectsEnumerator())
            {
                if(changed.Key == "refType" &&
                    changed.Value == srsly.SerializeOne(changed.Key, myList))
                {
                    listCameBack = true;
                }
                else if (changed.Key == "otherRefType" &&
                    changed.Value == srsly.SerializeOne(changed.Key, myOtherList))
                {
                    otherListCameBack = true;
                }
            }

            Assert.IsTrue(listCameBack, "failed to return string list");
            Assert.IsTrue(otherListCameBack, "failed to return int list");

            // test that if we get the changed objects again, they won't come back
            listCameBack = false;
            otherListCameBack = false;

            foreach (KeyValuePair<string, string> changed in this.items.GetChangedObjectsEnumerator())
            {
                if(changed.Key == "refType")
                {
                    listCameBack = true;
                }
                else if(changed.Key == "otherRefType")
                {
                    otherListCameBack = true;
                }
            }

            Assert.IsFalse(listCameBack, "incorrectly returned string list");
            Assert.IsFalse(otherListCameBack, "incorrectly returned int list");


            // now let's modify a list
            myOtherList.Add(1);

            otherListCameBack = false;
            foreach (KeyValuePair<string, string> changed in this.items.GetChangedObjectsEnumerator())
            {
                if(changed.Key == "otherRefType" && 
                    changed.Value == srsly.SerializeOne(changed.Key, myOtherList))
                {
                    otherListCameBack = true;
                }
            }

            Assert.IsTrue(otherListCameBack, "list that was modified not returned when it should have been");

            // ok, let's see if modifying the list, then undoing that modification results
            //      in it being returned (it shouldn't)

            myList.Add("a");
            myList.Clear();

            listCameBack = false;
            foreach (KeyValuePair<string, string> changed in this.items.GetChangedObjectsEnumerator())
            {
                if (changed.Key == "refType" && 
                    changed.Value == srsly.SerializeOne(changed.Key, myList))
                {
                    listCameBack = true;
                }
            }

            Assert.IsFalse(listCameBack, "list that was modified then reset should not come back");
        }
    }
}
