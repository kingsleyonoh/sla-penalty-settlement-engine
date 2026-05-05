# Notification Hub Registration Gate

Batch 011 does not perform live tenant or template registration.

Required local-only inputs for a live registration attempt:

- `NOTIFICATION_HUB_URL`
- `NOTIFICATION_HUB_ADMIN_API_KEY`
- `NOTIFICATION_HUB_REGISTRATION_RECIPIENT`

Dry-run command:

```powershell
.\scripts\register-notification-hub.ps1
```

The manifest at `docs/integrations/notification-hub-onboarding-manifest.json` is the no-secret source of truth for tenant name, templates, rules, and event names.

Live registration remains skipped until credentials and explicit approval are supplied.
