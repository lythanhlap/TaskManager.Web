using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskManager.Identity.Abstractions;
using TaskManager.Web.Models;

[Route("account")]
public class AccountController : Controller
{
    private readonly IAuthService _auth;
    public AccountController(IAuthService auth) => _auth = auth;

    [HttpGet("register")]
    public IActionResult Register() => View(new RegisterVm());

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        try
        {
            await _auth.RegisterAsync(
                new RegisterRequest(vm.Email, vm.Username, vm.Password, vm.FullName));
            return RedirectToAction("Login");
        }
        catch (InvalidOperationException ex)
        {
            // Map lỗi duplicate -> hiển thị
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(vm);
        }
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null) => View(new LoginVm { ReturnUrl = returnUrl });

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(vm);
        try
        {
            var resp = await _auth.LoginAsync(new LoginRequest(vm.Identifier, vm.Password));

            Response.Cookies.Append("access_token", resp.AccessToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,      
                    SameSite = SameSiteMode.Lax,
                    Expires = resp.ExpiresAt,
                    Path = "/"                    // cookie có hiệu lực toàn site
                });

            Console.WriteLine($"[Debug] JWT = {resp.AccessToken}");
            return Redirect(returnUrl ?? Url.Action("Index", "Home")!);
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Sai tài khoản hoặc mật khẩu.");
            return View(vm);
        }
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public IActionResult Logout(string? returnUrl = null)
    {
        // Xoá cookie JWT
        Response.Cookies.Append("access_token", "",
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(-1),
                Path = "/" // nhớ khớp Path với lúc set khi login
            });
        Response.Cookies.Delete("access_token", new CookieOptions { Path = "/" });

        return Redirect(returnUrl ?? Url.Action("Login", "Account")!);
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirst("sub")?.Value
                  ?? "";
        if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login");

        var u = await _auth.GetUserByIdAsync(userId);
        if (u is null) return NotFound();

        var vm = new ProfileVm { Id = u.Id, Email = u.Email, Username = u.Username, FullName = u.FullName };
        return View(vm);
    }

    [HttpGet("change-password")]
    public IActionResult ChangePassword() => View(new ChangePasswordVm());

    [HttpPost("change-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirst("sub")?.Value
                  ?? "";
        //if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login");

        var ok = await _auth.ChangePasswordAsync(userId, vm.OldPassword, vm.NewPassword);
        if (!ok)
        {
            ModelState.AddModelError(string.Empty, "Mật khẩu hiện tại không đúng.");
            return View(vm);
        }

        ViewBag.SuccessNow = "Đổi mật khẩu thành công. Hệ thống sẽ đăng xuất trong giây lát…";
        // khong logout. Trả về view để hiển thị -> js logout
        ModelState.Clear();
        return View(new ChangePasswordVm());

        //TempData["Success"] = "Đổi mật khẩu thành công. Vui lòng đăng nhập lại.";
        //// Đăng xuất sau khi đổi mật khẩu
        //Response.Cookies.Delete("access_token", new CookieOptions { Path = "/" });
        //return RedirectToAction("Login");
    }

}
