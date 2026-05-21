/// Italian-first / English-second i18n surface per
/// `docs/Standards/DESIGN_SYSTEM.md` L267-309. Every visible string
/// in any future MVU view passes through a function in this module —
/// the `Lang` DU guarantees that adding a new language fails the
/// build at every call site until each function handles the new
/// case, and adding a new string forces an `It` and `En` value at
/// the declaration site.
///
/// This commit lands the scaffold (DU + runtime default + one
/// starter function) so the contract is concrete. Migrating the
/// pre-existing hard-coded English labels in `Dictionary/*` and
/// `App.fs` is out of scope for the Tier 3 PR; a follow-up issue
/// will route them through here once the MVU `Model.Lang` field
/// exists.
module Stem.ButtonPanelTester.GUI.Strings

/// Two-case DU; expand here when a third language earns its keep.
/// Every string function pattern-matches exhaustively so the
/// compiler refuses to build the GUI until each function handles
/// the new case.
type Lang =
    | It
    | En

/// Startup default per L307-309. The first-run experience will
/// eventually offer a language picker; until then the runtime
/// surfaces Italian everywhere `Strings.<f> Strings.initial` is
/// called.
let initial : Lang = It

/// Starter function — pins the pattern future strings follow.
/// Stays in the module rather than at a call site so the
/// scaffold-only nature of this commit reads as a fixture, not
/// dead code.
let appTitle (lang: Lang) : string =
    match lang with
    | It -> "Tester Pannello Pulsanti"
    | En -> "Button Panel Tester"
