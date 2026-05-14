# Contract: Registration API

**Owner**: `stem-dictionaries-manager` (external)
**Consumer**: `Stem.ButtonPanelTester.Infrastructure.Http.HttpRegistrationClient`
**Verified against**: API-survey of `stem-dictionaries-manager` (read 2026-05-13, `RegistrationEndpoints.cs`).

---

## Endpoint

```
POST {Dictionary:BaseUrl}/register
```

Path is `/register`, **not** `/api/register`. The endpoint is unauthenticated — it predates the `X-Api-Key` middleware and is the bootstrap entry point.

## Request headers

| Header | Value |
|---|---|
| `Content-Type` | `application/json; charset=utf-8` |
| `Accept` | `application/json` |
| `User-Agent` | `Stem.ButtonPanelTester/<assemblyVersion>` |

No `X-Api-Key` (we don't have one yet — that's why we're calling `/register`).

## Request body

```json
{
  "bootstrapToken": "<value>"
}
```

The `bootstrapToken` is supplied by the technician through the registration dialog. The client passes it through after `BootstrapToken.TryCreate` validation (trim + non-empty).

## Successful response

```http
200 OK
Content-Type: application/json

{
  "apiCredential": "<server-issued opaque secret>"
}
```

The client extracts `apiCredential` and constructs `InstallationCredential.Create response.apiCredential`. The plaintext credential is then handed to `DpapiCredentialStore.SaveAsync` for encryption-at-rest. The plaintext is never logged, never written anywhere except DPAPI-encrypted, never displayed in the UI after the dialog closes.

## Error responses

| HTTP status | Body shape | F# `RegistrationError` |
|---|---|---|
| `200 OK` | `{ "apiCredential": "..." }` | n/a — `Ok credential` |
| `400 Bad Request` | `{ "error": "..." }` | `TokenInvalid` (server rejected the token format) |
| `401 Unauthorized` | n/a (endpoint is anonymous) | n/a |
| `409 Conflict` | `{ "error": "Token already consumed." }` | `TokenInvalid` (the token was used before; supplier must request a fresh one) |
| any other 4xx | `{ "error": "..." }` | `RegistrationServerError httpStatus` |
| `5xx` | best-effort | `RegistrationServerError httpStatus` |
| network failure | n/a | `RegistrationNetwork (NetworkUnreachable / Timeout)` |

The registration dialog displays an inline error keyed off `RegistrationError`:

| `RegistrationError` | Message shown |
|---|---|
| `TokenInvalid` | "The token was not accepted. Check it and try again, or contact STEM for a fresh one." |
| `RegistrationServerError 503` | "The dictionary service is temporarily unavailable. Try again later." |
| `RegistrationServerError n` | "The dictionary service returned an unexpected error (HTTP n)." |
| `RegistrationNetwork NetworkUnreachable` | "Could not reach the dictionary service. Check your network and try again." |
| `RegistrationNetwork Timeout` | "The registration request timed out. Try again." |
| `RegistrationNetwork _` | (unreachable in practice; treated as generic network error) |

## Timeout and retries

- Client-side timeout: **10 s**, same as the dictionary fetch (uniform user expectation).
- Retries: **none**. The technician is at the keyboard; if it fails they retry by clicking Submit again.

## Idempotency

A `BootstrapToken` is single-use server-side. A second `POST /register` with the same token returns `409 Conflict`. The client makes no attempt to "remember" tokens — every Submit is a fresh attempt against the server.
