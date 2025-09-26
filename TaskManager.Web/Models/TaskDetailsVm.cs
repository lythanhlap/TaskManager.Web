using TaskManager.Tasks.Abstractions;

namespace TaskManager.Web.Models;

public sealed class TaskDetailsVm
{
    public Guid ProjectId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public Tasks.Abstractions.TaskStatus Status { get; set; } = Tasks.Abstractions.TaskStatus.Incomplete;

    public List<UserChip> Assignees { get; set; } = new();
    public sealed record UserChip(string Id, string Username);
}
