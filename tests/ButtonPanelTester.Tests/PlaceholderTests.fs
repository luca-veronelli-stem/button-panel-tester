module Stem.ButtonPanelTester.Tests.PlaceholderTests

open Xunit
open Stem.ButtonPanelTester.Core

[<Fact>]
let ``bootstrap marker is wired into the build`` () =
    Assert.Equal("v0.0.0-bootstrap", Placeholder.markerVersion)
