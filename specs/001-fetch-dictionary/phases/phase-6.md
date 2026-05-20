# Phase 6 — Polish & cross-cutting concerns

This document scopes the sixth (and final) phase-PR for spec 001
(dictionary fetch and status display). It is the durable anchor for the
Phase 6 work driven by the `resolve-ticket` (unsupervised) protocol.

## Scope

Ship the release-hardening bundle: a seed-refresh tool, the user-
facing release notes, public XML documentation that matches the data-
model surface, a logging audit per the LOGGING standard, an FR-020
PII grep audit recorded in the quickstart, and the final Constitution
Principle I gate re-run. No new user-visible behaviour; no new Lean
theorems; no production code changes beyond doc comments and the
operator script.

Concretely, Phase 6 lands:

- An `eng/refresh-seed.ps1` operator script (PowerShell 7) that
  reads the API key from `$env:STEM_DICT_KEY`, calls `GET
  {BaseUrl}/api/dictionaries/{Dictionary:Id}/resolved`, normalises
  the response, stamps a top-level `"seededAt": "<ISO 8601 UTC>"`
  marker, and writes `src/ButtonPanelTester.GUI/Assets/
  dictionary.seed.json`. No secret-management — the operator
  supplies the key per invocation, as documented in
  `research.md` "Open follow-ups".
- A `[Unreleased]` CHANGELOG entry that captures the feat/001
  user-visible surface in one line.
- A README update that links the user to
  `specs/001-fetch-dictionary/quickstart.md` and names the
  dictionary status row + registration flow as the new
  user-visible surface.
- XML doc comments on every public type and member listed in
  `data-model.md` §1 and §2 — `Variable`, `PanelType`,
  `ButtonPanelDictionary`, `CacheOrigin`, `DictionarySource`,
  `FetchFailureReason`, `DictionaryFetchResult`, `BootstrapToken`,
  `InstallationCredential`, `RegistrationError`, the five port
  interfaces (`IClock`, `IDictionaryProvider`, `IDictionaryCache`,
  `ICredentialStore`, `IRegistrationClient`),
  `DictionaryStateUpdate`, and `IDictionaryService` — per the
  COMMENTS standard.
- A logging audit per the LOGGING standard recording, for every
  adapter and `DictionaryService`: `ILogger<T>` via DI, no
  `Console.WriteLine`, no string-interpolation in templates
  (parameterised templates only), and no `BootstrapToken.Value`
  or `InstallationCredential.Value` at any verbosity level
  (`contracts/credential-format.md` "Logging" section).
- An FR-020 compliance check: grep the HTTP layer for any field
  that could carry machine name, OS user, machine identifier,
  MAC, or SID. Expected zero hits — the wire surface is
  `Dictionary:Id` (URL) + `X-Api-Key` (header) only. The result
  is recorded as a one-line `# Compliance` note appended to the
  "Troubleshooting" tail of `quickstart.md`.

T064 (run `eng/refresh-seed.ps1` against the production service to
generate a committed seed) and T065 (fresh-machine quickstart §1–§9
walkthrough validating SC-001/-002/-003/-004/-006/-007; SC-005 is a
usability metric deferred to supplier-side observation) are operator
steps and live outside the worker's scope: the worker leaves them
unticked in `tasks.md`, the operator runs them post-merge and ticks
them separately.

## Tasks (T058..T066)

Mapped one-to-one to the task list in [`../tasks.md`](../tasks.md) lines
168..176 and to GitHub issues #65..#73:

