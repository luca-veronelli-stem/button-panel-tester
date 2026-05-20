namespace Stem.ButtonPanelTester.Core.Dictionary

open System

/// One variable in a panel type. Mirrors stem-dictionaries-manager's
/// ResolvedVariableDto exactly so the wire payload deserialises directly.
type Variable = {
    /// Stable identifier of the variable as defined in the source
    /// dictionary (e.g. `BTN_RIGHT_TOP`). Not localised.
    Name        : string
    /// High byte of the composite CAN object address; the full address
    /// is `(AddressHigh <<< 8) ||| AddressLow`.
    AddressHigh : byte
    /// Low byte of the composite CAN object address; see `AddressHigh`.
    AddressLow  : byte
    /// Server-defined scalar type tag (e.g. `uint8`, `float32`). String
    /// rather than enum so unknown types from a future server schema
    /// deserialise as-is without rejecting the whole payload.
    DataType    : string
    /// Server-defined access mode (e.g. `read`, `write`, `readwrite`).
    /// Same string-not-enum rationale as `DataType`.
    Access      : string
    /// Optional human-readable description, shown in tooltips. `None`
    /// when the server omitted the field per its null-suppression policy.
    Description : string option
    /// Optional inclusive minimum value for validation/display. `None`
    /// when not provided by the server.
    Min         : float option
    /// Optional inclusive maximum value for validation/display. `None`
    /// when not provided by the server.
    Max         : float option
    /// Optional engineering unit string (e.g. `V`, `A`). `None` when
    /// not applicable or not provided.
    Unit        : string option
    /// `true` when the variable belongs to the STEM standard set, `false`
    /// when it is panel-type specific.
    IsStandard  : bool
}

/// One panel type (e.g. "Pulsantiera 12 tasti rev A"). Mirrors the API's
/// shape; the dictionary itself is one PanelType in this slice's payload,
/// but the type is a list to leave room for a future API change.
type PanelType = {
    /// Numeric panel-type identifier as defined server-side. Distinct
    /// from `Dictionary:Id` in app settings.
    Id          : int
    /// Display name of the panel type, as authored in the source
    /// dictionary. Not localised.
    Name        : string
    /// Optional human-readable description of the panel type. `None`
    /// when the server omitted it.
    Description : string option
    /// Variables belonging to this panel type, sorted by composite
    /// address ascending (server contract — disabled variables are not
    /// included).
    Variables   : Variable list
}

/// The loaded dictionary. ContentHash is computed at receipt by hashing
/// the canonicalised JSON; it is what the cache sidecar carries and what
/// CacheConsistency reasons over.
type ButtonPanelDictionary = {
    /// 64-character lowercase hexadecimal SHA-256 over the canonicalised
    /// JSON of the dictionary at receipt; the value the cache sidecar
    /// carries and the predicate `cache_memory_equal_post_first_success`
    /// reasons over. Never derived from any wire field — always computed
    /// client-side.
    ContentHash : string
    /// Panel types contained in the dictionary. Always one element in
    /// this slice (the API returns a single panel type and the client
    /// wraps it); the list shape is deliberately preserved for a future
    /// multi-panel API.
    PanelTypes  : PanelType list
}
