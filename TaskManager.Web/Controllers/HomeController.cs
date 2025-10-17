using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using TaskManager.Web.Models;

namespace TaskManager.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
           
            if (User.Identity?.IsAuthenticated == true)
            {
                var hour = DateTime.Now.Hour;
                var timeGreeting = hour switch
                {
                    < 12 => "Good morning",
                    < 18 => "Good afternoon",
                    _ => "Good evening"
                };

                // Với NameClaimType = "username", Name chính là username
                var userName = string.IsNullOrWhiteSpace(User.Identity.Name) ? "Meowster" : User.Identity.Name;
                var fullName = User?.FindFirst("name")?.Value ?? userName;
                ViewBag.WelcomeMessage = $"{timeGreeting}, {fullName}! ";
            }
            else
            {
                return View("Landing");
            }

            return View();
        }

        public IActionResult UITest()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var hour = DateTime.Now.Hour;
                var timeGreeting = hour switch
                {
                    < 12 => "Good morning",
                    < 18 => "Good afternoon",
                    _ => "Good evening"
                };

                var userName = string.IsNullOrWhiteSpace(User.Identity.Name) ? "Meowster" : User.Identity.Name;
                var fullName = User?.FindFirst("name")?.Value ?? userName;
                ViewBag.WelcomeMessage = $"{timeGreeting}, {fullName}! ";
            }
            else
            {
                return View("Landing");
            }

            return View();
        }
        public IActionResult Dashboard()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }
        public IActionResult Test()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                var hour = DateTime.Now.Hour;
                var timeGreeting = hour switch
                {
                    < 12 => "Good morning",
                    < 18 => "Good afternoon",
                    _ => "Good evening"
                };

                // Với NameClaimType = "username", Name chính là username
                var userName = string.IsNullOrWhiteSpace(User.Identity.Name) ? "Meowster" : User.Identity.Name;
                ViewBag.WelcomeMessage = $"{timeGreeting}, {userName}! 👋";
            }
            else
            {
                return View("Landing");
            }

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
