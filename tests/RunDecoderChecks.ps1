$ErrorActionPreference = 'Stop'
$source = @("$PSScriptRoot/DecoderChecks.cs") + @(Get-ChildItem "$PSScriptRoot/../GifDisplay/UniGif" -Filter '*.cs' | ForEach-Object FullName)
Add-Type -Path $source
[UniGif]::RunChecks()
