namespace RedisSessionProviderUnitTests.RedisSessionStateItemCollectionTests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using NUnit.Framework;
    using Moq;
    using RedisSessionProvider;
    using RedisSessionProvider.Serialization;

    [TestFixture]
    public class MultiThreadedTests
    {
        private RedisSessionStateItemCollection items;

        private IRedisSerializer srsly;

        [SetUp]
        public void OnBeforeTestExecute()
        {
            this.items = new RedisSessionStateItemCollection();
            this.srsly = new RedisJSONSerializer();
        }

        [Test]
        public void MultiAddAndRemove()
        {
            List<Action> AddRemoveOrder = new List<Action> { 
                () => { this.items["a"] = 1; },
                () => { this.items["b"] = 1; },
                () => { this.items["c"] = 1; },
                () => { this.items["d"] = 1; },
                () => { this.items["e"] = 1; },
                () => { this.items["f"] = 1; },
                () => { this.items["g"] = 1; },

                () => { this.items.Remove("a"); },
                () => { this.items.Remove("b"); },
                () => { this.items.Remove("c"); },
                () => { this.items.Remove("d"); },
                () => { this.items.Remove("e"); },
                () => { this.items.Remove("f"); },
                () => { this.items.Remove("g"); },

                () => { this.items["a"] = 2; },
                () => { this.items["b"] = 2; },
                () => { this.items["c"] = 2; },
                () => { this.items["d"] = 2; },
                () => { this.items["e"] = 2; },
                () => { this.items["f"] = 2; },
                () => { this.items["g"] = 2; },

                () => { this.items["g"] = 1; },
                () => { this.items["f"] = 1; },
                () => { this.items["e"] = 1; },
                () => { this.items["d"] = 1; },
                () => { this.items["c"] = 1; },
                () => { this.items["b"] = 1; },
                () => { this.items["a"] = 1; },

                () => { this.items["g"] = null; },
                () => { this.items["f"] = null; },
                () => { this.items["e"] = null; },
                () => { this.items["d"] = null; },
                () => { this.items["c"] = null; },
                () => { this.items["b"] = null; },
                () => { this.items["a"] = null; },

                () => { this.items["g"] = 2; },
                () => { this.items["f"] = 2; },
                () => { this.items["e"] = 2; },
                () => { this.items["d"] = 2; },
                () => { this.items["c"] = 2; },
                () => { this.items["b"] = 2; },
                () => { this.items["a"] = 2; }
            };

            int numIterations = 1000000;

            Parallel.For(0, 4, (int index) => {
                int offset = index * 10;
                for(int i = 0; i < numIterations; i++)
                {
                    // proceed thru the list of actions starting at an offset
                    int actionIndex = (i + offset) % AddRemoveOrder.Count;
                    // if odd index, go in reverse order
                    if(index % 2 == 1)
                    {
                        actionIndex = Math.Abs((i - offset)) % AddRemoveOrder.Count;
                    }

                    AddRemoveOrder[actionIndex]();
                }
            });
                        
            // note that we do not have a deterministic answer for what the value of each index is.
            //      but assuming there have been no exceptions, the values will be one of the 3 
            //      possible values we set each key to, in the action list above.

            Assert.Contains(this.items["a"], new object[] { 1, 2, null });
            Debug.WriteLine("a value: " + this.items["a"]);
            Assert.Contains(this.items["b"], new object[] { 1, 2, null });
            Debug.WriteLine("b value: " + this.items["b"]);
            Assert.Contains(this.items["c"], new object[] { 1, 2, null });
            Debug.WriteLine("c value: " + this.items["c"]);
            Assert.Contains(this.items["d"], new object[] { 1, 2, null });
            Debug.WriteLine("d value: " + this.items["d"]);
            Assert.Contains(this.items["e"], new object[] { 1, 2, null });
            Debug.WriteLine("e value: " + this.items["e"]);
            Assert.Contains(this.items["f"], new object[] { 1, 2, null });
            Debug.WriteLine("f value: " + this.items["f"]);
            Assert.Contains(this.items["g"], new object[] { 1, 2, null });
            Debug.WriteLine("g value: " + this.items["g"]);
        }

        [Test]
        public void MultiIterator()
        {
            List<Action> AddRemoveOrder = new List<Action> { 
                () => { this.items["a"] = 1; },
                () => { this.items["b"] = 1; },
                () => { this.items["c"] = 1; },
                () => { this.items["d"] = 1; },
                () => { this.items["e"] = 1; },
                () => { this.items["f"] = 1; },
                () => { this.items["g"] = 1; },

                () => { this.items.Remove("a"); },
                () => { this.items.Remove("b"); },
                () => { this.items.Remove("c"); },
                () => { this.items.Remove("d"); },
                () => { this.items.Remove("e"); },
                () => { this.items.Remove("f"); },
                () => { this.items.Remove("g"); },

                () => { this.items["a"] = 2; },
                () => { this.items["b"] = 2; },
                () => { this.items["c"] = 2; },
                () => { this.items["d"] = 2; },
                () => { this.items["e"] = 2; },
                () => { this.items["f"] = 2; },
                () => { this.items["g"] = 2; },

                () => { this.items["g"] = 1; },
                () => { this.items["f"] = 1; },
                () => { this.items["e"] = 1; },
                () => { this.items["d"] = 1; },
                () => { this.items["c"] = 1; },
                () => { this.items["b"] = 1; },
                () => { this.items["a"] = 1; },

                () => { this.items["g"] = null; },
                () => { this.items["f"] = null; },
                () => { this.items["e"] = null; },
                () => { this.items["d"] = null; },
                () => { this.items["c"] = null; },
                () => { this.items["b"] = null; },
                () => { this.items["a"] = null; },

                () => { this.items["g"] = 2; },
                () => { this.items["f"] = 2; },
                () => { this.items["e"] = 2; },
                () => { this.items["d"] = 2; },
                () => { this.items["c"] = 2; },
                () => { this.items["b"] = 2; },
                () => { this.items["a"] = 2; }
            };

            int numIterations = 10000;
            int numIterationsPerEnumeration = 100;

            Parallel.For(0, 4, (int index) =>
            {
                int offset = index * 10;
                for (int i = 0; i < numIterations; i++)
                {
                    // proceed thru the list of actions starting at an offset
                    int actionIndex = (i + offset) % AddRemoveOrder.Count;
                    // if odd index, go in reverse order
                    if (index % 2 == 1)
                    {
                        actionIndex = Math.Abs((i - offset)) % AddRemoveOrder.Count;
                    }

                    AddRemoveOrder[actionIndex]();

                    // if we are at a point that we should enumerate, do so. Should 
                    //      not throw any exceptions
                    if(i > numIterationsPerEnumeration && 
                        ((i % numIterationsPerEnumeration) == (2 * index)))
                    {
                        Debug.WriteLine("Thread {0} enumerating session items", index);

                        foreach(KeyValuePair<string, string> changedObj in 
                            this.items.GetChangedObjectsEnumerator())
                        {
                            if (changedObj.Value != null)
                            {
                                Assert.Contains(
                                    this.srsly.DeserializeOne(changedObj.Value),
                                    new object[] { 1, 2 });
                            }
                        }
                    }
                }
            });

            // note that we do not have a deterministic answer for what the value of each index is.
            //      but assuming there have been no exceptions, the values will be one of the 3 
            //      possible values we set each key to, in the action list above.

            Assert.Contains(this.items["a"], new object[] { 1, 2, null });
            Debug.WriteLine("a value: " + this.items["a"]);
            Assert.Contains(this.items["b"], new object[] { 1, 2, null });
            Debug.WriteLine("b value: " + this.items["b"]);
            Assert.Contains(this.items["c"], new object[] { 1, 2, null });
            Debug.WriteLine("c value: " + this.items["c"]);
            Assert.Contains(this.items["d"], new object[] { 1, 2, null });
            Debug.WriteLine("d value: " + this.items["d"]);
            Assert.Contains(this.items["e"], new object[] { 1, 2, null });
            Debug.WriteLine("e value: " + this.items["e"]);
            Assert.Contains(this.items["f"], new object[] { 1, 2, null });
            Debug.WriteLine("f value: " + this.items["f"]);
            Assert.Contains(this.items["g"], new object[] { 1, 2, null });
            Debug.WriteLine("g value: " + this.items["g"]);
        }
    }
}
