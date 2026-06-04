#Requires -Version 7
$ErrorActionPreference = 'Stop'

# Universal: catch whitespace errors in the diff
git diff --check

# Build the whole solution (Release, as CI does)
dotnet build -c Release Stem.ButtonPanelTester.slnx

# Both target frameworks must stay green (acceptance #5):
#   - ButtonPanelTester.Tests          -> net10.0          (Services + Core + in-memory fakes)
#   - ButtonPanelTester.Tests.Windows  -> net10.0-windows  (Infrastructure DPAPI + headless GUI)
dotnet test -c Release tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj
dotnet test -c Release tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj

# Whitespace-format gate, matching the CI standard's invocation
dotnet format whitespace --verify-no-changes --no-restore Stem.ButtonPanelTester.slnx
