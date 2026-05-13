# Standard: BUILD_CONFIG

> **Stability:** v1.0.0
> **Goal:** every repo builds with `dotnet build` from a clean clone, no IDE involvement, no per-developer state.

## Files at the repo root

| File | Role |
| --- | --- |
| `Stem.<App>.slnx` | Modern XML solution file. Lists all projects under `src/` and `tests/`. |
| `Directory.Build.props` | Per-repo MSBuild defaults (TFM, nullability, warnings, language version). Applies to **every project in the repo**. |
| `Directory.Packages.props` | Central Package Management. Pins versions for **every NuGet dependency** used in the repo. |
| `global.json` | Pins the .NET SDK version. |
| `.editorconfig` | Style for C#/F#/JSON/YAML/MD. Read by `dotnet format` and IDEs. |

## Directory.Build.props — what's inside

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <RootNamespace>$(MSBuildProjectName)</RootNamespace>
    <Company>STEM E.m.s.</Company>
    <Authors>STEM E.m.s.</Authors>
    <Copyright>© $([System.DateTime]::Now.ToString('yyyy')) STEM E.m.s.</Copyright>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

The `BannedApiAnalyzers` reference is at solution level so every project picks it up. Whether the analyzer **bans anything** depends on whether the project has a `BannedSymbols.txt` next to its `.fsproj`/`.csproj` (see MODULE_SEPARATION).

### Per-project overrides

A project can override any of the above in its own `.fsproj` / `.csproj`. Common cases:

- **Driver projects with multi-TFM:** override `<TargetFramework>` with `<TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>`.
- **Test projects:** add `<IsPackable>false</IsPackable>` and `<GenerateDocumentationFile>false</GenerateDocumentationFile>`.

## Directory.Packages.props — what's inside

Central Package Management lets every `<PackageReference>` in the solution omit `Version=`. Versions are listed once, here:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <!-- Source generators / analyzers -->
    <PackageVersion Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="x.y.z" />

    <!-- Cross-platform replacements -->
    <PackageVersion Include="ClosedXML" Version="x.y.z" />
    <PackageVersion Include="SkiaSharp" Version="x.y.z" />

    <!-- Avalonia + FuncUI (archetype A) -->
    <PackageVersion Include="Avalonia" Version="x.y.z" />
    <PackageVersion Include="Avalonia.Desktop" Version="x.y.z" />
    <PackageVersion Include="Avalonia.Themes.Fluent" Version="x.y.z" />
    <PackageVersion Include="Avalonia.FuncUI" Version="x.y.z" />

    <!-- Testing -->
    <PackageVersion Include="xunit" Version="x.y.z" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="x.y.z" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="x.y.z" />
    <PackageVersion Include="FsCheck.Xunit" Version="x.y.z" />
    <PackageVersion Include="Avalonia.Headless.XUnit" Version="x.y.z" />

    <!-- Driver-only (referenced under TFM conditions) -->
    <PackageVersion Include="System.IO.Ports" Version="x.y.z" />
    <PackageVersion Include="InTheHand.Net.Bluetooth" Version="x.y.z" />
  </ItemGroup>
</Project>
```

The list above is illustrative — the actual baseline lives at `shared/templates/Directory.Packages.props` and is the source of truth for new repos.

### Bumping a package

Edit `Directory.Packages.props` only. No project file ever carries a `Version=`. Renovate / Dependabot writes here.

## global.json — what's inside

```json
{
  "sdk": {
    "version": "x.y.z",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

Pin to the highest installed minor; allow patch roll-forward so CI doesn't break on a fresh runner image. Bump the major/minor only when adopting a new SDK feature. The actual pinned version lives in `shared/templates/global.json` and is the source of truth for new repos.

## .editorconfig — what's inside

The full file lives at `shared/templates/.editorconfig`. Highlights:

- 4-space indent for C#, F#, MSBuild, JSON.
- 2-space indent for YAML, Markdown.
- LF line endings everywhere except `*.{ps1,psd1,psm1,bat,cmd}` (CRLF — required by Windows).
- `csharp_style_*` and `fsharp_*` analyzer rules to align with `Directory.Build.props`'s `EnforceCodeStyleInBuild`.

## Build invariants (CI enforces these)

```powershell
dotnet format whitespace --verify-no-changes --no-restore
dotnet build             --configuration Release
dotnet test              --configuration Release --no-build
```

A green build means: whitespace is clean, analyzer/style rules pass via `TreatWarningsAsErrors`, all tests pass on both `ubuntu-latest` and `windows-latest`. CI runs the whitespace-only variant of `dotnet format` — see CI.md → "Format check is a hard gate" for why.

## Husky.NET pre-commit hook

`eng/install-hooks.ps1` (and `.sh`) installs a Husky.NET pre-commit hook that runs:

```bash
dotnet format --verify-no-changes
```

This catches formatting drift before push. The hook is opt-in (run `eng/install-hooks.ps1` once per clone). CI runs the same check independently as a backstop.
