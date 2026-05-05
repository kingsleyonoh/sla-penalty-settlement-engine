# Batch 011 Dashboard Load Audit

Tooling:

```powershell
.\scripts\run-dashboard-load-audit.ps1
.\scripts\run-dashboard-load-audit.ps1 -FullVolume
```

Default local mode seeds a deterministic scaled dataset through the application test harness:

- breaches: `250`
- settlements: `125`

Full-volume mode sets:

- breaches: `10000`
- settlements: `5000`

The assertion checks that the dashboard summary is served through one bounded aggregate command and returns within 500ms in the local Testcontainers PostgreSQL environment.
