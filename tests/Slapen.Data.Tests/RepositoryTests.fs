namespace Slapen.Data.Tests

open System
open System.Threading.Tasks
open FsUnit.Xunit
open Npgsql
open Slapen.Audit
open Slapen.Data
open Slapen.Tenants
open Xunit

[<CollectionDefinition("postgres")>]
type PostgresCollection() =
    interface ICollectionFixture<PostgresFixture>

[<Collection("postgres")>]
type RepositoryTests(fixture: PostgresFixture) =
    let acmeTenantId = Guid.Parse "10000000-0000-0000-0000-000000000001"
    let globexTenantId = Guid.Parse "20000000-0000-0000-0000-000000000001"
    let acmeContractReference = "ACME-MSA-2026-001"
    let acmeEntityId = Guid.Parse "12000000-0000-0000-0000-000000000001"

    let execute sql (parameters: (string * obj) list) =
        task {
            use! connection = fixture.DataSource.OpenConnectionAsync().AsTask()
            use command = new NpgsqlCommand(sql, connection)

            for name, value in parameters do
                command.Parameters.AddWithValue(name, value) |> ignore

            let! _ = command.ExecuteNonQueryAsync()
            return ()
        }

    [<Fact>]
    member _.``contract repository returns tenant scoped rows only``() : Task =
        task {
            let acmeScope = TenantScope.create acmeTenantId
            let globexScope = TenantScope.create globexTenantId

            let! visible = ContractsRepository.findByReference fixture.DataSource acmeScope acmeContractReference

            let! hidden = ContractsRepository.findByReference fixture.DataSource globexScope acmeContractReference

            visible.Value.TenantId |> should equal acmeTenantId
            visible.Value.Reference |> should equal acmeContractReference
            hidden |> should equal None
        }

    [<Fact>]
    member _.``identity resolves active tenant from API key prefix and SHA256 hash``() : Task =
        task {
            let apiKey = "slapen-acme-test-key"
            let prefix = ApiKey.prefix apiKey
            let hash = ApiKey.sha256 apiKey

            do!
                execute
                    "update tenants set api_key_prefix = @prefix, api_key_hash = @hash where id = @tenant_id"
                    [ "prefix", prefix :> obj
                      "hash", hash :> obj
                      "tenant_id", acmeTenantId :> obj ]

            let! resolved = Identity.resolveApiKey fixture.DataSource apiKey
            let! missing = Identity.resolveApiKey fixture.DataSource "slapen-acme-wrong-key"

            resolved.Value.TenantId |> should equal acmeTenantId
            resolved.Value.Slug |> should equal "acme-gmbh-de"
            missing |> should equal None
        }

    [<Fact>]
    member _.``tenant snapshot captures immutable identity fields from database``() : Task =
        task {
            let scope = TenantScope.create acmeTenantId

            let! original = TenantSnapshotter.capture fixture.DataSource scope

            do!
                execute
                    "update tenants set legal_name = @legal_name, display_name = @display_name where id = @tenant_id"
                    [ "legal_name", "Updated Legal Name" :> obj
                      "display_name", "Updated Display" :> obj
                      "tenant_id", acmeTenantId :> obj ]

            let! changed = TenantSnapshotter.capture fixture.DataSource scope

            original.Value.LegalName |> should equal "Acme GmbH"
            original.Value.DisplayName |> should equal "Acme Procurement DE"
            original.Value.AddressJson.Contains("San Francisco") |> should equal false
            changed.Value.LegalName |> should equal "Updated Legal Name"
            changed.Value.DisplayName |> should equal "Updated Display"
        }

    [<Fact>]
    member _.``audit recorder writes tenant scoped mutation events``() : Task =
        task {
            let acmeScope = TenantScope.create acmeTenantId
            let globexScope = TenantScope.create globexTenantId

            let entry =
                { Actor = AuditActor.System "accrual-worker"
                  Action = "contract.updated"
                  EntityKind = "contract"
                  EntityId = acmeEntityId
                  BeforeStateJson = Some """{"status":"draft"}"""
                  AfterStateJson = Some """{"status":"active"}""" }

            let! auditId = AuditRecorder.record fixture.DataSource acmeScope entry

            let! acmeRows = AuditRepository.listForEntity fixture.DataSource acmeScope "contract" acmeEntityId

            let! globexRows = AuditRepository.listForEntity fixture.DataSource globexScope "contract" acmeEntityId

            auditId |> should not' (equal Guid.Empty)
            acmeRows |> should haveLength 1
            acmeRows.Head.Action |> should equal "contract.updated"
            globexRows |> should haveLength 0
        }
