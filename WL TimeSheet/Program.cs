using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WL_TimeSheet.Data;
using WL_TimeSheet.Diagnostics;
using WL_TimeSheet.Models;
using WL_TimeSheet.Security;
using WL_TimeSheet.Services;
using WL_TimeSheet.Controllers;

AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
#if BUNDLED_DEPLOYMENT
// Bundled assets are extracted; editable settings remain beside the original EXE.
#pragma warning disable IL3000 // This publish profile explicitly extracts all managed assemblies.
var bundledRoot = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
#pragma warning restore IL3000
var deploymentRoot = Path.GetDirectoryName(Environment.ProcessPath!)!;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = bundledRoot
});
builder.Configuration
    .AddJsonFile(Path.Combine(deploymentRoot, "appsettings.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(deploymentRoot, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);
#else
var builder = WebApplication.CreateBuilder(args);
#endif
builder.Services.AddWindowsService(options => options.ServiceName = "WLTimeSheet");
var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"];
if (!string.IsNullOrWhiteSpace(keyDirectory))
{
    var protection = builder.Services.AddDataProtection()
        .SetApplicationName("WLTimeSheet")
        .PersistKeysToFileSystem(new DirectoryInfo(Environment.ExpandEnvironmentVariables(keyDirectory)));
    if (OperatingSystem.IsWindows())
    {
        protection.ProtectKeysWithDpapi();
    }
}
var diagnosticDirectory = Environment.ExpandEnvironmentVariables(
    builder.Configuration["Logging:DiagnosticFile:Directory"] ?? AppContext.BaseDirectory);
var clientReview = builder.Environment.IsEnvironment("ClientReview");
if (clientReview)
{
    var reviewConnection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Timesheet") ?? "");
    if (reviewConnection.InitialCatalog != "WLTimesheetReview")
    {
        throw new InvalidOperationException("ClientReview must use the isolated WLTimesheetReview database.");
    }
}
builder.Services.AddSingleton<ILoggerProvider>(_ => new DiagnosticFileLoggerProvider(diagnosticDirectory,
    builder.Configuration.GetSection("Logging:DiagnosticFile").Get<DiagnosticFileOptions>() ?? new()));
// Kestrel's Windows authentication handshake needs HTTP/1.1; avoid browser HTTP/2 negotiation failures.
builder.WebHost.ConfigureKestrel(o => o.ConfigureEndpointDefaults(endpoint => endpoint.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1));
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<TimesheetOptions>().Bind(builder.Configuration.GetSection("Timesheet"))
    .Validate(x => x.EditablePreviousDays is null or >= 0 && x.ReopenHours is > 0 and <= 168, "Invalid timesheet policy").ValidateOnStart();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Timesheet") ?? throw new InvalidOperationException("Configure ConnectionStrings:Timesheet.")));
builder.Services.AddScoped<CurrentEmployee>();
builder.Services.AddScoped<AdministrationService>();
builder.Services.AddScoped<IPasswordHasher<AdministrationSettings>, PasswordHasher<AdministrationSettings>>();
builder.Services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210000);
builder.Services.AddScoped<TimesheetPolicy>();
builder.Services.AddScoped<ActivityLogService>();
builder.Services.AddScoped<TimesheetService>();
builder.Services.AddScoped<ManagementService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<DatabaseBackupService>();
builder.Services.AddHostedService<DatabaseBackupHostedService>();
builder.Services.AddScoped<TemplateImporter>();
builder.Services.AddScoped<IAuthorizationHandler, AdminUnlockedHandler>();
var authentication = builder.Services.AddAuthentication(clientReview ? ReviewController.SessionScheme : NegotiateDefaults.AuthenticationScheme);
if (clientReview)
{
    authentication.AddCookie(ReviewController.SessionScheme, o =>
    {
        o.Cookie.Name = "__Host-WLReview";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = false;
        o.Events.OnRedirectToLogin = context => { if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 401; } else { context.Response.Redirect("/review/login"); } return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var stamp = await db.AdministrationSettings.AsNoTracking().Where(x => x.Id == 1).Select(x => x.SecurityStamp).SingleOrDefaultAsync();
            if (context.Principal?.FindFirst("wl:review-stamp")?.Value != stamp.ToString())
            {
                context.RejectPrincipal();
            }
        };
    });
}
else
{
    authentication.AddNegotiate();
}

authentication.AddCookie(CurrentEmployee.AdminScheme, o =>
{
    o.Cookie.Name = "__Host-WLAdmin";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = false;
});
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    o.AddPolicy("AdminUnlocked", p => p.RequireAuthenticatedUser().AddRequirements(new AdminUnlockedRequirement()));
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("review-login", _ => RateLimitPartition.GetFixedWindowLimiter("review-login", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("admin-password", context => RateLimitPartition.GetFixedWindowLimiter(context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
try
{
    app.Logger.LogInformation("Application starting in {Environment}; diagnostic directory {LogDirectory}", app.Environment.EnvironmentName, diagnosticDirectory);
    app.Lifetime.ApplicationStopping.Register(() => app.Logger.LogInformation("Application stopping"));
    if (args.Contains("--initialize"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        if (!await db.AdministrationSettings.AnyAsync())
        {
            var password = builder.Configuration["Bootstrap:AdminPassword"];
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("Set Bootstrap:AdminPassword in user secrets or environment for first initialization.");
            }

            var settings = new AdministrationSettings();
            settings.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdministrationSettings>>().HashPassword(settings, password);
            db.AdministrationSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        if (args.Contains("--import-template"))
        {
            app.Logger.LogInformation("Imported {TaskTypeCount} task types from the client workbook", await scope.ServiceProvider.GetRequiredService<TemplateImporter>().ImportAsync());
        }

        if (clientReview)
        {
            await ReviewSeed.InitializeAsync(db, scope.ServiceProvider.GetRequiredService<TimesheetPolicy>());
        }

        app.Logger.LogInformation("Database initialization completed");
        return;
    }
    // Do not interpret six-minute database values as minutes before the conversion.
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            throw new InvalidOperationException("Database upgrade required. Stop the service, back up the database, and run this EXE with --environment Production --initialize under a SQL migration administrator before restarting.");
        }
    }
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseMiddleware<RequestDiagnosticsMiddleware>();
    app.UseHttpsRedirection();
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        if (!context.Request.Path.StartsWithSegments("/css") && !context.Request.Path.StartsWithSegments("/js"))
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        await next();
    });
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.MapControllers();
    app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
    app.Run();
}
catch (Exception exception) { app.Logger.LogCritical(exception, "Application terminated unexpectedly"); throw; }
finally { await app.DisposeAsync(); }
public partial class Program;

