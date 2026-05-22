module Stem.ButtonPanelTester.Tests.Windows.Unit.StemAppDataTests

open System
open System.IO
open Xunit
open Stem.ButtonPanelTester.GUI.Composition

[<Fact>]
let ``appRoot ends with Stem then ButtonPanelTester segments`` () =
    let root = StemAppData.appRoot ()
    let expectedTail = Path.Combine("Stem", "ButtonPanelTester")
    Assert.EndsWith(expectedTail, root)

[<Fact>]
let ``appRoot is rooted under LocalApplicationData`` () =
    let root = StemAppData.appRoot ()
    let localAppData = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
    Assert.StartsWith(localAppData, root)

[<Fact>]
let ``logsDir cacheDir credentialsDir dbDir are distinct sub-folders of appRoot`` () =
    let root = StemAppData.appRoot ()
    let logs = StemAppData.logsDir ()
    let cache = StemAppData.cacheDir ()
    let creds = StemAppData.credentialsDir ()
    let db = StemAppData.dbDir ()
    Assert.Equal(Path.Combine(root, "logs"), logs)
    Assert.Equal(Path.Combine(root, "cache"), cache)
    Assert.Equal(Path.Combine(root, "credentials"), creds)
    Assert.Equal(Path.Combine(root, "db"), db)

[<Fact>]
let ``each sub-folder exists on disk after the helper returns`` () =
    Assert.True(Directory.Exists(StemAppData.logsDir ()))
    Assert.True(Directory.Exists(StemAppData.cacheDir ()))
    Assert.True(Directory.Exists(StemAppData.credentialsDir ()))
    Assert.True(Directory.Exists(StemAppData.dbDir ()))
