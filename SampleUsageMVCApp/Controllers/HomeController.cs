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

            if (this.Session["count"] != null)
            {
                curCount = (int)this.Session["count"];
            }

            this.Session["count"] = curCount + 1;

            return Content("Hello world: " + curCount, "text/html");
        }
    }
}
