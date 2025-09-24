using System.ComponentModel.DataAnnotations;
using TaskManager.Projects.Abstractions;

public class ProjectEditVm
{
    public Guid Id { get; set; }

    [Required, StringLength(200)]
    public string Name { get; set; } = default!;

    [StringLength(2000)]
    public string? Description { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    [Required]
    public ProjectStatus Status { get; set; }
}
