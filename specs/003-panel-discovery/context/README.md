# context/ — source material for the independent spec-003

This folder holds reference material for authoring spec-003 (panel discovery
via passive `WHO_I_AM` observation). It is **not** part of the live spec — the
live artifacts sit at the `specs/003-panel-discovery/` root.

## `previous-003/`

The earlier spec-003 draft, frozen as it stood on `main` before the
independence rewrite (archived 2026-06-05). That draft was extracted from the
former combined `002-can-link-and-panel-discovery` spec via #151, so it
cross-referenced spec-002's documents throughout. After #197 split the
panel-discovery code seam out of `CanLinkService` into its own
`IPanelDiscoveryService` / `PanelDiscoveryService`, the draft had also gone
**stale against the code** — e.g. `plan.md` still claimed a documentation-only
seam that no longer matched the tree.

It is preserved verbatim as **source material** for the fresh, self-contained
spec now authored at the `003-panel-discovery` root. The new spec is baselined
on the shipped code contracts (`ICanLinkService.LinkStateChanged`,
`IPanelDiscoveryService`, the `Core/Can` domain types) rather than on
sibling-spec documents — so spec-003 stays correct when spec-001 / spec-002 are
eventually superseded.

### Empirical content carried forward faithfully

These are firmware fact, not design choices, and were restated in the new spec
rather than re-derived:

- the `WHO_I_AM` wire format — `previous-003/contracts/who-i-am-wire-format.md`,
  which cites panel firmware `AutoAddressSlave.c:165-183`;
- the variant-identity byte table — `VariantIdentity` in
  `previous-003/data-model.md`.

### Rules

- Do **not** edit anything under `previous-003/`. It is a frozen snapshot kept
  for traceability and to source the rewrite.
- Tracker: #153.

## Numbering map (previous-003 → independent spec)

The independent spec renumbers requirements and success criteria from 1 and
promotes the previously-implicit no-transmit invariant to an explicit
requirement. For traceability with #153's body and any PR that cites the old
numbers:

| previous-003 | independent spec | Requirement |
|---|---|---|
| FR-007 | FR-001 | listen for `WHO_I_AM` while Connected, present each panel |
| FR-008 | FR-002 | identify by UUID, coalesce — no duplicates |
| FR-009 | FR-003 | decode the variant-identity byte |
| FR-010 | FR-004 | show + update the last-seen timestamp in place |
| FR-011 | FR-005 | prune after 15 s |
| FR-012 | FR-006 | empty-state explanation |
| FR-013 | FR-007 | silently discard a malformed frame |
| FR-015' | FR-008 | clear the list when the link leaves Connected |
| (spec-002 FR-014 / SC-007, implicit) | FR-009 / SC-003 | no CAN transmit — now explicit |
| SC-003 | SC-001 | discovery within 6 s |
| SC-004 | SC-002 | in-place coalesce, no duplicate row |
