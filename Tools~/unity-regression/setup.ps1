param(
    [Parameter(Mandatory=$true)][string]$DependencyProject,
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$athRoot = (Resolve-Path "$PSScriptRoot/../..").Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $athRoot 'artifacts/active/unity' }
$athDependencyRoot = (Resolve-Path -LiteralPath $DependencyProject).Path
$athOutput = [System.IO.Path]::GetFullPath($OutputRoot)
foreach ($athFolder in @('Assets', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $athOutput $athFolder) -Force | Out-Null
}
$athDependencies = @{}
$athManifest = Get-Content (Join-Path $athDependencyRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
foreach ($athProperty in $athManifest.dependencies.PSObject.Properties) {
    if ($athProperty.Name.StartsWith('com.unity.modules.')) { $athDependencies[$athProperty.Name] = $athProperty.Value }
}
foreach ($athPackage in @('com.ivanmurzak.unity.mcp','extensions.unity.playerprefsex','com.unity.test-framework','com.unity.ext.nunit','com.unity.inputsystem','com.unity.ugui')) {
    $athCache = Get-ChildItem (Join-Path $athDependencyRoot 'Library/PackageCache') -Directory -Filter "$athPackage@*" | Select-Object -First 1
    if (!$athCache) { throw "Required cached dependency missing: $athPackage" }
    $athDependencies[$athPackage] = 'file:' + $athCache.FullName.Replace('\','/')
}
# Stage an exact source view as an embedded package. A local UPM link to this repository
# would recursively import the generated test host under artifacts/.
$athPackageRoot = Join-Path $athOutput 'Packages/com.llamabrainlabs.ai-test-harness'
New-Item -ItemType Directory -Path $athPackageRoot -Force | Out-Null
foreach ($athSource in @('Runtime','Runtime.meta','Editor','Editor.meta','Tests','Tests.meta','package.json')) {
    Copy-Item -LiteralPath (Join-Path $athRoot $athSource) -Destination $athPackageRoot -Recurse -Force
}
$athJson = @{ dependencies=$athDependencies; testables=@('com.llamabrainlabs.ai-test-harness') } | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText((Join-Path $athOutput 'Packages/manifest.json'), $athJson)
Copy-Item -LiteralPath (Join-Path $athDependencyRoot 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $athOutput 'ProjectSettings/ProjectVersion.txt') -Force
# Hydrate complete dependency roots, including .meta files; no gameplay or generated Library copy.
Copy-Item -LiteralPath (Join-Path $athDependencyRoot 'Assets/Plugins') -Destination (Join-Path $athOutput 'Assets') -Recurse -Force
Write-Output $athOutput
