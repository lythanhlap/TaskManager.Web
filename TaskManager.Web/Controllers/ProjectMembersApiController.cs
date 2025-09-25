using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Projects.Persistence.EFCore;
using TaskManager.Identity.Persistence.EFCore; // DbContext có bảng Users

namespace TaskManager.Web.Controllers;

[Authorize]
[Route("api/projects/{projectId:guid}/members")]
public sealed class ProjectMembersApiController : Controller
{
    private readonly ProjectsDbContext _prjDb;
    private readonly IdentityDbContext _idDb;

    public ProjectMembersApiController(ProjectsDbContext prjDb, IdentityDbContext idDb)
    {
        _prjDb = prjDb;
        _idDb = idDb;
    }

    // (Tuỳ chọn) Prefetch toàn bộ username để UI tự lọc
    // GET /api/projects/{pid}/members/usernames
    [HttpGet("usernames")]
    public async Task<IActionResult> Usernames(Guid projectId, CancellationToken ct = default)
    {
        // B1: lấy list UserId từ ProjectsDbContext
        var memberIds = await _prjDb.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        if (memberIds.Count == 0) return Ok(Array.Empty<object>());

        // B2: query trong IdentityDbContext (một mình) với IN (...)
        var rows = await _idDb.Users.AsNoTracking()
#if USE_IDENTITY_USERNAME // nếu schema của bạn dùng UserName (chuẩn Identity) thì bật define này, hoặc đổi thủ công
            .Where(u => memberIds.Contains(u.Id))
            .OrderBy(u => u.UserName)
            .Select(u => new { userId = u.Id, username = u.UserName })
#else
            .Where(u => memberIds.Contains(u.Id))
            .OrderBy(u => u.Username)                 // nếu cột là Username
            .Select(u => new { userId = u.Id, username = u.Username })
#endif
            .ToListAsync(ct);

        return Ok(rows);
    }

    // Gợi ý theo tiền tố username (prefix), CHỈ trong project
    // GET /api/projects/{pid}/members/suggest?q=al&take=8
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest(Guid projectId, string q, int take = 8, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<object>());
        q = q.Trim();
        take = Math.Clamp(take, 1, 20);

        // B1: lấy list UserId là member
        var memberIds = await _prjDb.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        if (memberIds.Count == 0) return Ok(Array.Empty<object>());

        // B2: lọc Users theo IN + prefix, CHỈ dùng IdentityDbContext
        var rows = await _idDb.Users.AsNoTracking()
#if USE_IDENTITY_USERNAME
            .Where(u => memberIds.Contains(u.Id) && EF.Functions.Like(u.UserName, q + "%"))
            .OrderBy(u => u.UserName)
            .Select(u => new { userId = u.Id, username = u.UserName })
#else
            .Where(u => memberIds.Contains(u.Id) && EF.Functions.Like(u.Username, q + "%"))
            .OrderBy(u => u.Username)
            .Select(u => new { userId = u.Id, username = u.Username })
#endif
            .Take(take)
            .ToListAsync(ct);

        return Ok(rows);
    }
}
