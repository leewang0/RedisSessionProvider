using Moq;
using RedisSessionProvider.Config;
using StackExchange.Redis;
using System.Web;

namespace RedisSessionProviderUnitTests.Integration
{
    using NUnit.Framework;
    using RedisSessionProvider;
    using System;


    [TestFixture]
    public class ExpirationTests
    {
        private static string REDIS_SERVER = "192.168.0.12:6379";
        private static int REDIS_DB = 13;
        private static TimeSpan TIMEOUT = new TimeSpan(1, 0, 0);
        private static string SESSION_ID = "SESSION_ID";

        static ConfigurationOptions _redisConfigOpts;

        private IDatabase db;

        [SetUp]
        public void OnBeforeTestExecute()
        {
            _redisConfigOpts = ConfigurationOptions.Parse(REDIS_SERVER);
            RedisConnectionConfig.GetSERedisServerConfigDbIndex = @base => new Tuple<string, int, ConfigurationOptions>(
                "SessionConnection", REDIS_DB, _redisConfigOpts);
            RedisSessionConfig.SessionTimeout = TIMEOUT;

            // StackExchange Redis client
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(REDIS_SERVER);
            db = redis.GetDatabase(REDIS_DB);
        }

        [Test]
        public void ExpirationSet_AsExpected()
        {

            var mockHttpContext = new Mock<HttpContextBase>();
            var mockHttpRequest = new Mock<HttpRequestBase>();
            mockHttpRequest.Setup(x => x.Cookies).Returns(new HttpCookieCollection()
                                                          {
                                                              new HttpCookie(RedisSessionConfig.SessionHttpCookieName, SESSION_ID)
                                                          });
            mockHttpContext.Setup(x => x.Request).Returns(mockHttpRequest.Object);


            using (var sessAcc = new RedisSessionAccessor(mockHttpContext.Object))
            {
                sessAcc.Session["MyKey"] = DateTime.UtcNow;
            }

            // Assert directly using Stackexchange.Redis
            var ttl = db.KeyTimeToLive(SESSION_ID);
            
            // We should not have a null here
            Assert.IsNotNull(ttl);
            Assert.IsTrue(ttl.Value <= TIMEOUT);
            Assert.IsTrue(ttl.Value.Minutes > 0);

        }

        [TearDown]
        public void OnAfterTestExecute()
        {
            // cleanup
            db.KeyDelete(SESSION_ID);
        }
    }
}
