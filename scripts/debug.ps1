chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$src = 'D:\csp\GifDisplay\GifDisplay\bin\Debug\GifDisplay.dll'
$srcInfo = 'D:\csp\GifDisplay\Info.json'
$srcLang = 'D:\csp\GifDisplay\lang'
$destDir = 'D:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\Mods\GifDisplay'
$destFile = Join-Path $destDir 'GifDisplay.dll'
$exe = 'D:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice.exe'
$log = 'C:\Users\Skrepy\AppData\LocalLow\7th Beat Games\A Dance of Fire and Ice\Player.log'

$availableColors = @(
    'Cyan', 'Magenta', 'Yellow', 'Green', 'DarkBlue', 'DarkCyan',
    'DarkMagenta', 'DarkRed', 'DarkGreen', 'DarkYellow', 'Blue',
    'DarkGray', 'Gray', 'White'
)
$colorIndex = 0
$modColorCache = @{ }

Clear-Host
function Get-ModColor($modName)
{
    if (-not $modColorCache.ContainsKey($modName))
    {
        $color = $availableColors[$colorIndex % $availableColors.Count]
        $modColorCache[$modName] = $color
        $script:colorIndex++
    }
    return $modColorCache[$modName]
}

function Get-LevelColor($text)
{
    if ($text -match '\b(ERROR|Exception|Failed)\b')
    {
        return 'Red'
    }
    if ($text -match '\b(WARN|Warning)\b')
    {
        return 'Yellow'
    }
    if ($text -match '\b(INFO|Success|Loaded|Initialized|Active|Complete)\b')
    {
        return 'Green'
    }
    return 'Gray'
}

function Write-ColorLogLine($line)
{
    if ($line -match '^\[([^\]]+)\]')
    {
        $rawMod = $Matches[1]
        $modName = ($rawMod -split '[\s/:]+')[0]
        $modColor = Get-ModColor $modName

        $closeBracket = $line.IndexOf(']')
        if ($closeBracket -gt 0)
        {
            $modPart = $line.Substring(0, $closeBracket + 1)
            $rest = $line.Substring($closeBracket + 1)
        }
        else
        {
            $modPart = $Matches[0]
            $rest = $line.Substring($modPart.Length)
        }

        Write-Host -NoNewline $modPart -ForegroundColor $modColor
        $levelColor = Get-LevelColor $rest
        Write-Host $rest -ForegroundColor $levelColor
    }
    else
    {
        $color = Get-LevelColor $line
        Write-Host $line -ForegroundColor $color
    }
}

function Write-Color($text, $color)
{
    Write-Host $text -ForegroundColor $color
}

# 确保目标目录存在
if (-not (Test-Path $destDir))
{
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    Write-Color '[WARN] Target directory created' Yellow
}

# 复制 DLL
Copy-Item -Path $src -Destination $destFile -Force
if ($?)
{
    Write-Color '[SUCCESS] DLL copied' Green
}
else
{
    Write-Color '[ERROR] Copy failed' Red
    Read-Host 'Press Enter to exit'
    exit 1
}

# 复制 Info.json
if (Test-Path $srcInfo)
{
    Copy-Item -Path $srcInfo -Destination $destDir -Force
    Write-Color '[SUCCESS] Info.json copied' Green
}
else
{
    Write-Color '[WARN] Info.json not found' Yellow
}

# 复制 lang 文件夹
if (Test-Path $srcLang)
{
    Copy-Item -Path $srcLang -Destination $destDir -Recurse -Force
    Write-Color '[SUCCESS] lang folder copied' Green
}
else
{
    Write-Color '[WARN] lang folder not found' Yellow
}

Write-Color '[INFO] Launching game...' Cyan
$gameProcess = Start-Process -FilePath $exe -PassThru
Write-Color "[INFO] Game PID = $( $gameProcess.Id )" Cyan

$logJob = Start-Job -ScriptBlock { param($logPath) Get-Content -Path $logPath -Wait } -ArgumentList $log

Write-Color '[INFO] Monitoring log (game exit will stop automatically)...' Cyan

while (-not $gameProcess.HasExited)
{
    $logJob | Receive-Job | ForEach-Object { Write-ColorLogLine $_ }
    Start-Sleep -Milliseconds 500
}

$logJob | Receive-Job | ForEach-Object { Write-ColorLogLine $_ }
Write-Color "[INFO] Game exited, stopping log monitor..." Yellow
Stop-Job -Job $logJob
Remove-Job -Job $logJob -Force

Write-Color '[INFO] Script finished.' Cyan