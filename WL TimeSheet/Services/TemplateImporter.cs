using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Models;
namespace WL_TimeSheet.Services;

public sealed class TemplateImporter(AppDbContext db, IWebHostEnvironment environment)
{
    public async Task<int> ImportAsync()
    {
        using var workbook = new XLWorkbook(Path.Combine(environment.ContentRootPath, "Templates", "WL-TimeLog.xlsx"));
        var sheet = workbook.Worksheet("Task Type Lookups");
        var added = 0;
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var name = row.Cell(1).GetString().Trim();
            if (name.Length == 0 || await db.TaskTypes.AnyAsync(x => x.Name == name))
            {
                continue;
            }

            db.TaskTypes.Add(new TaskType
            {
                Name = name,
                ExplanatoryNotes = row.Cell(2).GetString(),
                IsBreak = name.Equals("Lunch Break", StringComparison.OrdinalIgnoreCase) || name.Equals("Time out (E.g. coffee/bathroom break)", StringComparison.OrdinalIgnoreCase),
                DescriptionRule = name.Equals("File Closures", StringComparison.OrdinalIgnoreCase) ? DescriptionRule.FilesClosed : DescriptionRule.Optional
            });
            added++;
        }
        await db.SaveChangesAsync();
        return added;
    }
}

