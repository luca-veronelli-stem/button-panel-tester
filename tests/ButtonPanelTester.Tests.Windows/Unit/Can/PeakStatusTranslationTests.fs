module Stem.ButtonPanelTester.Tests.Windows.Unit.Can.PeakStatusTranslationTests

open Xunit
open Peak.Can.Basic.BackwardCompatibility
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Infrastructure.Can

/// #150/#139 regression: `PeakStatusTranslation.translate` is a pure
/// lookup table from a raw PEAK `TPCANStatus` code to a user-facing
/// cause + remediation suggestion. These cases pin the known entries
/// and the unknown-code fallback. No hardware: `translate` takes the
/// code + text as arguments, so the table is exercised without touching
/// `PCANBasic`.

let private busyCode = uint32 TPCANStatus.PCAN_ERROR_INITIALIZE
let private busOffCode = uint32 TPCANStatus.PCAN_ERROR_BUSOFF

[<Fact>]
let ``translate_BusyCode_YieldsRecoverableAdapterBusyWithCloseAppSuggestion`` () =
    let t = PeakStatusTranslation.translate busyCode "raw busy text"

    Assert.False(t.Fatal)
    Assert.Equal("adapter busy", t.Cause)
    Assert.Equal(Some "close the app holding the channel", t.Suggestion)
    Assert.Equal(busyCode, t.RawCode)
    Assert.Equal<string>("raw busy text", t.RawText)

[<Fact>]
let ``translate_BusOffCode_YieldsBusOffWithReconnectSuggestion`` () =
    let t = PeakStatusTranslation.translate busOffCode "raw bus-off text"

    Assert.False(t.Fatal)
    Assert.Equal("bus-off", t.Cause)
    Assert.Equal(Some "try reconnect", t.Suggestion)
    Assert.Equal(busOffCode, t.RawCode)

[<Fact>]
let ``translate_UnknownCode_FallsBackToFirstLineOfRawTextNoSuggestion`` () =
    let t = PeakStatusTranslation.translate 0xDEADu "first line\nsecond line"

    Assert.False(t.Fatal)
    Assert.Equal("first line", t.Cause)
    Assert.Equal(None, t.Suggestion)
    Assert.Equal(0xDEADu, t.RawCode)

/// The compose helper renders `headline\nPEAK status 0x<code>: <text>`,
/// where the headline appends `-- <suggestion>` when present.
[<Fact>]
let ``detailText_WithSuggestion_AppendsSuggestionToHeadlineAndKeepsTechnicalLine`` () =
    let t = PeakStatusTranslation.translate busyCode "channel claimed"
    let detail = PeakStatusTranslation.detailText t

    let lines = detail.Split('\n')
    Assert.Equal(2, lines.Length)
    Assert.Equal<string>("adapter busy -- close the app holding the channel", lines.[0])
    Assert.Contains("PEAK status 0x", lines.[1])
    Assert.Contains("channel claimed", lines.[1])

[<Fact>]
let ``detailText_WithoutSuggestion_HeadlineIsBareCause`` () =
    let t = PeakStatusTranslation.translate 0xDEADu "mystery"
    let detail = PeakStatusTranslation.detailText t

    let lines = detail.Split('\n')
    Assert.Equal<string>("mystery", lines.[0])

/// Every table entry in this slice is non-fatal, so `toErrorClassification`
/// yields `Recoverable`. The detail it carries is the composed string.
[<Fact>]
let ``toErrorClassification_NonFatal_YieldsRecoverableWithComposedDetail`` () =
    let t = PeakStatusTranslation.translate busyCode "claimed"

    match PeakStatusTranslation.toErrorClassification t with
    | Recoverable detail -> Assert.Equal<string>(PeakStatusTranslation.detailText t, detail)
    | Fatal _ -> Assert.Fail("expected Recoverable for a non-fatal status")
