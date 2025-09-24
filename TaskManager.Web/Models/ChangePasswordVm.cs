using System.ComponentModel.DataAnnotations;

namespace TaskManager.Web.Models
{
    public class ChangePasswordVm
    {
        [Required, DataType(DataType.Password), Display(Name = "Mật khẩu hiện tại")]
        public string OldPassword { get; set; } = "";

        [Required, DataType(DataType.Password), MinLength(6), Display(Name = "Mật khẩu mới")]
        public string NewPassword { get; set; } = "";

        [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Xác nhận không khớp")]
        [Display(Name = "Xác nhận mật khẩu mới")]
        public string ConfirmNewPassword { get; set; } = "";
    }
}
