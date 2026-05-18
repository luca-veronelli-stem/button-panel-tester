namespace Stem.ButtonPanelTester.Services.Dictionary

/// Configuration shape for the dictionary endpoint, bound to the
/// `"Dictionary"` section of `appsettings.json` via
/// `IOptions<DictionaryOptions>` at the GUI composition root. The
/// fields trace back to the placeholder + dev values currently checked
/// in:
///
///   - `appsettings.json`            — production placeholder URL +
///                                     `Id = 2` (replaced before any
///                                     real shipping; see
///                                     `quickstart.md` §3).
///   - `appsettings.Development.json` — `BaseUrl = https://localhost:7065`
///                                     for the local
///                                     `stem-dictionaries-manager`.
///
/// Lives in Services (`net10.0`) rather than Infrastructure because
/// both the registration adapter (T041, US2) and the dictionary fetch
/// adapter (T049, US3) consume the same options shape. Keeping it in
/// Services puts a single source of truth above the Infrastructure
/// boundary so both Windows-only adapters reference the same record.
///
/// `[<CLIMutable>]` is required for `Microsoft.Extensions.Configuration`
/// binder support — the binder constructs the instance through a
/// parameterless constructor and assigns the properties.
[<CLIMutable>]
type DictionaryOptions =
    { /// Base URL of the dictionary service. The registration client
      /// appends `/register` (unauthenticated bootstrap endpoint per
      /// `registration-api.md`); the fetch client appends
      /// `/api/dictionaries/{Id}/resolved` (T049, US3).
      BaseUrl: string

      /// Numeric identifier of the button-panel dictionary this
      /// installation operates against (FR-021 — exactly one
      /// per installation, set at install time, not selectable at
      /// runtime). Consumed by T049's URL path.
      Id: int }
