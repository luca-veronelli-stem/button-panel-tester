namespace Stem.ButtonPanelTester.Infrastructure.Http

open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Dictionary

/// `DelegatingHandler` that injects `X-Api-Key:
/// <InstallationCredential.Value>` on every outgoing request, per
/// `specs/001-fetch-dictionary/contracts/dictionary-api.md`
/// §"Request headers". Composes into the
/// `IHttpClientFactory`-managed pipeline for the `"Dictionary"`
/// named client at the composition root (T051) so
/// `HttpDictionaryProvider.FetchAsync` (T049) never sees the
/// credential — separation of concerns, mirroring the standard
/// HttpMessageHandler delegate pattern that
/// `AuthenticationHandler` / token-refresh handlers in
/// ASP.NET Core HTTP-client extensions land on.
///
/// Constructor parameters:
///   - `credentialStore` — `ICredentialStore` resolved from DI at
///     the composition root (T051). `LoadAsync` is invoked on
///     every outgoing request — the underlying DPAPI decrypt is
///     fast and avoids stale-cached state if the credential is
///     rotated mid-process (e.g. after a successful re-register
///     flow surfaced from FR-018, leveraging
///     `stem-dictionaries-manager` v0.8.0 atomic re-registration).
///   - `logger` — required `ILogger<ApiKeyAuthHandler>` per the
///     STEM LOGGING standard for archetype A. The
///     `InstallationCredential.Value` is NEVER logged at any
///     level; only the presence/absence flag and request URI
///     appear in diagnostic output.
///
/// Missing-credential policy: when `LoadAsync` returns `None`
/// (no credential on disk, or DPAPI decrypt failed) the handler
/// forwards the request **without** an `X-Api-Key` header. The
/// server replies `401 Unauthorized`, which `HttpDictionaryProvider`
/// maps to `Failed(Unauthorized, _)` and the status row surfaces
/// as `Cached(_, _, Some Unauthorized)` with the FR-018
/// "Re-register" affordance (T052). This is preferable to
/// short-circuiting here: the handler does not own the contract
/// that "no credential ⇒ Unauthorized"; the server does, and
/// surfacing the live `401` keeps the wire and the in-process
/// state machine in lockstep.
type ApiKeyAuthHandler(credentialStore: ICredentialStore, logger: ILogger<ApiKeyAuthHandler>) =
    inherit DelegatingHandler()

    // `base.SendAsync` is `protected` and not reachable from inside a
    // computation expression's nested closures (the F# `task { }`
    // builder hoists each `let!` into a continuation). Wrap the call
    // in a private member so the override body can invoke the base
    // pipeline through `this.` without tripping the protected-access
    // check.
    member private this.InvokeInner(request: HttpRequestMessage, ct: CancellationToken) =
        base.SendAsync(request, ct)

    override this.SendAsync(request: HttpRequestMessage, ct: CancellationToken) : Task<HttpResponseMessage> = task {
        let! credential = credentialStore.LoadAsync(ct)

        match credential with
        | Some c ->
            request.Headers.TryAddWithoutValidation("X-Api-Key", c.Value) |> ignore
        | None ->
            logger.LogWarning(
                "Outgoing dictionary request to {Uri} has no installation credential — server will reply 401.",
                request.RequestUri
            )

        return! this.InvokeInner(request, ct)
    }
