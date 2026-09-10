param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$sdk = (Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).sdk.version
$dockerfile = Get-Content (Join-Path $root 'Dockerfile') -Raw
if ($dockerfile -notmatch ('FROM mcr\.microsoft\.com/dotnet/sdk:' + [regex]::Escape($sdk) + '\s')) {
    throw 'Docker SDK must match global.json.'
}
$restore = [regex]::Match($dockerfile, 'RUN dotnet restore ([^\s]+\.csproj)')
if (-not $restore.Success) { throw 'Dockerfile must restore an explicit host project.' }
$copied = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($match in [regex]::Matches($dockerfile.Substring(0, $restore.Index), '(?m)^COPY ([^\s]+\.csproj) ')) {
    [void]$copied.Add([IO.Path]::GetFullPath((Join-Path $root $match.Groups[1].Value)))
}
$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Visit-Project([string]$project) {
    if (-not $visited.Add($project)) { return }
    if (-not (Test-Path -LiteralPath $project)) { throw "Missing project: $project" }
    if (-not $copied.Contains($project)) { throw "Docker restore has no COPY for $project" }
    [xml]$document = Get-Content -LiteralPath $project -Raw
    foreach ($reference in $document.Project.ItemGroup.ProjectReference) {
        if ($reference.Include) {
            Visit-Project ([IO.Path]::GetFullPath((Join-Path (Split-Path $project -Parent) $reference.Include)))
        }
    }
}
Visit-Project ([IO.Path]::GetFullPath((Join-Path $root $restore.Groups[1].Value)))
Write-Output "Validated SDK $sdk and $($visited.Count) projects in the Docker restore dependency graph."
