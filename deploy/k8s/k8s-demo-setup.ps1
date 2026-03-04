# ============================================================
#  k8s-demo-setup.ps1
#  One-shot script to build images, load them into Docker Desktop
#  Kubernetes, create secrets, and deploy the full stack.
#
#  Prerequisites:
#    - Docker Desktop running with Kubernetes enabled
#    - kubectl context pointing to docker-desktop
#    - .env exists at repo root (copy from .env.example)
#
#  Usage:
#    .\deploy\k8s\k8s-demo-setup.ps1
# ============================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../../")

Write-Host "`n═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Parcel Tracking — Kubernetes Demo Setup" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════`n" -ForegroundColor Cyan

# ── 1. Verify kubectl context is docker-desktop ──────────────
$context = kubectl config current-context 2>&1
Write-Host "► kubectl context: $context"
if ($context -ne "docker-desktop") {
    Write-Warning "Context is not 'docker-desktop'. Switch with:"
    Write-Warning "  kubectl config use-context docker-desktop"
    Write-Host ""
}

# ── 2. Build Docker images (same ones Docker Compose builds) ──
Write-Host "`n► Building Docker images..." -ForegroundColor Yellow
Set-Location $repoRoot
docker compose build api ingestion
Write-Host "  Images built." -ForegroundColor Green

# ── 3. Apply all manifests ────────────────────────────────────
Write-Host "`n► Creating Kubernetes secrets from .env..." -ForegroundColor Yellow
& "$PSScriptRoot\create-secrets.ps1"

Write-Host "`n► Applying Kubernetes manifests..." -ForegroundColor Yellow
kubectl apply -f "$PSScriptRoot\00-namespace.yaml"
kubectl apply -f "$PSScriptRoot\02-configmap.yaml"
kubectl apply -f "$PSScriptRoot\03-sqlserver.yaml"
kubectl apply -f "$PSScriptRoot\04-redis.yaml"
kubectl apply -f "$PSScriptRoot\05-sqledge.yaml"
kubectl apply -f "$PSScriptRoot\06-servicebus.yaml"
kubectl apply -f "$PSScriptRoot\07-adminer.yaml"
kubectl apply -f "$PSScriptRoot\08-ingestion.yaml"
kubectl apply -f "$PSScriptRoot\09-api.yaml"

# ── 4. Wait for pods to be ready ─────────────────────────────
Write-Host "`n► Waiting for all pods to be Running (up to 3 minutes)..." -ForegroundColor Yellow
kubectl wait --for=condition=Ready pod --all -n parcel-tracking --timeout=180s

# ── 5. Show status ────────────────────────────────────────────
Write-Host "`n► Pod status:" -ForegroundColor Cyan
kubectl get pods -n parcel-tracking

Write-Host "`n► Services:" -ForegroundColor Cyan
kubectl get svc -n parcel-tracking

Write-Host "`n═══════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  ✓ Stack deployed!" -ForegroundColor Green
Write-Host "  API + Swagger : http://localhost:5058/swagger" -ForegroundColor Green
Write-Host "  Adminer (DB)  : http://localhost:8080" -ForegroundColor Green
Write-Host "  Health live   : http://localhost:5058/healthz/live" -ForegroundColor Green
Write-Host "  Health ready  : http://localhost:5058/healthz/ready" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════`n" -ForegroundColor Green
