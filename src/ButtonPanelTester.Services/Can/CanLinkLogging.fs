namespace Stem.ButtonPanelTester.Services.Can

open System
open Microsoft.Extensions.Logging
open Stem.ButtonPanelTester.Core.Can

/// Pure projection helpers that render a `CanLinkState` into the
/// structured fields `CanLinkService` logs on every surfaced
/// transition (#148). No logger, no side effects — each function is a
/// total match over the four-family `CanLinkState` DU so a future case
/// fails to compile rather than silently logging a wrong default.
module CanLinkLogging =

    /// Log level for a surfaced transition. Lifecycle states
    /// (`Initializing` / `Connected` / `Disconnected`) are
    /// `Information`; a `Recoverable` error is `Warning`; a `Fatal`
    /// error is `Error`. Mirrors the LOGGING-standard level table.
    let levelFor (state: CanLinkState) : LogLevel =
        match state with
        | Initializing -> LogLevel.Information
        | Connected _ -> LogLevel.Information
        | Disconnected _ -> LogLevel.Information
        | Error(Recoverable _, _) -> LogLevel.Warning
        | Error(Fatal _, _) -> LogLevel.Error

    /// Stable, filterable name for the `{State}` field. Lifecycle
    /// families render bare; `Disconnected` appends its reason case
    /// name and `Error` its classification name so dashboards can
    /// distinguish causes without reading the free-text detail.
    let stateName (state: CanLinkState) : string =
        match state with
        | Initializing -> "Initializing"
        | Connected _ -> "Connected"
        | Disconnected(reason, _) ->
            let reasonName =
                match reason with
                | NoAdapterPresent -> "NoAdapterPresent"
                | LinkNotYetOpened -> "LinkNotYetOpened"
                | MidSessionUnplug -> "MidSessionUnplug"
                | ReconnectPending -> "ReconnectPending"

            "Disconnected." + reasonName
        | Error(Recoverable _, _) -> "Error.Recoverable"
        | Error(Fatal _, _) -> "Error.Fatal"

    /// Error severity for the `{Severity}` field — `Recoverable` or
    /// `Fatal` for an `Error`, `-` for every non-error state.
    let severityName (state: CanLinkState) : string =
        match state with
        | Error(Recoverable _, _) -> "Recoverable"
        | Error(Fatal _, _) -> "Fatal"
        | Initializing
        | Connected _
        | Disconnected _ -> "-"

    /// Free-text cause for the `{Detail}` field. Carried only by
    /// `Error` (the PEAK status / escalation message); the four-family
    /// `DisconnectReason` cases carry no payload, so every non-error
    /// state renders empty.
    let detailString (state: CanLinkState) : string =
        match state with
        | Error(Recoverable detail, _) -> detail
        | Error(Fatal detail, _) -> detail
        | Initializing
        | Connected _
        | Disconnected _ -> ""

    /// The `since` / `openedAt` timestamp a state carries, if any.
    /// `Initializing` is the only state with no timestamp.
    let sinceOf (state: CanLinkState) : DateTimeOffset option =
        match state with
        | Initializing -> None
        | Connected(_, openedAt) -> Some openedAt
        | Disconnected(_, since) -> Some since
        | Error(_, since) -> Some since
