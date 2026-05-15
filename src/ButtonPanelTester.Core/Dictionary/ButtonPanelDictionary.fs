namespace Stem.ButtonPanelTester.Core.Dictionary

open System

/// One variable in a panel type. Mirrors stem-dictionaries-manager's
/// ResolvedVariableDto exactly so the wire payload deserialises directly.
type Variable = {
    Name        : string
    AddressHigh : byte
    AddressLow  : byte
    DataType    : string
    Access      : string
    Description : string option
    Min         : float option
    Max         : float option
    Unit        : string option
    IsStandard  : bool
}

/// One panel type (e.g. "Pulsantiera 12 tasti rev A"). Mirrors the API's
/// shape; the dictionary itself is one PanelType in this slice's payload,
/// but the type is a list to leave room for a future API change.
type PanelType = {
    Id          : int
    Name        : string
    Description : string option
    Variables   : Variable list
}

/// The loaded dictionary. ContentHash is computed at receipt by hashing
/// the canonicalised JSON; it is what the cache sidecar carries and what
/// CacheConsistency reasons over.
type ButtonPanelDictionary = {
    ContentHash : string             // 64-char lowercase hex SHA-256
    PanelTypes  : PanelType list
}
