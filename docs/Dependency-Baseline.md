# Dependency baseline

Package versions are owned by `Directory.Packages.props`. `nuget.config` clears inherited sources so a clean checkout uses the same public feed as CI; private feeds belong to a consuming application.

The baseline removes blanket vulnerability suppression. The following transitive overrides keep the existing feature packages while replacing affected dependencies:

| Dependency | Version | Reason |
| --- | --- | --- |
| Microsoft.Build.Tasks.Git | 10.0.303 | SourceLink pulled by NPOI: [CVE-2026-62900](https://github.com/advisories/GHSA-23fw-v26w-5fgq). |
| System.Security.Cryptography.Xml | 10.0.10 | XML encryption fixes, including [CVE-2026-50527](https://github.com/advisories/GHSA-mmjf-rqrv-855v). |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.2 | Replaces the obsolete native SQLite bundle with SourceGear.sqlite3; [CVE-2025-6965](https://github.com/advisories/GHSA-2m69-gcr7-jv3q). |
| SSH.NET | 2026.0.0 | SqlSugar's dependency chain: [CVE-2026-48798](https://github.com/advisories/GHSA-q939-rpr3-3284). |

The SQLite runtime regression test opens an actual in-memory database, checks the native runtime, and executes aggregate queries. Retain that test when changing the bundle; successful restore alone does not prove native compatibility.

```powershell
dotnet restore --configfile nuget.config
dotnet build --no-restore -c Release
dotnet list package --vulnerable --include-transitive
```

Re-evaluate overrides when the owning package updates its dependency requirements. A newly reported advisory should fail validation until the dependency is fixed or a specific, documented exception is reviewed; do not add a global warning suppression.
