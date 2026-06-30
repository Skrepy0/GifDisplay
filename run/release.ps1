chcp 65001 > $null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$src = 'D:\csp\GifDisplay\GifDisplay\bin\Release\GifDisplay.dll'
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
        # 提取模组名：按空格、斜杠、冒号分割取第一个单词
        $modName = ($rawMod -split '[\s/:]+')[0]
        $modColor = Get-ModColor $modName

        # 直接找到第一个 ']' 的位置，确保分割准确
        $closeBracket = $line.IndexOf(']')
        if ($closeBracket -gt 0)
        {
            $modPart = $line.Substring(0, $closeBracket + 1)   # 包含 ']'
            $rest = $line.Substring($closeBracket + 1)
        }
        else
        {
            # 容错：如果没找到，按原逻辑
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

if (-not (Test-Path $destDir))
{
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    Write-Color '[WARN] Target directory created' Yellow
}
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