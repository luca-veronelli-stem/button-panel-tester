# Data Model: Panel Discovery via Passive WHO_I_AM Observation

**Phase 1 output for**: [plan.md](./plan.md)

F# types, operations over them, invariants, and cross-references to the Lean Phase 2 modules that mechanise the invariants. Extracted from former `specs/002-can-link-and-panel-discovery/data-model.md` §2–§5 + §7 via #151. Lifecycle types (CanLinkState, AdapterIdentification) live in [`../002-can-link-lifecycle/data-model.md`](../002-can-link-lifecycle/data-model.md).

---

## 1. WHO_I_AM frame

### 1.1 Wire layout (`src/ButtonPanelTester.Core/Can/WhoIAmFrame.fs`)

Authoritative source: panel firmware `stem-fw-pac5524-tastiera-can-app-*/AutoAddressSlave.c:175–181` (broadcast payload construction), confirmed by the audit recorded in [`docs/Context/bpt-rollout/CORRECTIONS.md`](../../docs/Context/bpt-rollout/CORRECTIONS.md) §C1, §C2. The canonical wire-format contract lives in [contracts/who-i-am-wire-format.md](./contracts/who-i-am-wire-format.md); this section reflects the F# representation only.

### 1.2 F# types

```fsharp
type PanelUuid = PanelUuid of uuid0: uint32 * uuid1: uint32 * uuid2: uint32
type FwType = FwType of byte
type MachineTypeByte = MachineTypeByte of byte

type WhoIAmFrame = {
    MachineType : MachineTypeByte
    FwType : FwType
    Uuid : PanelUuid
}

val parse : ReadOnlyMemory<byte> -> WhoIAmFrame option   // None on malformed (FR-013)
val encode : WhoIAmFrame -> byte[]                       // 15-byte buffer
```

### 1.3 Invariant

- **Round-trip**: `parse (encode f) = Some f` for every well-formed `WhoIAmFrame`. **Lean**: `Phase2/WhoIAmFrame.lean` — `parse_encode_roundtrip`.

---

## 2. Variant identity

### 2.1 `VariantIdentity` (closed DU, `src/ButtonPanelTester.Core/Can/PanelObservation.fs`)

```fsharp
type MarketingVariant =
    | EdenXp     // machineType = 0x03
    | OptimusXp  // machineType = 0x0A
    | R3LXp      // machineType = 0x0B
    | EdenBs8    // machineType = 0x0C

type VariantIdentity =
    | Marketing of MarketingVariant
    | Virgin                  // machineType = 0xFF
    | Unknown of raw: byte    // any other value

val decodeVariant : MachineTypeByte -> VariantIdentity   // total
```

### 2.2 Invariant

- **Totality**: `decodeVariant` is defined on every `byte`. **Lean**: `Phase2/PanelObservation.lean` — `variant_decoding_total`.

The four marketing variants and their `machineType` bytes come from CORRECTIONS.md §"Items unchanged" — confirmed against each motherboard's `ID_MACHINE_TYPE` constant.

---

## 3. Panel observation

### 3.1 `PanelObservation` (record, `src/ButtonPanelTester.Core/Can/PanelObservation.fs`)

```fsharp
type PanelObservation = {
    Uuid : PanelUuid
    VariantByte : MachineTypeByte         // raw byte (FR-009 detail affordance)
    VariantIdentity : VariantIdentity     // decoded (FR-009 row label)
    LastSeen : DateTimeOffset             // FR-010 timestamp
}
```

### 3.2 Mapping rule

A `WhoIAmFrame f` arriving at `now` produces a `PanelObservation` with `Uuid = f.Uuid`, `VariantByte = f.MachineType`, `VariantIdentity = decodeVariant f.MachineType`, `LastSeen = now`.

---

## 4. Panels-on-bus list

### 4.1 `PanelsOnBus` (UUID-keyed map, `src/ButtonPanelTester.Core/Can/PanelsOnBus.fs`)

```fsharp
type PanelsOnBus = Map<PanelUuid, PanelObservation>

val empty : PanelsOnBus
val observe : DateTimeOffset -> WhoIAmFrame -> PanelsOnBus -> PanelsOnBus
val clear : PanelsOnBus -> PanelsOnBus   // for FR-015' link-loss
```

### 4.2 Pruning (`src/ButtonPanelTester.Core/Can/Pruning.fs`)

```fsharp
val prune : ttl: TimeSpan -> now: DateTimeOffset -> PanelsOnBus -> PanelsOnBus
```

For spec-003, `ttl = TimeSpan.FromSeconds 15.0` (FR-011, locked by clarify session 2026-05-24).

### 4.3 Operational semantics

- `observe now f m` inserts-or-updates `m[f.Uuid]` with a fresh `PanelObservation`. Existing rows have their `LastSeen` advanced; the `VariantByte` and `VariantIdentity` are also re-derived from the latest frame (handles the edge "panel power-cycled out of `AAS_STAND_BY` mid-session" cleanly).
- `prune ttl now m` returns `m` with every row whose `now - lastSeen > ttl` removed.
- `clear m` returns `empty`. Used by `CanLinkService` on Connected → ¬Connected (FR-015' consumer; upstream FR-015 lives in spec-002).

### 4.4 Invariants

- **Coalescing**: `(observe now f m).Count ≤ m.Count + 1`, and the equality case holds iff `f.Uuid ∉ m.Keys`. Same-UUID observations never produce duplicate rows. **Lean**: `Phase2/PanelsOnBus.lean` — `observe_coalesces_by_uuid`.
- **Pruning correctness**: post-prune membership iff `now - lastSeen ≤ ttl`. **Lean**: `Phase2/Pruning.lean` — `prune_partitions_by_threshold`.

---

## 5. Cross-reference to Lean Phase 2

| Lean module | Mechanises | F# source |
|---|---|---|
| `Phase2/WhoIAmFrame.lean` | §1.3 Round-trip | `Core/Can/WhoIAmFrame.fs` |
| `Phase2/PanelObservation.lean` | §2.2 Totality | `Core/Can/PanelObservation.fs` |
| `Phase2/PanelsOnBus.lean` | §4.4 Coalescing | `Core/Can/PanelsOnBus.fs` |
| `Phase2/Pruning.lean` | §4.4 Pruning correctness | `Core/Can/Pruning.fs` |

Lifecycle cross-references (`CanLinkState.lean`, `PassiveObserver.lean`) live in [`../002-can-link-lifecycle/data-model.md`](../002-can-link-lifecycle/data-model.md) §3.
