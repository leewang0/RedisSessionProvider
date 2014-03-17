namespace SampleUsageMVCApp.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Web;
    using System.Web.Mvc;

    using Models;

    public class HomeController : Controller
    {
        private static readonly object listIncrLock = new object();

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
            this.Session["listCount"] = new List<byte>();
        }

        private TestPageModel GetModel()
        {
            this.Session["count"] = (int)this.Session["count"] + 1;


            SafelyIncrementableIntHolder safeInt = this.Session["safeCount"] as SafelyIncrementableIntHolder;
            safeInt.Increment();

            
            List<byte> sessList = this.Session["listCount"] as List<byte>;
            lock (HomeController.listIncrLock)
            {
                sessList.Add(0);
            }

            
            TestPageModel mdl = new TestPageModel
            {
                Count = (int)this.Session["count"],
                SafeCount = safeInt.Value,
                ListCount = sessList.Count
            };

            return mdl;
        }

        class SafelyIncrementableIntHolder
        {
            public void Increment()
            {
                Interlocked.Increment(ref this.internalVal);
            }

            private int internalVal = 0;

            public int Value 
            { 
                get 
                {
                    return internalVal;
                }
                set
                {
                    // if not initialized
                    if (this.internalVal == 0)
                    {
                        this.internalVal = value;
                    }
                }
            }
        }
    }
}
