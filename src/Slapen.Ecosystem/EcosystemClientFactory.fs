namespace Slapen.Ecosystem

open System.Net.Http

type EcosystemClientFactory(httpClientFactory: IHttpClientFactory) =
    member _.Hub(config) =
        HubClient.create (httpClientFactory.CreateClient "slapen.notification-hub") config

    member _.Workflow(config) =
        WorkflowClient.create (httpClientFactory.CreateClient "slapen.workflow-engine") config

    member _.InvoiceRecon(config) =
        InvoiceReconClient.create (httpClientFactory.CreateClient "slapen.invoice-recon") config

    member _.Vpi(config) =
        VpiClient.create (httpClientFactory.CreateClient "slapen.vpi") config

    member _.ContractLifecycle(config) =
        ContractLifecycleClient.create (httpClientFactory.CreateClient "slapen.contract-lifecycle") config
