namespace Stem.ButtonPanelTester.Tests.Fakes.Can

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Stem.ButtonPanelTester.Core.Can
open Stem.ButtonPanelTester.Core.Dictionary

/// One recorded master-sequence send, mirroring the two members of
/// `IMasterSequenceTransmitter` per
/// `specs/004-baptism-workflow/contracts/master-sequence-transmitter-port.md` §Port.
/// Test-side recording helper only — not a wire or domain type, so no Lean triple
/// applies; the normative shapes live in the port contract above.
type MasterSequenceSend =
    | WhoAreYouSent of machineType: byte * fwType: uint16 * reset: bool
    | SetAddressSent of uuid: PanelUuid * spAddress: uint32

/// Test adapter for `IMasterSequenceTransmitter` per
/// `specs/004-baptism-workflow/contracts/master-sequence-transmitter-port.md`
/// §Adapter pair: records every successful send in order (timestamped via the
/// injected `IClock`) so the Phase C/D suites can assert payload fields and
/// ordering (`NoSetAddressWithoutMatch`, FR-014 discipline tests), and offers
/// scriptable per-call fault injection for every `TransmissionFailure` path.
///
/// Behaviour:
///   - `Sent` lists successful sends oldest-first; faulted or cancelled calls
///     record nothing.
///   - `ScriptFault(n, error)` faults the n-th send call (1-based, counted over
///     all sends). Successful and faulted calls advance the call index;
///     cancelled calls do NOT — cancellation is caller-side and must never
///     consume a scripted fault.
///   - A pre-cancelled `ct` surfaces as `OperationCanceledException` (never a
///     transmission failure), checked before recording or faulting.
///   - Thread-safe: a private lock guards the mutable state; the fake is fully
///     synchronous inside, so the lock is never held across an await.
type InMemoryMasterSequenceTransmitter(clock: IClock) =
    let gate = obj ()
    let faults = Dictionary<int, exn>()
    let mutable sentNewestFirst: (MasterSequenceSend * DateTimeOffset) list = []
    let mutable sendCount = 0

    /// Synchronous send core. The `task { }` builder turns the
    /// `OperationCanceledException` raised by `ThrowIfCancellationRequested`
    /// into a cancelled task carrying the original exception, and any scripted
    /// fault into a faulted task.
    let send (command: MasterSequenceSend) (ct: CancellationToken) : Task =
        task {
            lock gate (fun () ->
                ct.ThrowIfCancellationRequested()
                sendCount <- sendCount + 1

                match faults.TryGetValue sendCount with
                | true, error -> raise error
                | false, _ -> sentNewestFirst <- (command, clock.UtcNow()) :: sentNewestFirst)
        }

    /// Every successful send in call order (oldest first), stamped with
    /// `clock.UtcNow()` at the moment it was recorded.
    member _.Sent: (MasterSequenceSend * DateTimeOffset) list =
        lock gate (fun () -> List.rev sentNewestFirst)

    /// Scripts the `callIndex`-th send call (1-based, over all sends in order)
    /// to return a task faulted with `error`; nothing is recorded for that
    /// call. Feeds the `TransmissionFailure` paths: fault claim = call 1,
    /// fault assign = call 2, reset's first/second broadcast = calls 1/2.
    member _.ScriptFault(callIndex: int, error: exn) : unit =
        lock gate (fun () -> faults[callIndex] <- error)

    interface IMasterSequenceTransmitter with
        member _.SendWhoAreYouAsync(machineType, fwType, reset, ct) =
            send (WhoAreYouSent(machineType, fwType, reset)) ct

        member _.SendSetAddressAsync(uuid, spAddress, ct) =
            send (SetAddressSent(uuid, spAddress)) ct
