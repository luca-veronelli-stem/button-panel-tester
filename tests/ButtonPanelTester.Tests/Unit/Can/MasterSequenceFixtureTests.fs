module Stem.ButtonPanelTester.Tests.Unit.Can.MasterSequenceFixtureTests

open System
open System.IO
open System.Text.Json
open Xunit
open Stem.ButtonPanelTester.Core.Can

/// Path of the fixture file shipped by T007 at
/// `tests/ButtonPanelTester.Tests/Fixtures/Can/masterSequenceFixtures.json`,
/// copied to the test bin directory by the project's
/// `<Content CopyToOutputDirectory="PreserveNewest" />` item.
let private fixturePath =
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "Can", "masterSequenceFixtures.json")

let private hexToBytes (hex: string) : byte[] =
    let length = hex.Length / 2
    let bytes = Array.zeroCreate length

    for i in 0 .. length - 1 do
        bytes[i] <- Convert.ToByte(hex.Substring(i * 2, 2), 16)

    bytes

let private requireString (element: JsonElement) (propertyName: string) : string =
    let raw = element.GetProperty(propertyName).GetString()

    match raw with
    | null -> failwithf "Fixture JSON: property %s missing or null" propertyName
    | s -> s

let private tryGet (element: JsonElement) (propertyName: string) : JsonElement option =
    let mutable value = JsonElement()

    if element.TryGetProperty(propertyName, &value) then
        Some value
    else
        None

/// Find one named entry in the master-sequence fixture file and apply
/// `project` to it while the backing `JsonDocument` is still alive.
/// Generic over the projection so A3 can layer SET_ADDRESS-shaped
/// records over the same fixture file without touching the loader.
let private loadEntry (name: string) (project: JsonElement -> 'T) : 'T =
    let json = File.ReadAllText fixturePath
    use doc = JsonDocument.Parse json
    let entries = doc.RootElement.GetProperty("fixtures").EnumerateArray()
    let element = entries |> Seq.find (fun e -> requireString e "name" = name)
    project element

/// In-memory shape of one WHO_ARE_YOU fixture entry. Mirrors the JSON
/// schema in `Fixtures/Can/masterSequenceFixtures.json`; the expected
/// fields are absent on the malformed entry.
type private WhoAreYouFixture =
    { Name: string
      Payload: string
      ExpectsParse: bool
      ExpectedMachineType: byte option
      ExpectedFwType: uint16 option
      ExpectedReset: bool option }

let private loadWhoAreYouFixture (name: string) : WhoAreYouFixture =
    loadEntry name (fun element ->
        { Name = name
          Payload = requireString element "payload"
          ExpectsParse = element.GetProperty("expectsParse").GetBoolean()
          ExpectedMachineType = tryGet element "expectedMachineType" |> Option.map (fun v -> v.GetByte())
          ExpectedFwType = tryGet element "expectedFwType" |> Option.map (fun v -> v.GetUInt16())
          ExpectedReset = tryGet element "expectedReset" |> Option.map (fun v -> v.GetBoolean()) })

/// Combined TX + round-trip assertion for one WHO_ARE_YOU fixture:
/// build the frame from the expected fields and check `encode`
/// reproduces the normative wire bytes verbatim (TX direction — the
/// load-bearing assert; the payloads are firmware-parser-verified
/// synthesis targets per contract §"WHO_ARE_YOU app payload (4 B)"),
/// then check `parse` recovers the same frame from those bytes.
/// Non-parseable entries assert the silent `None` drop instead —
/// length is the only rejection axis.
let private assertWhoAreYouFixture (fixtureName: string) =
    let fixture = loadWhoAreYouFixture fixtureName
    let bytes = hexToBytes fixture.Payload
    let parsed = WhoAreYouFrame.parse(ReadOnlyMemory bytes)

    match fixture.ExpectsParse, parsed with
    | true, Some frame ->
        let expected: WhoAreYouFrame =
            { MachineType = fixture.ExpectedMachineType.Value
              FwType = fixture.ExpectedFwType.Value
              Reset = fixture.ExpectedReset.Value }

        Assert.Equal<byte[]>(bytes, WhoAreYouFrame.encode expected)
        Assert.Equal(expected, frame)
    | false, None -> () // expected silent drop — length-only reject
    | true, None -> Assert.Fail $"Fixture {fixtureName}: expected parse Some, got None"
    | false, Some _ -> Assert.Fail $"Fixture {fixtureName}: expected parse None, got Some"

[<Fact>]
let ``Fixture claim_eden_xp_12v encodes the EDEN-XP claim at fwType 0x0004`` () =
    assertWhoAreYouFixture "claim_eden_xp_12v"

[<Fact>]
let ``Fixture claim_optimus_xp_12v encodes the OPTIMUS-XP claim at fwType 0x0004`` () =
    assertWhoAreYouFixture "claim_optimus_xp_12v"

[<Fact>]
let ``Fixture claim_r3l_xp_12v encodes the R-3L XP claim at fwType 0x0004`` () =
    assertWhoAreYouFixture "claim_r3l_xp_12v"

[<Fact>]
let ``Fixture claim_eden_bs8_12v encodes the EDEN-BS8 claim at fwType 0x0004`` () =
    assertWhoAreYouFixture "claim_eden_bs8_12v"

[<Fact>]
let ``Fixture claim_eden_xp_24v encodes the EDEN-XP claim at fwType 0x000F`` () =
    assertWhoAreYouFixture "claim_eden_xp_24v"

[<Fact>]
let ``Fixture reset_12v encodes the virgin-marker reset at fwType 0x0004`` () =
    assertWhoAreYouFixture "reset_12v"

[<Fact>]
let ``Fixture reset_24v encodes the virgin-marker reset at fwType 0x000F`` () =
    assertWhoAreYouFixture "reset_24v"

[<Fact>]
let ``Fixture malformed_too_short_3b silently drops on length mismatch`` () =
    assertWhoAreYouFixture "malformed_too_short_3b"
