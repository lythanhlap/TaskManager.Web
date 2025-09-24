using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskManager.Projects.Abstractions;
using TaskManager.Notifications.Abstractions;          // INotificationClient
using TaskManager.Notifications.Abstractions.Events;   // ProjectMemberAdded
using TaskManager.Users.Abstractions;

namespace TaskManager.Web.Controllers
{
    [Authorize]
    [Route("projects")]
    public class ProjectsController : Controller
    {
        private readonly IProjectService _svc;
        private readonly INotificationClient _noti;
        private readonly IUserReadOnly _users;
        public ProjectsController(IProjectService svc, INotificationClient noti, IUserReadOnly users)
        {
            _svc = svc;
            _noti = noti;
            _users = users;
        }

        private string Uid =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("No user id claim.");

        [HttpGet("")]
        public async Task<IActionResult> Index()
            => View(await _svc.GetForUserAsync(Uid));

        [HttpGet("create")]
        public IActionResult Create() => View();

        [HttpPost("create"), ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string name, string? memberUsernamesCsv,
                                               string? description, DateTime? startDate, DateTime? endDate, ProjectStatus status = ProjectStatus.Planned)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Err"] = "Tên dự án không được rỗng.";
                return View();
            }

            string[]? members = null;
            if (!string.IsNullOrWhiteSpace(memberUsernamesCsv))
                members = memberUsernamesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var dto = await _svc.CreateAsync(new CreateProjectRequest(
                Name: name,
                OwnerUserId: Uid,
                Description: description,
                StartDate: startDate,
                EndDate: endDate,
                Status: status,
                MemberUsernames: members
            ));

            // Gửi notification cho các member mới được thêm

            var (ownerDisplay, ownerUser) = await ResolveOwnerNamesAsync(Uid);

            if (members is not null && members.Length > 0)
            {
                foreach (var key in members)
                {
                    var (email, userId) = await ResolveUserAsync(key);
                    if (string.IsNullOrWhiteSpace(email)) continue;

                    await _noti.EnqueueAsync(new ProjectMemberAdded(
                        email,                  // RecipientEmail
                        userId,                 // RecipientUserId
                        dto.Id.ToString(),      // ProjectId
                        name,                   // ProjectName
                        ownerDisplay,           // AddedBy (FullName/DisplayName)
                        ownerUser               // AddedByUserName   👈 tham số thứ 6 mới
                    ));
                }
            }


            TempData["Ok"] = "Tạo dự án thành công.";
            return RedirectToAction(nameof(Details), new { id = dto.Id });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Details(Guid id)
        {
            var dto = await _svc.GetDetailsAsync(id, Uid);
            if (dto is null) return NotFound();
            return View(dto);
        }

