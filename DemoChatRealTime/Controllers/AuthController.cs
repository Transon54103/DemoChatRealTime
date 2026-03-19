using System.Security.Claims;
using DemoChatRealTime.Models.DTOs;
using DemoChatRealTime.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace DemoChatRealTime.Controllers;

/// <summary>
/// NOTE - Authentication Controller:
/// - Dùng Cookie Authentication (built-in ASP.NET Core) - phù h?p cho MVC/Razor Pages.
/// - Flow: Login ? t?o Claims ? SignIn (t?o cookie) ? redirect
/// - Cookie ch?a encrypted claims, server decrypt m?i request.
///
/// QUAN TR?NG cho h? th?ng khác:
/// 1. Cookie Auth phù h?p cho: MVC, Razor Pages, same-domain SPA
/// 2. JWT Auth phù h?p cho: API, mobile app, cross-domain, microservices
/// 3. Claims = thông tin user g?n vào identity. Dùng ? m?i n?i (Controller, Hub, Middleware).
/// 4. [ValidateAntiForgeryToken] ch?ng CSRF attack cho POST requests.
/// 5. Cookie options quan tr?ng:
///    - HttpOnly = true: JS không ??c ???c cookie (ch?ng XSS)
///    - Secure = true: ch? g?i qua HTTPS
///    - SameSite = Strict: ch?ng CSRF
///    - ExpireTimeSpan: th?i gian s?ng cookie
/// </summary>
public class AuthController : Controller
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        // N?u ?ã ??ng nh?p thì redirect v? chat
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Chat");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginDto dto, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var (success, message, user) = await _authService.LoginAsync(dto);

        if (!success || user == null)
        {
            ModelState.AddModelError(string.Empty, message);
            return View(dto);
        }

        // NOTE: T?o Claims cho user ? g?n vào Cookie
        // Claims là "tuyên b?" v? user: tôi là ai, tôi có quy?n gì
        await SignInUser(user.Id, user.Username, user.DisplayName);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Chat");
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Chat");
        }
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        if (!ModelState.IsValid)
        {
            return View(dto);
        }

        var (success, message, user) = await _authService.RegisterAsync(dto);

        if (!success || user == null)
        {
            ModelState.AddModelError(string.Empty, message);
            return View(dto);
        }

        // Auto login sau khi register
        await SignInUser(user.Id, user.Username, user.DisplayName);
        return RedirectToAction("Index", "Chat");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    #region Helpers

    /// <summary>
    /// NOTE: Claims-based Authentication flow:
    /// 1. T?o list Claims (key-value pairs mô t? user)
    /// 2. T?o ClaimsIdentity v?i authentication scheme
    /// 3. T?o ClaimsPrincipal (??i di?n user trong h? th?ng)
    /// 4. HttpContext.SignInAsync ? t?o encrypted cookie ch?a claims
    /// 
    /// Sau ?ó ? m?i n?i trong app:
    /// - User.FindFirst(ClaimTypes.NameIdentifier) ? l?y UserId
    /// - User.Identity.Name ? l?y Username
    /// - [Authorize] ? ki?m tra user ?ã login ch?a
    /// </summary>
    private async Task SignInUser(int userId, string username, string displayName)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new("DisplayName", displayName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true, // NOTE: Cookie t?n t?i sau khi ?óng browser
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });
    }

    #endregion
}
