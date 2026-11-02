[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UnityEditor,
    [Parameter(Mandatory = $true)][string]$Analyzer,
    [Parameter(Mandatory = $true)][string]$Model,
    [Parameter(Mandatory = $true)][string]$Fixture,
    [Parameter(Mandatory = $true)][string]$McpDependencyDirectory,
    [Parameter(Mandatory = $true)][string]$ConsoleAssembly,
    [Parameter(Mandatory = $true)][string]$ProjectDirectory,
    [string]$RepositoryRoot,
    [ValidateRange(60, 7200)][int]$TimeoutSeconds = 1800,
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
function FullFile([string]$Value) {
    $resolved = (Resolve-Path -LiteralPath $Value).Path
    if (-not [IO.File]::Exists($resolved)) { throw "Required file is missing: $Value" }
    return $resolved
}
function Digest([string]$File) { return (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteUtf8([string]$File, [string]$Value) { [IO.File]::WriteAllText($File, $Value, (New-Object Text.UTF8Encoding($false))) }
function JsonFile([string]$File, $Value) { WriteUtf8 $File ($Value | ConvertTo-Json -Depth 10) }

if ([string]::IsNullOrEmpty($RepositoryRoot)) { $RepositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$UnityEditor = FullFile $UnityEditor
$Analyzer = FullFile $Analyzer
$Model = FullFile $Model
$Fixture = FullFile $Fixture
$fixtureManifest = FullFile ([IO.Path]::ChangeExtension($Fixture, '.json'))
$ConsoleAssembly = FullFile $ConsoleAssembly
$McpDependencyDirectory = (Resolve-Path -LiteralPath $McpDependencyDirectory).Path
foreach ($required in @('McpPlugin.dll', 'McpPlugin.Common.dll', 'ReflectorNet.dll')) {
    $null = FullFile (Join-Path $McpDependencyDirectory $required)
}
$ProjectDirectory = [IO.Path]::GetFullPath($ProjectDirectory)
if (Test-Path -LiteralPath $ProjectDirectory) { throw 'Select a fresh project directory. This script never replaces or deletes an existing project.' }

$runtimeSources = @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Runtime') -Recurse -Filter '*.cs')
$testSources = @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Tests/Editor') -Filter 'RuntimeSmoke*Tests.cs')
$testSources += Get-Item -LiteralPath (Join-Path $RepositoryRoot 'Tests/Editor/AthTraceWriterTests.cs')
$editorSources = @('Editor/AssemblyInfo.cs', 'Editor/McpSkills/AthTraceWriter.cs', 'Editor/McpSkills/AthTraceEmitter.cs',
    'Editor/McpSkills/AthMediaUtil.cs', 'Editor/McpSkills/Tool_AthTraceEmit.cs', 'Editor/McpSkills/Tool_AthRuntimeSmoke.cs',
    'Editor/RuntimeSmokeWindow.cs', 'Editor/RuntimeSmokeEditorWindowViewModel.cs') |
    ForEach-Object { Get-Item -LiteralPath (Join-Path $RepositoryRoot $_) }
foreach ($selection in @(@{ files = $runtimeSources }, @{ files = $testSources }, @{ files = $editorSources })) {
    if (@($selection.files | Group-Object Name | Where-Object Count -gt 1).Count -gt 0) {
        throw 'Source selection has duplicate filenames; update the isolated layout explicitly before copying.'
    }
}

foreach ($folder in @('Assets/Runtime', 'Assets/Tests', 'Assets/EditorTrace', 'Assets/Plugins', 'Packages', 'ProjectSettings', 'Verification')) {
    $null = New-Item -ItemType Directory -Path (Join-Path $ProjectDirectory $folder) -Force
}
$snapshots = @()
foreach ($selection in @(@{ files = $runtimeSources; target = 'Assets/Runtime' }, @{ files = $testSources; target = 'Assets/Tests' },
    @{ files = $editorSources; target = 'Assets/EditorTrace' })) {
    foreach ($source in $selection.files) {
        $destination = Join-Path (Join-Path $ProjectDirectory $selection.target) $source.Name
        Copy-Item -LiteralPath $source.FullName -Destination $destination
        $snapshots += [ordered]@{ path = $source.FullName.Substring($RepositoryRoot.TrimEnd('\', '/').Length + 1).Replace('\', '/');
            sha256 = Digest $source.FullName; copiedPath = $selection.target + '/' + $source.Name }
    }
}
$dependencies = @(Get-ChildItem -LiteralPath $McpDependencyDirectory -Filter '*.dll')
foreach ($dependency in $dependencies) { Copy-Item -LiteralPath $dependency.FullName -Destination (Join-Path $ProjectDirectory 'Assets/Plugins') }
Copy-Item -LiteralPath $ConsoleAssembly -Destination (Join-Path $ProjectDirectory 'Assets/Plugins/IngameDebugConsole.Runtime.dll')

WriteUtf8 (Join-Path $ProjectDirectory 'Assets/Runtime/Runtime.asmdef') '{"name":"LlamaBrainLabs.Ath.Runtime"}'
WriteUtf8 (Join-Path $ProjectDirectory 'Assets/EditorTrace/Editor.asmdef') '{"name":"LlamaBrainLabs.Ath.Editor","references":["LlamaBrainLabs.Ath.Runtime"],"includePlatforms":["Editor"],"overrideReferences":true,"precompiledReferences":["ReflectorNet.dll","McpPlugin.dll","McpPlugin.Common.dll"]}'
Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'Tests/Editor/LlamaBrainLabs.Ath.Editor.Tests.asmdef') -Destination (Join-Path $ProjectDirectory 'Assets/Tests/Tests.asmdef')
WriteUtf8 (Join-Path $ProjectDirectory 'Packages/manifest.json') '{"dependencies":{"com.unity.test-framework":"1.6.0","com.unity.modules.screencapture":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.ugui":"2.0.0","com.unity.inputsystem":"1.18.0"}}'
WriteUtf8 (Join-Path $ProjectDirectory 'ProjectSettings/ProjectVersion.txt') 'm_EditorVersion: 6000.3.10f1'

$proofDirectory = Join-Path $ProjectDirectory 'Verification'
$toolInputs = @($Analyzer, $Model, $Fixture, $fixtureManifest, $ConsoleAssembly) + @($dependencies.FullName)
$inputHashes = @($toolInputs | ForEach-Object { [ordered]@{ path = $_; sha256 = Digest $_ } })
$setup = [ordered]@{ schemaVersion = 1; status = 'prepared'; testedUnityVersion = '6000.3.10f1'; testFrameworkVersion = '1.6.0';
    unityExecutable = $UnityEditor; unityFileVersion = (Get-Item -LiteralPath $UnityEditor).VersionInfo.FileVersion;
    sourceSnapshots = $snapshots; inputHashes = $inputHashes;
    excludedTestClasses = @();
    limitations = @('Isolated source validation, not a consuming game build', 'No live game capture or built-player launch verification') }
JsonFile (Join-Path $proofDirectory 'setup.json') $setup
if ($PrepareOnly) { Write-Output "Prepared isolated validation project: $ProjectDirectory"; return }

$savedEnvironment = @{}
$childEnvironment = @{ ATH_VISUAL_ANALYZER = $Analyzer; ATH_VISUAL_MODEL = $Model; ATH_VISUAL_FIXTURE = $Fixture }
$process = $null
try {
    foreach ($key in $childEnvironment.Keys) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $childEnvironment[$key], 'Process')
    }
    $arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $ProjectDirectory + '"'), '-runTests', '-testPlatform', 'EditMode',
        '-testResults', ('"' + (Join-Path $proofDirectory 'results.xml') + '"'), '-logFile', ('"' + (Join-Path $proofDirectory 'editor.log') + '"'))
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
} finally {
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
}

$timer = [Diagnostics.Stopwatch]::StartNew()
while (-not $process.WaitForExit(1000)) {
    if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
        $process.Kill()
        $null = $process.WaitForExit(5000)
        throw "Owned Unity validation process exceeded $TimeoutSeconds seconds. Evidence retained: $proofDirectory"
    }
}
$process.Refresh()
$resultPath = Join-Path $proofDirectory 'results.xml'
if (-not (Test-Path -LiteralPath $resultPath)) { throw "Unity produced no test results (exit $($process.ExitCode)). Inspect $proofDirectory/editor.log" }
[xml]$results = Get-Content -LiteralPath $resultPath -Raw
$cases = @($results.SelectNodes('//test-case'))
$failedCases = @($cases | Where-Object { $_.result -ne 'Passed' })
$changedInputs = @($inputHashes | Where-Object { (Digest $_.path) -ne $_.sha256 })
$changedSources = @($snapshots | Where-Object { (Digest (Join-Path $ProjectDirectory $_.copiedPath)) -ne $_.sha256 })
$nativeCases = @($cases | Where-Object { $_.fullname -like '*.NativeAnalyzerOutcomesReachTheAuthoritativeCoordinator' })
$passed = $process.ExitCode -eq 0 -and $results.'test-run'.result -eq 'Passed' -and $cases.Count -gt 0 -and
    [int]$results.'test-run'.total -eq $cases.Count -and [int]$results.'test-run'.passed -eq $cases.Count -and
    [int]$results.'test-run'.failed -eq 0 -and [int]$results.'test-run'.skipped -eq 0 -and
    $failedCases.Count -eq 0 -and $nativeCases.Count -eq 1 -and $changedInputs.Count -eq 0 -and $changedSources.Count -eq 0
$report = [ordered]@{ schemaVersion = 1; passed = $passed; processExitCode = $process.ExitCode; elapsedSeconds = $timer.Elapsed.TotalSeconds;
    reportedResult = $results.'test-run'.result; reportedTotal = $results.'test-run'.total; actualTestcaseCount = $cases.Count;
    nonpassingTestcases = @($failedCases | ForEach-Object { $_.fullname }); changedInputs = $changedInputs; changedSources = $changedSources;
    nativeSuiteCases = @('pass', 'fail', 'inconclusive', 'corrupt', 'wrong-correlation', 'missing-analyzer', 'adjustable', 'unresolved-profile', 'missing-source', 'optional-id', 'boundless') }
JsonFile (Join-Path $proofDirectory 'verification.json') $report
if (-not $passed) { throw "Unity validation did not pass completely. Inspect retained proof: $proofDirectory" }
Write-Output "Verified $($cases.Count) tests, including native RuntimeSmoke suite execution. Evidence: $proofDirectory"
