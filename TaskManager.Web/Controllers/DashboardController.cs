using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskManager.Projects.Abstractions;

namespace TaskManager.Web.Controllers
{
    [Authorize]
    [Route("dashboard")]
    public sealed class DashboardController : Controller
    {
        private readonly IProjectService _svc;
        public DashboardController(IProjectService svc) => _svc = svc;


        private string Uid =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("No user id claim.");


        [HttpGet("/dashboard")]
        public async Task<IActionResult> Index()
        {
            var projects = await _svc.GetForUserAsync(Uid);
            return View(projects); // Model = IEnumerable<ProjectDto> 
        }

        [HttpGet("/Test2")]
        public async Task<IActionResult> Test2()
        {
            var projects = await _svc.GetForUserAsync(Uid);
            return View(projects);
        }
    }
}
