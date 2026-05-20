module Stem.ButtonPanelTester.Tests.Windows.Integration.HttpRegistrationClientTimeoutTests

open Xunit
open Stem.ButtonPanelTester.Infrastructure.Http

/// Companion of `HttpDictionaryProviderTimeoutTests`. The two clients
/// keep uniform timeout behaviour per `contracts/registration-api.md`
/// §"Timeout and retries"; both move from 10 s to 90 s for cold-start
/// tolerance per `specs/001-fetch-dictionary/phases/phase-7.md`.

[<Fact>]
let HttpRegistrationClient_TimeoutSeconds_IsNinety () =
    Assert.Equal(90.0, HttpRegistrationClient.TimeoutSeconds)
