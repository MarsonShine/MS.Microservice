param(
    [Parameter(Mandatory)][string[]]$Modules,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'module-tools.ps1')
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Export requires a new directory; existing files are never replaced.' }
$projects = @(Get-ModuleProjects $root $Modules)
$files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'nuget.config')) {
    [void]$files.Add($file)
}
foreach ($project in $projects) {
    $directory = [IO.Path]::GetRelativePath($root, (Split-Path $project -Parent)).Replace('\', '/')
    $paths = @($directory)
    if ([IO.Path]::GetFileNameWithoutExtension($project) -eq 'MS.Microservice.Excel') {
        $paths += 'src/MS.Microservice.Excel.Aot'
    }
    $segments = $directory.Split('/')
    if ($segments.Length -ge 3 -and $segments[0].StartsWith('MS.Microservice.') -and $segments[1] -eq 'src') {
        $paths += @("$($segments[0])/README.md", "$($segments[0])/docs")
    }
    $tracked = @(& git -C $root -c core.quotepath=false ls-files -- @paths)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked component files.' }
    foreach ($file in $tracked) {
        if (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf) { [void]$files.Add($file) }
    }
}
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($file in $files) {
    $target = [IO.Path]::GetFullPath((Join-Path $output $file))
    if (-not $target.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Export path escaped its destination: $file"
    }
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
}
$projectPaths = @($projects | ForEach-Object { [IO.Path]::GetRelativePath($root, $_).Replace('\', '/') })
$solution = @('<Solution>') + @($projectPaths | ForEach-Object { '  <Project Path="' + $_ + '" />' }) + @('</Solution>')
[IO.File]::WriteAllLines((Join-Path $output 'modules.slnx'), $solution)
$revision = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source revision.' }
$manifest = [ordered]@{
    formatVersion = 1; revision = $revision; modules = $Modules; projects = $projectPaths
    files = @($files | Sort-Object | ForEach-Object {
        @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $output $_) -Algorithm SHA256).Hash }
    })
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'module-manifest.json')
Write-Output "Exported $($projectPaths.Count) projects to $output"
