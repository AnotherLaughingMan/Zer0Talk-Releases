param(
    [string]$ProcessName = 'Zer0Talk',
    [int]$GraceMillis = 1500,
    [switch]$Force,
    [switch]$VerboseMode
)

Write-Host "[prebuild] Checking for running $ProcessName.exe..." -ForegroundColor DarkCyan
$procs = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID }
if (-not $procs) {
    Write-Host "[prebuild] No running instance found." -ForegroundColor DarkGray
    exit 0
}

foreach ($p in $procs) {
    try {
        $path = $null
        try { $path = $p.MainModule.FileName } catch { }
        Write-Host "[prebuild] Found PID $($p.Id) Path=$path" -ForegroundColor Yellow
        if (-not $Force) {
            if ($p.MainWindowHandle -ne 0) {
                Write-Host "[prebuild] Sending CloseMainWindow() to PID $($p.Id)" -ForegroundColor Yellow
                $null = $p.CloseMainWindow()
            } else {
                Write-Host "[prebuild] No window handle; will terminate." -ForegroundColor DarkYellow
            }
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            while (-not $p.HasExited -and $sw.ElapsedMilliseconds -lt $GraceMillis) {
                Start-Sleep -Milliseconds 100
                try { $p.Refresh() } catch { break }
            }
        }
        if (-not $p.HasExited) {
            Write-Host "[prebuild] Forcing Kill() on PID $($p.Id)" -ForegroundColor Red
            $p.Kill()
        }
        Write-Host "[prebuild] Terminated PID $($p.Id)." -ForegroundColor Green
    } catch {
        Write-Host "[prebuild] Error handling PID $($p.Id): $_" -ForegroundColor Red
    }
}

exit 0
