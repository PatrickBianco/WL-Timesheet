# Cloudflare client-preview hosting

This document covers the **temporary client-review preview** only. The real staff app is unchanged: it still uses Windows Negotiate against the `WLTimesheet` database and is not exposed.

## Why the preview is a separate environment

Kestrel + Negotiate cannot be published through an ordinary HTTP tunnel, because Microsoft requires one-to-one persistent connection affinity between client and origin ([Microsoft guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0)). Rather than weaken the staff app, the `ClientReview` environment swaps Negotiate for a single password-gated cookie session and points at an isolated database.

What `ClientReview` changes (see `Program.cs`):

- Authentication scheme becomes the `ClientReview` cookie instead of Negotiate.
- Startup refuses to run unless the connection string's database is `WLTimesheetReview`.
- `/review/login` and `/review/logout` exist only in this environment; everywhere else still requires an authenticated user via the fallback policy.
- The session cookie is `__Host-WLReview`: Secure, HttpOnly, SameSite=Strict, 8 hours, non-sliding, and invalidated when the administration security stamp changes.
- Login is rate limited to 10 attempts per minute.
- Every page shows a "sample data only" banner.

All preview data is synthetic, seeded by `ReviewSeed` (one "Client Reviewer" employee and three days of sample entries). Task types come from the client workbook in `Templates/`.

## One-time setup

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'ClientReview'
$env:Bootstrap__AdminPassword = '<review password>'
dotnet run --no-launch-profile --project "WL TimeSheet.csproj" -- --initialize --import-template
```

`--no-launch-profile` is required. Without it `dotnet run` applies `launchSettings.json` and silently overrides `ASPNETCORE_ENVIRONMENT` back to `Development`, which would target the live `WLTimesheet` database.

This creates `WLTimesheetReview`, applies migrations, imports task types, sets the review password and seeds the sample timesheets. It is safe to re-run: the admin row and seed are only created when absent.

## Running the preview

Two processes must stay running. Terminal 1:

```powershell
dotnet run --launch-profile ClientReview --project "WL TimeSheet.csproj"
```

The `ClientReview` profile listens on `https://localhost:7443`. Terminal 2:

```powershell
C:\Cloudflare\cloudflared-windows-amd64.exe tunnel --url https://localhost:7443 --no-tls-verify --http-host-header localhost
```

- `--no-tls-verify` is needed because the origin uses the untrusted ASP.NET Core development certificate. It affects only the loopback hop; the public hop is Cloudflare's own TLS.
- `--http-host-header localhost` keeps the origin's `AllowedHosts: localhost` restriction intact while the public hostname is a `trycloudflare.com` name. Without it every request returns 400.

cloudflared prints the public URL. Send that URL and the review password to the client separately.

## Limitations of a quick tunnel

- The URL is ephemeral and changes on every restart. A stable hostname needs a named tunnel and a Cloudflare account with a domain.
- Anyone with the URL reaches the login page, so the password is the only barrier. For anything longer-lived, put Cloudflare Access in front of it.
- Closing either terminal takes the preview offline.

## Verifying before sending the link

`scripts/Test-CloudflarePrerequisites.ps1` checks the Windows-authenticated staff app, not the preview. For the preview, confirm against the public URL that `/` redirects to `/review/login`, that the correct password redirects to `/timesheet`, and that a wrong one does not.
