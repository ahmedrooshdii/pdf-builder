<#
.SYNOPSIS
    Submits a PDF job, polls it to completion, and downloads the result.

.DESCRIPTION
    Wraps the three calls the API needs - POST the job, poll the status URL, GET the file - so
    trying a payload is one command instead of three.

.PARAMETER PayloadPath
    A JSON file holding the template data. This is the "data" field of the request, not the whole
    request body: the script wraps it for you.

.PARAMETER TemplateId
    Which registered template to render. Defaults to "invoice".

.PARAMETER BaseUrl
    Where the API is listening. Defaults to the local `dotnet run` address.

.PARAMETER OutFile
    Where to save the PDF. Defaults to the payload's name with a .pdf extension, in the temp folder.

.PARAMETER Open
    Open the PDF in the default viewer once it is downloaded.

.EXAMPLE
    ./samples/Invoke-PdfJob.ps1 -PayloadPath templates/invoice.sample.json -Open

.EXAMPLE
    ./samples/Invoke-PdfJob.ps1 -PayloadPath samples/multipage.json -BaseUrl http://localhost:8080
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PayloadPath,

    [string] $TemplateId = 'invoice',
    [string] $BaseUrl = 'http://localhost:5052',
    [string] $OutFile,
    [int]    $TimeoutSeconds = 120,
    [switch] $Open
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $PayloadPath)) { throw "Payload not found: $PayloadPath" }

$BaseUrl = $BaseUrl.TrimEnd('/')
if (-not $OutFile) {
    $OutFile = Join-Path ([IO.Path]::GetTempPath()) "$([IO.Path]::GetFileNameWithoutExtension($PayloadPath)).pdf"
}

# Parsed and re-serialised rather than pasted in as text, so a malformed payload is caught here
# with a clear error instead of arriving at the API as an unreadable 400.
$data = Get-Content $PayloadPath -Raw | ConvertFrom-Json
$body = @{ templateId = $TemplateId; data = $data } | ConvertTo-Json -Depth 20 -Compress

Write-Host "POST $BaseUrl/api/pdf/jobs  (template: $TemplateId)" -ForegroundColor Cyan

try {
    $accepted = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/pdf/jobs" `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
}
catch {
    # A 400 carries a ProblemDetails body naming the rule that was broken. That is the useful part,
    # so print it and stop - a PowerShell stack trace through Invoke-RestMethod tells nobody anything.
    $response = $_.Exception.Response
    if ($response) {
        $detail = (New-Object IO.StreamReader($response.GetResponseStream())).ReadToEnd()
        try {
            $problem = $detail | ConvertFrom-Json
            Write-Host "Rejected ($($problem.status)): $($problem.title)" -ForegroundColor Red
            foreach ($field in $problem.errors.PSObject.Properties) {
                foreach ($message in $field.Value) { Write-Host "  $($field.Name): $message" -ForegroundColor Red }
            }
        }
        catch { Write-Host "Rejected: $detail" -ForegroundColor Red }
    }
    else {
        Write-Host "Could not reach $BaseUrl - is the API running?" -ForegroundColor Red
    }
    exit 1
}

Write-Host "  jobId: $($accepted.jobId)" -ForegroundColor DarkGray

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Milliseconds 400
    $job = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/pdf/jobs/$($accepted.jobId)"

    if ((Get-Date) -gt $deadline) {
        throw "Job $($accepted.jobId) was still '$($job.status)' after $TimeoutSeconds seconds. Is a worker consuming the queue?"
    }
} while ($job.status -eq 'Pending')

if ($job.status -ne 'Succeeded') {
    Write-Host "FAILED  $($job.errorCode)" -ForegroundColor Red
    Write-Host "        $($job.errorMessage)" -ForegroundColor Red
    exit 1
}

$elapsed = [datetime]$job.completedAt - [datetime]$job.enqueuedAt
Write-Host ("  rendered {0:N0} bytes in {1:N0} ms" -f $job.sizeBytes, $elapsed.TotalMilliseconds) -ForegroundColor Green

Invoke-WebRequest -Uri $job.fileUrl -OutFile $OutFile | Out-Null
Write-Host "  saved to $OutFile" -ForegroundColor Green

if ($Open) { Invoke-Item $OutFile }
