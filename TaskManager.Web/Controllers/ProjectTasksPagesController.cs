using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskManager.Tasks.Abstractions;
using TaskManager.Web.Models;

namespace TaskManager.Web.Controllers;

[Authorize]
[Route("projects/{projectId:guid}/tasks")]
public sealed class ProjectTasksPagesController : Controller
{
    private readonly ITaskService _tasks;
    public ProjectTasksPagesController(ITaskService tasks) => _tasks = tasks;

    private string ActorId => User.FindFirst("sub")?.Value
                           ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // GET /projects/{pid}/tasks  h
    [HttpGet("")]
    public async Task<IActionResult> Index(Guid projectId, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        try
        {
            var list = await _tasks.ListByProjectAsync(projectId, ActorId, page, pageSize, ct);
            ViewBag.ProjectId = projectId;
            ViewBag.ActorId = ActorId;
            return View(list);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
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
            AssigneeUserIds = vm.AssigneeUserIds ?? new()
        };

        try
        {
            await _tasks.CreateAsync(dto, ActorId, ct); // chỉ Owner mới tạo được, service sẽ kiểm
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
}
