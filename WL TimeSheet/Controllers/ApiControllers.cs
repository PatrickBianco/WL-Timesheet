using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WL_TimeSheet.Models;
using WL_TimeSheet.Services;
namespace WL_TimeSheet.Controllers;

[ApiController, Authorize, Route("api/timesheets")]
public sealed class TimesheetsController(TimesheetService service, ExcelExportService export) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(DateOnly from, DateOnly to, Guid? employeeId, Guid? taskTypeId, int page = 1, int pageSize = 20)
        => Ok(await service.PageAsync(from, to, employeeId, taskTypeId, page, pageSize));
    [HttpGet("day")] 
    public async Task<IActionResult> Day(DateOnly date, Guid? employeeId) => Ok(await service.DayAsync(date, employeeId));
    [HttpPut("day")] public async Task<IActionResult> SaveDay(DayInput input) => Ok(await service.SaveDayAsync(input));
    [Authorize(Policy = "AdminUnlocked"), HttpPost("reopen")]
    public async Task<IActionResult> Reopen(ReopenInput input)
    {
        await service.ReopenAsync(input);
        return NoContent();
    }
    [HttpGet("calendar")] public async Task<IActionResult> Calendar(DateOnly month, Guid? employeeId, Guid? taskTypeId) => Ok(await service.CalendarAsync(month, employeeId, taskTypeId));
    [HttpPost]
    public async Task<IActionResult> Create(EntryInput input)
    {
        var e = await service.SaveAsync(null, input);
        return Ok(new
        {
            e.Id,
            e.Version
        });
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, EntryInput input)
    {
        var e = await service.SaveAsync(id, input);
        return Ok(new
        {
            e.Id,
            e.Version
        });
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] VersionInput input)
    {
        await service.DeleteAsync(id, input.Version, input.DayVersion);
        return NoContent();
    }
    [Authorize(Policy = "AdminUnlocked"), HttpGet("admin-export")]
    public async Task<IActionResult> AdminExport(DateOnly from, DateOnly to, Guid? employeeId, Guid? taskTypeId, string format = "xlsx")
    {
        if (format is not ("xlsx" or "csv"))
        {
            throw new BusinessException("Select CSV or Excel export.");
        }
        var content = await export.AdminExportAsync(from, to, employeeId, taskTypeId, format == "csv");
        var contentType = format == "csv" ? "text/csv; charset=utf-8" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return File(content, contentType, $"WL-admin-timesheets-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.{format}");
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(DateOnly from, DateOnly to, Guid? employeeId, Guid? taskTypeId, string format = "xlsx")
    {
        if (format == "csv")
        {
            return File(await export.CsvAsync(from, to, employeeId, taskTypeId), "text/csv; charset=utf-8", $"WL-timesheets-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
        }

        if (format != "xlsx")
        {
            throw new BusinessException("Select CSV or Excel export.");
        }

        return File(await export.ExportAsync(from, to, employeeId, taskTypeId), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"WL-timesheets-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx");
    }
}
public sealed record VersionInput(byte[] Version, byte[] DayVersion);
[ApiController, Authorize(Policy = "AdminUnlocked"), Route("api/employees")]
public sealed class EmployeesController(ManagementService service) : ControllerBase
{
    [HttpGet("page")]
    public async Task<IActionResult> Page(string? search, int page = 1, int pageSize = 20)
        => Ok(await service.EmployeePageAsync(search, page, pageSize));
    [HttpGet] public async Task<IActionResult> List(string? search) => Ok(await service.EmployeesAsync(search));
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, EmployeeInput input) => Ok(await service.SaveEmployeeAsync(id, input));
}
[ApiController, Authorize, Route("api/task-types")]
public sealed class TaskTypesController(ManagementService service) : ControllerBase
{
    [Authorize(Policy = "AdminUnlocked"), HttpGet("page")]
    public async Task<IActionResult> Page(string? search, int page = 1, int pageSize = 20)
        => Ok(await service.TaskTypePageAsync(search, page, pageSize));
    [HttpGet] public async Task<IActionResult> List(string? search, bool includeInactive = false) => Ok(await service.TaskTypesAsync(search, includeInactive));
    [Authorize(Policy = "AdminUnlocked"), HttpPost] public async Task<IActionResult> Create(TaskTypeInput input) => Ok(await service.SaveTaskTypeAsync(null, input));
    [Authorize(Policy = "AdminUnlocked"), HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, TaskTypeInput input) => Ok(await service.SaveTaskTypeAsync(id, input));
}
