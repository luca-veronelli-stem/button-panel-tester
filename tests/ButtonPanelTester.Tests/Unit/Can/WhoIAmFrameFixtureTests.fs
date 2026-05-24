module Stem.ButtonPanelTester.Tests.Unit.Can.WhoIAmFrameFixtureTests

open System
open System.IO
open System.Text.Json
open Xunit
open Stem.ButtonPanelTester.Core.Can

/// Path of the fixture file shipped by T021 at
/// `tests/ButtonPanelTester.Tests/Fixtures/Can/whoIAmFixtures.json`,
/// copied to the test bin directory by the project's
/// `<Content CopyToOutputDirectory="PreserveNewest" />` item.
let private fixturePath =
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "Can", "whoIAmFixtures.json")

/// In-memory shape of one fixture entry. Mirrors the JSON schema in
/// `Fixtures/Can/whoIAmFixtures.json`. The `expectedVariantIdentity`
/// label is matched against `VariantDecoder.decode` (T014, commit 3);
/// `expectedVariantRawByte` is only present on the `Unknown` fixture
/// and carries the raw byte the decoder must surface inside
/// `VariantIdentity.Unknown raw`.
type private Fixture =
    { Name: string
      Payload: string
      ExpectsParse: bool
      ExpectedMachineType: byte option
      ExpectedFwType: byte option
      ExpectedUuid: uint32[] option
      ExpectedVariantIdentity: string option
      ExpectedVariantRawByte: byte option }

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
      ExpectedMachineType = tryGet element "expectedMachineType" |> Option.map (fun v -> v.GetByte())
      ExpectedFwType = tryGet element "expectedFwType" |> Option.map (fun v -> v.GetByte())
      ExpectedUuid =
        tryGet element "expectedUuid"
        |> Option.map (fun v -> v.EnumerateArray() |> Seq.map (fun e -> e.GetUInt32()) |> Array.ofSeq)
      ExpectedVariantIdentity =
        tryGet element "expectedVariantIdentity"
        |> Option.bind (fun v ->
            match v.GetString() with
            | null -> None
            | s -> Some s)
      ExpectedVariantRawByte = tryGet element "expectedVariantRawByte" |> Option.map (fun v -> v.GetByte()) }

let private expectedIdentityFor (fixture: Fixture) : VariantIdentity option =
    fixture.ExpectedVariantIdentity
    |> Option.map (fun label ->
        match label with
        | "EdenXp" -> Marketing EdenXp
        | "OptimusXp" -> Marketing OptimusXp
        | "R3LXp" -> Marketing R3LXp
        | "EdenBs8" -> Marketing EdenBs8
        | "Virgin" -> Virgin
        | "Unknown" ->
            match fixture.ExpectedVariantRawByte with
            | Some raw -> Unknown raw
            | None ->
                failwithf
                    "Fixture %s: variant identity 'Unknown' requires expectedVariantRawByte"
                    fixture.Name
        | other -> failwithf "Fixture %s: unknown variant identity label %s" fixture.Name other)

/// Combined parse + variant assertion for one fixture: rebuild the
/// wire bytes from the hex string, run `WhoIAmFrame.parse`, check the
/// outcome matches the fixture's `expectsParse` flag plus (on Some)
/// the machine-type byte, fwType byte, three UUID words, and the
/// decoded `VariantIdentity`.
let private assertParseMatches (fixtureName: string) =
    let fixture = loadFixture fixtureName
    let bytes = hexToBytes fixture.Payload
    let parsed = WhoIAmFrame.parse(ReadOnlyMemory bytes)

    match fixture.ExpectsParse, parsed with
    | true, Some frame ->
        let (MachineTypeByte mt) = frame.MachineType
        let (FwType fw) = frame.FwType
        let (PanelUuid(u0, u1, u2)) = frame.Uuid
        Assert.Equal(fixture.ExpectedMachineType.Value, mt)
        Assert.Equal(fixture.ExpectedFwType.Value, fw)
        Assert.Equal<uint32[]>(fixture.ExpectedUuid.Value, [| u0; u1; u2 |])

        match expectedIdentityFor fixture with
        | Some expected -> Assert.Equal(expected, VariantDecoder.decode frame.MachineType)
        | None -> ()
    | false, None -> () // expected silent drop per FR-013
    | true, None -> Assert.Fail $"Fixture {fixtureName}: expected parse Some, got None"
    | false, Some _ -> Assert.Fail $"Fixture {fixtureName}: expected parse None, got Some"

[<Fact>]
let ``Fixture virgin_panel_uuid_AABBCC parses with machineType 0xFF`` () =
    assertParseMatches "virgin_panel_uuid_AABBCC"

[<Fact>]
let ``Fixture eden_xp_uuid_112233 parses with machineType 0x03`` () =
    assertParseMatches "eden_xp_uuid_112233"

[<Fact>]
let ``Fixture optimus_xp_uuid_445566 parses with machineType 0x0A`` () =
    assertParseMatches "optimus_xp_uuid_445566"

[<Fact>]
let ``Fixture r3l_xp_uuid_778899 parses with machineType 0x0B`` () =
    assertParseMatches "r3l_xp_uuid_778899"

[<Fact>]
let ``Fixture eden_bs8_uuid_AABBCC parses with machineType 0x0C`` () =
    assertParseMatches "eden_bs8_uuid_AABBCC"

[<Fact>]
let ``Fixture unknown_machine_type_uuid_DDEEFF parses with machineType 0x77`` () =
    assertParseMatches "unknown_machine_type_uuid_DDEEFF"

[<Fact>]
let ``Fixture malformed_too_short_14b silently drops on length mismatch`` () =
    assertParseMatches "malformed_too_short_14b"

[<Fact>]
let ``Fixture malformed_wrong_fwtype silently drops on fwType mismatch`` () =
    assertParseMatches "malformed_wrong_fwtype"
