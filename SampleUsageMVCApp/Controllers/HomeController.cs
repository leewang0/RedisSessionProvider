using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace SampleUsageMVCApp.Controllers
{
    public class HomeController : Controller
    {
        //
        // GET: /Home/
        public ActionResult Index()
        {
            int curCount = 0;
            int listCount = 0;

            if (this.Session["count"] != null)
            {
                curCount = (int)this.Session["count"];
            }

            this.Session["count"] = curCount + 1;

            List<byte> sessList = this.Session["listCount"] as List<byte>;

            if (sessList != null)
            {
                listCount = sessList.Count;
                sessList.Add(0);
            }
            else
            {
                this.Session["listCount"] = new List<byte> { 0 };
            }

            var x = this.Session["count"];



            return Content(
                string.Format(
                    "<div>Count: {0}</div><div>List Count: {1}</div>",
                    curCount,
                    listCount), 
                "text/html");
        }
    }
}
