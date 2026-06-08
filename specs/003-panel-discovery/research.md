# Research: Panel Discovery via Passive WHO_I_AM Observation

**Phase 0 output for**: [plan.md](./plan.md)

These are spec-003's own Phase 0 decisions. To keep the spec **independent** (it
must stay correct when spec-001 / spec-002 are eventually superseded — see
[plan.md](./plan.md) §Relationship to spec-002), each decision is grounded in
two durable sources only: the **shipped code contracts** already in the tree and
the **firmware** facts verified on 2026-06-05. No decision here depends on a
sibling spec's planning documents.

---

## R1 — Adopt the firmware-verified WHO_I_AM layout; correct the shipped codec first *(load-bearing)*

**Decision.** Treat
[contracts/who-i-am-wire-format.md](./contracts/who-i-am-wire-format.md) —
read directly from `AutoAddressSlave.c:175-181` — as the authoritative wire
format: `[0]` `machineType` u8, `[1..2]` `fwType` big-endian `uint16`, `[3/7/11]`
three big-endian `uint32` UUID words, 15 bytes, no padding. The shipped
`WhoIAmFrame.fs` (#121) encodes a different, wrong layout and **rejects every
real frame** (it gates on `byte[1] = fwType >> 8 = 0x00 ≠ 0x04`). Correcting the
codec — `WhoIAmFrame.fs`, `Phase2/WhoIAmFrame.lean`, the FsCheck round-trip, and
`whoIAmFixtures.json` — is the **first** implementation slice, landing before any
pipeline work.

**Rationale.** The bench scenario (SC-001: a virgin panel appears within 6 s)
cannot pass on the shipped codec — the list never populates because parsing
rejects 100% of real frames. Every downstream behaviour (coalescing, pruning,
link-loss clear) is unreachable until the frame parses, so the correction is a
strict prerequisite, not a parallel concern. Folding it into spec-003 (rather
than a separate bug ticket) keeps the corrected contract, the corrected Lean
theorem, and the rebuilt fixtures in one reviewable vertical slice.

**Alternatives considered.** *Separate "fix WHO_I_AM" bug ticket first* —
rejected: it would split a single conceptual change (the wire format) across two
PRs and block spec-003 on an external merge. *Keep the codec, special-case the
bench* — rejected: it bakes the wrong format deeper.

---

## R2 — Sit below the vendored `PacketDecoder`; parse the raw frame in F#

**Decision.** `PanelDiscoveryService` consumes raw `RawCanFrame` from the shipped
`ICanFrameStream` port, filters for `CanId = 0x1FFFFFFF`, and parses the 15-byte
payload with `WhoIAmFrame.parse`. The vendored `PacketDecoder` is **not**
invoked, and the hardcoded protocol-metadata tables (`KnownStemCommands` /
`KnownProtocolAddresses`) are not needed in this slice.

**Rationale.** `WHO_I_AM` is an auto-address-layer broadcast, not an
application-layer protocol command: its CAN ID is fixed (`SRID_BROADCAST`) and
its 15-byte payload is parsable without any command-table lookup. Routing it
through `PacketDecoder` would only synthesise an "unknown command" event.
Staying below the decoder keeps the Lean round-trip self-contained (no Lean
model of `PacketDecoder`) and defers all command-resolution machinery to the
first transmit-side feature.

**Alternatives considered.** *Wire `PacketDecoder` for symmetry* — rejected,
buys nothing for a passive single-frame read and pulls in the metadata stopgap
early. *Parse against `Peak.PCANBasic.NET` directly* — rejected, throws away the
vendored reassembly/chunking the port already exposes.

---

## R3 — `RawCanFrame` as a struct over `ReadOnlyMemory<byte>`

**Decision.** Keep the shipped `RawCanFrame` shape: a `[<Struct>]` record
`{ CanId: uint32; Payload: ReadOnlyMemory<byte>; ReceivedAt: DateTimeOffset }`.
Parse inside the `OnNext` callback so no buffer reference escapes.

**Rationale.** On a fully-loaded 250 kbps bus the receive thread sees hundreds of
frames per second; a reference record + `byte[]` would allocate steady garbage
and risk GC-induced UI hitches. The struct + pooled `ReadOnlyMemory<byte>` costs
zero per-frame heap allocation. For WHO_I_AM alone (≈ 1 frame per panel per ~6 s)
this is overkill, but the same port serves denser transmit-side traffic later,
so the allocation-free shape is the right port-level default.

**Caveat (carried into the contract).** The `ReadOnlyMemory<byte>` is valid only
for the `OnNext` duration. `WhoIAmFrame.parse` returns a pure value type
(`uint32`/`uint16`/`byte`), so the service retains no span; any future retaining
subscriber MUST copy.

---

## R4 — Pruning timer lives in `PanelDiscoveryService`

**Decision.** `PanelDiscoveryService` owns a single `System.Threading.Timer`
ticking at 1 s. Each tick computes `prune 15s (clock.UtcNow())` and publishes
`PanelsOnBusChanged` only when the map actually changed.

**Rationale.** Pruning is a temporal service-layer concern, not a port concern.
A 1 s tick bounds the worst-case lag past the 15 s threshold to 1 s — responsive
without thrashing the UI. A single timer avoids the per-row timer-leak bug class.
Publishing only on change keeps an idle bench (steady-state, nothing expiring)
from emitting redundant renders; `prune_idempotent` (Lean) guarantees a no-op
tick is genuinely a no-op. After #197 split discovery out of `CanLinkService`
into its own service, this timer belongs in `PanelDiscoveryService`, not the
lifecycle service.

**Alternatives considered.** *Reactive `Throttle`/`Sample`* — rejected, pulls in
`System.Reactive`. *Lazy prune-on-read* — rejected, gives the GUI no signal to
re-render when a row expires. *Prune on every frame* — rejected, bursts cause
render storms. *100 ms tick* — rejected, no benefit over 1 s for a 15 s TTL.

---

## R5 — Hand-rolled hot observable, no `System.Reactive`

**Decision.** `PanelsOnBusChanged` stays a hand-rolled
`IObservable<PanelsOnBus>` backed by a fan-out subject (the
`DiscoveryObservable` module already in `PanelDiscoveryService.fs`), mirroring
the lifecycle service's subject style. The pipeline slice makes it actually
publish (the stub's `OnNext` is currently never called) and gives subscribers a
working `Dispose`.

**Rationale.** The GUI needs exactly one hot stream of `PanelsOnBus` snapshots,
subscribed once at composition time through FuncUI's `Cmd.ofSub`. A full Rx
dependency is unjustified for one subject with one consumer, and the repo already
standardised on hand-rolled subjects for the link-state feed. Keeping the same
pattern means one mental model for both feeds.

**Caveat.** The shipped stub backs the subject with a `ConcurrentBag`, which
cannot remove an observer — acceptable for a never-unsubscribing composition-time
GUI subscriber, but the pipeline slice should either document that single-shot
lifetime explicitly or move to a structure whose `Dispose` truly detaches, so a
test that subscribes and disposes does not leak into later assertions.

---

## R6 — Depend on the CAN-link lifecycle as a capability, through the shipped service contract

**Decision.** Express the only cross-feature dependency — "observe while
Connected, clear when not" — against the **shipped interface contracts**
`ICanLinkService` (`CurrentState`, `LinkStateChanged : IObservable<CanLinkState>`)
and the `IPanelDiscoveryService` facade this feature fills. Spec-003 does not
reference how the link is established, which adapter backs it, or which feature
delivered it.

**Rationale.** This is what makes the spec independent. The dependency is a
behavioural capability — an observable `Connected` state plus change
notifications — and that capability is fully captured by the F# interface already
in the tree. Binding to the interface rather than to spec-002's planning prose
means spec-003 stays valid when those documents are superseded (tracked under
#190). The discovery → lifecycle edge is one-directional: nothing in
`ICanLinkService` references discovery (verified in the shipped code after #197).

**Alternatives considered.** *Re-derive the lifecycle model in spec-003* —
rejected, duplicates a contract another feature owns. *Cite spec-002's plan as
the dependency* — rejected, couples spec-003 to a document slated for supersede.

---

## R7 — `fwType` is informational; do not gate acceptance

**Decision.** Read `fwType` as a big-endian `uint16` and retain it on the parsed
frame, but never reject on it. Acceptance is length-only (= 15). Add a 24 V
fixture (`fwType = 0x000F`) so the non-12 V path is exercised; `fwType` is not
surfaced in the Panels-on-bus row.

**Rationale.** Only panels run `AA_Slave` and emit `WHO_I_AM`
(`SP_APP_CMD_AA_WHO_I_AM`), so the frame already identifies a panel — `fwType`
adds nothing to acceptance and gating on `0x0004` wrongly rejects every 24 V
panel (`0x000F`). The spec asks only for UUID, decoded variant identity, and
last-seen, so `fwType` rides along as metadata for a later feature without a row
of its own.

**Open, non-blocking.** Whether any in-scope machine actually ships 24 V panels
affects only the fixture set (does the 24 V case come from a real capture or stay
synthetic?), not the parser. Resolve when the correction slice's fixtures are
captured at the bench.

---

## R8 — No identity-bearing data on this feature's path

**Decision.** The observed UUIDs are panel **hardware device** identifiers, held
only in volatile UI memory; nothing on this feature's path is an OS user / machine
name / SID / MAC, and nothing flows to STEM-controlled storage. Constitution
Principle V (hash-at-capture) does not apply.

**Rationale.** Principle V enumerates the identity-bearing fields it governs;
device UUIDs are not among them, and the discovery list never persists or leaves
the supplier's machine (it rebuilds from live broadcasts every session, per the
spec's "no persistence" out-of-scope item). This matches the conclusion the
shared CAN foundation reached when it shipped.

---

## Shared infrastructure (not a spec dependency)

The vendored STEM protocol stack under `ButtonPanelTester.Infrastructure` (the
reassembly/chunking layer the production `PcanCanFrameStream` will subscribe to)
is **repo-level shared infrastructure**, governed by its own vendoring discipline
and stopgap waiver. Spec-003 consumes it through the `ICanFrameStream` port and
does not re-vendor or modify it. This is an infrastructure fact about the tree,
not a dependency on another spec's documents.
