namespace Slapen.Jobs

open Slapen.Application

[<RequireQualifiedAccess>]
module HangfireWiring =
    type RegistrationPlan =
        { JobId: string
          Cron: string
          Description: string }

    let outboxProcessor pollIntervalSeconds =
        HangfireOutboxBoundary.registrations pollIntervalSeconds
        |> List.map (fun item ->
            { JobId = item.JobId
              Cron = item.Cron
              Description = item.Description })
