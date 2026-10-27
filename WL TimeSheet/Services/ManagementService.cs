using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Models;
using WL_TimeSheet.Security;
namespace WL_TimeSheet.Services;

public sealed class ManagementService(AppDbContext db, CurrentEmployee current, TimesheetService timesheets)
{
    public async Task<List<Employee>> EmployeesAsync(string? search)
    {
        var query = await EmployeeQueryAsync(search);
        return await query.OrderBy(employee => employee.DisplayName).ThenBy(employee => employee.Id).ToListAsync();
    }

    public async Task<object> EmployeePageAsync(string? search, int page, int pageSize)
    {
        var query = await EmployeeQueryAsync(search);
        return await PageAsync(query.OrderBy(employee => employee.DisplayName).ThenBy(employee => employee.Id), page, pageSize);
    }

    private async Task<IQueryable<Employee>> EmployeeQueryAsync(string? search)
    {
        await current.RequireAdminAsync();
        var query = db.Employees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(employee =>
                employee.DisplayName.Contains(search) ||
                (employee.WindowsAccount != null && employee.WindowsAccount.Contains(search)) ||
                (employee.Email != null && employee.Email.Contains(search)));
        }

        return query;
    }

    public async Task<Employee> SaveEmployeeAsync(Guid id, EmployeeInput input)
    {
        var actor = await current.RequireAdminAsync();

        if (string.IsNullOrWhiteSpace(input.DisplayName))
        {
            throw new BusinessException("Enter a display name.");
        }

        if (id == actor.Id && !input.IsActive)
        {
            throw new BusinessException("You cannot deactivate your current Windows account.");
        }

        var employee = await db.Employees.FindAsync(id)
            ?? throw new BusinessException("Employee not found.", 404);
        timesheets.SetVersion(employee, input.Version);
        employee.DisplayName = input.DisplayName.Trim();
        employee.Email = string.IsNullOrWhiteSpace(input.Email)
            ? null
            : input.Email.Trim().ToLowerInvariant();
        employee.IsActive = input.IsActive;
        employee.UpdatedBy = actor.WindowsSid!;
        employee.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        return employee;
    }

    public async Task<List<TaskType>> TaskTypesAsync(string? search, bool includeInactive)
    {
        var query = await TaskTypeQueryAsync(search, includeInactive);
        return await query.OrderBy(taskType => taskType.Name).ThenBy(taskType => taskType.Id).ToListAsync();
    }

    public async Task<object> TaskTypePageAsync(string? search, int page, int pageSize)
    {
        var query = await TaskTypeQueryAsync(search, includeInactive: true);
        return await PageAsync(query.OrderBy(taskType => taskType.Name).ThenBy(taskType => taskType.Id), page, pageSize);
    }

    private async Task<IQueryable<TaskType>> TaskTypeQueryAsync(string? search, bool includeInactive)
    {
        if (includeInactive)
        {
            await current.RequireAdminAsync();
        }
        else
        {
            await current.GetAsync();
        }

        var query = db.TaskTypes.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.Name.Contains(search) || x.ExplanatoryNotes.Contains(search));
        }

        return query;
    }

    private static async Task<object> PageAsync<T>(IOrderedQueryable<T> query, int page, int pageSize)
    {
        if (page < 1 || pageSize is not (10 or 20 or 50 or 100))
        {
            throw new BusinessException("Choose a valid page and a page size of 10, 20, 50 or 100.");
        }

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, pages);

        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new
        {
            rows,
            page,
            pageSize,
            total,
            pages
        };
    }

    public async Task<TaskType> SaveTaskTypeAsync(Guid? id, TaskTypeInput input)
    {
        var actor = await current.RequireAdminAsync();

        if (string.IsNullOrWhiteSpace(input.Name) || !Enum.IsDefined(input.DescriptionRule))
        {
            throw new BusinessException("Enter a task type name and valid description rule.");
        }

        var name = input.Name.Trim();
        if (await db.TaskTypes.AnyAsync(x => x.Name == name && x.Id != id))
        {
            throw new BusinessException("That task type already exists.");
        }

        var taskType = id.HasValue
            ? await db.TaskTypes.FindAsync(id.Value)
                ?? throw new BusinessException("Task type not found.", 404)
            : new TaskType { CreatedBy = actor.WindowsSid! };
        if (id.HasValue)
        {
            timesheets.SetVersion(taskType, input.Version);
        }
        else
        {
            db.TaskTypes.Add(taskType);
        }

        taskType.Name = name;
        taskType.ExplanatoryNotes = input.ExplanatoryNotes?.Trim() ?? "";
        taskType.IsActive = input.IsActive;
        taskType.IsBreak = input.IsBreak;
        taskType.DescriptionRule = input.DescriptionRule;
        taskType.UpdatedBy = actor.WindowsSid!;
        taskType.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        return taskType;
    }
}
