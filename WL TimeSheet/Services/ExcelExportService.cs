using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using WL_TimeSheet.Models;
namespace WL_TimeSheet.Services;

public sealed class ExcelExportService(TimesheetService timesheets, IWebHostEnvironment environment)
{
    public async Task<byte[]> AdminExportAsync(DateOnly from, DateOnly to, Guid? employeeId, Guid? taskTypeId, bool csv)
    {
        var entries = await timesheets.ListAsync(from, to, employeeId, taskTypeId);
        return csv ? BuildAdminCsv(entries) : BuildAdmin(entries, Path.Combine(environment.ContentRootPath, "Templates", "WL-AdminExport.xlsx"));
    }

    public static byte[] BuildAdmin(IReadOnlyList<EntryRow> entries, string template)
    {
        using var output = new XLWorkbook(template);
        var sheet = output.Worksheet("Sheet1");
        var lastRow = Math.Max(43, entries.Count + 1);
        // Remove all client example values and formulas, including hidden columns.
        sheet.Range(2, 1, Math.Max(lastRow, sheet.LastRowUsed()!.RowNumber()), 19).Clear(XLClearOptions.Contents);
        for (var row = 2; row <= lastRow; row++)
        {
            for (var column = 1; column <= 9; column++)
            {
                sheet.Cell(row, column).Style = sheet.Cell(2, column).Style;
            }
            sheet.Row(row).Height = sheet.Row(2).Height;
        }
        var index = 2;
        sheet.Cell(1, 9).Value = "Minutes per unit";
        sheet.Cell(1, 9).Style = sheet.Cell(1, 5).Style;
        sheet.Column(9).Width = 18;
        foreach (var entry in entries.OrderBy(x => x.WorkDate).ThenBy(x => x.EmployeeName).ThenBy(x => x.EmployeeId).ThenBy(x => x.Position))
        {
            sheet.Cell(index, 1).Value = entry.EmployeeName;
            sheet.Cell(index, 2).Value = entry.WorkDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(index, 2).Style.NumberFormat.Format = "dd/MM/yyyy";
            sheet.Cell(index, 3).Value = entry.StartMinute / 1440d;
            sheet.Cell(index, 4).Value = entry.EndMinute / 1440d;
            sheet.Range(index, 3, index, 4).Style.NumberFormat.Format = "[h]:mm";
            sheet.Cell(index, 5).Value = entry.Units;
            sheet.Cell(index, 6).Value = entry.DurationMinutes / 1440d;
            sheet.Cell(index, 6).Style.NumberFormat.Format = "[h]:mm";
            sheet.Cell(index, 7).Value = entry.TaskTypeName;
            sheet.Cell(index, 8).Value = entry.Description;
            sheet.Cell(index, 9).Value = entry.MinutesPerUnit;
            index++;
        }
        sheet.Range(1, 1, Math.Max(2, entries.Count + 1), 9).SetAutoFilter();
        sheet.PageSetup.PrintAreas.Clear();
        sheet.PageSetup.PrintAreas.Add(1, 1, lastRow, 9);
        using var stream = new MemoryStream();
        output.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] BuildAdminCsv(IReadOnlyList<EntryRow> entries)
    {
        var csv = new StringBuilder("Employee,Date,Time Started,Time Finished,Units,Duration,Task Type,Description,Minutes per unit\r\n");
        foreach (var entry in entries.OrderBy(x => x.WorkDate).ThenBy(x => x.EmployeeName).ThenBy(x => x.EmployeeId).ThenBy(x => x.Position))
        {
            var values = new[] { entry.EmployeeName, entry.WorkDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), entry.StartTime, entry.EndTime,
                entry.Units.ToString(CultureInfo.InvariantCulture), $"{entry.DurationMinutes / 60}:{entry.DurationMinutes % 60:00}", entry.TaskTypeName, entry.Description, entry.MinutesPerUnit.ToString(CultureInfo.InvariantCulture) };
            csv.Append(string.Join(",", values.Select(CsvCell))).Append("\r\n");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    private static string CsvCell(string value)
    {
        if (value.TrimStart().StartsWithAny('=', '+', '-', '@') || value.StartsWith('\t') || value.StartsWith('\r'))
        {
            value = "'" + value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public async Task<byte[]> ExportAsync(DateOnly from, DateOnly to, Guid? employeeId, Guid? taskTypeId)
    {
        var entries = await timesheets.ListAsync(from, to, employeeId, taskTypeId);
        var emptyDay = entries.Count == 0 && from == to && employeeId.HasValue ? await timesheets.DayAsync(from, employeeId) : null;
        return Build(entries, Path.Combine(environment.ContentRootPath, "Templates", "WL-TimeLog.xlsx"), emptyDay);
    }

    public static byte[] Build(IReadOnlyList<EntryRow> entries, string template, DayView? emptyDay = null)
    {
        // Open the whole template: copying only Sheet1 loses workbook-level lookup names/settings.
        using var output = new XLWorkbook(template);
        var original = output.Worksheet("Sheet1");
        var groups = entries.GroupBy(x => new { x.EmployeeId, x.EmployeeName, x.WorkDate }).ToList();
        var sheets = new List<IXLWorksheet> { original };
        // Copy pristine sheets before writing any exported data.
        if (groups.Count > 1)
        {
            original.Name = $"{groups[0].Key.WorkDate:yyyy-MM-dd} 1";
            for (var i = 1; i < groups.Count; i++)
            {
                var copy = original.CopyTo($"{groups[i].Key.WorkDate:yyyy-MM-dd} {i + 1}");
                copy.Position = i + 1;
                sheets.Add(copy);
            }
        }
        if (groups.Count == 0)
        {
            Prepare(original, 45, emptyDay?.MinutesPerUnit ?? 1);
            original.Cell("B1").Value = emptyDay?.DisplayName ?? "";
            original.Cell("B2").Clear(XLClearOptions.Contents);
            if (emptyDay is not null)
            {
                original.Cell("B2").Value = emptyDay.WorkDate.ToString("dd/MM/yyyy");
            }

            original.Cell("F1").Value = 0;
        }
        var index = 0;
        foreach (var group in groups)
        {
            var sheet = sheets[index++];
            var lastRow = Math.Max(45, group.Count() + 3);
            Prepare(sheet, lastRow, group.First().MinutesPerUnit);
            sheet.Cell("B1").Value = group.Key.EmployeeName;
            sheet.Cell("B2").Value = group.Key.WorkDate.ToString("dd/MM/yyyy");
            sheet.Cell("F1").Value = group.Sum(x => x.ProductiveMinutes) / 1440d;
            var row = 4;
            foreach (var e in group.OrderBy(x => x.Position))
            {
                sheet.Cell(row, 1).Value = e.StartMinute / 1440d;
                sheet.Cell(row, 2).Value = e.EndMinute / 1440d;
                // Only midnight needs an elapsed-hour format to avoid displaying 24:00 as 00:00.
                if (e.EndMinute == 1440)
                {
                    sheet.Cell(row, 2).Style.NumberFormat.Format = "[h]:mm";
                }

                sheet.Cell(row, 3).Value = e.Units;
                sheet.Cell(row, 4).Value = e.DurationMinutes / 1440d;
                sheet.Cell(row, 5).Value = e.TaskTypeName;
                sheet.Cell(row, 6).Value = e.Description;
                sheet.Cell(row, 7).Value = e.ExplanatoryNotes;
                row++;
            }
        }
        using var stream = new MemoryStream();
        output.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Prepare(IXLWorksheet sheet, int lastRow, int minutesPerUnit)
    {
        if (lastRow > 45)
        {
            sheet.Row(45).InsertRowsBelow(lastRow - 45);
            for (var row = 46; row <= lastRow; row++)
            {
                for (var column = 1; column <= 8; column++)
                {
                    sheet.Cell(row, column).Style = sheet.Cell(45, column).Style;
                }

                sheet.Row(row).Height = sheet.Row(45).Height;
            }
        }
        sheet.Range(4, 1, lastRow, 7).Clear(XLClearOptions.Contents);
        // Preserve the original validation messages and extend the source's missing final dropdown.
        var tasks = sheet.Cell("E4").GetDataValidation();
        tasks.ClearRanges();
        tasks.AddRange(sheet.Range(4, 5, lastRow, 5));
        var units = sheet.Cell("C4").GetDataValidation();
        units.ClearRanges();
        units.AddRange(sheet.Range(4, 3, lastRow, 3));
        var maxUnits = 1440 / minutesPerUnit;
        sheet.Cell("C3").Value = $"Units (1 = {minutesPerUnit} min)";
        units.WholeNumber.Between(0, maxUnits);
        units.InputTitle = "Units";
        units.InputMessage = $"1 unit = {minutesPerUnit} minute(s). Enter a whole number from 0 to {maxUnits}.";
        units.ErrorMessage = $"Enter whole units between 0 and {maxUnits}.";
        sheet.Range(3, 1, lastRow, 7).SetAutoFilter();
        // Keep original fonts, borders, fills, sizes, blank rows, freeze panes and portrait print settings.
    }

    public async Task<byte[]> CsvAsync(DateOnly from, DateOnly to, Guid? employeeId, Guid? taskTypeId)
        => BuildCsv(await timesheets.ListAsync(from, to, employeeId, taskTypeId));
    public static byte[] BuildCsv(IReadOnlyList<EntryRow> rows)
    {
        // Quote every field; prefix dangerous spreadsheet input so opening the CSV cannot run formulas.
        static string Cell(string value)
        {
            if (value.TrimStart().StartsWithAny('=', '+', '-', '@') || value.StartsWith('\t') || value.StartsWith('\r'))
            {
                value = "'" + value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("User,Date,Start Time,Finish Time,Units,Duration,Task Type,Description,Explanatory Notes,Is Break,Productive Minutes,Minutes per unit\r\n");
        foreach (var e in rows)
        {
            csv.AppendLine(string.Join(",", new[]{e.WindowsAccount,e.WorkDate.ToString("yyyy-MM-dd"),e.StartTime,e.EndTime,e.Units.ToString(CultureInfo.InvariantCulture),
                $"{e.DurationMinutes/60}:{e.DurationMinutes%60:00}",e.TaskTypeName,e.Description,e.ExplanatoryNotes,e.IsBreak?"Yes":"No",e.ProductiveMinutes.ToString(CultureInfo.InvariantCulture),e.MinutesPerUnit.ToString(CultureInfo.InvariantCulture)}.Select(Cell)));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }
}
internal static class CsvTextExtensions
{
    public static bool StartsWithAny(this string value, params char[] chars) => value.Length > 0 && chars.Contains(value[0]);
}
