#Requires -Version 7
$ErrorActionPreference = 'Stop'

# Universal: catch whitespace errors in the diff
git diff --check

# STEM .NET default - uncomment + adjust per repo:
# dotnet build -c Release
# dotnet test Tests/Tests.csproj --framework net10.0
# dotnet format --verify-no-changes

# Lean formalization (for repos with specs/ Lean projects):
# lake build

# Ticket-specific proof (extend per ticket):
# dotnet test Tests/Tests.csproj --filter FullyQualifiedName~<focused-pattern>