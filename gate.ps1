#Requires -Version 7
$ErrorActionPreference = 'Stop'

# Catch whitespace errors in the diff.
git diff --check

# Build the solution and run both suites (Category!=Hardware excluded).
dotnet build -c Release
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj --framework net10.0 --filter "Category!=Hardware"
dotnet test tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj --framework net10.0-windows --filter "Category!=Hardware"
