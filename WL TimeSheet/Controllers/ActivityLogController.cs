using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WL_TimeSheet.Security;
using WL_TimeSheet.Services;

namespace WL_TimeSheet.Controllers;

[Authorize(Policy = "AdminUnlocked")]
public sealed class ActivityLogController(CurrentEmployee current, ActivityLogService activity) : Controller
{
    [HttpGet("/admin/activity")]
    public async Task<IActionResult> Index(Guid? employeeId, DateOnly? from, DateOnly? to, int page = 1, int pageSize = 20)
    {
        ViewData["Employee"] = await current.RequireAdminAsync();
        ViewData["IsAdmin"] = true;
        ViewData["Page"] = "activity";
        ViewData["Title"] = "Activity log";
        if (employeeId == Guid.Empty)
        {
            employeeId = null;
        }

        if (pageSize is not (10 or 20 or 50 or 100))
        {
            pageSize = 20;
        }

        if (page < 1)
        {
            page = 1;
        }

        return View(await activity.PageAsync(employeeId, from, to, page, pageSize));
    }
}
