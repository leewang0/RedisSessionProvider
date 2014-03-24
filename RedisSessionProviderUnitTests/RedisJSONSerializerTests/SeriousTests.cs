namespace RedisSessionProviderUnitTests.RedisJSONSerializerTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Threading.Tasks;

    using NUnit.Framework;
    using Moq;
    using RedisSessionProvider;
    using RedisSessionProvider.Serialization;

    [TestFixture]
    public class SeriousTests
    {
        private RedisJSONSerializer srsly;

        [SetUp]
        public void OnBeforeTestExecute()
        {
            this.srsly = new RedisJSONSerializer();
        }

        [Test]
        public void BasicCorrectnessTest()
        {
            // basic test of a string
            string testString = "foo bar baz";
            string testSerialized = this.srsly.SerializeOne("testKey", testString);

            Assert.AreEqual(testString, (string)this.srsly.DeserializeOne(testSerialized));



            // basic test of an int
            int testInt = 153;
            string testIntSrlzed = this.srsly.SerializeOne("testInt", testInt);

            Assert.AreEqual(testInt, (int)this.srsly.DeserializeOne(testIntSrlzed));



            // basic test of a long
            long testLong = 123456L;
            string testLongSrlzed = this.srsly.SerializeOne("testLong", testLong);

            Assert.AreEqual(testLong, (long)this.srsly.DeserializeOne(testLongSrlzed));



            // basic test of a long
            double testDouble = 123456.756D;
            string testDoubleSrlzed = this.srsly.SerializeOne("testDouble", testDouble);

            Assert.AreEqual(testDouble, (double)this.srsly.DeserializeOne(testDoubleSrlzed));




            // basic test of a float
            float testFloat = 1234.8564F;
            string testFloatSrlzed = this.srsly.SerializeOne("testFloat", testFloat);

            Assert.AreEqual(testFloat, (float)this.srsly.DeserializeOne(testFloatSrlzed));



            // basic test of a long
            int[] testIntArr = new int[] { 1, 2, 3 };
            string testIntArrSrlzed = this.srsly.SerializeOne("testLong", testIntArr);

            Assert.AreEqual(testIntArr, (int[])this.srsly.DeserializeOne(testIntArrSrlzed));




            // basic test of a long
            string[] testStringArr = new string[] { "a", "b", "c" };
            string testStringArrSrlzed = this.srsly.SerializeOne("testLong", testStringArr);

            Assert.AreEqual(testStringArr, (string[])this.srsly.DeserializeOne(testStringArrSrlzed));
                        



            // basic class serialization
            TestSerializableClass tClass = new TestSerializableClass() 
            { 
                Prop1 = "first",
                Prop2 = 2,
                Prop3 = null
            };

            string tClassSrlzed = this.srsly.SerializeOne("testClass", tClass);

            TestSerializableClass dsrlzdTClass = this.srsly.DeserializeOne(tClassSrlzed) as TestSerializableClass;

            Assert.IsTrue(tClass.Equals(dsrlzdTClass));
            Assert.Throws<InvalidCastException>(() => 
            {
                var x = (TestSubclass)this.srsly.DeserializeOne(tClassSrlzed);
            });


            // test deserialize type correctness
            TestSubclass subTClass = new TestSubclass()
            {
                Prop1 = "second",
                Prop2 = 42,
                Prop3 = new List<string> 
                { 
                    "a",
                    "b"
                }
            };

            string subClassSrlzed = this.srsly.SerializeOne("testSubClass", subTClass);

            TestSerializableClass dsrlzdSubTClass = this.srsly.DeserializeOne(subClassSrlzed) as TestSubclass;

            Assert.IsTrue(dsrlzdSubTClass.Equals(subTClass));
        }

        [Serializable]
        public class TestSerializableClass
        {
            public string Prop1 { get; set; }

            public int Prop2 { get; set; }

            public List<string> Prop3 { get; set; }

            [IgnoreDataMember]
            public long IgnoredProp { get; set; }

            public override bool Equals(object obj)
            {
                TestSerializableClass other = obj as TestSerializableClass;
                if(other != null)
                {
                    return
                        this.Prop1 == other.Prop1 &&
                        this.Prop2 == other.Prop2 &&
                        this.Prop3Equal(other.Prop3);
                }

                return false;
            }

            private bool Prop3Equal(List<string> otherList)
            {
                if(this.Prop3 == null && otherList == null)
                {
                    return true;
                }
                else if(this.Prop3 != null && otherList != null
                    && this.Prop3.Count == otherList.Count)
                {
                    bool allElementsEqual = true;

                    for(int i = 0; i < this.Prop3.Count; i++)
                    {
                        if(this.Prop3[i] != otherList[i])
                        {
                            allElementsEqual = false;
                            break;
                        }
                    }

                    return allElementsEqual;
                }

                return false;
            }
        }

        public class TestSubclass : TestSerializableClass
        {
        }
    }
}
