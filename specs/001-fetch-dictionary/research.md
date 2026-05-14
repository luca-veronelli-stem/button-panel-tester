# Research: Dictionary Fetch and Status Display

**Phase 0 output for**: [plan.md](./plan.md)

This document records the decisions taken during planning, with the alternatives considered and the rationale for each. It resolves the questions deferred from `/speckit.specify` and `/speckit.clarify` to the plan phase.

---

## R1 — HTTP proxy handling

**Decision**: rely on .NET's default `HttpClient` proxy resolution. No explicit `HttpClientHandler.Proxy` assignment, no configuration knob in v1.

**Rationale**: `HttpClientHandler.UseProxy` defaults to `true` and reads `WinHTTP` system settings on Windows. Suppliers whose corporate networks require a proxy already configure it via Group Policy / WinHTTP Proxy Configuration Tool, and `HttpClient` honours that automatically. Adding a config knob preempts a problem we have no evidence of and clutters `appsettings.json` with a setting nobody tunes.

**Alternatives considered**:
- *Explicit `Dictionary:Proxy` config key* — premature; adds an inert knob and a parsing path. Reconsider only if a supplier reports a real failure.
- *Bundle a proxy detection probe at startup* — over-engineering; the OS already does this.
- *Use `HttpClient.DefaultProxy = WebRequest.GetSystemWebProxy()` explicitly* — equivalent to the default; explicit assignment offers no behaviour change.

**Open issue if reality bites**: file an upstream `chore` issue if a real supplier failure surfaces; treat as a small follow-up, not a blocker for this slice.

---

## R2 — DPAPI on .NET 10

**Decision**: use `System.Security.Cryptography.ProtectedData` (via the NuGet of the same name) with `DataProtectionScope.CurrentUser` and an empty `optionalEntropy` (`Array.empty<byte>`).

**Rationale**: `ProtectedData.Protect/Unprotect` is the OS primitive (Windows DPAPI) that produces a ciphertext readable only by the same user on the same machine. `CurrentUser` scope satisfies FR-016 directly. The package is `net10.0-windows`-only, which is why `ButtonPanelTester.Infrastructure` carries that TFM. No third-party crypto needed.

**Alternatives considered**:
- *DPAPI-NG* — heavier; aimed at multi-user / domain protection, which we don't need for a single-technician tool.
- *Roll our own AES + machine-key derivation* — re-implements DPAPI badly; introduces key-storage problems we'd then have to solve (a chicken-and-egg).
- *Windows Credential Manager (`vaultcli.dll`)* — works for username/password pairs but is awkward for opaque token blobs and is also Windows-only (no portability win).

**Cross-platform note**: when porting to non-Windows, `ICredentialStore` gets an alternative adapter (Keychain on macOS, libsecret/Secret Service on Linux). The port abstraction makes this a single-file swap.

---

## R3 — Content-hash strategy

