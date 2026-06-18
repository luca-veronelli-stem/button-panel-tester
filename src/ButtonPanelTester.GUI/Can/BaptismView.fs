namespace Stem.ButtonPanelTester.GUI.Can

open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Types
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Services.Can

/// FuncUI view for the spec-004 BAPTIZE + RESET surfaces (Phase E, T034–T037,
/// FR-002 / FR-005 / FR-006 / FR-007 / FR-008 / FR-009 / FR-010). Pure render:
/// the host (`App.fs`) passes the already-computed `Baptism.baptizeEnablement`
/// and `Baptism.resetEnablement` verdicts, the latest `IBaptismService`
/// state, the picked variant, the in-flight / last attempt's `(variant, uuid)`,
/// the latest FR-007 warning uuid, and the latest `ResetOutcome`, then
/// re-renders on every observable emission. The GUI renders enablement and
/// outcomes; it decides nothing — every guard verdict and outcome is consumed
/// from Core / the service, never re-derived here.
///
/// The reset surface (E3) consumes the `IBaptismService.ResetAsync` confirmation
/// SEAM via `runReset`: the caller supplies the already-decided confirmation
/// (the host's FR-009 modal dialog or a scripted test bool). There is no
/// confirmation logic in this view — only the seam.
[<RequireQualifiedAccess>]
module BaptismView =

    /// The exactly-four marketed variants offered in the picker, in
    /// `MarketingVariant` declaration order. The virgin marker is NEVER a
    /// picker option (data-model §1): baptism assigns one of the four
    /// production identities to a virgin panel; "Virgin" is the source state,
    /// not a target.
    let pickerVariants: MarketingVariant list = [ EdenXp; OptimusXp; R3LXp; EdenBs8 ]

    /// Marketing label for a picker variant, reusing the Panels-on-bus list's
    /// `variantLabel` so the picker and the row read identically (e.g.
    /// "Eden XP", "R-3L XP").
    let variantLabel (v: MarketingVariant) : string = PanelsOnBusView.variantLabel (Marketing v)

    /// `true` while a baptism attempt is in flight — `ClaimSent`,
    /// `AwaitingAnnounce`, or `Assigning`. The surface is MODAL while an
    /// attempt runs (CHK013): the picker and the Baptize button are disabled,
    /// so a second concurrent attempt can never be started from the GUI.
    let isRunning (state: BaptismState) : bool =
        match state with
        | ClaimSent
        | AwaitingAnnounce _
        | Assigning
        | AwaitingAdoption _ -> true
        | Idle
        | Terminal _ -> false

    /// Whether the Baptize button is active: the enablement guard says
    /// `Enabled`, a variant is picked, AND no attempt is running (modality).
    let baptizeEnabled (enablement: Enablement) (selectedVariant: MarketingVariant option) (state: BaptismState) : bool =
        enablement = Enabled && Option.isSome selectedVariant && not (isRunning state)

    /// The reason the Baptize button is disabled, for rendering under the
    /// button (acceptance 1.6). A `Disabled` enablement carries its own
    /// explanation (the unmet FR-002 conjunct); otherwise, an `Enabled` guard
    /// with no variant picked yields the pick-a-variant hint. `None` when the
    /// button is (modulo modality) actionable.
    let disabledHint (enablement: Enablement) (selectedVariant: MarketingVariant option) : string option =
        match enablement with
        | Disabled e -> Some e
        | Enabled ->
            if Option.isNone selectedVariant then
                Some "Pick one of the four variants to baptize the selected panel."
            else
                None

    /// One human-readable line describing a terminal baptism outcome (FR-005 /
    /// FR-006). For every failure it names (a) the step/phase that failed, (b)
    /// the panel's likely state, and (c) the recommended next action. `Succeeded`
    /// explains the by-design silence + list age-out (FR-006). `WaitTimeout`
    /// reuses `BaptismGuidance.recoveryText` (the clarification-4 guidance), so
    /// the wait-step / likely-incomplete-state / re-run next-action all ride that
    /// shared text.
    let describeOutcome (variant: MarketingVariant) (uuid: PanelUuid) (outcome: BaptismOutcome) : string =
        let label = variantLabel variant
        let uuidHex = PanelsOnBusView.uuidText uuid

        match outcome with
        | Succeeded ->
            sprintf
                "Baptized %s as %s. The panel now goes silent by design — its row will age out of the Panels-on-bus list."
                uuidHex
                label
        | WaitTimeout -> BaptismGuidance.recoveryText WaitTimeout |> Option.defaultValue ""
        | UnexpectedVariant announced ->
            sprintf
                "The panel re-announced as %s, not the chosen %s: the claim did not apply. Re-check the variant choice and re-run Baptize, or Reset first."
                (PanelsOnBusView.variantLabel announced)
                label
        // RW06 refines the guided-recovery rendering (full FR-015 text + Headless test).
        | ClaimNotAdopted ->
            sprintf
                "%s did not adopt the %s identity: it kept announcing after the assignment (or never confirmed). Reset it to virgin, then re-run Baptize."
                uuidHex
                label
        | PanelDisappeared ->
            "The selected panel left the bus before it could be claimed: it stopped announcing (likely unplugged). Reconnect the panel and retry once it re-announces."
        | LinkLost ->
            "The CAN link dropped during the attempt, so the claim may be incomplete. Reconnect the adapter and retry."
        | TransmissionFailure ClaimStep ->
            "The WHO_ARE_YOU claim write failed: the panel was not claimed — still virgin. Check the link and retry."
        | TransmissionFailure AssignStep ->
            "The SET_ADDRESS assignment write failed: the panel heard the claim but the address was not assigned. Re-run Baptize."

    /// The FR-007 "claim did not take" warning text, naming the claimed uuid.
    /// Fired when a just-claimed panel re-announces while still unclaimed within
    /// the post-success window — the claim may not have taken.
    let warningText (uuid: PanelUuid) : string =
        sprintf
            "The just-claimed panel %s re-announced while still unclaimed, so the claim may not have taken. Re-run Baptize or Reset."
            (PanelsOnBusView.uuidText uuid)

    /// Whether the Reset button is active. Reset needs NO list selection
    /// (FR-008): it is a broadcast, not anchored to a selected row. Active iff
    /// the `Baptism.resetEnablement` guard says `Enabled` AND no baptize attempt
    /// is running — the two surfaces gate each other so a reset can never race a
    /// baptize claim/assign in flight (and vice versa via `isRunning`).
    let resetEnabled (resetEnablement: Enablement) (state: BaptismState) : bool =
        resetEnablement = Enabled && not (isRunning state)

    /// The reason the Reset button is disabled, for rendering under the button.
    /// A `Disabled` reset guard carries its own explanation (the unmet FR-008
    /// conjunct — link down or two-or-more announcing); `Enabled` yields `None`.
    let resetDisabledHint (resetEnablement: Enablement) : string option =
        match resetEnablement with
        | Disabled e -> Some e
        | Enabled -> None

    /// The FR-009 confirmation prompt the host's modal dialog shows before a
    /// reset broadcasts. It names the two facts a technician must understand to
    /// give informed consent: reset ERASES a panel's machine identity (back to
    /// virgin), and it is a BROADCAST that reaches EVERY matching panel on the
    /// bus — including SILENT (already-baptized) panels the list cannot show.
    let resetConfirmationMessage : string =
        "Reset to virgin erases the panel's machine identity, returning it to the unbaptized state. "
        + "This is a broadcast: it reaches every matching panel on the bus, including silent (already-baptized) "
        + "panels the list cannot show — not just the ones you can see. Continue only if you mean to reset all of them."

    /// One human-readable line per `ResetOutcome` (FR-010). Wildcard-free —
    /// adding a `ResetOutcome` case makes this a compile error.
    ///   * `Sent` — the honest acceptance-2.5 message: the reset commands were
    ///     written to the bus; a matching panel, if present, re-announces as
    ///     virgin within ~6 s; otherwise the list simply stays empty (the
    ///     firmware never replies, so write completion is the only signal).
    ///   * `Declined` — cancelled at confirmation; nothing was sent.
    ///   * `ResetLinkLost` — the link was not connected (or dropped
    ///     mid-broadcast); reconnect the adapter and retry.
    ///   * `ResetTransmissionFailure` — a reset broadcast write failed; check
    ///     the link and retry.
    let describeResetOutcome (outcome: ResetOutcome) : string =
        match outcome with
        | Sent ->
            "Reset written to the bus. A matching panel, if present, re-announces as virgin within ~6 s and "
            + "its row reappears; if no panel matched, the list simply stays empty (the firmware sends no reply)."
        | Declined -> "Reset was cancelled at confirmation; nothing was sent."
        | ResetLinkLost ->
            "The CAN link was not connected, or dropped mid-broadcast, so the reset did not complete. Reconnect the adapter and retry."
        | ResetTransmissionFailure ->
            "A reset broadcast write failed. Check the link and retry."

    /// The confirmation SEAM for reset (FR-009 / FR-010). `confirm` shows the
    /// host's FR-009 modal dialog (production) or is scripted to a bool (tests);
    /// its decision feeds `IBaptismService.ResetAsync`. This single orchestration
    /// is what the host's `onReset` and the T037 wired-service tests both drive —
    /// the GUI decides nothing, it only relays the technician's confirmation.
    let runReset (confirm: unit -> Task<bool>) (service: IBaptismService) (ct: CancellationToken) : Task<ResetOutcome> =
        task {
            let! ok = confirm ()
            return! service.ResetAsync(ok, ct)
        }

    /// Pure render of the combined baptize + reset surface. The host supplies
    /// the already-computed `baptizeEnablement` / `resetEnablement` verdicts, the
    /// latest FSM state, the picked variant, the in-flight / last attempt's
    /// `(variant, uuid)` (for the terminal rendering), the latest FR-007 warning
    /// uuid, the latest `ResetOutcome`, and the three callbacks. No confirmation
    /// dialog inline: clicking Baptize invokes `onBaptize` directly (FR-009);
    /// clicking Reset invokes `onReset`, which drives the host's FR-009 modal
    /// confirmation seam (`runReset`).
    let view
        (baptizeEnablement: Enablement)
        (resetEnablement: Enablement)
        (state: BaptismState)
        (selectedVariant: MarketingVariant option)
        (attempt: (MarketingVariant * PanelUuid) option)
        (warning: PanelUuid option)
        (resetOutcome: ResetOutcome option)
        (onVariantSelected: MarketingVariant -> unit)
        (onBaptize: MarketingVariant -> unit)
        (onReset: unit -> unit)
        : IView =
        let running = isRunning state

        // One picker button per marketed variant. Modal: disabled while an
        // attempt runs. The picked option carries a LightBlue highlight, added
        // by the conditional-attr concat idiom (never a `match -> ()` in the
        // attr list).
        let variantOption (v: MarketingVariant) : IView =
            let attrs: IAttr<Button> list =
                [ Button.name "VariantOption"
                  Button.content (variantLabel v)
                  Button.isEnabled (not running)
                  Button.onClick (fun _ -> onVariantSelected v) ]
                @ (if selectedVariant = Some v then
                       [ Button.background Brushes.LightBlue ]
                   else
                       [])

            Button.create attrs :> IView

        let picker: IView =
            StackPanel.create [
                StackPanel.name "VariantPicker"
                StackPanel.orientation Orientation.Horizontal
                StackPanel.spacing 4.0
                StackPanel.children [ for v in pickerVariants -> variantOption v ]
            ]
            :> IView

        let baptizeButton: IView =
            Button.create [
                Button.name "BaptizeButton"
                Button.content "Baptize"
                Button.isEnabled (baptizeEnabled baptizeEnablement selectedVariant state)
                Button.onClick (fun _ ->
                    match selectedVariant with
                    | Some v -> onBaptize v
                    | None -> ())
            ]
            :> IView

        // Conditional children, assembled by `@` of optional singletons — never
        // a `match -> ()` inside the children list.
        let disabledReason: IView list =
            match (if running then None else disabledHint baptizeEnablement selectedVariant) with
            | Some hint ->
                [ TextBlock.create [
                      TextBlock.name "BaptizeDisabledReason"
                      TextBlock.text hint
                      TextBlock.textWrapping TextWrapping.Wrap
                  ]
                  :> IView ]
            | None -> []

        let progress: IView list =
            if running then
                [ TextBlock.create [
                      TextBlock.name "BaptismProgress"
                      TextBlock.text "Baptizing the selected panel…"
                  ]
                  :> IView ]
            else
                []

        let outcome: IView list =
            match state, attempt with
            | Terminal o, Some(v, u) ->
                [ TextBlock.create [
                      TextBlock.name "BaptismOutcome"
                      TextBlock.text (describeOutcome v u o)
                      TextBlock.textWrapping TextWrapping.Wrap
                  ]
                  :> IView ]
            | _ -> []

        let warningView: IView list =
            match warning with
            | Some u ->
                [ TextBlock.create [
                      TextBlock.name "ClaimWarning"
                      TextBlock.text (warningText u)
                      TextBlock.textWrapping TextWrapping.Wrap
                  ]
                  :> IView ]
            | None -> []

        // Reset-to-virgin surface (E3, FR-008 / FR-009 / FR-010). A separate
        // section: the Reset button gated by `resetEnabled` (no list selection,
        // disabled while a baptize attempt runs), its disabled reason, and the
        // last `ResetOutcome` line. Assembled by `@` of optional singletons.
        let resetButton: IView =
            Button.create [
                Button.name "ResetButton"
                Button.content "Reset to virgin"
                Button.isEnabled (resetEnabled resetEnablement state)
                Button.onClick (fun _ -> onReset ())
            ]
            :> IView

        let resetDisabledReason: IView list =
            match (if running then None else resetDisabledHint resetEnablement) with
            | Some hint ->
                [ TextBlock.create [
                      TextBlock.name "ResetDisabledReason"
                      TextBlock.text hint
                      TextBlock.textWrapping TextWrapping.Wrap
                  ]
                  :> IView ]
            | None -> []

        let resetOutcomeView: IView list =
            match resetOutcome with
            | Some o ->
                [ TextBlock.create [
                      TextBlock.name "ResetOutcome"
                      TextBlock.text (describeResetOutcome o)
                      TextBlock.textWrapping TextWrapping.Wrap
                  ]
                  :> IView ]
            | None -> []

        let children: IView list =
            [ picker; baptizeButton ]
            @ disabledReason
            @ progress
            @ outcome
            @ warningView
            @ [ resetButton ]
            @ resetDisabledReason
            @ resetOutcomeView

        StackPanel.create [
            StackPanel.name "BaptismSurface"
            StackPanel.orientation Orientation.Vertical
            StackPanel.spacing 8.0
            StackPanel.children children
        ]
        :> IView
