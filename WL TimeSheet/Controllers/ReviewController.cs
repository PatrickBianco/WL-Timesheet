using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Models;
using WL_TimeSheet.Services;

namespace WL_TimeSheet.Controllers;

public sealed class ReviewController(IWebHostEnvironment environment, AppDbContext db, IPasswordHasher<AdministrationSettings> hasher) : Controller
{
    public const string SessionScheme = "ClientReview";
    [AllowAnonymous, HttpGet("/review/login")]
    public IActionResult Login() => environment.IsEnvironment("ClientReview") ? View(new UnlockInput()) : NotFound();

    [AllowAnonymous, HttpPost("/review/login"), EnableRateLimiting("review-login")]
    public async Task<IActionResult> Login(UnlockInput input)
    {
        if (!environment.IsEnvironment("ClientReview"))
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            var settings = await db.AdministrationSettings.AsNoTracking().SingleAsync();
            if (hasher.VerifyHashedPassword(settings, settings.PasswordHash, input.Password) != PasswordVerificationResult.Failed)
            {
                var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, ReviewSeed.ReviewerSid), new Claim(ClaimTypes.Name, "Client Reviewer"), new Claim("wl:review-stamp", settings.SecurityStamp.ToString()) }, SessionScheme);
                await HttpContext.SignInAsync(SessionScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });
                return LocalRedirect("/");
            }
            ModelState.AddModelError("", "The review password is incorrect.");
        }
        ModelState.Remove(nameof(input.Password));
        return View(new UnlockInput());
    }

    [Authorize, HttpPost("/review/logout")]
    public async Task<IActionResult> Logout()
    {
        if (!environment.IsEnvironment("ClientReview"))
        {
            return NotFound();
        }

        await HttpContext.SignOutAsync(SessionScheme);
        await HttpContext.SignOutAsync(WL_TimeSheet.Security.CurrentEmployee.AdminScheme);
        return LocalRedirect("/review/login");
    }
}