- T058 — issue [#65](https://github.com/luca-veronelli-stem/button-panel-tester/issues/65):
  add `eng/refresh-seed.ps1` — PowerShell 7 script that reads
  `$env:STEM_DICT_KEY`, calls `GET {BaseUrl}/api/dictionaries/
  {id}/resolved` against the production URL, normalises the
  response, stamps top-level `"seededAt": "<ISO 8601 UTC>"`,
  and writes `src/ButtonPanelTester.GUI/Assets/
  dictionary.seed.json`. Header comment documents prereqs
  (`research.md` "Open follow-ups"). No secret-management — the
  operator supplies the key per invocation.
- T059 — issue [#66](https://github.com/luca-veronelli-stem/button-panel-tester/issues/66):
  add the feat/001 entry to `CHANGELOG.md` under `[Unreleased]` —
  one line summarising "Dictionary fetch with status row,
  registration ceremony, and manual refresh".
- T060 — issue [#67](https://github.com/luca-veronelli-stem/button-panel-tester/issues/67):
  update `README.md` — link to
  `specs/001-fetch-dictionary/quickstart.md`; one-paragraph
  mention of the dictionary status row and registration flow as
  the user-visible surface.
- T061 — issue [#68](https://github.com/luca-veronelli-stem/button-panel-tester/issues/68):
  add XML doc comments to every public type and member listed in
  `data-model.md` §1 and §2 per the COMMENTS standard.
- T062 — issue [#69](https://github.com/luca-veronelli-stem/button-panel-tester/issues/69):
  logging audit per the LOGGING standard — confirm every adapter
  and the `DictionaryService` use `ILogger<T>` via DI, no
  `Console.WriteLine`, no string-interpolation in log messages
  (parameterised templates only), no `BootstrapToken.Value` or
  `InstallationCredential.Value` appears in any log statement at
  any verbosity level (`contracts/credential-format.md`
  "Logging" section).
- T063 — issue [#70](https://github.com/luca-veronelli-stem/button-panel-tester/issues/70):
  compliance check for FR-020: grep the HTTP layer for any
  field that could carry machine name, OS user, machine
  identifier, MAC, or SID. Expected zero hits — the wire
  surface is `Dictionary:Id` (URL) + `X-Api-Key` (header) only.
  Document the audit result in a one-line `# Compliance` note
  inside `quickstart.md` "Troubleshooting" tail.
- T064 — issue [#71](https://github.com/luca-veronelli-stem/button-panel-tester/issues/71):
  operator runs `eng/refresh-seed.ps1` once against a known-
  good dictionary service to generate a real
  `dictionary.seed.json` for the release; commits the
  refreshed seed. Left unticked by the worker; operator ticks
  separately.
- T065 — issue [#72](https://github.com/luca-veronelli-stem/button-panel-tester/issues/72):
  operator end-to-end validation: walks `quickstart.md` §1–§9
  on a freshly-provisioned machine; verifies SC-001 (status
  row within 1 s on launch), SC-002 (cold-start usable without
  network), SC-003 (registration end-to-end < 30 s), SC-004
  (failed refresh surfaced < 12 s), SC-006 (no re-prompts
  during a 4-hour session), SC-007 (failed refresh has zero
  effect on the in-memory dictionary). SC-005 is a usability
  metric, deferred to supplier-side observation per
  `tasks.md` line 175. Left unticked by the worker; operator
  ticks separately.
- T066 — issue [#73](https://github.com/luca-veronelli-stem/button-panel-tester/issues/73):
  `cd lean && lake build` — confirm all four Phase 1 theorems
  still compile with no `sorry` and no custom axioms after
  every preceding task (Constitution Principle I gate).

## Exit state

- `dotnet build -c Release` green across all six projects, 0 warnings.
- `dotnet test -c Release --no-build` green across Unit / Property /
  Integration / GUI partitions; no new test files, no regressions.
- `dotnet format --verify-no-changes` green.
- `cd lean && lake build` green — the four Phase 1 theorems
  still elaborate with no `sorry` and no custom axioms
  (Constitution Principle I gate, no new Lean work in Phase 6).
- `eng/refresh-seed.ps1` exists and is executable; running it
  with `$env:STEM_DICT_KEY` set against a reachable dictionary
  service produces a normalised
  `src/ButtonPanelTester.GUI/Assets/dictionary.seed.json` with
  a top-level `"seededAt"` ISO 8601 UTC stamp (verified
  operator-side at T064).
- Every public type and member named in `data-model.md` §1+§2
  carries an XML doc comment (T061); the logging audit
  (T062) shows zero `Console.WriteLine`, zero string-interp
  templates, and zero secret values across `Services` +
  `Infrastructure`; the FR-020 grep (T063) shows zero
  PII-shaped fields on the HTTP layer and the result is
  recorded in `quickstart.md` "Troubleshooting".

## Logging audit notes (T062)

The four LOGGING red flags fail cleanly: 24 log call sites
across `Services` and `Infrastructure`, all parameterised
templates, no `Console.WriteLine` / `Debug.WriteLine` /
`Trace.WriteLine` anywhere under `src/`, and no
`BootstrapToken.Value` or `InstallationCredential.Value`
reaching any `Log*` argument (the `.Value` accessors are
only consumed at the three legitimate sinks — registration
request body, `X-Api-Key` header injection, DPAPI plaintext
encryption).

Two long-standing gaps to the LOGGING archetype-A "every
adapter takes `ILogger<TThis>` via DI" rule pre-date Phase 6
and are deliberately not fixed in this release-hardening PR
(per the standard's adoption clause: "doesn't have to
retro-add logging everywhere"):

- `Services.Dictionary.DictionaryService` (orchestrator) —
  Phase 3/5 review let it through without an `ILogger`. A
  good candidate for a follow-up ticket: log
  `InitializeAsync` outcome (live/cache/seed/no-dict),
  `RefreshAsync` outcome including the coalescing-leader
  flag, and any unexpected exception promoted to
  `tcs.TrySetException`.
- `Infrastructure.Persistence.JsonFileDictionaryCache`
  (IO adapter) — same Phase 3 origin. Useful follow-up log
  sites: sidecar hash mismatch, IOException during atomic
  write, skip-write decisions.

Pure types (`SystemClock`, `EmbeddedSeedExtractor`) are
correctly logger-less per the standard's "pure functions /
DTOs / records — no logger field" carve-out.
- `tasks.md` ticks T058–T063 and T066; T064 and T065 remain
  unticked for the operator to claim after merge.

## Protocol customization

The `resolve-ticket` lifecycle for Phase 6 starts at `WorkRequired`.
The spec, plan, tasks, research, data model, contracts, and quickstart
artifacts for spec 001 already exist on `main`, ratified against
constitution v1.0.2. Re-running speckit-specify / speckit-plan /
speckit-tasks per phase-PR would rewrite shared feature artifacts six
times and is forbidden.

The reviewer's per-commit consistency check is: the durable diff
matches the named T-task in `../tasks.md` and respects constitution
Principles I–VI.

Batched amend policy (inherited from Phase 1–5): approved
titles/bodies are **not** mechanically amended on each `WorkApproved
→ WorkRequired` transition. Worker appends each approved
title+body+durable-sha to `llm/reviews/approved-commits.yml`. Owner
applies all approved messages in one rewrite at
`FinalizationRequired`. This keeps the durable-commit graph stable
across the run and contains the message-rewrite blast radius to a
single owner-side operation.

Quality gate `llm/reviews/gate.{ps1,sh}` is reused unchanged from
Phase 5. T066 is the explicit phase-end `lake build` re-run; locally,
the gate also drives `lake build` on every handoff, so the Principle I
check fires throughout the run.

Commit cadence: one durable commit per T-task that produces a
durable change (T058–T063 plus the final `docs(001)` tick commit
that ticks T058–T063 and T066). T064 and T065 produce no commit on
this PR — they're operator follow-ups. T066 is a gate re-run rather
than a discrete diff; its result is verified on the tick commit's
green gate, not as a separate commit. Single feature branch
`feat/001-phase-6`. One PR encompassing all commits, rebase-merged
on completion.
