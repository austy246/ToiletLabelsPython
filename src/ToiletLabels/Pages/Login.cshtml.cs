using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ToiletLabels.Users;

namespace ToiletLabels.Pages;

public class LoginModel(IUserRepository users, IPasswordHasher<AppUser> hasher) : PageModel
{
    [BindProperty(Name = "next", SupportsGet = true)]
    public string? Next { get; set; }

    [BindProperty(Name = "username")]
    public string? UserName { get; set; }

    [BindProperty(Name = "password")]
    public string? Password { get; set; }

    public bool LoginFailed { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var user = string.IsNullOrEmpty(UserName) || string.IsNullOrEmpty(Password)
            ? null
            : await users.GetAsync(UserName, cancellationToken);

        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, Password!) == PasswordVerificationResult.Failed)
        {
            LoginFailed = true;
            return Page();
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(AppClaims.IsAdmin, user.IsAdmin ? "true" : "false"),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return LocalRedirect(Url.IsLocalUrl(Next) ? Next! : "/");
    }
}
