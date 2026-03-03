# ============================================================
#  create-secrets.ps1
#  Reads credentials from the root .env file and creates (or
#  replaces) the Kubernetes Secret without writing them to any
#  file that could be committed to source control.
#
#  Prerequisites:
#    - kubectl configured to target the correct cluster/context
#    - .env exists at the repo root (copy from .env.example)
#
#  Usage:
#    .\deploy\k8s\create-secrets.ps1
# ============================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../../")
$envFile  = Join-Path $repoRoot ".env"

if (-not (Test-Path $envFile)) {
    Write-Error ".env file not found at $envFile. Copy .env.example to .env and fill in real values."
    exit 1
}

# Parse .env (skip blank lines and comments)
$envVars = @{}
Get-Content $envFile | Where-Object { $_ -match "^\s*[^#\s]" } | ForEach-Object {
    $parts = $_ -split "=", 2
    if ($parts.Length -eq 2) {
        $envVars[$parts[0].Trim()] = $parts[1].Trim()
    }
}

$required = @("SA_PASSWORD", "SQLSERVER_CONNECTION", "REDIS_CONNECTION", "SERVICEBUS_CONNECTION")
foreach ($key in $required) {
    if (-not $envVars.ContainsKey($key)) {
        Write-Error "Missing required key '$key' in .env"
        exit 1
    }
}

Write-Host "Ensuring namespace 'parcel-tracking' exists..."
kubectl apply -f "$repoRoot\deploy\k8s\00-namespace.yaml"

Write-Host "Creating / updating Kubernetes Secret 'parcel-secrets'..."
kubectl create secret generic parcel-secrets `
    --namespace parcel-tracking `
    --from-literal=sa-password=$($envVars["SA_PASSWORD"]) `
    --from-literal=sqlserver-connection=$($envVars["SQLSERVER_CONNECTION"]) `
    --from-literal=redis-connection=$($envVars["REDIS_CONNECTION"]) `
    --from-literal=servicebus-connection=$($envVars["SERVICEBUS_CONNECTION"]) `
    --save-config `
    --dry-run=client -o yaml | kubectl apply -f -

Write-Host ""
Write-Host "Done. Secret 'parcel-secrets' is live in namespace 'parcel-tracking'." -ForegroundColor Green
Write-Host "Credentials were read from .env and passed directly to kubectl — no file written." -ForegroundColor Green
