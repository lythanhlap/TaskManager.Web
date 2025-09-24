using System.ComponentModel.DataAnnotations;

namespace TaskManager.Web.Models;
public class LoginVm
{
    [Required, Display(Name = "Username or Email")]
    public string Identifier { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}