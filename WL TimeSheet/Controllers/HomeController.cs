using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WL_TimeSheet.Security;
namespace WL_TimeSheet.Controllers;

[Authorize]
public sealed class HomeController(CurrentEmployee current) : Controller
{
    public Task<IActionResult> Index() => Page("timesheet", "Workspace");
    private async Task<IActionResult> Page(string page, string view, bool admin = false)
    {
        ViewData["Employee"] = admin ? await current.RequireAdminAsync() : await current.GetAsync();
        ViewData["IsAdmin"] = await current.IsAdminAsync();
        ViewData["Page"] = page;
        return View(view);
    }
    // Keep existing bookmarks working while making the root URL canonical.
    [HttpGet("/timesheet")] public IActionResult Timesheet() => RedirectPermanent("/");
    [Authorize(Policy = "AdminUnlocked"), HttpGet("/admin")] public Task<IActionResult> Admin() => Page("admin", "Workspace", true);
    [Authorize(Policy = "AdminUnlocked"), HttpGet("/employees")] public Task<IActionResult> Employees() => Page("employees", "Management", true);
    [Authorize(Policy = "AdminUnlocked"), HttpGet("/task-types")] public Task<IActionResult> TaskTypes() => Page("task-types", "Management", true);
}

