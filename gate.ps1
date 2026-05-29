#Requires -Version 7
$ErrorActionPreference = 'Stop'

# Catch whitespace errors in the diff.
git diff --check

# Build the solution and run the net10.0 suite (Core + Services).
# Hardware-bound CAN E2E tests (Category=Hardware) are excluded from the gate.
dotnet build -c Release
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj --framework net10.0 --filter "Category!=Hardware"
