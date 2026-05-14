# Contract: F# port signatures

**Owner**: `Stem.ButtonPanelTester.Core.Dictionary.Ports`
**Consumers**: `Services.Dictionary.DictionaryService`, `Composition.CompositionRoot`, all test fakes.

---

This document is the authoritative signature reference. The implementation file `src/ButtonPanelTester.Core/Dictionary/Ports.fs` is the source of truth for the F# code; this document mirrors it for the spec layer (with the cancellation-token and `Async`/`Task` story spelled out for downstream readers).

The full type definitions live in [data-model.md §2](../data-model.md). This file restates them once with a focus on each port's contract — preconditions, return semantics, and what each adapter must provide.

---

## Common conventions

- All async methods accept a `CancellationToken` and return `Task<T>` (not `Async<T>`) so consumers in F# and C# both have a uniform consumption shape (`task { … }` in F#, `await` in C#).
- Cancellation honours the CANCELLATION standard: every IO and awaitable observes the token; an `OperationCanceledException` flowed up to the caller is the cooperative termination signal.
- Methods that may produce a typed failure return `Result<_,_>` or a domain DU (e.g. `DictionaryFetchResult`); they do not throw for expected failures. Unexpected failures (programmer error, invariant violation) propagate as exceptions.

---

## `IClock`

```fsharp
type IClock =
    abstract member UtcNow : unit -> DateTimeOffset
```

**Why a port**: lets `DictionaryService` and tests reason deterministically about `FetchedAt` timestamps. Without this, every test that compares `DictionarySource.Live(t)` against a fixture would have to allow time-skew tolerance.

**Production adapter** (`Infrastructure.Clock.SystemClock`):
```fsharp
type SystemClock() =
    interface IClock with
        member _.UtcNow () = DateTimeOffset.UtcNow
```

**Test adapter** (`Tests.Fakes.FrozenClock`):
```fsharp
type FrozenClock(initial: DateTimeOffset) =
    let mutable now = initial
    interface IClock with
        member _.UtcNow () = now
    member _.Advance (span: TimeSpan) = now <- now + span
    member _.SetTo (t: DateTimeOffset) = now <- t
```

---

## `IDictionaryProvider`

```fsharp
type IDictionaryProvider =
    abstract member FetchAsync :
        ct: CancellationToken -> Task<DictionaryFetchResult>
```

