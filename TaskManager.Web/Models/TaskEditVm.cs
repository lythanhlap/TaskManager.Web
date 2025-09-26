using System.ComponentModel.DataAnnotations;
using TaskManager.Tasks.Abstractions;

namespace TaskManager.Web.Models;

public sealed class TaskEditVm
{
    public Guid ProjectId { get; set; }
    public Guid Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    public Tasks.Abstractions.TaskStatus Status { get; set; } = Tasks.Abstractions.TaskStatus.Incomplete; // mặc định

    public List<string> AssigneeUserIds { get; set; } = new();
}
