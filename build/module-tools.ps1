Set-StrictMode -Version Latest

# Shared projects may be small root components or members of an independently opened module family.
# Classification depends on source roots, never on an assembly-name prefix or on the main solution.
function Get-SharedProjects {
    param([string]$RepositoryRoot)
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $roots = @((Join-Path $root 'src')) + @(Get-ChildItem -LiteralPath $root -Directory -Filter 'MS.Microservice.*' |
        ForEach-Object { Join-Path $_.FullName 'src' } | Where-Object { Test-Path -LiteralPath $_ -PathType Container })
    foreach ($source in $roots) {
        foreach ($directory in Get-ChildItem -LiteralPath $source -Directory) {
            $project = Join-Path $directory.FullName ($directory.Name + '.csproj')
            if (Test-Path -LiteralPath $project -PathType Leaf) { [IO.Path]::GetFullPath($project) }
        }
    }
}

# Compiler-only projects belong to the source/build graph but have no standalone runtime package.
function Test-ProjectPackable {
    param([string]$Project)
    [xml]$document = Get-Content -LiteralPath $Project -Raw
    $setting = $document.SelectSingleNode('/Project/PropertyGroup[not(@Condition)]/IsPackable')
    return $null -eq $setting -or $setting.InnerText -ne 'false'
}

function Get-ModuleProjects {
    param([string]$RepositoryRoot, [string[]]$Modules)
    $available = @{}
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($project in @(Get-SharedProjects $RepositoryRoot)) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        if ($available.ContainsKey($name)) { throw "Ambiguous component name: $name" }
        $available[$name] = $project
        [void]$allowed.Add($project)
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $ordered = [Collections.Generic.List[string]]::new()
    function Visit-ModuleProject([string]$project) {
        $project = [IO.Path]::GetFullPath($project)
        if (-not $allowed.Contains($project)) { throw "A component references a non-shared project: $project" }
        if (-not $seen.Add($project)) { return }
        [xml]$document = Get-Content -LiteralPath $project -Raw
        foreach ($reference in $document.SelectNodes('//ProjectReference')) {
            Visit-ModuleProject (Join-Path (Split-Path $project -Parent) $reference.Include)
        }
        $ordered.Add($project)
    }
    foreach ($module in $Modules) {
        if (-not $available.ContainsKey($module)) { throw "Unknown shared component: $module" }
        Visit-ModuleProject $available[$module]
    }
    $ordered.ToArray()
}
function Invoke-CheckedDotnet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}
