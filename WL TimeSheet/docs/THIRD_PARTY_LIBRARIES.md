# Third-party libraries

Application libraries are open-source; Windows and SQL Server are requested external infrastructure with separate licenses. There are no paid UI components. Keycloak/OIDC and Java are no longer application dependencies. Earlier isolated downloads are unused and excluded from publication.

| Dependency | Version | Purpose | License |
|---|---|---|---|
| .NET / ASP.NET Core | 10.0.12; SDK 10.0.401 | MVC, Razor, hosting, cookies and password hashing | MIT |
| Authentication.Negotiate | 10.0.12 | Windows authentication | MIT |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.12 | Windows Service lifetime and Event Log integration | MIT |
| EntityFrameworkCore.SqlServer | 10.0.12 | Persistence | MIT |
| EntityFrameworkCore.Design / dotnet-ef | 10.0.12 | Migration tools | MIT |
| Microsoft.Data.SqlClient | 7.0.3 | Managed SQL driver | MIT |
| ClosedXML | 0.105.1 | Import/export | MIT |
| ClosedXML.Parser | 2.0.0 | Formula parsing | MIT |
| DocumentFormat.OpenXml / Framework | 3.1.1 | XLSX serialization | MIT |
| ExcelNumberFormat | 1.1.0 | Number formatting | MIT |
| RBush.Signed | 4.0.0 | ClosedXML indexing | MIT |
| SixLabors.Fonts | 1.0.0 | Font metrics | Apache-2.0 |
| Lucide SVG subset | 0.468.0 | Locally served UI icons | ISC; inherited Feather portions MIT |

The [resolved inventory](DEPENDENCY_INVENTORY.md) lists all transitive versions and official repository/license links. Regenerate with scripts/Write-DependencyInventory.ps1. Review licenses before upgrades, especially later SixLabors releases.

## Native SQL networking exclusion

Microsoft.Data.SqlClient.SNI.runtime 6.0.3 carries proprietary Microsoft terms. Its assets are excluded using ExcludeAssets=all and PrivateAssets=all. It may be present in NuGet's resolution cache but must not ship. SqlClient 7.0.3 avoids the earlier OneAuth dependency chain.

The app enables managed networking on Windows for local development. Microsoft's [AppContext switch documentation](https://learn.microsoft.com/en-us/sql/connect/ado-net/appcontext-switches) describes the switch for testing/debugging. Windows production hosting and the original strict open-source constraint require a support/licensing decision before deployment; see PROGRAMMERS_GUIDE.md.

The UI uses application CSS/JavaScript, native browser APIs and 18 SVGs from [Lucide 0.468.0](https://github.com/lucide-icons/lucide/tree/0.468.0/icons). Its upstream ISC notice acknowledging Feather MIT portions is retained in wwwroot/icons/lucide/LICENSE. Unused inherited wwwroot/lib files retain their MIT notices. Node and Edge are developer test tools, not application dependencies.

