#!/usr/bin/env bash
# Local quality gate for the resolve-ticket-supervised protocol.
# Worker runs this before every handoff. Fail-fast.

set -euo pipefail

echo '== gate: dotnet build -c Release =='
dotnet build -c Release

echo '== gate: dotnet test -c Release --no-build =='
dotnet test -c Release --no-build

echo '== gate: dotnet format --verify-no-changes =='
dotnet format --verify-no-changes

echo '== gate: PASS =='
