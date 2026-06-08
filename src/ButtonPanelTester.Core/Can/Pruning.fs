namespace Stem.ButtonPanelTester.Core.Can

open System

module Pruning =

    /// Remove every row whose `LastSeen` is older than `ttl` from the
    /// reference instant `now`, per `specs/003-panel-discovery/
    /// data-model.md` §4 / FR-005. The kept-iff-`≤ttl`
    /// boundary is the contract pinned by clarify (spec-002 ttl =
    /// `TimeSpan.FromSeconds 15.0`) and mechanised by the Lean theorem
    /// `prune_partitions_by_threshold` in `Phase2/Pruning.lean` (T031).
    ///
    /// Implementation is `Map.filter` over `now - observation.LastSeen
    /// ≤ ttl`. `now < observation.LastSeen` would yield a negative
    /// `TimeSpan` which trivially satisfies `≤ ttl` (so future-dated
    /// observations are preserved) — this is consistent with the F#
    /// `FrozenClock` scripted-time tests in
    /// `Tests/Fakes/Wiring.fs` and the operational reality that the
    /// adapter's `ReceivedAt` timestamp must monotonically lead the
    /// service's `IClock.UtcNow()`.
    let prune (ttl: TimeSpan) (now: DateTimeOffset) (m: PanelsOnBus) : PanelsOnBus =
        m |> Map.filter (fun _ observation -> now - observation.LastSeen <= ttl)
