param(
    [string]$BaseUrl = "http://localhost:5109",
    [string]$ApiKey = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

function Read-LocalEnv {
    param([string]$Path)

    $values = @{}

    if (Test-Path -LiteralPath $Path) {
        foreach ($line in Get-Content -LiteralPath $Path) {
            $trimmed = $line.Trim()

            if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#") -or -not $trimmed.Contains("=")) {
                continue
            }

            $parts = $trimmed.Split("=", 2)
            $values[$parts[0]] = $parts[1]
        }
    }

    $values
}

function Get-RequiredEnvValue {
    param(
        [hashtable]$Values,
        [string]$Name,
        [string]$Fallback
    )

    if ($Values.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace($Values[$Name])) {
        return $Values[$Name]
    }

    $Fallback
}

function Invoke-DockerCompose {
    param([string[]]$Command)

    & docker @script:ComposeArgs @Command

    if ($LASTEXITCODE -ne 0) {
        throw "docker compose command failed: $($Command -join ' ')"
    }
}

function Invoke-Psql {
    param([string]$Sql)

    $Sql | & docker @script:ComposeArgs exec -T postgres psql -U $script:PostgresUser -d $script:PostgresDb -v ON_ERROR_STOP=1

    if ($LASTEXITCODE -ne 0) {
        throw "psql command failed"
    }
}

function Invoke-PsqlFile {
    param([string]$Path)

    Get-Content -LiteralPath $Path -Raw |
        & docker @script:ComposeArgs exec -T postgres psql -U $script:PostgresUser -d $script:PostgresDb -v ON_ERROR_STOP=1

    if ($LASTEXITCODE -ne 0) {
        throw "psql seed failed: $Path"
    }
}

function Get-Sha256Hex {
    param([string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $hashBytes = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
}

function Wait-HttpOk {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3

            if ($response.StatusCode -eq 200) {
                return
            }
        } catch {
            Start-Sleep -Seconds 2
        }
    }

    try {
        Invoke-DockerCompose @("logs", "--tail=120", "app")
    } catch {
        Write-Warning "Could not read app logs after health timeout."
    }

    throw "Timed out waiting for $Url"
}

function Invoke-Json {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [hashtable]$Headers = @{}
    )

    $parameters = @{
        Uri         = "$BaseUrl$Path"
        Method      = $Method
        Headers     = $Headers
        ContentType = "application/json"
        TimeoutSec  = 15
    }

    if ($null -ne $Body) {
        $parameters["Body"] = $Body | ConvertTo-Json -Depth 10
    }

    Invoke-RestMethod @parameters
}

function Assert-Equal {
    param(
        [object]$Actual,
        [object]$Expected,
        [string]$Message
    )

    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = "slapen_live_" + "acceptance_local_" + ("0" * 32)
}

$envValues = Read-LocalEnv ".env.local"
$script:PostgresDb = Get-RequiredEnvValue $envValues "POSTGRES_DB" "slapen"
$script:PostgresUser = Get-RequiredEnvValue $envValues "POSTGRES_USER" "slapen"
$postgresPassword = Get-RequiredEnvValue $envValues "POSTGRES_PASSWORD" "slapen_dev_password"

$overridePath = Join-Path ([System.IO.Path]::GetTempPath()) ("slapen-compose-acceptance-{0}.yml" -f ([guid]::NewGuid().ToString("N")))

