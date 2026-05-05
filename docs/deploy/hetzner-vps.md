# Hetzner VPS Deployment

Target: `https://slapen.kingsleyonoh.com`

Image: `ghcr.io/kingsleyonoh/sla-penalty-settlement-engine:latest`

## Local Build

```powershell
dotnet restore Slapen.sln
dotnet test
docker build -t ghcr.io/kingsleyonoh/sla-penalty-settlement-engine:latest .
```

## GHCR

The GitHub Actions image job builds on all pushes and pushes to GHCR only from `main`.
No credentials are stored in the repo; GHCR uses `GITHUB_TOKEN` in Actions.

## VPS Files

Copy `docker-compose.prod.yml` to the VPS project directory as `docker-compose.yml`.
Create `.env` on the VPS from `.env.production.example` and fill secrets there only.

Do not paste secrets into `docker-compose.prod.yml`.

## Start

```bash
cd /apps/slapen
docker compose pull
docker compose up -d
```

## Verify

```bash
curl -fsS https://slapen.kingsleyonoh.com/api/health
curl -fsS https://slapen.kingsleyonoh.com/api/health/ready
curl -u "$METRICS_BASIC_AUTH_USER:$METRICS_BASIC_AUTH_PASS" https://slapen.kingsleyonoh.com/metrics
```

Live deploy was not performed in Batch 011 because the session did not include VPS credentials or explicit approval for production mutation.
