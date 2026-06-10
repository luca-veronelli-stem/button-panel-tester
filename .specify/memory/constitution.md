<!--
Sync Impact Report
==================
Version change: 1.0.1 → 1.0.2
Bump rationale: PATCH — clarification preserving semantics. Drops the
now-obsolete deviation paragraph from Architecture Constraints §"Lean
workspace". Standards v1.5.1 (upstream luca-veronelli-stem/standards#79)
clarified REPO_STRUCTURE.md so `lean/` is the Lean 4 workspace and
`specs/` is the spec-kit feature root — what this repo did at bootstrap
is no longer a deviation. The principle (Lean spec ahead of F#
implementation, theorems compile with no `sorry`) is unchanged; only
the prose describing the workspace location is.

Discovered during /speckit follow-up after PR #77 bumped this repo
from standards v1.5.0 to v1.5.3 (which supersets v1.5.1's #79 fix).
Closes issue #6's last remaining acceptance item ("Constitution PATCH:
deviation paragraph removed, Sync Impact Report appended").

Updated wording:
  - Architecture Constraints §"Lean workspace": removed the four-line
    "This deviates from STEM REPO_STRUCTURE.md..." paragraph; replaced
    with a one-line note that `lean/` is the standards-blessed Lean
    workspace location as of standards v1.5.1.

Templates requiring updates:
  ✅ .specify/templates/plan-template.md         — no mention of the
                                                    deviation.
  ✅ .specify/templates/spec-template.md         — no mention.
  ✅ .specify/templates/tasks-template.md        — no mention.

Propagation review (other artifacts):
  - specs/001-fetch-dictionary/plan.md           — no mention of the
                                                   deviation.
  - specs/001-fetch-dictionary/tasks.md          — no mention.

Sync Impact Report
==================
Version change: 1.0.0 → 1.0.1
Bump rationale: PATCH — clarification preserving semantics. The Lean
workspace lives at lean/ as before; this amendment recognises Lake's
TOML manifest format (lakefile.toml) as the configuration carrier
instead of the F#-syntax lakefile.lean. The principle (Lean spec ahead
of F# implementation, theorems compile with no `sorry`) is unchanged.

Discovered during /speckit.analyze of feat/001-fetch-dictionary: the
spec-kit plan and tasks artifacts both pointed at `lakefile.toml`,
which conflicted with the constitution's `lakefile.lean` wording. The
team prefers the TOML variant (current Lake-recommended manifest for
new projects); the constitution is brought in line.

Updated wording:
  - Principle I (Formal Verification of Invariants): `lean/lakefile.lean`
    → `lean/lakefile.toml`.
  - Architecture Constraints §"Lean workspace": `lean/lakefile.lean`
    → `lean/lakefile.toml`.

Templates requiring updates:
  ✅ .specify/templates/plan-template.md         — no mention of the
                                                    lakefile filename.
  ✅ .specify/templates/spec-template.md         — no mention.
  ✅ .specify/templates/tasks-template.md        — no mention.

Propagation review (other artifacts):
  - specs/001-fetch-dictionary/plan.md           — already says
                                                   `lakefile.toml`; now aligned.
  - specs/001-fetch-dictionary/tasks.md (T007)   — already says
                                                   `lakefile.toml`; now aligned.

Sync Impact Report
==================
Version change: (template) → 1.0.0
Bump rationale: First ratified governance for the button-panel-tester rebuild.
Establishes six core principles drawn from the four-round scope interview
(2026-05-13), paolino/haskell-mts's RFC-2119-style constitution, and the
"never again" lessons from the legacy stem-button-panel-tester repo.

Added principles:
  - I.   Formal Verification of Invariants (NON-NEGOTIABLE)
  - II.  Property-Driven Correctness
  - III. Ports and Adapters for Every External Boundary
  - IV.  CI Greens the Whole Stack; Hardware Tests Are Explicit
  - V.   Supplier-Deployed Identity Is Hashed at Capture (NON-NEGOTIABLE)
  - VI.  Stopgap Discipline

Added sections: Architecture Constraints, Development Workflow, Governance.

Templates requiring updates:
  ✅ .specify/templates/plan-template.md         — Constitution Check section
                                                    populated with the six gates.
  ⚠ .specify/templates/spec-template.md         — review on first
                                                    speckit-specify; expect no change.
  ⚠ .specify/templates/tasks-template.md        — review on first
                                                    speckit-tasks; expect no change.

Follow-up TODOs:
  - Upstream clarification PR to luca-veronelli-stem/standards: REPO_STRUCTURE.md
    currently asserts "specs/ is the Lean workspace", which collides with
    spec-kit's own use of specs/NNN-feature-name/. This repo resolves the
    collision by putting Lean at lean/ instead. Track as a sibling of the
    existing first-adopter gap issues (#74, #75).
  - Once stem-dictionaries-manager's HTTP API contract is verified during the
    first speckit-plan, revisit Principle III with the concrete port shape —
    that audit may surface a clarification.
-->

# Stem.ButtonPanelTester Constitution

## Core Principles

### I. Formal Verification of Invariants (NON-NEGOTIABLE)

Every domain type and every state-changing action MUST be formalized in
Lean 4 before a corresponding F# implementation lands. Lean modules live
under `lean/Stem/ButtonPanelTester/Phase<N>/`, with `lean/lakefile.toml`
and `lean/lean-toolchain` at the workspace root. The order is fixed:
**Lean spec → xUnit test → F# implementation.** Theorems MUST compile
with no `sorry`. Plans that change protocol framing, the test-session
state machine, or dictionary mapping MUST identify which Lean modules
they extend and which preservation theorems they prove.

**Rationale:** The legacy repo bolted protocol invariants on after the
fact; they silently drifted from firmware behaviour, and the resulting
correctness gap surfaced only as field bugs. Formalization makes the
divergence visible at design time, before any code runs.

### II. Property-Driven Correctness

FsCheck properties are the **primary** correctness mechanism for pure
F# code under `<App>.Core` and `<App>.Services`. Properties MUST be
implementation-agnostic where feasible (the same property holds for the
virtual CAN adapter and the Peak adapter). Example-based `[<Fact>]`
tests are acceptable only when no reasonable property can be expressed,
or for documenting concrete protocol fixtures. Plans MUST list the
FsCheck properties intended to cover each new behaviour; example-only
coverage requires a one-line rationale in the plan.

**Rationale:** Examples cover the cases the author thinks of; properties
cover the ones they don't. For protocol code where input shape is
combinatorially rich, examples leave large unexercised regions.

### III. Ports and Adapters for Every External Boundary

Every external boundary — the CAN bus, the `stem-dictionaries-manager`
HTTP API, the OS (filesystem, registry, identity APIs), wall-clock time —
MUST be expressed as an F# port (interface or function type) defined in
`<App>.Core`. Concrete adapters live in `<App>.Infrastructure`. **A
virtual / loopback adapter MUST exist for every port** so the integration
test layer runs on CI without hardware or network. New external
boundaries that lack a port fail the Constitution Check in
`/speckit-plan`.

**Rationale:** The legacy `Services` was hardware-coupled, which is why
its tests were largely `Category=FlakyOnCi`. The port + virtual-adapter
pattern is what makes a bench tool's logic CI-testable; bolting it on
after the fact never works.

### IV. CI Greens the Whole Stack; Hardware Tests Are Explicit

Every test layer the codebase ships MUST run on every PR:
unit (xUnit + FsCheck), integration (against the virtual CAN adapter
and an HTTP-fake of the dictionary API), and Avalonia.Headless GUI smoke.
Hardware-bound E2E tests are tagged `[<Trait("Category", "Hardware")>]`,
excluded from default CI, and gated as a manual pre-release check. **No
silent `FlakyOnCi`, no untagged skip, no `[Fact(Skip = ...)]` without a
linked tracking issue.**

**Rationale:** The legacy lost regression coverage on critical paths
because hardware-coupled tests were quietly excluded. Making every
exclusion explicit, named, and linkable to an issue keeps the coverage
gap visible at every PR review.

### V. Supplier-Deployed Identity Is Hashed at Capture (NON-NEGOTIABLE)

The tool ships to external suppliers; identity-bearing fields cross an
organisational boundary on capture. Any OS user identifier, machine
name, machine SID, MAC address, or other identity-bearing field that
flows into STEM-controlled storage (Azure SQL, telemetry, support
bundles, crash dumps) MUST be hashed at the `<App>.Infrastructure`
boundary using a documented, salt-free deterministic hash. Raw identity
MAY render locally on the supplier's machine for UX clarity; it MUST
NOT leave that machine.

**Rationale:** Raw supplier-side identity in STEM systems is a
compliance liability — GDPR and STEM's internal supplier-data policies
both apply. Hashing at the boundary removes the foot-gun by
construction; downstream consumers (tickets, Azure SQL, support
queries) get a stable correlation key without the legal exposure.

### VI. Stopgap Discipline

A stopgap is any code path that knowingly violates one of the
principles above or a STEM standard, retained because the correct path
is not yet available. Every stopgap MUST have:

1. A GitHub tracking issue describing the correct end state.
2. A waiver document at `docs/STOPGAP_<short-name>.md` naming the
   violated principle, the rationale, and the removal path.
3. The violated principle explicitly listed under "Complexity Tracking"
   in the plan that introduces the stopgap.
4. **One issue per bypass.** A single waiver document MUST NOT cover
   multiple independent bypasses; each gets its own issue + waiver
   entry. Removal lands in independent PRs.

**Rationale:** The legacy repo accumulated a triple-bypass auth path
(DPAPI bypass + wire-protocol substitution + endpoint substitution)
hidden under a single waiver. Each bypass individually was defensible;
the aggregate masked a systemic gap. One-issue-per-bypass forces the
review surface to scale with the actual debt.

## Architecture Constraints

- **Locked stack.** .NET 10 (`net10.0` everywhere; `net10.0-windows`
  only for Windows-confined drivers per STEM PORTABILITY), F# default
  per STEM LANGUAGE, Avalonia 11.3.x + Avalonia.FuncUI 1.5.x +
  Elmish-MVU per STEM GUI, xUnit 2.9.x + FsCheck.Xunit 3.3.x +
  Avalonia.Headless.XUnit for tests, Peak.PCANBasic.NET as the physical
  CAN adapter, Lean 4 (toolchain pinned in `lean/lean-toolchain`). No
  mocking libraries (no Moq, no NSubstitute) — manual fakes only.
- **Project layout (archetype A).** `src/ButtonPanelTester.Core/`,
  `src/ButtonPanelTester.Services/`,
  `src/ButtonPanelTester.Infrastructure/`,
  `src/ButtonPanelTester.GUI/`,
  `tests/ButtonPanelTester.Tests/`. Solution file
  `Stem.ButtonPanelTester.slnx` at the repo root. Dependencies follow
  the onion direction: `Core ← Services ← Infrastructure ← GUI`. No
  skip-layer references; no upward references. `GUI` is the composition
  root and the only project that wires concrete adapters.
- **Lean workspace.** `lean/lakefile.toml` + `lean/lean-toolchain` at
  the Lean workspace root; namespace folders under
  `lean/Stem/ButtonPanelTester/Phase<N>/`. This is the standards-blessed
  location for Lean (REPO_STRUCTURE.md acknowledges `lean/` for the
  Lean 4 workspace as of standards v1.5.1; `specs/` is the spec-kit
  feature root).
- **Diagrams.** All diagrams in `spec.md`, `plan.md`, `data-model.md`,
  `research.md` MUST use Mermaid fenced code blocks (` ```mermaid `).
  No ASCII art. Diagrams render natively on GitHub and Bitbucket;
  ASCII drifts on every edit and obscures the intent.
- **Dictionary source.** The button-panel dictionary is fetched via the
  `stem-dictionaries-manager` HTTP API. Direct Azure SQL access from
  this repo is forbidden (compliance + change-coupling). The concrete
  API contract lives in the first feature's `specs/NNN-*/contracts/`.

## Development Workflow

- **Worktree-per-branch.** The main repo stays on `main`. All work
  happens in a sibling worktree managed via the `worktrees` skill. The
  `worktree-guard.ps1` hook enforces this locally; the `main` ruleset
  enforces remotely.
- **Issue-first.** Every feature, fix, or chore opens a GitHub issue
  before the branch is cut. Plans cite the issue number. Issues live on
  the Planning project board.
- **Branches.** `feat/NNN-<short-description>` (matches the spec-kit
  folder name `specs/NNN-feature-name/`),
  `fix/<short>`, `refactor/<short>`, `docs/<short>`, `chore/<short>`,
  `test/<short>`, `ci/<short>`. All branches are cut from
  `github/main`.
- **Commits.** Conventional Commits (`feat:`, `fix:`, `chore:`, …),
  lowercase after the colon, imperative mood, English body. Every
  commit MUST compile and pass tests on its own (the `bisect-safe`
  rule); vertical commits, never horizontal layer stacks (the
  `vertical-commits` rule). Review fixes go into the commit that
  introduced the issue, not as fixups at the tip.
- **Pre-push CI parity.** `dotnet build -c Release` and
  `dotnet test -c Release` MUST pass locally before push. CI catches
  cross-platform issues; the local pre-flight catches everything else
  before it costs CI minutes.
- **Pull Requests.** Open on **GitHub** via `gh pr create`. PR title in
  Conventional Commits form; at least one of the
  `feat / fix / chore / docs / refactor / test / ci` labels is
  mandatory. PR body explains what + why + alternatives considered.
  All four required status checks MUST be green before merge.
- **Merge strategy.** Rebase merge on `main` (linear history, enforced
  by ruleset). Squash only to consolidate a deliberately noisy commit
  stream that does not reflect the intended history.
- **Dual-remote.** `github` is the active remote; `bitbucket` is a
  mirror, kept in sync by `.github/workflows/mirror-bitbucket.yml` on
  every push to `main`. Direct pushes to `bitbucket` are blocked by
  `git remote set-url --push bitbucket no_push`.
- **Speckit phases.** `/speckit-constitution` (this file) →
  `/speckit-specify` → `[/speckit-clarify]` → `/speckit-plan` →
  `[/speckit-checklist]` → `/speckit-tasks` → `[/speckit-analyze]` →
  `/speckit-implement`. Each phase produces an artifact under
  `specs/<feature-name>/`. Optional phases are recommended for any
  feature that touches protocol framing, the state machine, or
  identity-bearing data.

## Governance

- This constitution governs work inside `button-panel-tester`. It
  defers to (a) the STEM standards in `docs/Standards/`, (b) the global
  rules in `~/.claude/rules/` and `llm-settings/`, and (c) the upstream
  `standards` repository for cross-repo conventions. When the
  constitution and a standard genuinely disagree, the constitution
  wins inside this repo and the disagreement is escalated to the
  `standards` repo as a clarification PR.
- **Amendments** require a PR that updates this file with: (a) the new
  content, (b) a Sync Impact Report appended to the existing history at
  the top of the file (do not overwrite prior reports), (c) the
  `Last Amended` date bumped, (d) a propagation review for
  `.specify/templates/*`, and (e) the constitutional reasoning in the
  PR description.
- **Versioning** follows semver: MAJOR for principle removal or
  backward-incompatible redefinition, MINOR for new principle/section
  or material expansion, PATCH for clarifications and wording fixes
  that preserve semantics.
- **Compliance.** Every `/speckit-plan` invocation MUST include a
  Constitution Check gate that explicitly addresses Principles I–VI.
  Plans with unresolved gate violations MUST list them under
  "Complexity Tracking" with rationale; unjustified violations block
  `/speckit-tasks`.
- **Runtime guidance.** `CLAUDE.md` carries project-specific notes
  (vendor quirks, deviations, active migrations). `docs/Standards/`
  carries the cross-repo standards. This constitution carries only the
  principles that govern *how this project plans and ships work*.

**Version**: 1.0.2 | **Ratified**: 2026-05-13 | **Last Amended**: 2026-05-14
