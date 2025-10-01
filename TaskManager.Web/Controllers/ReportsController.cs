using Microsoft.AspNetCore.Mvc;

namespace TaskManager.Web.Controllers
{
    [Route("reports")]
    public class ReportsController : Controller
    {
        [HttpGet("Portfolio")]
        public IActionResult Portfolio() => View();

    }
}
