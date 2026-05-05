namespace Slapen.Jobs

[<RequireQualifiedAccess>]
module HangfireWiring =
    type RegistrationPlan =
        { JobId: string
          Cron: string
          Description: string }

    let registrations pollIntervalSeconds =
        [ { JobId = "slapen-accrual"
            Cron = sprintf "*/%i * * * * *" pollIntervalSeconds
            Description = "Accrues pending SLA breach penalties." }
          { JobId = "slapen-settlement-builder"
            Cron = "0 0 1 * *"
            Description = "Builds tenant-scoped settlement drafts from uncommitted accruals." }
          { JobId = "slapen-outbox-processor"
            Cron = sprintf "*/%i * * * * *" pollIntervalSeconds
            Description = "Runs the durable outbox processor." }
          { JobId = "slapen-stale-breach-reminder"
            Cron = "0 */6 * * *"
            Description = "Schedules reminders for breaches stuck before settlement." }
          { JobId = "slapen-stale-ingestion-detector"
            Cron = "*/15 * * * *"
            Description = "Detects ingestion adapters that have not completed recently." }
          { JobId = "slapen-outbox-dead-letter-reaper"
            Cron = "0 * * * *"
            Description = "Surfaces dead outbox messages for operator follow-up." } ]

    let outboxProcessor pollIntervalSeconds =
        registrations pollIntervalSeconds
        |> List.filter (fun item -> item.JobId = "slapen-outbox-processor")
