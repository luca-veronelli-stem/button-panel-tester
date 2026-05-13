#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

echo "Installing Husky.NET pre-commit hook..."

if [ ! -f ".config/dotnet-tools.json" ]; then
  dotnet new tool-manifest >/dev/null
fi

if ! dotnet tool list --local | grep -qi "^Husky\b"; then
  dotnet tool install Husky >/dev/null
fi

dotnet tool restore >/dev/null

if [ ! -f ".husky/pre-commit" ]; then
  dotnet husky install
  mkdir -p .husky
  cat > .husky/pre-commit <<'EOF'
#!/usr/bin/env sh
. "$(dirname -- "$0")/_/husky.sh"

dotnet format --verify-no-changes
EOF
  chmod +x .husky/pre-commit
fi

echo "Done. Pre-commit will run 'dotnet format --verify-no-changes'."
