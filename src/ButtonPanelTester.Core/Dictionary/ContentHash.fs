module Stem.ButtonPanelTester.Core.Dictionary.ContentHash

open System
open System.Security.Cryptography

/// SHA-256 of the input bytes, rendered as a 64-character lowercase
/// hex string. The canonicalised on-wire JSON is hashed at receipt
/// (HttpDictionaryProvider) and the result is what
/// JsonFileDictionaryCache writes to the .sha256 sidecar.
let compute (bytes: byte[]) : string =
    SHA256.HashData(bytes) |> Convert.ToHexStringLower