try {
    @"
services:
  app:
    environment:
      DATABASE_URL: "Host=postgres;Port=5432;Database=$script:PostgresDb;Username=$script:PostgresUser;Password=$postgresPassword"
      REDIS_URL: "redis:6379"
      HUB_INGRESS_SECRET: "local-hub-ingress-secret"
      METRICS_BASIC_AUTH_USER: "metrics"
      METRICS_BASIC_AUTH_PASS: "metrics-local"
      OUTBOX_POLL_INTERVAL_SECONDS: "5"
      AUTO_MIGRATE: "true"
"@ | Set-Content -LiteralPath $overridePath -Encoding ASCII

    $script:ComposeArgs = @("compose", "-f", "docker-compose.yml", "-f", $overridePath)

    Write-Host "Starting local production-shaped stack..."

    if ($SkipBuild) {
        Invoke-DockerCompose @("--profile", "app", "up", "-d", "postgres", "redis", "app")
    } else {
        Invoke-DockerCompose @("--profile", "app", "up", "-d", "--build", "postgres", "redis", "app")
    }

    Wait-HttpOk "$BaseUrl/api/health" 180
    Wait-HttpOk "$BaseUrl/api/health/db" 60

    $ready = Invoke-WebRequest -Uri "$BaseUrl/api/health/ready" -UseBasicParsing -TimeoutSec 15
    Assert-Equal $ready.StatusCode 200 "Readiness endpoint is not healthy."

    Write-Host "Seeding deterministic acceptance tenant and contract fixtures..."

    Invoke-PsqlFile "db/seeds/fixtures/tenants.sql"
    Invoke-PsqlFile "db/seeds/fixtures/counterparties.sql"
    Invoke-PsqlFile "db/seeds/fixtures/contracts_and_clauses.sql"

    $apiHash = Get-Sha256Hex $ApiKey
    $apiPrefix = $ApiKey.Substring(0, 12)

    Invoke-Psql @"
update tenants
set api_key_prefix = '$apiPrefix',
    api_key_hash = '$apiHash',
    is_active = true,
    updated_at = now()
where id = '10000000-0000-0000-0000-000000000001';
"@

    Write-Host "Driving public API flow through the running container..."

    $headers = @{ "X-API-Key" = $ApiKey }
    $tenant = Invoke-Json "GET" "/api/tenants/me" $null $headers
    Assert-Equal $tenant.slug "acme-gmbh-de" "Tenant API key did not resolve to the expected tenant."

    $sourceRef = "local-acceptance-$([guid]::NewGuid().ToString("N"))"

    $created = Invoke-Json "POST" "/api/breaches/manual" @{
        contractId  = "12000000-0000-0000-0000-000000000001"
        slaClauseId = "13000000-0000-0000-0000-000000000001"
        sourceRef   = $sourceRef
        metricValue = 88.0
        unitsMissed = $null
        observedAt  = "2026-05-03T09:00:00Z"
        reportedAt  = "2026-05-03T10:00:00Z"
    } $headers

    if ([string]::IsNullOrWhiteSpace($created.id)) {
        throw "Manual breach creation returned no id."
    }

    $accrued = Invoke-Json "POST" "/api/breaches/$($created.id)/accrue" $null $headers
    Assert-Equal $accrued.status "accrued" "Breach did not accrue."
    Assert-Equal @($accrued.ledgerEntryIds).Count 2 "Accrual did not create a bilateral ledger pair."

    $reversed = Invoke-Json "POST" "/api/breaches/$($created.id)/reverse" @{
        reasonNotes = "local acceptance reversal"
    } $headers
    Assert-Equal $reversed.status "withdrawn" "Breach did not reverse to withdrawn."
    Assert-Equal @($reversed.ledgerEntryIds).Count 2 "Reversal did not create a bilateral compensating pair."

    $ledger = Invoke-Json "GET" "/api/ledger/breaches/$($created.id)" $null $headers
    Assert-Equal @($ledger.items).Count 4 "Append-only ledger does not expose four rows after accrue and reverse."

    $entryKinds = @($ledger.items | ForEach-Object { $_.entryKind })
    Assert-Equal @($entryKinds | Where-Object { $_ -eq "accrual" }).Count 2 "Ledger accrual row count is wrong."
    Assert-Equal @($entryKinds | Where-Object { $_ -eq "reversal" }).Count 2 "Ledger reversal row count is wrong."

    Write-Host "Checking UI login session against the same running app..."
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $login = Invoke-WebRequest -Uri "$BaseUrl/login" -UseBasicParsing -WebSession $session -TimeoutSec 15
    Assert-Equal $login.StatusCode 200 "Login page did not render."

    $dashboard = Invoke-WebRequest `
        -Uri "$BaseUrl/login" `
        -UseBasicParsing `
        -WebSession $session `
        -Method Post `
        -Body @{ apiKey = $ApiKey; returnUrl = "/" } `
        -ContentType "application/x-www-form-urlencoded" `
        -TimeoutSec 15

    Assert-Equal $dashboard.StatusCode 200 "UI login did not reach the dashboard."

    if ($dashboard.Content -notmatch "Acme Procurement DE") {
        throw "Dashboard did not render the authenticated tenant identity."
    }

    Write-Host ""
    Write-Host "LOCAL ACCEPTANCE PASSED"
    Write-Host "Base URL: $BaseUrl"
    Write-Host "Tenant: $($tenant.slug)"
    Write-Host "Breach: $($created.id)"
    Write-Host "Ledger rows: $(@($ledger.items).Count)"
} finally {
    if (Test-Path -LiteralPath $overridePath) {
        Remove-Item -LiteralPath $overridePath -Force
    }
}
