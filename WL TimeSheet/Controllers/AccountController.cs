using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WL_TimeSheet.Models;
using WL_TimeSheet.Security;
namespace WL_TimeSheet.Controllers;

[Authorize]
public sealed class AccountController(CurrentEmployee current, AdministrationService administration) : Controller
{
    [HttpGet("/account/login")] public IActionResult Login() => Redirect("/");
    [HttpGet("/account/admin-unlock")]
    public async Task<IActionResult> Unlock()
    {
        ViewData["Employee"] = await current.GetAsync();
        ViewData["IsAdmin"] = await current.IsAdminAsync();
        return View();
    }
    [HttpPost("/account/admin-unlock"), EnableRateLimiting("admin-password")]
    public async Task<IActionResult> Unlock(UnlockInput input)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await administration.UnlockAsync(input.Password);
                return Redirect("/admin");
            }
            catch (BusinessException e) { ModelState.AddModelError("", e.Message); }
        }
        ModelState.Remove(nameof(input.Password)); // Never redisplay a password value.
        ViewData["Employee"] = await current.GetAsync();
        return View(new UnlockInput());
    }
    [HttpPost("/account/admin-lock")]
    public async Task<IActionResult> Lock()
    {
        await HttpContext.SignOutAsync(CurrentEmployee.AdminScheme);
        return Redirect("/");
    }
    [Authorize(Policy = "AdminUnlocked"), HttpGet("/account/password")]
    public async Task<IActionResult> Password()
    {
        ViewData["Employee"] = await current.RequireAdminAsync();
        ViewData["IsAdmin"] = true;
        return View();
    }
    [Authorize(Policy = "AdminUnlocked"), HttpPost("/account/password"), EnableRateLimiting("admin-password")]
    public async Task<IActionResult> Password(ChangePasswordInput input)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await administration.ChangePasswordAsync(input);
                return Redirect("/account/admin-unlock?changed=true");
            }
            catch (BusinessException e) { ModelState.AddModelError("", e.Message); }
        }
        foreach (var field in new[] { "CurrentPassword", "NewPassword", "ConfirmPassword" })
        {
            ModelState.Remove(field);
        }

        ViewData["Employee"] = await current.RequireAdminAsync();
        ViewData["IsAdmin"] = true;
        return View(new ChangePasswordInput());
    }
}

