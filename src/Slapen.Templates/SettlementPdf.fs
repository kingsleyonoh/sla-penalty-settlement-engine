namespace Slapen.Templates

open System
open System.Text.Json
open System.Text.Json.Serialization

[<CLIMutable>]
type TenantIdentityBlock =
    { [<JsonPropertyName("tenant_id")>]
      TenantId: Guid
      [<JsonPropertyName("legal_name")>]
      LegalName: string
      [<JsonPropertyName("full_legal_name")>]
      FullLegalName: string
      [<JsonPropertyName("display_name")>]
      DisplayName: string
      [<JsonPropertyName("address")>]
      AddressJson: string
      [<JsonPropertyName("registration")>]
      RegistrationJson: string
      [<JsonPropertyName("contact")>]
      ContactJson: string
      [<JsonPropertyName("locale")>]
      Locale: string
      [<JsonPropertyName("timezone")>]
      Timezone: string
      [<JsonPropertyName("captured_at")>]
      CapturedAt: DateTimeOffset }

[<CLIMutable>]
type CounterpartyIdentityBlock =
    { [<JsonPropertyName("counterparty_id")>]
      CounterpartyId: Guid
      [<JsonPropertyName("canonical_name")>]
      CanonicalName: string
      [<JsonPropertyName("tax_id")>]
      TaxId: string option
      [<JsonPropertyName("country_code")>]
      CountryCode: string option }

[<CLIMutable>]
type ContractIdentityBlock =
    { [<JsonPropertyName("contract_id")>]
      ContractId: Guid
      [<JsonPropertyName("reference")>]
      Reference: string
      [<JsonPropertyName("title")>]
      Title: string }

[<CLIMutable>]
type SettlementLineSnapshot =
    { [<JsonPropertyName("ledger_entry_id")>]
      LedgerEntryId: Guid
      [<JsonPropertyName("breach_event_id")>]
      BreachEventId: Guid
      [<JsonPropertyName("clause_reference")>]
      ClauseReference: string
      [<JsonPropertyName("amount_cents")>]
      AmountCents: int64
      [<JsonPropertyName("currency")>]
      Currency: string
      [<JsonPropertyName("period_start")>]
      PeriodStart: DateTimeOffset
      [<JsonPropertyName("period_end")>]
      PeriodEnd: DateTimeOffset }

[<CLIMutable>]
type SettlementSnapshot =
    { [<JsonPropertyName("settlement_id")>]
      SettlementId: Guid
      [<JsonPropertyName("settlement_number")>]
      SettlementNumber: string
      [<JsonPropertyName("tenant")>]
      Tenant: TenantIdentityBlock
      [<JsonPropertyName("counterparty")>]
      Counterparty: CounterpartyIdentityBlock
      [<JsonPropertyName("contract")>]
      Contract: ContractIdentityBlock
      [<JsonPropertyName("currency")>]
      Currency: string
      [<JsonPropertyName("amount_cents")>]
      AmountCents: int64
      [<JsonPropertyName("period_start")>]
      PeriodStart: DateOnly
      [<JsonPropertyName("period_end")>]
      PeriodEnd: DateOnly
      [<JsonPropertyName("created_at")>]
      CreatedAt: DateTimeOffset
      [<JsonPropertyName("lines")>]
      Lines: SettlementLineSnapshot list }

[<RequireQualifiedAccess>]
module SettlementPdf =
    let private options =
        JsonSerializerOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)

    let serializeSnapshot snapshot =
        JsonSerializer.Serialize(snapshot, options)

    let deserializeSnapshot (json: string) =
        JsonSerializer.Deserialize<SettlementSnapshot>(json, options)
