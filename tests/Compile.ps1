param(
  [string]$OutputPath = "$PSScriptRoot/../artifacts/GifDisplay.dll",
  [string]$BuildLog
)
$ErrorActionPreference = 'Stop'
Add-Type -Path "$PSHOME/Microsoft.CodeAnalysis.dll"
Add-Type -Path "$PSHOME/Microsoft.CodeAnalysis.CSharp.dll"
$projectRoot = (Resolve-Path "$PSScriptRoot/../GifDisplay").Path
[xml]$project = Get-Content "$projectRoot/GifDisplay.csproj"
$trees = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($item in $project.Project.ItemGroup.Compile) {
  if ($item.Include) {
    $file = Join-Path $projectRoot $item.Include
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($file), $null, $file))
  }
}
$refs = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8.1'
$managed = Split-Path ([string]($project.Project.ItemGroup.Reference | Where-Object Include -eq 'UnityEngine.CoreModule').HintPath)
foreach ($item in $project.Project.ItemGroup.Reference) {
  $file = [string]$item.HintPath
  if (!$file) {
    # Match MSBuild: framework references without HintPath resolve against
    # the target framework rather than Unity's Managed directory.
    $file = Join-Path $framework "$($item.Include).dll"
    if (!(Test-Path $file)) { $file = Join-Path $managed "$($item.Include).dll" }
  }
  if (Test-Path $file) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file))
  }
}
$memory = Join-Path $managed 'System.Memory.dll'
if (Test-Path $memory) { $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($memory)) }
if ($BuildLog) {
  $refs.Clear()
  $logText = [IO.File]::ReadAllText((Resolve-Path $BuildLog))
  $paths = [regex]::Matches($logText, '/reference:(?:"([^"]+)"|([^\s]+))') |
    ForEach-Object { if ($_.Groups[1].Success) { $_.Groups[1].Value } else { $_.Groups[2].Value } } |
    Select-Object -Unique
  if (!$paths) { throw 'No compiler reference arguments found in the build log.' }
  foreach ($file in $paths) {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file))
  }
}
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('GifDisplay', $trees, $refs, $options)
New-Item -ItemType Directory -Path (Split-Path $OutputPath) -Force | Out-Null
$stream = [IO.File]::Create($OutputPath)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
$result.Diagnostics | Where-Object { $_.Severity -eq 'Error' } | ForEach-Object { Write-Output $_.ToString() }
if (!$result.Success) { exit 1 }
Write-Output "Release compilation passed: $OutputPath"
