using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WL_TimeSheet.Models;
using WL_TimeSheet.Security;
using WL_TimeSheet.Services;

namespace WL_TimeSheet.Controllers;

[Authorize(Policy = "AdminUnlocked")]
public sealed class SettingsController(CurrentEmployee current, AdministrationService administration, DatabaseBackupService backups) : Controller
{
    [HttpGet("/settings")]
    public async Task<IActionResult> Index()
    {
        await PrepareView();
        return View(await administration.UnitSettingsAsync());
    }

    [HttpPost("/settings")]
    public async Task<IActionResult> Save(UnitSettingsInput input)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await administration.SaveUnitSettingsAsync(input);
                TempData["SettingsSaved"] = "Minutes per unit saved. Newly created timesheets will use this setting.";
                return Redirect("/settings");
            }
            catch (BusinessException exception) { ModelState.AddModelError("", exception.Message); }
        }
        await PrepareView();
        return View("Index", input);
    }

    [HttpGet("/settings/backup")]
    public async Task<IActionResult> Backup()
    {
        await PrepareBackupView();
        return View(await backups.SettingsAsync());
    }

    [HttpPost("/settings/backup")]
    public async Task<IActionResult> SaveBackup(BackupSettingsInput input)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await backups.SaveAsync(input);
                TempData["BackupSaved"] = "Backup settings saved.";
                return Redirect("/settings/backup");
            }
            catch (BusinessException exception) { ModelState.AddModelError("", exception.Message); }
        }
        await PrepareBackupView();
        var currentSettings = await backups.SettingsAsync();
        input.LastBackupText = currentSettings.LastBackupText;
        input.NextBackupText = currentSettings.NextBackupText;
        input.FilesText = currentSettings.FilesText;
        input.LastBackupError = currentSettings.LastBackupError;
        return View("Backup", input);
    }

    [HttpPost("/settings/backup/run")]
    public async Task<IActionResult> RunBackup()
    {
        try
        {
            await backups.RunNowAsync(HttpContext.RequestAborted);
            TempData["BackupSaved"] = "Database backup completed. Backup files older than the retention period were removed.";
        }
        catch (BusinessException exception) { TempData["BackupError"] = exception.Message; }
        return Redirect("/settings/backup");
    }

    private async Task PrepareView()
    {
        ViewData["Employee"] = await current.RequireAdminAsync();
        ViewData["IsAdmin"] = true;
        ViewData["Page"] = "settings";
    }

    private async Task PrepareBackupView()
    {
        ViewData["Employee"] = await current.RequireAdminAsync();
        ViewData["IsAdmin"] = true;
        ViewData["Page"] = "backup";
    }
}
