module Stem.ButtonPanelTester.Tests.Windows.Integration.HttpDictionaryProviderTimeoutTests

open Xunit
open Stem.ButtonPanelTester.Infrastructure.Http

/// Pins the cold-start-tolerant client-side timeout per
/// `specs/001-fetch-dictionary/phases/phase-7.md`. The 90 s deadline
/// covers the full distribution of cold-starts observed against
/// `app-dictionaries-manager-prod.azurewebsites.net` (PR #91 / issue
/// #92: max 89.91 s, mean 42 s). Asserting the literal here keeps
/// the timeout-value change reviewable without paying a real-time
/// wait — `FetchAsync_TaskCanceledFromInsideHandler_ReturnsFailedTimeout`
/// already covers the behavioural mapping at the adapter boundary.

[<Fact>]
let HttpDictionaryProvider_TimeoutSeconds_IsNinety () =
    Assert.Equal(90.0, HttpDictionaryProvider.TimeoutSeconds)
