using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Models;
namespace WL_TimeSheet.Security;

public sealed class CurrentEmployee(IHttpContextAccessor accessor, AppDbContext db)
{
    public const string AdminScheme = "AdminUnlock";
    private Employee? resolved;
    public static string? Sid(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (OperatingSystem.IsWindows() && user.Identity is WindowsIdentity windows)
        {
            return windows.User?.Value;
        }

        return user.FindFirstValue(ClaimTypes.PrimarySid);
    }

    public async Task<Employee> GetAsync()
    {
        if (resolved is not null)
        {
            return resolved;
        }

        var user = accessor.HttpContext?.User;
        var sid = user is null ? null : Sid(user);
        var account = user?.Identity?.Name;
        if (sid is null || string.IsNullOrWhiteSpace(account))
        {
            throw new BusinessException("Windows authentication is required.", 401);
        }

        var e = await db.Employees.SingleOrDefaultAsync(x => x.WindowsSid == sid);
        if (e is null)
        {
            e = new Employee { WindowsSid = sid, WindowsAccount = account, DisplayName = account, CreatedBy = sid, UpdatedBy = sid };
            db.Employees.Add(e);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.Entry(e).State = EntityState.Detached;
                e = await db.Employees.SingleOrDefaultAsync(x => x.WindowsSid == sid) ?? throw new BusinessException("Could not register your Windows account. Please retry.", 409);
            }
        }
        if (!e.IsActive)
        {
            throw new BusinessException("Your employee account is inactive.", 403);
        }

        return resolved = e;
    }

    public async Task<bool> IsAdminAsync()
    {
        var e = await GetAsync();
        var result = await accessor.HttpContext!.AuthenticateAsync(AdminScheme);
        if (!result.Succeeded || result.Principal?.FindFirstValue(ClaimTypes.PrimarySid) != e.WindowsSid)
        {
            return false;
        }

        var stamp = await db.AdministrationSettings.AsNoTracking().Where(x => x.Id == 1).Select(x => x.SecurityStamp).SingleOrDefaultAsync();
        return result.Principal!.FindFirstValue("wl:admin-stamp") == stamp.ToString();
    }

    public async Task<Employee> RequireAdminAsync()
    {
        var e = await GetAsync();
        if (!await IsAdminAsync())
        {
            throw new BusinessException("Unlock administration to continue.", 403);
        }

        return e;
    }
}
public sealed class AdminUnlockedRequirement : IAuthorizationRequirement;
public sealed class AdminUnlockedHandler(CurrentEmployee current) : AuthorizationHandler<AdminUnlockedRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminUnlockedRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && await current.IsAdminAsync())
        {
            context.Succeed(requirement);
        }
    }
}