        // Thêm nhiều user (CSV)
        [HttpPost("{id:guid}/members/bulk"), ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMembers(Guid id, string usernamesCsv, string role = "member")
        {
            var usernames = (usernamesCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var result = await _svc.AddMembersByUsernamesAsync(
                new AddMembersByUsernamesRequest(id, usernames.ToArray(), role), Uid);

            // Gửi notification cho các member mới được thêm
            var details = await _svc.GetDetailsAsync(id, Uid);
            var projectName = details?.Name ?? "Dự án";

            string ownerDisplay, ownerUser;
            if (details is not null)
            {
                (ownerDisplay, ownerUser) = await ResolveOwnerNamesAsync(details.OwnerUserId);
            }
            else
            {
                // fallback nếu không lấy được details
                var me = User.Identity?.Name ?? "system";
                ownerDisplay = me;
                ownerUser = me;
            }

            foreach (var key in result.Added)
            {
                var (email, userId) = await ResolveUserAsync(key);
                if (string.IsNullOrWhiteSpace(email)) continue;

                await _noti.EnqueueAsync(new ProjectMemberAdded(
                    email,
                    userId,
                    id.ToString(),
                    projectName,
                    ownerDisplay,            // AddedBy
                    ownerUser                // AddedByUserName  👈 tham số thứ 6
                ));
            }

            TempData["Ok"] =
                $"Added: {string.Join(", ", result.Added)}; " +
                $"Exists: {string.Join(", ", result.AlreadyMembers)}; " +
                $"Not found: {string.Join(", ", result.NotFound)}; " +
                $"Ignored(owner): {string.Join(", ", result.IgnoredOwner)}";

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost("{id:guid}/members/remove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMember(Guid id, string? memberUserId, string? username)
        {
            try
            {
                bool ok = false;
                if (!string.IsNullOrWhiteSpace(memberUserId))
                    ok = await _svc.RemoveMemberByUserIdAsync(
                            new RemoveMemberByUserIdRequest(id, memberUserId), Uid);
                else if (!string.IsNullOrWhiteSpace(username))
                    ok = await _svc.RemoveMemberByUsernameAsync(
                            new RemoveMemberByUsernameRequest(id, username), Uid);

                TempData[ok ? "Ok" : "Err"] = ok ? "Đã xoá thành viên." : "Không tìm thấy thành viên.";
            }
            catch (UnauthorizedAccessException)
            {
                TempData["Err"] = "Bạn không có quyền xoá thành viên này.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Err"] = ex.Message; // ví dụ: owner cannot be removed
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        // GET /projects/edit/{id}
        [HttpGet("edit/{id:guid}")]
        public async Task<IActionResult> Edit(Guid id)
        {
            var d = await _svc.GetDetailsAsync(id, Uid);
            if (d is null) return NotFound();

            // chỉ owner được sửa
            if (d.OwnerUserId != Uid) return Forbid();

            var vm = new ProjectEditVm
            {
                Id = d.Id,
                Name = d.Name,
                Description = d.Description,
                StartDate = d.StartDate,
                EndDate = d.EndDate,
                Status = d.Status
            };
            return View(vm);
        }

        // POST /projects/edit/{id}
        [HttpPost("edit/{id:guid}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, ProjectEditVm vm)
        {
            if (id != vm.Id) return BadRequest();
            if (!ModelState.IsValid) return View(vm);

            try
            {
                await _svc.UpdateAsync(
                    new UpdateProjectRequest(vm.Id, vm.Name, vm.Description, vm.StartDate, vm.EndDate, vm.Status),
                    Uid);
                TempData["Ok"] = "Cập nhật dự án thành công.";
                return RedirectToAction(nameof(Details), new { id = vm.Id });
            }
            catch (UnauthorizedAccessException)
            {
                TempData["Err"] = "Bạn không có quyền sửa dự án này.";
                return RedirectToAction(nameof(Details), new { id = vm.Id });
            }
        }

        // POST /projects/delete/{id}
        [HttpPost("delete/{id:guid}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                await _svc.DeleteAsync(id, Uid);   // cần method này trong component
                TempData["Ok"] = "Đã xóa dự án.";
                return RedirectToAction(nameof(Index));
            }
            catch (UnauthorizedAccessException)
            {
                TempData["Err"] = "Bạn không có quyền xóa dự án này.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        private async Task<(string? Email, string? UserId)> ResolveUserAsync(string key)
        {
            var ct = HttpContext.RequestAborted;

            if (key.Contains('@'))
            {
                // Không có FindByEmailAsync → dùng thẳng email, không có userId
                return (key, null);
            }
            else
            {
                var u = await _users.FindByUsernameAsync(key, ct);
                return (u?.Email, u?.Id);
            }
        }

        private async Task<string> ResolveOwnerNameAsync(string ownerUserId)
        {
            var ct = HttpContext.RequestAborted;
            var u = await _users.GetUserByIdAsync(ownerUserId, ct);
            return u?.FullName ?? u?.Username ?? User.Identity?.Name ?? "system";
        }
        private async Task<(string DisplayName, string UserName)> ResolveOwnerNamesAsync(string ownerUserId)
        {
            var ct = HttpContext.RequestAborted;
            var u = await _users.GetUserByIdAsync(ownerUserId, ct);
            var display = string.IsNullOrWhiteSpace(u?.FullName) ? (u?.Username ?? "system") : u!.FullName;
            var uname = u?.Username ?? "system";
            return (display, uname);
        }

    }

}
