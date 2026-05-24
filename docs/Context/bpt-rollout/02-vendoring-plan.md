# Protocol stack — vendoring plan (fallback path)

## Decision

The button-panel-tester needs the STEM 7-layer protocol stack to talk
to the panel over CAN. There are three plausible sources:

1. **Wait for the `Stem.Communication` NuGet to ship** and consume it
   as a dependency. The library exists at `/code/stem/stem-communication`
   with ~2602 tests and 96% coverage, but is not yet packaged or
   released. Estimated time to get it shippable is too long given the
   tester's delivery pressure.
2. **Vendor-copy from `stem-communication`** into the tester's
   Infrastructure layer and treat the copy as frozen. ← **chosen path**.
3. **Reimplement from the firmware C source.** Cleanest but several
   weeks of work; out of scope.

We use option (2) as a deliberate, time-boxed compromise. The vendored
code is a *frozen import*, never edited locally; when the NuGet is
ready, the swap is mechanical.

## Discipline — how to vendor without forking

Three rules. Following them keeps the eventual migration cheap; breaking
any of them turns the vendoring into a fork.

### Rule 1 — Vendor verbatim, then namespace-rename, then stop

- Copy the source files unchanged from `stem-communication` HEAD.
- Record the upstream commit SHA in a single `VENDOR.md` at the top
  of the vendored tree.
- The **only** modification permitted at copy time is a namespace
  rename — `Stem.Communication.*` → `Stem.ButtonPanelTester.Infrastructure.Protocol.*`
  — and only if compilation requires it.
- Every vendored file gets a one-line header comment:

  ```fsharp
  // Vendored from stem-communication @ <SHA>. Do not edit; report upstream bugs.
  ```

  (Or the equivalent in C#.)

- A pre-commit check (`eng/check-vendored-untouched.sh` or similar)
  hashes the vendored tree and warns on any local diff that isn't a
  re-vendor. The receiving Claude should add this to spec-002's plan.

### Rule 2 — Bug fixes go upstream

If the tester finds a bug in the vendored code:
- Open an issue on `stem-communication`.
- Fix it upstream.
- Re-vendor — bump the SHA in `VENDOR.md`, copy the changed files,
  done.

The tester repo never carries a "local patch" to the vendored stack.

### Rule 3 — Public boundary is owned by the tester

The vendored code is an implementation detail. The tester's `Core`
layer defines its own seams:

- `ICommunicationPort` — open / close / send / receive / state-changed.
  CAN-flavoured payloads; one implementation in the tester (`PcanPort`)
  which wraps the vendored Drivers.Can adapter.
- `IProtocolService` — high-level operations: baptize, read variable,
  write variable, subscribe to button events, send LED command, etc.
  One implementation in the tester which wraps the vendored
  `SP_Application`-equivalent.
- `BoardVariant` — pure tester domain; never sees the vendored code.

Consumers of the tester's `Services/` layer **must not** import from
the vendored namespace. If they need to, the boundary is wrong and
should be widened in `Core`.

When the NuGet ships, those interfaces stay; only the implementation
files swap. Spec-006/007 should not require touching `Core` to do the
swap.

## What to vendor — concrete file list

From `/code/stem/stem-communication`, copy these subtrees into
`/code/stem/button-panel-tester/src/ButtonPanelTester.Infrastructure/Vendored/StemCommunication/`:

### Always-needed (any CAN consumer)

```
Protocol/                       ← all 7-layer modules
  Application/
  Presentation/
  Transport/
  Network/
  Common/                       ← shared utilities (CRC, framing helpers)
Drivers.Can/                    ← PEAK PCAN-USB adapter
Client/                         ← high-level client surface (used as
                                  inspiration for IProtocolService)
```

### Probably-needed (verify on first compile)

```
Protocol/Telemetry/             ← only if spec-004's button events use
                                  the telemetry stream rather than
                                  polled variable reads
```

### Do NOT vendor

```
Drivers.Ble/                    ← BLE transport unused by the tester
Tests/                          ← the vendored code rides on its own
                                  upstream test suite; the tester's
                                  tests target the tester's seams
```

## Tester-side wiring

```
src/
  ButtonPanelTester.Core/
    Protocol/                                ← interfaces only
      ICommunicationPort.fs
      IProtocolService.fs
      BoardVariant.fs
      ProtocolAddress.fs

  ButtonPanelTester.Infrastructure/
    Vendored/StemCommunication/              ← frozen import
      VENDOR.md                              ← SHA + date + how-to-rebump
      Protocol/...
      Drivers.Can/...
      Client/...
    Hardware/
      PcanPort.fs                            ← ICommunicationPort impl,
                                              wraps Vendored.Drivers.Can
    Protocol/
      ProtocolService.fs                     ← IProtocolService impl,
                                              wraps Vendored.SP_Application

  ButtonPanelTester.Services/
    Baptize/                                 ← uses IProtocolService only,
    ButtonTest/                                never the vendored namespace
    LedBuzzerTest/
    Session/
```

The `Vendored/` directory is `<EmbeddedResource>` for nothing — it
just compiles as part of `Infrastructure`. It is excluded from
formatting/linting checks via the standards' usual `.editorconfig`
exemption for vendored trees (the receiving Claude should add this
exemption explicitly in spec-002's plan if it isn't already inherited).

## VENDOR.md template

The vendored tree's `VENDOR.md` should record exactly:

```markdown
# Vendored: stem-communication

**Upstream:** https://github.com/luca-veronelli-stem/stem-communication
**Vendored SHA:** <copy from `git rev-parse HEAD` in stem-communication>
**Vendored at:** <ISO date>
**Vendored by:** spec-002 (replace with PR # when known)

## Subtrees copied

- `Protocol/` (all 7 layers + Common)
- `Drivers.Can/`
- `Client/`

## Migration path

When `Stem.Communication` ships as a NuGet, this entire directory is
deleted in one commit. The replacement is a `<PackageReference>` in
`ButtonPanelTester.Infrastructure.fsproj` plus `open` rewrites in the
two wrapper files (`PcanPort.fs`, `ProtocolService.fs`). No tester
domain code should need to change.

## How to re-vendor (bump the SHA)

1. `cd /code/stem/stem-communication && git pull && git rev-parse HEAD`
2. From the tester repo, run `eng/revendor-stem-communication.sh <SHA>`
   (to be added in spec-002 — the script wipes `Vendored/StemCommunication/`,
   re-copies, re-applies the header comments, updates this file).
3. Re-run the tester's full test suite. Any new failure is either an
   upstream regression (file upstream) or a tester seam that needs
   widening (file under tester).
```

## Risks of the vendor path

1. **Test-coverage gap.** `stem-communication` carries its own test
   suite that we don't vendor; we won't know if a future re-vendor
   regresses something the upstream tests would have caught. Mitigation:
   each `revendor` PR runs the tester's full test suite, including the
   spec-004/005 hardware-side validation on the bench.

2. **Namespace drift.** If we do edit the namespace, it touches every
   vendored file. Try to compile vendored code under its original
   namespace first; only rename if there's a concrete collision.

3. **Public-API drift in `stem-communication`.** Between vendoring SHAs,
   the upstream's public surface may change. The tester's wrappers
   (`PcanPort.fs`, `ProtocolService.fs`) absorb the change; the rest
   of the tester is shielded by `ICommunicationPort` / `IProtocolService`.

4. **The "vendoring is forever" trap.** It is tempting to start
   editing vendored code locally for "just this one fix". The
   pre-commit check exists to make that physically annoying. Don't
   bypass it.
