module Stem.ButtonPanelTester.Tests.Unit.Can.ButtonStateFrameFixtureTests

open System
open System.IO
open System.Text.Json
open Xunit
open Stem.ButtonPanelTester.Core.Can

/// Path of the fixture file shipped by T006 at
/// `tests/ButtonPanelTester.Tests/Fixtures/Can/buttonStateFixtures.json`,
/// copied to the test bin directory by the project's
/// `<Content CopyToOutputDirectory="PreserveNewest" />` item.
let private fixturePath =
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "Can", "buttonStateFixtures.json")

/// In-memory shape of one fixture entry. Mirrors the JSON schema in
/// `Fixtures/Can/buttonStateFixtures.json`. `ExpectedAddress` /
/// `ExpectedBitmap` are present only on the parseable fixtures.
type private Fixture =
    { Name: string
      Payload: string
      ExpectsParse: bool
      ExpectedAddress: uint16 option
      ExpectedBitmap: byte option }

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

let private loadFixture (name: string) : Fixture =
    let json = File.ReadAllText fixturePath
    use doc = JsonDocument.Parse json
    let entries = doc.RootElement.GetProperty("fixtures").EnumerateArray()

    let element =
        entries
        |> Seq.find (fun e -> requireString e "name" = name)

    { Name = name
      Payload = requireString element "payload"
      ExpectsParse = element.GetProperty("expectsParse").GetBoolean()
      ExpectedAddress = tryGet element "expectedAddress" |> Option.map (fun v -> v.GetUInt16())
      ExpectedBitmap = tryGet element "expectedBitmap" |> Option.map (fun v -> v.GetByte()) }

/// Combined parse + encode assertion for one fixture: rebuild the wire
/// bytes from the hex string, run `ButtonStateFrame.parse`, check the
/// outcome matches `expectsParse` plus (on Some) the decoded address and
/// bitmap, and confirm `encode` reproduces the exact fixture bytes
/// (the round-trip the contract pins).
let private assertParseMatches (fixtureName: string) =
    let fixture = loadFixture fixtureName
    let bytes = hexToBytes fixture.Payload
    let parsed = ButtonStateFrame.parse(ReadOnlyMemory bytes)

    match fixture.ExpectsParse, parsed with
    | true, Some frame ->
        let expected =
            { Address = VariableAddress fixture.ExpectedAddress.Value
              Bitmap = KeyStateBitmap fixture.ExpectedBitmap.Value }

        Assert.Equal<ButtonStateFrame>(expected, frame)
        Assert.Equal<byte[]>(bytes, ButtonStateFrame.encode frame)
    | false, None -> () // expected silent drop on length mismatch
    | true, None -> Assert.Fail $"Fixture {fixtureName}: expected parse Some, got None"
    | false, Some _ -> Assert.Fail $"Fixture {fixtureName}: expected parse None, got Some"

[<Fact>]
let ``Fixture idle_all_released round-trips the all-released frame`` () =
    assertParseMatches "idle_all_released"

[<Fact>]
let ``Fixture optimus_light_pressed round-trips (DOWN bit1 cleared)`` () =
    assertParseMatches "optimus_light_pressed"

[<Fact>]
let ``Fixture optimus_suspension_pressed round-trips (P1 bit2 cleared)`` () =
    assertParseMatches "optimus_suspension_pressed"

[<Fact>]
let ``Fixture two_button_transition round-trips (DOWN and P1 cleared)`` () =
    assertParseMatches "two_button_transition"

[<Fact>]
let ``Fixture virgin_sentinel round-trips`` () =
    assertParseMatches "virgin_sentinel"

[<Fact>]
let ``Fixture virgin_sentinel parses to the 0x80FE marker; the observer drops it, not the parser`` () =
    let fixture = loadFixture "virgin_sentinel"
    let parsed = ButtonStateFrame.parse(ReadOnlyMemory(hexToBytes fixture.Payload))

    match parsed with
    | Some frame -> Assert.Equal(VariableAddress 0x80FEus, frame.Address)
    | None -> Assert.Fail "virgin_sentinel must parse (length-only reject); the observer drops 0x80FE, not the parser"

[<Fact>]
let ``Fixture malformed_too_short silently drops on length mismatch`` () =
    assertParseMatches "malformed_too_short"
