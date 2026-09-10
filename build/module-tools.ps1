Set-StrictMode -Version Latest

function Get-ModuleProjects {
    param([string]$RepositoryRoot, [string[]]$Modules)
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $source = Join-Path $root 'src'
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $ordered = [Collections.Generic.List[string]]::new()
    function Visit-ModuleProject([string]$project) {
        $project = [IO.Path]::GetFullPath($project)
        if (-not $project.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "A reusable component references a project outside src: $project"
        }
        if (-not $seen.Add($project)) { return }
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Missing project: $project" }
        [xml]$document = Get-Content -LiteralPath $project -Raw
        foreach ($reference in $document.SelectNodes('//ProjectReference')) {
            Visit-ModuleProject (Join-Path (Split-Path $project -Parent) $reference.Include)
        }
        $ordered.Add($project)
    }
    foreach ($module in $Modules) {
        if ($module -notmatch '^MS\.Microservice\.[A-Za-z0-9.]+$') { throw "Invalid module name: $module" }
        Visit-ModuleProject (Join-Path $source "$module/$module.csproj")
    }
    $ordered.ToArray()
}

function Invoke-CheckedDotnet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}
