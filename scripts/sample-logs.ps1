param(
    [int]$Minutes = 5,
    [int]$IntervalSeconds = 60
)

$iterations = [Math]::Ceiling(($Minutes * 60) / $IntervalSeconds)
for ($i = 1; $i -le $iterations; $i++) {
    Write-Host "=== Sample #$i @ $(Get-Date -Format 'HH:mm:ss')"

    if (Test-Path 'bin\Debug\net9.0\logs\app.log') {
        Write-Host '--- app.log (filtered) ---'
        Get-Content 'bin\Debug\net9.0\logs\app.log' -Tail 200 | Select-String -Pattern 'UPnP verify|AutoMap|hairpin|handshake|bind-ok'
    }
    else {
        Write-Host 'app.log not found'
    }

    if (Test-Path 'bin\Debug\net9.0\logs\network.log') {
        Write-Host '--- network.log (filtered) ---'
        Get-Content 'bin\Debug\net9.0\logs\network.log' -Tail 200 | Select-String -Pattern 'session|connect|handshake|ECDH|HKDF|AEAD|error|fail|timeout'
    }
    else {
        Write-Host 'network.log not found'
    }

    if ($i -lt $iterations) { Start-Sleep -Seconds $IntervalSeconds }
}