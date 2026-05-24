# Contract: WHO_I_AM wire format

**Phase 1 output for**: [../plan.md](../plan.md)
**Implements**: FR-007, FR-008, FR-009, FR-013

Canonical wire-format specification for the `WHO_I_AM` broadcast that spec-002 consumes. The authoritative source is panel firmware `stem-fw-pac5524-tastiera-can-app-*/AutoAddressSlave.c` lines 165–183 (broadcast loop) and 175–181 (payload construction); the tester treats this contract as frozen for spec-002 and reads from it. See also [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../../docs/Context/bpt-rollout/CORRECTIONS.md) §C1 and §C2 for the audit trail.

## Transport

| Aspect | Value |
|---|---|
| CAN ID | `0x1FFFFFFF` (29-bit extended; `SRID_BROADCAST` in panel firmware) |
| Frame type | Logical 15-byte message — emitted via the vendored stack's chunking layer; spec-002 receives the reassembled `Payload` already concatenated. |
| Bitrate | 250 kbps |
| Direction | Slave-to-bus broadcast; the tester receives only (FR-014). |
| Cadence | Every `2000 + (sum(uuid_bytes) mod 4000)` ms — i.e. 2–6 s worst case. Source: `AutoAddressSlave.c:167`. |
| Trigger | Panel state ∈ `{AAS_STARTUP, AAS_ANSWER_TO_MASTER}` (virgin or mid-baptism). Silent in `AAS_STAND_BY` (claimed). |

## Payload (15 bytes)

| Offset | Width | Field | Encoding | Notes |
|---|---|---|---|---|
| 0 | 1 | `machineType` | `UInt8` | Virgin = `0xFF`; claimed = one of `{0x03 EDEN-XP, 0x0A OPTIMUS-XP, 0x0B R-3L XP, 0x0C EDEN-BS8}` |
| 1 | 1 | `fwType` | `UInt8` | Always `0x04` for button panels (per audit) |
| 2 | 4 | `uuid0` | `UInt32` big-endian | First UUID word |
| 6 | 4 | `uuid1` | `UInt32` big-endian | Second UUID word |
| 10 | 4 | `uuid2` | `UInt32` big-endian | Third UUID word |
| 14 | 1 | (padding) | — | Unused; value ignored on receive |

## Parse contract

The tester `parse` function (`src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs`) MUST:

1. **Length check.** Reject payloads whose length ≠ 15 by returning `None` (FR-013 silent drop).
2. **fwType check.** Reject payloads whose `fwType ≠ 0x04` by returning `None`. The auto-address layer is only used for button panels in this product; an `fwType` mismatch implies a different device type on the bus, which is out of scope for spec-002 and a silent-drop case per FR-013.
3. **Accept any `machineType`.** Decoding to `Marketing _ | Virgin | Unknown _` happens downstream in `decodeVariant` (FR-009). The parser is not the place to reject unknown machine types — `Unknown raw` is a first-class outcome.
4. **Big-endian UUID reads.** `uuid0..2` use `BinaryPrimitives.ReadUInt32BigEndian` against the corresponding 4-byte spans.

## Fixtures

`tests/ButtonPanelTester.Tests/Fixtures/Can/whoIAmFixtures.json` carries one captured 15-byte payload per known case:

| Fixture | `machineType` | Expected `VariantIdentity` | Source |
|---|---|---|---|
| `virgin_panel_uuid_AABBCC.json` | `0xFF` | `Virgin` | Bench capture 2026-05-24 |
| `eden_xp_uuid_112233.json` | `0x03` | `Marketing EdenXp` | Bench capture 2026-05-24 |
| `optimus_xp_uuid_445566.json` | `0x0A` | `Marketing OptimusXp` | Bench capture 2026-05-24 |
| `r3l_xp_uuid_778899.json` | `0x0B` | `Marketing R3LXp` | Bench capture 2026-05-24 |
| `eden_bs8_uuid_AABBCC.json` | `0x0C` | `Marketing EdenBs8` | Bench capture 2026-05-24 |
| `unknown_machine_type_uuid_DDEEFF.json` | `0x77` | `Unknown 0x77uy` | Synthetic |
| `malformed_too_short_14b.json` | n/a | `None` (parse fails) | Synthetic |
| `malformed_wrong_fwtype.json` | `0x03`, fwType `0x07` | `None` (parse fails per rule 2) | Synthetic |

The bench-captured fixtures are committed verbatim; the synthetic ones are written by the test setup in `Fixtures/Can/` and refreshed only if the wire layout itself changes.

## Lean cross-reference

`lean/Stem/ButtonPanelTester/Phase2/WhoIAmFrame.lean` proves `parse_encode_roundtrip`. The Lean model uses `Nat`-encoded bytes; the F# implementation reads `UInt8` / big-endian `UInt32`. The round-trip property holds across both representations because `encode` is a bijection on its image (well-formed frames) and `parse` is its left inverse on that image.