**Decision**: SHA-256 over the canonicalised JSON body of the dictionary, written as a 64-char lowercase hex string into a sidecar file `dictionary.json.sha256`. Canonicalisation is `System.Text.Json.JsonSerializer.Serialize(value, JsonSerializerOptions(WriteIndented=false))` after deserialising the response into our own `ButtonPanelDictionary` record (so server-side whitespace differences don't perturb the hash).

**Rationale**: SHA-256 has well-understood properties (collision resistance is more than sufficient for tamper-detection, not a security boundary), available in BCL (`SHA256.HashData`), no external dependency. Canonicalising via a deserialise-then-reserialise round-trip gives us a hash that depends only on the content of the dictionary, not on the server's whitespace or field ordering — which we don't control.

**Alternatives considered**:
- *Hash the raw HTTP response bytes* — depends on server whitespace, which is fragile. The hash would change on every minor server-side framework upgrade.
- *MD5* — collision-fragile; signals "we got crypto wrong" even though tamper-detection isn't a security boundary. Avoid for cultural reasons.
- *xxHash* — fast but non-cryptographic; same cultural concern, plus a NuGet dependency we don't otherwise need.

---

## R4 — Embedded seed extraction

**Decision**: ship `Assets/dictionary.seed.json` as an `<EmbeddedResource>` in `ButtonPanelTester.GUI`. On launch, `EmbeddedSeedExtractor.ensureExtracted` checks whether `%LOCALAPPDATA%\Stem.ButtonPanelTester\dictionary.json` exists; if not, it reads the embedded resource via `Assembly.GetManifestResourceStream("Stem.ButtonPanelTester.GUI.Assets.dictionary.seed.json")`, writes it atomically (write-temp + rename), computes the sidecar SHA-256, and writes the sidecar.

**Rationale**: keeps the seed in the binary (no separate file ships, no signature concerns), uses the `JsonFileDictionaryCache` adapter as the destination (single read path), and the atomic write pattern protects against half-written files if the process is killed mid-extraction.

**Alternatives considered**:
- *Ship `dictionary.seed.json` next to the binary* — vulnerable to user-side tampering or accidental deletion; embedded resource is integrity-bound to the binary itself.
- *Lazy seed loading from the assembly on every launch* — wastes the read; the on-disk cache is what every subsequent launch uses anyway. Extracting once converts the steady state to "just read the cache file".
- *Skip seeding, force first-launch to wait on network* — reintroduces the cold-start hang the spec was designed to avoid.

**Seeded-at metadata**: the seed JSON carries a top-level `"seededAt": "<ISO 8601 UTC>"` field set by `eng/refresh-seed.ps1` at build time. The cache adapter exposes this as the `FetchedAt` for `DictionarySource.Cached(...)` when the cache was extracted from the seed and never overwritten by a live fetch.

---

## R5 — In-flight refresh coalescing

**Decision**: replicate the legacy `DictionaryService.fs` pattern — a `lock`-guarded `inFlight: TaskCompletionSource<DictionaryStateUpdate> voption` that the second concurrent caller observes and awaits. The worker clears `inFlight` before signalling the TCS.

**Rationale**: the legacy implementation is correct (the comment in `Services.FSharp/Dictionary/DictionaryService.fs:124-138` explains the subtle ordering), well-understood, and pure F#/BCL. No need to invent. A `SemaphoreSlim` would also work but produces the same observable behaviour with extra ceremony.

**Alternatives considered**:
- *`SemaphoreSlim(1)`* — works, but second caller blocks rather than receiving the in-flight task; same observable behaviour, slightly different cancellation semantics. Either is fine; TCS is what the legacy team chose and what we know works.
- *No coalescing — just race* — violates FR-007. Two concurrent refreshes would issue two HTTP calls and two cache writes, with a race on the cache file. Reject.
- *Reactive (`IObservable`) pipeline* — overkill for one user-triggered button press.

---

## R6 — JSON library

**Decision**: plain `System.Text.Json` (BCL), with the source-generated metadata API (`JsonSerializerOptions { TypeInfoResolver = SourceGenerationContext.Default }`) so AOT and trimming are safe later. No F#-aware converter package; the wire DTOs are pure records with public properties, which `System.Text.Json` handles natively — DUs stay in the domain layer and never appear on the wire.

**Rationale**: BCL, no NuGet dependency, source generators eliminate reflection at hot paths. The only F# shape we deserialise (`DictionaryResolvedDto`) is a record-of-records; the BCL's record support is sufficient.

**Alternatives considered**:
- *`JsonFSharpConverter` (`JsonFSharp` package)* — F#-aware (handles DUs, options idiomatically) but adds a NuGet dependency for shapes we don't have on the wire. Reconsider only if a future slice needs to deserialise DUs.
- *Newtonsoft.Json* — mature but heavyweight; we only need the basics. Adding it is justified only if we hit a real `System.Text.Json` limitation.
- *FsToolkit / Thoth.Json* — F#-idiomatic but introduces a NuGet dependency and bespoke encoder/decoder ceremony that's overkill for two DTO shapes.
- *Hand-rolled parser* — no.

---

## R7 — Avalonia.FuncUI registration dialog

**Decision**: implement the registration dialog as an Avalonia `Window` opened with `ShowDialog(MainWindow)` from the GUI layer's startup orchestration. The dialog hosts a FuncUI Elmish view with `model = { Token: string; State: Idle | Submitting | Failed of string }` and three messages: `TokenChanged`, `Submit`, `RegistrationCompleted of Result<unit, RegistrationError>`. On `Submit`, dispatch `IRegistrationClient.RegisterAsync token` via Elmish's `Cmd.OfAsync`. On success, persist via `ICredentialStore.SaveAsync` then close the dialog.

**Rationale**: Avalonia's modal-window machinery is what the platform expects for blocking UX (FR-014). FuncUI's Elmish loop is the standard architecture for this app's GUI — keeping the dialog Elmish keeps the testing story uniform (`Avalonia.Headless` can drive the message loop directly).

**Alternatives considered**:
- *Inline overlay in the main window* — possible but loses the OS-level "dialog in front" affordance; the spec asked for a modal experience (FR-014 "blocks the main window").
- *Custom popup with hand-rolled focus management* — reinvents `Window`; rejected.

---

## R8 — Status-row in-flight UX detail

**Decision**: while a refresh is in flight, the indicator pill remains its current colour (green/orange) but pulses subtly (Avalonia `Animation` on the indicator's `Opacity` between 0.6 and 1.0, 800 ms cycle). The Refresh button's content swaps to a small spinner glyph and the button is disabled. The headline gains a trailing ellipsis: `Live · synced 14:32 · refreshing…`.

**Rationale**: pulsing avoids "indicator went grey" confusion (which would imply "no data"). Spinner-on-button is the standard "in-flight" idiom. Headline ellipsis is the textual cue that survives screenshot review.

**Alternatives considered**:
- *Replace the indicator with a spinner* — loses state continuity; the user momentarily can't tell what state they were in.
- *Banner across the top "Refreshing dictionary…"* — shouts; this should be unobtrusive.
- *No visual cue, only enable/disable the button* — fails the "user can tell something is happening" usability bar.

---

## R9 — Seed-staleness soft threshold

**Decision**: 90 days. If the seed's `seededAt` is older than 90 days at launch and no live fetch has succeeded since, render an additional muted-yellow advisory glyph next to the headline ("⚠ seed is stale") with a hover tooltip ("Last refreshed by STEM YYYY-MM-DD; update via Refresh when network is available."). No hard block; the technician can still operate.

**Rationale**: 90 days mirrors the typical release cadence buffer. A hard block would prevent perfectly valid offline use; an advisory cue is enough to nudge a refresh.

**Alternatives considered**:
- *60 days* — too aggressive given current release cadence guesses.
- *180 days* — doesn't communicate urgency in time for a quarterly release rhythm.
- *Configurable threshold* — premature; unilateral 90-day default is fine until evidence says otherwise.
- *Hard block* — punishes offline use; rejected.

---

## R10 — Lean Phase1 module scope

**Decision**: four modules, one theorem each, one Lean file per concept. Naming: `Stem.ButtonPanelTester.Phase1.<ConceptName>`. Each `.lean` file proves exactly one preservation theorem and re-exports the closed types it operates on.

**Rationale**: starting Lean small lets the formalisation track stay green from day one. Each theorem is independently provable and re-usable in Phase 2 modules. Bundling more concepts per file makes the proofs harder to read and the dependencies harder to manage.

**Alternatives considered**:
- *One mega-file `Phase1.lean`* — couples everything; refactors propagate; reading proofs requires scanning the whole file. Rejected.
- *One sub-namespace per port* — premature; we have one port boundary that warrants a theorem here (`IDictionaryProvider`'s success-xor-failed return). Adding sub-namespaces is overhead for one concept.

The four modules and their theorems are listed in `plan.md`'s Constitution Check (Principle I).

---

## R11 — Re-registration UX on auth failure (FR-018 mechanics)

**Decision**: on a refresh whose HTTP layer returns 401, surface an inline "Re-register" button in the status row's detail affordance. Click re-opens the registration dialog without deleting the existing credential. The new credential overwrites the stored one only on a successful `POST /register` round-trip (write the new one before deleting the old; if the new write succeeds and the credential file path is the same, the rename is atomic).

**Rationale**: protects against the "user clicks re-register, mistypes, dialog fails, now I have no credential" foot-gun. Atomic-overwrite-on-success preserves the prior credential as a fallback.

**Alternatives considered**:
- *Delete first, then prompt* — leaves the user in a no-credential state if they cancel mid-dialog.
- *Auto-trigger re-registration on 401* — surprising; the user should be the one who chooses to enter a new token.

---

## Open follow-ups (do not block this slice)

- **`eng/refresh-seed.ps1`**: requires a STEM-side dev API key passed as `$env:STEM_DICT_KEY` at the time the script is run. We document the script's prerequisites in its header comment; no secret-management infrastructure for v1.
- **CI seed-freshness lint**: deferred to a later release-hardening feature (see WIP.md / yesterday's seed-update conversation).
- **Cross-platform credential adapter**: tracked when the first non-Windows supplier deployment is requested. Not a v1 requirement.
- **`Dictionary:BaseUrl` per-environment config**: `appsettings.Development.json` carries `https://localhost:7065`, production carries the prod URL; both are checked-in. Production secrets (the per-supplier API key) live entirely in DPAPI on the supplier's machine, not in source.
