param(
    [string[]]$Modules,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Version = '1.0.0-local',
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'module-tools.ps1')
$root = [IO.Path]::GetFullPath($RepositoryRoot)
if (-not $Modules) { $Modules = @(Get-ChildItem (Join-Path $root 'src') -Directory | Where-Object { Test-Path (Join-Path $_.FullName ($_.Name + '.csproj')) } | ForEach-Object Name) }
$projects = @(Get-ModuleProjects $root $Modules)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Package output must be a new directory.' }
New-Item -ItemType Directory $output | Out-Null
Push-Location $root
try {
    foreach ($project in $projects) {
        Invoke-CheckedDotnet pack $project -c Release --output $output "-p:PackageVersion=$Version" --verbosity quiet
    }
}
finally { Pop-Location }
Get-ChildItem -LiteralPath $output -Filter '*.nupkg' | ForEach-Object {
    [ordered]@{ file = $_.Name; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
} | ConvertTo-Json | Set-Content (Join-Path $output 'packages.sha256.json')
Write-Output "Packed $($projects.Count) components into $output"
