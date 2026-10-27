# Resolved application dependency inventory

Generated from restored NuGet metadata. Runtime and design-time packages are included; packages.lock.json pins exact versions. Significant dependency usage is summarized in THIRD_PARTY_LIBRARIES.md. SNI is resolved by NuGet but all assets are excluded from use/publication.

| Library | Version | License | Official project/repository | Used by |
|---|---|---|---|---|
| ClosedXML | 0.105.1 | MIT | https://github.com/ClosedXML/ClosedXML | ExcelExportService via ClosedXML |
| ClosedXML.Parser | 2.0.0 | MIT | https://github.com/ClosedXML/ClosedXML.Parser | ExcelExportService via ClosedXML |
| DocumentFormat.OpenXml | 3.1.1 | MIT | https://github.com/dotnet/Open-XML-SDK | ExcelExportService via ClosedXML |
| DocumentFormat.OpenXml.Framework | 3.1.1 | MIT | https://github.com/dotnet/Open-XML-SDK | ExcelExportService via ClosedXML |
| ExcelNumberFormat | 1.1.0 | MIT | https://github.com/andersnm/ExcelNumberFormat | ExcelExportService via ClosedXML |
| Humanizer.Core | 2.14.1 | MIT | https://github.com/Humanizr/Humanizer | EF migration design tooling |
| Microsoft.AspNetCore.Authentication.Negotiate | 10.0.12 | MIT | https://github.com/dotnet/dotnet | Windows authentication |
| Microsoft.Bcl.Cryptography | 9.0.13 | MIT | https://github.com/dotnet/runtime | Framework/runtime support |
| Microsoft.Build.Framework | 18.0.2 | MIT | https://github.com/dotnet/msbuild | EF migration design tooling |
| Microsoft.CodeAnalysis.Analyzers | 3.11.0 | MIT | https://github.com/dotnet/roslyn-analyzers | EF migration design tooling |
| Microsoft.CodeAnalysis.Common | 5.0.0 | MIT | https://github.com/dotnet/roslyn | EF migration design tooling |
| Microsoft.CodeAnalysis.CSharp | 5.0.0 | MIT | https://github.com/dotnet/roslyn | EF migration design tooling |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.0.0 | MIT | https://github.com/dotnet/roslyn | EF migration design tooling |
| Microsoft.CodeAnalysis.Workspaces.Common | 5.0.0 | MIT | https://github.com/dotnet/roslyn | EF migration design tooling |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 5.0.0 | MIT | https://github.com/dotnet/roslyn | EF migration design tooling |
| Microsoft.Data.SqlClient | 7.0.3 | MIT | https://github.com/dotnet/sqlclient | AppDbContext / SQL persistence |
| Microsoft.Data.SqlClient.Extensions.Abstractions | 7.0.3 | MIT | https://github.com/dotnet/SqlClient | AppDbContext / SQL persistence |
| Microsoft.Data.SqlClient.Internal.Logging | 7.0.3 | MIT | https://github.com/dotnet/SqlClient | AppDbContext / SQL persistence |
| Microsoft.Data.SqlClient.SNI.runtime | 6.0.3 | Proprietary - EXCLUDED | https://aka.ms/sqlclientproject | Not used or shipped; ExcludeAssets=all |
| Microsoft.EntityFrameworkCore | 10.0.12 | MIT | https://github.com/dotnet/dotnet | AppDbContext / SQL persistence |
| Microsoft.EntityFrameworkCore.Abstractions | 10.0.12 | MIT | https://github.com/dotnet/dotnet | AppDbContext / SQL persistence |
| Microsoft.EntityFrameworkCore.Analyzers | 10.0.12 | MIT | https://github.com/dotnet/dotnet | AppDbContext / SQL persistence |
| Microsoft.EntityFrameworkCore.Design | 10.0.12 | MIT | https://github.com/dotnet/dotnet | EF migration design tooling |
| Microsoft.EntityFrameworkCore.Relational | 10.0.12 | MIT | https://github.com/dotnet/dotnet | AppDbContext / SQL persistence |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.12 | MIT | https://github.com/dotnet/dotnet | AppDbContext / SQL persistence |
| Microsoft.Extensions.DependencyModel | 10.0.12 | MIT | https://github.com/dotnet/dotnet | Framework/runtime support |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.12 | MIT | https://github.com/dotnet/dotnet | Windows Service hosting and Event Log integration |
| Microsoft.IdentityModel.Abstractions | 8.16.0 | MIT | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet | Transitive SQL driver identity support (no application OIDC login) |
| Microsoft.IdentityModel.JsonWebTokens | 8.16.0 | MIT | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet | Transitive SQL driver identity support (no application OIDC login) |
| Microsoft.IdentityModel.Logging | 8.16.0 | MIT | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet | Transitive SQL driver identity support (no application OIDC login) |
| Microsoft.IdentityModel.Protocols | 8.16.0 | MIT | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet | Transitive SQL driver identity support (no application OIDC login) |
| Microsoft.IdentityModel.Protocols.OpenIdConnect | 8.16.0 | MIT | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet | Transitive SQL driver identity support (no application OIDC login) |
| Microsoft.IdentityModel.Tokens | 8.16.0 | MIT | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet | Transitive SQL driver identity support (no application OIDC login) |
| Microsoft.SqlServer.Server | 1.0.0 | MIT | https://github.com/dotnet/sqlclient | AppDbContext / SQL persistence |
| Microsoft.VisualStudio.SolutionPersistence | 1.0.52 | MIT | https://github.com/microsoft/vs-solutionpersistence | EF migration design tooling |
| Mono.TextTemplating | 3.0.0 | MIT | https://github.com/mono/t4 | EF migration design tooling |
| Newtonsoft.Json | 13.0.4 | MIT | https://github.com/JamesNK/Newtonsoft.Json | Framework/runtime support |
| RBush.Signed | 4.0.0 | MIT | https://github.com/viceroypenguin/RBush | ExcelExportService via ClosedXML |
| SixLabors.Fonts | 1.0.0 | Apache-2.0 | https://github.com/SixLabors/Fonts | ExcelExportService via ClosedXML |
| System.CodeDom | 6.0.0 | MIT | https://github.com/dotnet/runtime | EF migration design tooling |
| System.Composition | 9.0.0 | MIT | https://github.com/dotnet/runtime | EF migration design tooling |
| System.Composition.AttributedModel | 9.0.0 | MIT | https://github.com/dotnet/runtime | EF migration design tooling |
| System.Composition.Convention | 9.0.0 | MIT | https://github.com/dotnet/runtime | EF migration design tooling |
| System.Composition.Hosting | 9.0.0 | MIT | https://github.com/dotnet/runtime | EF migration design tooling |
| System.Composition.Runtime | 9.0.0 | MIT | https://github.com/dotnet/runtime | EF migration design tooling |
| System.Composition.TypedParts | 9.0.0 | MIT | https://github.com/dotnet/runtime | EF migration design tooling |
| System.Configuration.ConfigurationManager | 9.0.13 | MIT | https://github.com/dotnet/runtime | Framework/runtime support |
| System.DirectoryServices.Protocols | 10.0.12 | MIT | https://github.com/dotnet/dotnet | Windows authentication |
| System.IdentityModel.Tokens.Jwt | 8.16.0 | MIT | https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet | Transitive SQL driver identity support (no application OIDC login) |
| System.IO.Packaging | 8.0.1 | MIT | https://github.com/dotnet/runtime | ExcelExportService via ClosedXML |
| System.Security.Cryptography.Pkcs | 9.0.13 | MIT | https://github.com/dotnet/runtime | Framework/runtime support |
| System.Security.Cryptography.ProtectedData | 9.0.13 | MIT | https://github.com/dotnet/runtime | Framework/runtime support |
| System.ServiceProcess.ServiceController | 10.0.12 | MIT | https://github.com/dotnet/dotnet | Windows Service hosting and Event Log integration |
| Lucide SVG subset | 0.468.0 | ISC / Feather MIT | https://github.com/lucide-icons/lucide/tree/0.468.0 | 18 local SVG UI icons; license in wwwroot/icons/lucide/LICENSE |
