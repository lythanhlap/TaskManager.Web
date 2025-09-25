using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskManager.Tasks.Abstractions;

namespace TaskManager.Web.Controllers;

[Authorize]
[Route("api/projects/{projectId:guid}/tasks")]
public sealed class ProjectTasksController : Controller
{
    private readonly ITaskService _svc;
    public ProjectTasksController(ITaskService svc) => _svc = svc;

    private string ActorId => User.FindFirst("sub")?.Value
                           ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, int page = 1, int pageSize = 20)
    {
        try { return Ok(await _svc.ListByProjectAsync(projectId, ActorId, page, pageSize)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet("{taskId:guid}")]
    public async Task<IActionResult> Get(Guid projectId, Guid taskId)
    {
        try
        {
            var t = await _svc.GetAsync(taskId, ActorId);
            return t is null || t.ProjectId != projectId ? NotFound() : Ok(t);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid projectId, [FromBody] TaskCreateDto input)
    {
        try
        {
            input.ProjectId = projectId;
            var t = await _svc.CreateAsync(input, ActorId);
            return CreatedAtAction(nameof(Get), new { projectId, taskId = t.Id }, t);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPut("{taskId:guid}")]
    public async Task<IActionResult> Update(Guid projectId, Guid taskId, [FromBody] TaskUpdateDto input)
    {
        try
        {
            var t = await _svc.UpdateAsync(taskId, input, ActorId);
            if (t.ProjectId != projectId) return BadRequest("Task không thuộc project.");
            return Ok(t);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpPatch("{taskId:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid projectId, Guid taskId, [FromQuery] Tasks.Abstractions.TaskStatus status)
    {
        try
        {
            var t = await _svc.SetStatusAsync(taskId, status, ActorId);
            if (t.ProjectId != projectId) return BadRequest();
            return Ok(t);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpDelete("{taskId:guid}")]
    public async Task<IActionResult> Delete(Guid projectId, Guid taskId)
    {
        try { await _svc.DeleteAsync(taskId, ActorId); return NoContent(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }
}