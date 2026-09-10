param([switch]$ResetDedicatedVolumes)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'local-tools.ps1')
if (-not $ResetDedicatedVolumes) { throw 'Pass -ResetDedicatedVolumes to explicitly delete this compose project''s development data.' }
[void](Get-LocalSettings)
Invoke-LocalCompose down --volumes
Write-Output 'Only ms-reference-lab compose volumes were reset. Legacy databases outside that project were not targeted.'
