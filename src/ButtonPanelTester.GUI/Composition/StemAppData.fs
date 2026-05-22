module Stem.ButtonPanelTester.GUI.Composition.StemAppData

open System
open System.IO

[<Literal>]
let private CompanySegment = "Stem"

[<Literal>]
let private AppSegment = "ButtonPanelTester"

let private localRoot () =
    Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData

let private ensureDir path =
    Directory.CreateDirectory path |> ignore
    path

let appRoot ()        = Path.Combine(localRoot (), CompanySegment, AppSegment) |> ensureDir
let logsDir ()        = Path.Combine(appRoot (), "logs")        |> ensureDir
let cacheDir ()       = Path.Combine(appRoot (), "cache")       |> ensureDir
let credentialsDir () = Path.Combine(appRoot (), "credentials") |> ensureDir
let dbDir ()          = Path.Combine(appRoot (), "db")          |> ensureDir
