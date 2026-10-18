param(
    [Parameter(Mandatory)][IO.FileInfo[]]$Files,
    [Parameter(Mandatory)][string]$BaseUri,
    [Parameter(Mandatory)][hashtable]$Headers,
    [ValidateRange(0, 120)][int]$RetryDelaySeconds = 15
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

foreach ($file in $Files) {
    $uri = "$($BaseUri.TrimEnd('/'))/$([uri]::EscapeDataString($file.Name))"
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        Write-Host "Uploading $($file.Name) ($($file.Length) bytes), attempt $attempt/5"
        try {
            # Reopen the file on every attempt, including after a broken transport connection.
            # 300 s per attempt: a stalled upload is retried quickly instead of eating the one-hour job limit
            # (v1.0.14: three 900 s attempts stalled on the first file). At ~1.2 MB/s the largest file takes
            # about 95 s. ponytail: a fixed limit, raise it if uploads get slower than ~400 KB/s.
            Invoke-RestMethod -Method Put -InFile $file.FullName -ContentType 'application/octet-stream' `
                -Headers $Headers -Uri $uri -MaximumRetryCount 0 -TimeoutSec 300 | Out-Null
            break
        } catch {
            $exception = $_.Exception
            $status = if ($exception.Response) { [int]$exception.Response.StatusCode } else { 0 }
            $transport = $exception -is [Net.Http.HttpRequestException] -or
                $exception -is [Net.Http.HttpIOException] -or $exception -is [Net.WebException] -or
                $exception -is [OperationCanceledException]
            $retryable = $status -in 408, 429, 500, 502, 503, 504 -or ($status -eq 0 -and $transport)
            if (-not $retryable -or $attempt -eq 5) { throw }
            $delay = [math]::Min(120, $RetryDelaySeconds * [math]::Pow(2, $attempt - 1))
            $retryAfter = $exception.Response.Headers.RetryAfter
            if ($retryAfter.Delta) { $delay = [math]::Max($delay, $retryAfter.Delta.TotalSeconds) }
            if ($retryAfter.Date) {
                $delay = [math]::Max($delay, ($retryAfter.Date - [DateTimeOffset]::UtcNow).TotalSeconds)
            }
            $reason = if ($status) { "HTTP $status" } else { 'transport connection interrupted' }
            Write-Warning "$($file.Name): $reason; retrying in $([math]::Ceiling($delay)) seconds"
            Start-Sleep -Seconds ([math]::Ceiling($delay))
        }
    }
}
