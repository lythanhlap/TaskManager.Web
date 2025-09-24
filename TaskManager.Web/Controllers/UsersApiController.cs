using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Identity.Persistence.EFCore;      // Users table
using TaskManager.Projects.Persistence.EFCore;      // để loại trừ member đã thuộc project

namespace TaskManager.Web.Controllers
{
    [Authorize]
    [Route("api/users")]
    public class UsersApiController : Controller
    {
        private readonly IdentityDbContext _idDb;
        private readonly ProjectsDbContext _prjDb;

        public UsersApiController(IdentityDbContext idDb, ProjectsDbContext prjDb)
        {
            _idDb = idDb;
            _prjDb = prjDb;
        }

        // GET /api/users/suggest?q=al&projectId=...
        [HttpGet("suggest")]
        public async Task<IActionResult> Suggest(string q, Guid? projectId, int take = 8)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Json(Array.Empty<string>());

            q = q.Trim();

            // Loại các user đã là member của project (nếu truyền projectId)
            HashSet<string> exclude = new();
            if (projectId.HasValue)
            {
                exclude = await _prjDb.ProjectMembers
                    .Where(m => m.ProjectId == projectId.Value)
                    .Select(m => m.UserId)
                    .ToHashSetAsync();
            }

            var names = await _idDb.Users.AsNoTracking()
                .Where(u => u.Username.StartsWith(q))     // prefix
                .Where(u => !exclude.Contains(u.Id))      // chưa là member
                .OrderBy(u => u.Username)
                .Select(u => u.Username)
                .Take(take)
                .ToListAsync();

            return Json(names);
        }
    }
}