**Contract**:
- MUST return either `Success(dict, fetchedAt)` or `Failed(reason, detail)`. Never both, never neither (enforced by the F# DU; mechanised in `lean/.../DictionaryProvider.lean`).
- MUST NOT throw for expected failures listed in `dictionary-api.md`'s error table. Surface them as `Failed`.
- MAY throw `OperationCanceledException` if `ct` is cancelled mid-flight.
- The `fetchedAt` returned in `Success` MUST be the time at which the live response was received (use the injected `IClock`).

**Production adapter** (`Infrastructure.Http.HttpDictionaryProvider`): drives the contract in [`dictionary-api.md`](./dictionary-api.md).

**Test adapter** (`Tests.Fakes.InMemoryDictionaryProvider`):
```fsharp
type InMemoryDictionaryProvider(scripted: DictionaryFetchResult seq) =
    let queue = System.Collections.Generic.Queue<_>(scripted)
    interface IDictionaryProvider with
        member _.FetchAsync _ = task {
            return queue.Dequeue ()  // throws InvalidOperationException if empty (test bug)
        }
```

---

## `IDictionaryCache`

```fsharp
type IDictionaryCache =
    abstract member ExistsAsync               : ct: CancellationToken -> Task<bool>
    abstract member ReadAsync                 : ct: CancellationToken -> Task<DictionaryFetchResult>
    abstract member WriteAsync                : dict: ButtonPanelDictionary
                                              * fetchedAt: DateTimeOffset
                                              * ct: CancellationToken
                                              -> Task
    abstract member ExtractSeedIfMissingAsync : ct: CancellationToken -> Task
```

**Contract**:
- `ExistsAsync` returns `true` iff a usable cache file pair (`dictionary.json` + sidecar) is on disk.
- `ReadAsync` returns `Success` if the cache reads cleanly and the sidecar hash matches; otherwise `Failed(CacheAbsent | CacheUnreadable, detail)`.
- `WriteAsync` is atomic (temp+rename per [`cache-format.md`](./cache-format.md)). Skips the write if the new content's hash equals the existing sidecar's value.
- `ExtractSeedIfMissingAsync` is no-op when `ExistsAsync` returns `true`. Otherwise extracts the embedded seed and writes it via the same atomic path.

**Production adapter** (`Infrastructure.Persistence.JsonFileDictionaryCache`): implements [`cache-format.md`](./cache-format.md).

**Test adapter** (`Tests.Fakes.InMemoryDictionaryCache`):
```fsharp
type InMemoryDictionaryCache() =
    let mutable state : (ButtonPanelDictionary * DateTimeOffset) option = None
    let mutable hasSeed = false
    member _.SeedWith (dict, fetchedAt) = hasSeed <- true; state <- Some(dict, fetchedAt)
    interface IDictionaryCache with
        member _.ExistsAsync _ = task { return state.IsSome }
        member _.ReadAsync _ = task {
            match state with
            | Some (d, t) -> return Success(d, t)
            | None -> return Failed(CacheAbsent, None)
        }
        member _.WriteAsync (d, t, _) = task { state <- Some(d, t) }
        member _.ExtractSeedIfMissingAsync _ = task {
            if state.IsNone && hasSeed then ()  // already in state from SeedWith
        }
```

---

## `ICredentialStore`

```fsharp
type ICredentialStore =
    abstract member ExistsAsync : ct: CancellationToken -> Task<bool>
    abstract member LoadAsync   : ct: CancellationToken -> Task<InstallationCredential option>
    abstract member SaveAsync   : credential: InstallationCredential
                                  * ct: CancellationToken
                                  -> Task
    abstract member DeleteAsync : ct: CancellationToken -> Task
```

**Contract**:
- `ExistsAsync` returns `true` iff a credential file exists on disk (does not validate decryptability).
- `LoadAsync` returns `Some credential` if the file is present and decrypts cleanly, `None` otherwise. Decryption failures are logged but not thrown (per [`credential-format.md`](./credential-format.md)).
- `SaveAsync` is atomic (temp+rename). Overwrites any prior value.
- `DeleteAsync` is idempotent — no error if the file is already absent.

**Production adapter** (`Infrastructure.Persistence.DpapiCredentialStore`): implements [`credential-format.md`](./credential-format.md).

**Test adapter** (`Tests.Fakes.InMemoryCredentialStore`):
```fsharp
type InMemoryCredentialStore() =
    let mutable value : InstallationCredential option = None
    interface ICredentialStore with
        member _.ExistsAsync _ = task { return value.IsSome }
        member _.LoadAsync _ = task { return value }
        member _.SaveAsync (c, _) = task { value <- Some c }
        member _.DeleteAsync _ = task { value <- None }
```

---

## `IRegistrationClient`

```fsharp
type IRegistrationClient =
    abstract member RegisterAsync :
        token: BootstrapToken
        * ct: CancellationToken
        -> Task<Result<InstallationCredential, RegistrationError>>
```

**Contract**:
- MUST translate every HTTP outcome listed in [`registration-api.md`](./registration-api.md) into `Ok` or a typed `Error`.
- MUST NOT throw for expected failures.
- MAY throw `OperationCanceledException`.
- The credential in `Ok` is the verbatim `apiCredential` field from the response, wrapped in `InstallationCredential.Create`.

**Production adapter** (`Infrastructure.Http.HttpRegistrationClient`): implements [`registration-api.md`](./registration-api.md).

**Test adapter** (`Tests.Fakes.InMemoryRegistrationClient`):
```fsharp
type InMemoryRegistrationClient() =
    let mutable script : Map<string, Result<InstallationCredential, RegistrationError>> = Map.empty
    member _.SetResult (token: string, result) =
        script <- script.Add(token, result)
    interface IRegistrationClient with
        member _.RegisterAsync (token, _) = task {
            match Map.tryFind token.Value script with
            | Some r -> return r
            | None -> return Error TokenInvalid    // unscripted = invalid by default
        }
```

---

## Composition root wiring

`GUI.Composition.CompositionRoot` registers the production adapters as singletons in the MEDI container. This is the **only** place where production adapter types are referenced — `Services` and `Core` do not name them. Test bootstraps register the in-memory fakes directly via service-collection extensions in `tests/.../Fakes/Wiring.fs`.

```fsharp
// CompositionRoot.fs (sketch)
let configure (services: IServiceCollection) (config: IConfiguration) =
    services
        .AddSingleton<IClock, SystemClock>()
        .AddHttpClient()  // standard MEDI extension
        .AddSingleton<IDictionaryProvider, HttpDictionaryProvider>()
        .AddSingleton<IDictionaryCache, JsonFileDictionaryCache>()
        .AddSingleton<ICredentialStore, DpapiCredentialStore>()
        .AddSingleton<IRegistrationClient, HttpRegistrationClient>()
        .AddSingleton<IDictionaryService, DictionaryService>()
    |> ignore
```
