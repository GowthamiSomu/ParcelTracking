# ============================================================
#  k8s-demo-teardown.ps1
#  Removes the entire parcel-tracking namespace and all resources.
#
#  Usage:
#    .\deploy\k8s\k8s-demo-teardown.ps1
# ============================================================

Write-Host "`n► Deleting all Parcel Tracking Kubernetes resources..." -ForegroundColor Yellow
kubectl delete namespace parcel-tracking --ignore-not-found
Write-Host "  ✓ Done. All resources removed." -ForegroundColor Green
