param(
  [switch]$Execute,
  [string]$ManifestPath = "docs/integrations/notification-hub-onboarding-manifest.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ManifestPath)) {
  throw "Manifest not found: $ManifestPath"
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$hubUrl = $env:NOTIFICATION_HUB_URL
$adminKey = $env:NOTIFICATION_HUB_ADMIN_API_KEY
$recipient = $env:NOTIFICATION_HUB_REGISTRATION_RECIPIENT

if (-not $Execute) {
  Write-Host "DRY RUN: would register Notification Hub tenant '$($manifest.tenant_name)' at $($manifest.default_hub_url)."
  Write-Host "Pass -Execute only after supplying NOTIFICATION_HUB_URL, NOTIFICATION_HUB_ADMIN_API_KEY, and NOTIFICATION_HUB_REGISTRATION_RECIPIENT."
  exit 0
}

if ([string]::IsNullOrWhiteSpace($hubUrl) -or [string]::IsNullOrWhiteSpace($adminKey) -or [string]::IsNullOrWhiteSpace($recipient)) {
  throw "Live registration skipped: NOTIFICATION_HUB_URL, NOTIFICATION_HUB_ADMIN_API_KEY, and NOTIFICATION_HUB_REGISTRATION_RECIPIENT are required."
}

Write-Host "Credentials detected. This script intentionally stops before live mutation in YOLO Batch 011."
Write-Host "Run the Notification Hub onboarding workflow manually with approval to create tenant, templates, and rules."
exit 2
