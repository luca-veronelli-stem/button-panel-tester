# STEM standards (Standard version: v1.6.0)

These are inline copies pinned to `v1.6.0`. Upstream source of truth is [standards/shared/standards/](https://github.com/luca-veronelli-stem/standards/tree/v1.6.0/shared/standards).

| Standard | Purpose |
| --- | --- |
| [REPO_STRUCTURE.md](./REPO_STRUCTURE.md) | Root layout, archetype trees, naming rules. |
| [LANGUAGE.md](./LANGUAGE.md) | F# default; layer-default table; deviation policy. |
| [MODULE_SEPARATION.md](./MODULE_SEPARATION.md) | Onion (A) and hexagonal (B) layering; banned APIs. |
| [PORTABILITY.md](./PORTABILITY.md) | net10.0 default; TFM-conditional drivers; cross-platform replacements. |
| [BUILD_CONFIG.md](./BUILD_CONFIG.md) | Directory.Build.props, Directory.Packages.props, global.json, .editorconfig. |
| [TESTING.md](./TESTING.md) | xUnit + FsCheck + Avalonia.Headless; single F# tests project default. |
| [CI.md](./CI.md) | GitHub Actions: ci.yml, mirror-bitbucket.yml, release.yml; matrix legs. |
| [MIGRATION.md](./MIGRATION.md) | Per-repo adoption phases; major/minor/patch bump procedures. |
| [EVENTARGS.md](./EVENTARGS.md) | Two valid event-payload shapes; banned primitives. |
| [VISIBILITY.md](./VISIBILITY.md) | Archetype-aware default-internal/default-public; seal-by-default. |
| [LOGGING.md](./LOGGING.md) | ILogger<T>; structured-only; Console.WriteLine banned. |
| [THREAD_SAFETY.md](./THREAD_SAFETY.md) | Decision order; .NET 10 Lock; sync-over-async banned. |
| [CANCELLATION.md](./CANCELLATION.md) | CancellationToken propagation; linked-CTS timeout; OCE handling. |
| [COMMENTS.md](./COMMENTS.md) | XML doc coverage by visibility; English by default; <inheritdoc/>. |
| [ERROR_HANDLING.md](./ERROR_HANDLING.md) | Try-pattern / Result type / exception decision tree. |
| [CONFIGURATION.md](./CONFIGURATION.md) | Constants -> Configuration -> Service pattern; library + app delivery. |
| [GUI.md](./GUI.md) | Avalonia + FuncUI + Elmish-MVU; <App>.GUI/ layout; composition root; legacy WinForms/WPF carve-out. |
| [DESIGN_SYSTEM.md](./DESIGN_SYSTEM.md) | Fluent theme + light default (brand-aligned); 4-pt spacing scale; Fluent System Icons; Poppins typography; Stem brand palette; F# strings module for i18n; toast/banner/inline/modal error surfaces. |
| [APP_SHELL.md](./APP_SHELL.md) | Canonical views (Settings, About, LanguagePicker, NotificationCenter, ConnectionStatus); typed ShellSlots record; Navigation pinned to left sidebar. |

## Bumping the standard version

Re-run the rollout from `<standards>/eng/apply-repo-standard.ps1` with `-StandardVersion vX.Y.Z`. The script reads `.stem-standard.json` at the repo root, so only the new tag needs to be passed.
