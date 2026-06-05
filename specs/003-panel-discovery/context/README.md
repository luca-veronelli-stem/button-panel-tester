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
