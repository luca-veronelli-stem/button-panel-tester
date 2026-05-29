#Requires -Version 7
$ErrorActionPreference = 'Stop'
git diff --check
dotnet build -c Release
dotnet test tests/ButtonPanelTester.Tests/ButtonPanelTester.Tests.fsproj --framework net10.0 --filter "Category!=Hardware"
dotnet test tests/ButtonPanelTester.Tests.Windows/ButtonPanelTester.Tests.Windows.fsproj --framework net10.0-windows --filter "Category!=Hardware"
