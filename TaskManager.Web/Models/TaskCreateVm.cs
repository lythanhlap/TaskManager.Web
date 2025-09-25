using System.ComponentModel.DataAnnotations;

namespace TaskManager.Web.Models;

public sealed class TaskCreateVm
{
    [Required] public Guid ProjectId { get; set; }

    [Required, StringLength(200)]
    [Display(Name = "Tiêu đề")]
    public string? Name { get; set; }

    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Display(Name = "Ngày bắt đầu")]
    public DateTime? StartAt { get; set; }

    [Display(Name = "Ngày kết thúc")]
    public DateTime? EndAt { get; set; }

    [Display(Name = "Giao cho")]
    public List<string> AssigneeUserIds { get; set; } = new();
}
