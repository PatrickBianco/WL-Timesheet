using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Models;
namespace WL_TimeSheet.Security;

public sealed class AdministrationService(AppDbContext db, CurrentEmployee current, IHttpContextAccessor accessor, IPasswordHasher<AdministrationSettings> hasher, ILogger<AdministrationService> logger)
{
    public async Task<UnitSettingsInput> UnitSettingsAsync()
    {
        await current.RequireAdminAsync();
        var settings = await db.AdministrationSettings.AsNoTracking().SingleAsync(x => x.Id == 1);
        return new() { MinutesPerUnit = settings.MinutesPerUnit, Version = Convert.ToBase64String(settings.Version) };
    }

    public async Task SaveUnitSettingsAsync(UnitSettingsInput input)
    {
        var actor = await current.RequireAdminAsync();
        if (input.MinutesPerUnit is null or < 1 or > 60)
            throw new BusinessException("Enter a whole number from 1 to 60 minutes per unit.");

        var version = new byte[8];
        if (!Convert.TryFromBase64String(input.Version ?? "", version, out var length) || length != 8)
            throw new BusinessException("Reload the settings before saving.", 409);

        var settings = await db.AdministrationSettings.SingleAsync(x => x.Id == 1);
        db.Entry(settings).Property(x => x.Version).OriginalValue = version;
        settings.MinutesPerUnit = input.MinutesPerUnit.Value;
        settings.ModifiedAt = DateTimeOffset.UtcNow;
        settings.ModifiedBy = actor.WindowsSid!;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessException("Settings changed in another session. Reload this page before saving.", 409);
        }
        logger.LogInformation("Minutes per unit changed to {MinutesPerUnit} by employee {EmployeeId}; existing timesheets retain their setting", settings.MinutesPerUnit, actor.Id);
    }

    public async Task UnlockAsync(string password)
    {
        var e = await current.GetAsync();
        var settings = await db.AdministrationSettings.SingleOrDefaultAsync(x => x.Id == 1)
            ?? throw new BusinessException("Administration password has not been initialized. Run the documented setup command.", 503);
        var result = hasher.VerifyHashedPassword(settings, settings.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Administration unlock rejected for employee {EmployeeId}", e.Id);
            throw new BusinessException("The administrator password is incorrect.", 403);
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            settings.PasswordHash = hasher.HashPassword(settings, password);
            await db.SaveChangesAsync();
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.PrimarySid, e.WindowsSid!), new("wl:admin-stamp", settings.SecurityStamp.ToString())], CurrentEmployee.AdminScheme));
        await accessor.HttpContext!.SignInAsync(CurrentEmployee.AdminScheme, principal, new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) });
        logger.LogInformation("Administration unlocked for employee {EmployeeId}", e.Id);
    }

    public async Task ChangePasswordAsync(ChangePasswordInput input)
    {
        var actor = await current.RequireAdminAsync();
        var settings = await db.AdministrationSettings.SingleAsync(x => x.Id == 1);
        if (hasher.VerifyHashedPassword(settings, settings.PasswordHash, input.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Administration password change rejected for employee {EmployeeId}", actor.Id);
            throw new BusinessException("The current administrator password is incorrect.", 403);
        }
        if (input.NewPassword.Length < 8 || input.NewPassword.Length > 128 || input.NewPassword != input.ConfirmPassword)
        {
            throw new BusinessException("Enter matching passwords between 8 and 128 characters.");
        }

        settings.PasswordHash = hasher.HashPassword(settings, input.NewPassword);
        settings.SecurityStamp = Guid.NewGuid();
        settings.ModifiedAt = DateTimeOffset.UtcNow;
        settings.ModifiedBy = actor.WindowsSid!;
        await db.SaveChangesAsync();
        await accessor.HttpContext!.SignOutAsync(CurrentEmployee.AdminScheme);
        logger.LogInformation("Administration password changed by employee {EmployeeId}; existing unlock sessions invalidated", actor.Id);
    }
}
