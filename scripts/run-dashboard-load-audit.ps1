param(
  [switch]$FullVolume
)

$ErrorActionPreference = "Stop"

if ($FullVolume) {
  $env:DASHBOARD_LOAD_BREACHES = "10000"
  $env:DASHBOARD_LOAD_SETTLEMENTS = "5000"
} else {
  $env:DASHBOARD_LOAD_BREACHES = "250"
  $env:DASHBOARD_LOAD_SETTLEMENTS = "125"
}

dotnet test tests/Slapen.Application.Tests/Slapen.Application.Tests.fsproj --filter "FullyQualifiedName~dashboard summary uses bounded aggregate queries" --logger "console;verbosity=minimal"
