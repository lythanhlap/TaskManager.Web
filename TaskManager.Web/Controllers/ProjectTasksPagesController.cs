using Humanizer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Security.Claims;
using TaskManager.Notifications.Abstractions;
using TaskManager.Notifications.Abstractions.Events;
using TaskManager.Tasks.Abstractions;
using TaskManager.Users.Abstractions;
using TaskManager.Web.Models;
using TaskManager.Web.Services;

namespace TaskManager.Web.Controllers;

[Authorize]
[Route("projects/{projectId:guid}/tasks")]
public sealed class ProjectTasksPagesController : Controller
{
    private readonly ITaskService _tasks;
    private readonly IUserReadOnly _users;
    private readonly INotificationClient _noti;
    private readonly IProjectAutoStatus _auto;
    public ProjectTasksPagesController(ITaskService tasks, IUserReadOnly users, INotificationClient _noti, IProjectAutoStatus auto)
    {
        _tasks = tasks;
        _users = users;
        this._noti = _noti;
        _auto = auto;

    }
    private string Uid =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("No user id claim.");

    private string ActorId => User.FindFirst("sub")?.Value
                           ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("")]
    public async Task<IActionResult> Index(Guid projectId, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        var list = await _tasks.ListByProjectAsync(projectId, ActorId, page, pageSize, ct);

        var allIds = list.SelectMany(t => t.AssigneeUserIds ?? Enumerable.Empty<string>())
                         .Distinct()
                         .ToList();

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in allIds)               // tuần tự -> không lỗi EF
        {
            var u = await _users.GetUserByIdAsync(id, ct);
            map[id] = u?.Username ?? id;
        }

        ViewBag.ProjectId = projectId;
        ViewBag.ActorId = ActorId;
        ViewBag.UsernameMap = map;               // đẩy xuống view

        return View(list);
    }

    // GET /projects/{pid}/tasks/create  
    [HttpGet("create")]
    public IActionResult Create(Guid projectId)
        => View(new TaskCreateVm { ProjectId = projectId });

    // POST /projects/{pid}/tasks/create  
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Guid projectId, TaskCreateVm vm, CancellationToken ct)
    {
        if (vm.ProjectId != projectId)
            ModelState.AddModelError(string.Empty, "Project không khớp.");
        if (vm.StartAt.HasValue && vm.EndAt.HasValue && vm.EndAt < vm.StartAt)
            ModelState.AddModelError(nameof(vm.EndAt), "Ngày kết thúc phải ≥ ngày bắt đầu.");
        if (!ModelState.IsValid) return View(vm);

        var dto = new TaskCreateDto
        {
            ProjectId = projectId,
            Name = vm.Name!,
            Description = vm.Description,
            StartAt = vm.StartAt,
            EndAt = vm.EndAt,
            AssigneeUserIds = vm.AssigneeUserIds ?? new List<string>()
        };

        try
        {
            var created = await _tasks.CreateAsync(dto, ActorId, ct); // chỉ Owner mới tạo được, service sẽ kiểm tra

            Guid newTaskId = created.Id;

            // send notification to assignees
            await NotifyTaskAssignedAsync(
            projectId, /* projectName */ "",
            newTaskId, vm.Name!,
            dto.AssigneeUserIds!,
            dto.EndAt,
            ct);

            await _auto.RecalcAsync(projectId, ct);

            TempData["Toast"] = "Đã tạo task.";
            return RedirectToAction(nameof(Index), new { projectId });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { ModelState.AddModelError(string.Empty, ex.Message); return View(vm); }
    }

    // delete task
    [HttpPost("{taskId:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid projectId, Guid taskId, CancellationToken ct)
    {
        try
        {
            await _tasks.DeleteAsync(taskId, ActorId, ct);
            await _auto.RecalcAsync(projectId, ct);
            TempData["Toast"] = "Đã xóa task.";
            return RedirectToAction(nameof(Index), new { projectId });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            TempData["Err"] = ex.Message;
            return RedirectToAction(nameof(Index), new { projectId });
        }
    }
    // GET /projects/{pid}/tasks/{taskId}/edit
    [HttpGet("{taskId:guid}/edit")]
    public async Task<IActionResult> Edit(Guid projectId, Guid taskId, CancellationToken ct)
    {
        try
        {
            var dto = await _tasks.GetAsync(taskId, ActorId, ct);
            if (dto is null || dto.ProjectId != projectId) return NotFound();

            var vm = new TaskEditVm
            {
                ProjectId = dto.ProjectId,
                Id = dto.Id,
                Name = dto.Name,
                Description = dto.Description,
                StartAt = dto.StartAt,
                EndAt = dto.EndAt,
                Status = dto.Status,
                AssigneeUserIds = dto.AssigneeUserIds?.ToList() ?? new()
            };

            return View(vm);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    // POST /projects/{pid}/tasks/{taskId}/edit
    [HttpPost("{taskId:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid projectId, Guid taskId, TaskEditVm vm, CancellationToken ct)
    {
        if (vm.ProjectId != projectId || vm.Id != taskId)
            ModelState.AddModelError(string.Empty, "Project/Task không khớp.");

        if (vm.StartAt.HasValue && vm.EndAt.HasValue && vm.EndAt < vm.StartAt)
            ModelState.AddModelError(nameof(vm.EndAt), "Ngày kết thúc phải ≥ ngày bắt đầu.");

        if (!ModelState.IsValid) return View(vm);

        var before = await _tasks.GetAsync(taskId, ActorId, ct);
        var beforeSet = new HashSet<string>(before?.AssigneeUserIds ?? Enumerable.Empty<string>());

        var input = new TaskUpdateDto
        {
            Name = vm.Name,
            Description = vm.Description,
            StartAt = vm.StartAt,
            EndAt = vm.EndAt,
            Status = vm.Status,
            AssigneeUserIds = vm.AssigneeUserIds ?? new List<string>()
        };

        try
        {
            var created = await _tasks.UpdateAsync(taskId, input, ActorId, ct);
            Guid newTaskId = created.Id;

            // send notification to assignees if changed

            var afterSet = new HashSet<string>(vm.AssigneeUserIds ?? new());
            afterSet.ExceptWith(beforeSet); // chỉ người mới

            if (afterSet.Count > 0)
            {
                await NotifyTaskAssignedAsync(
                    projectId, /* projectName */ "",
                    taskId,
                    vm.Name!,
                    afterSet,
                    vm.EndAt,               
                    ct);
            }

            await _auto.RecalcAsync(projectId, ct);

            TempData["Toast"] = "Đã cập nhật task.";
            return RedirectToAction(nameof(Index), new { projectId });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }
    // detail task
    [HttpGet("{taskId:guid}")]
    public async Task<IActionResult> Details(Guid projectId, Guid taskId, CancellationToken ct)
    {
        try
        {
            var dto = await _tasks.GetAsync(taskId, ActorId, ct);
            if (dto is null || dto.ProjectId != projectId) return NotFound();

            var vm = new TaskDetailsVm
            {
                ProjectId = dto.ProjectId,
                Id = dto.Id,
                Name = dto.Name,
                Description = dto.Description,
                StartAt = dto.StartAt,
                EndAt = dto.EndAt,
                Status = dto.Status
            };

            // resolve Id -> Username (tuần tự để tránh lỗi DbContext concurrency)
            foreach (var id in dto.AssigneeUserIds ?? Enumerable.Empty<string>())
            {
                var u = await _users.GetUserByIdAsync(id, ct);
                vm.Assignees.Add(new TaskDetailsVm.UserChip(id, u?.Username ?? id));
            }

            return View(vm);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    // lay ten nguoi gan
    private async Task<(string Display, string UserName)> ResolveAssignerAsync(CancellationToken ct)
    {
        var u = await _users.GetUserByIdAsync(ActorId, ct);
        var display = string.IsNullOrWhiteSpace(u?.FullName) ? (u?.Username ?? "system") : u!.FullName;
        var uname = u?.Username ?? "system";
        return (display, uname);
    }
    private async Task NotifyTaskAssignedAsync(
    Guid projectId, string projectName,
    Guid taskId, string taskName,
    IEnumerable<string> assigneeUserIds,
    DateTimeOffset? EndAt,
    CancellationToken ct)
    {
        var (assignerDisplay, assignerUser) = await ResolveAssignerAsync(ct);

        foreach (var uid in assigneeUserIds ?? Enumerable.Empty<string>())
        {
            var u = await _users.GetUserByIdAsync(uid, ct);
            var email = u?.Email;
            if (string.IsNullOrWhiteSpace(email)) continue;

            await _noti.EnqueueAsync(new TaskAssigned(
                email,                          
                uid,                          
                taskId.ToString(),             
                taskName,                       
                projectId.ToString(),           
                projectName ?? string.Empty,   
                EndAt?.UtcDateTime,            
                assignerDisplay,                
                assignerUser                    
            ), ct);
        }
    }

}
