# Contract: Dictionary fetch API

**Owner**: `stem-dictionaries-manager` (external)
**Consumer**: `Stem.ButtonPanelTester.Infrastructure.Http.HttpDictionaryProvider`
**Verified against**: API-survey of `stem-dictionaries-manager` (read 2026-05-13).

---

## Endpoint

```
GET {Dictionary:BaseUrl}/api/dictionaries/{id}/resolved
```

- `{Dictionary:BaseUrl}` is configured per-environment in `appsettings.{Development,Production}.json`. Production: STEM-hosted URL; Development: `https://localhost:7065` (local `stem-dictionaries-manager` run).
- `{id}` is the integer dictionary id from `appsettings.json:Dictionary:Id`. Default `2` (the "Pulsantiere" dictionary).

## Request headers

| Header | Value | Notes |
|---|---|---|
| `X-Api-Key` | `<InstallationCredential.Value>` | Loaded from `DpapiCredentialStore.LoadAsync`. Mandatory. |
| `Accept` | `application/json` | |
| `User-Agent` | `Stem.ButtonPanelTester/<assemblyVersion>` | Standard identification, no PII. |

No `Authorization: Bearer` header. The wire scheme is `X-Api-Key`. (See R6 in research.md and the constitution's Stopgap Discipline section — this is the API as it actually exists.)

## Request body

None (GET).

## Successful response

```http
200 OK
Content-Type: application/json; charset=utf-8

{
  "id": 2,
  "name": "Pulsantiere",
  "description": "Button panels — production",
  "variables": [
    {
      "name": "BTN_RIGHT_TOP",
      "addressHigh": 1,
      "addressLow": 17,
      "dataType": "uint8",
      "access": "read",
      "description": "Top-right button",
      "min": 0,
      "max": 1,
      "unit": null,
      "isStandard": false
    }
    // … repeated per variable
  ]
}
```

Field semantics:

| Field | Type | Mapped to F# |
|---|---|---|
| `id` | int | `PanelType.Id` |
| `name` | string | `PanelType.Name` |
| `description` | string \| null (omitted on null per server policy) | `PanelType.Description` (`string option`) |
| `variables[i].name` | string | `Variable.Name` |
| `variables[i].addressHigh` | byte | `Variable.AddressHigh` |
| `variables[i].addressLow` | byte | `Variable.AddressLow` |
| `variables[i].dataType` | string | `Variable.DataType` |
| `variables[i].access` | string | `Variable.Access` |
| `variables[i].description` | string \| null (omitted) | `Variable.Description` |
| `variables[i].min` | float \| null | `Variable.Min` |
| `variables[i].max` | float \| null | `Variable.Max` |
| `variables[i].unit` | string \| null | `Variable.Unit` |
| `variables[i].isStandard` | bool | `Variable.IsStandard` |

**Variables are sorted by composite address** `(addressHigh << 8) | addressLow` ascending, server-side. Disabled variables are not returned.

The response represents one panel type. The client wraps it in `PanelTypes = [pt]` to keep `ButtonPanelDictionary.PanelTypes` a list (future-proofing without changing the contract this slice consumes).

## Error responses

| HTTP status | Body shape | F# `FetchFailureReason` |
|---|---|---|
| `200 OK` | (above) | n/a — `Success` |
| `400 Bad Request` | `{ "error": "Use /api/dictionaries/standard for the standard dictionary." }` | `MalformedPayload` (configured id targets a meta-endpoint) |
| `401 Unauthorized` | `{ "error": "API key missing or invalid." }` | `Unauthorized` |
| `404 Not Found` | empty body | `NotFound` (configured id does not exist on this server) |
| `503 Service Unavailable` | `{ "error": "Database unavailable." }` (development includes `detail`) | `ServerError` |
| any other 4xx/5xx | best-effort `{ "error": ... }` | `ServerError` |
| TCP / DNS / TLS failure | n/a | `NetworkUnreachable` |
| HttpClient timeout (10 s) | n/a | `Timeout` |
| Body present but does not deserialise | n/a | `MalformedPayload` |

## What we do **not** rely on

- No `ETag` / `Last-Modified` / `Cache-Control` headers — `stem-dictionaries-manager` does not emit them. The client-side `ContentHash` substitutes for change detection between fetches.
- No pagination — `variables` is always complete.
- No partial responses — all fields returned.

## Timeout and retries

- Client-side timeout: **10 s** total (HttpClient.Timeout).
- Retries: **none** — the seed/cache is the resilience story (R1/R5 in research.md). On any failure the client surfaces the `FetchFailureReason` and the user retries via the Refresh button.
