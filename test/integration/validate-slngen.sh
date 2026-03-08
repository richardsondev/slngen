#!/usr/bin/env bash
#
# validate-slngen.sh — Runs inside Docker to validate SlnGen against the test projects.
#
# 1. Installs SlnGen as a dotnet tool from the local nupkg
# 2. Runs slngen against dirs.proj to generate a .sln
# 3. Verifies the .sln was created and contains expected projects
#
set -euo pipefail

echo "=== .NET SDK info ==="
dotnet --info

TESTPROJECTS_DIR="/testprojects"
PACKAGES_DIR="/packages"

# Generate a NuGet.config that includes the local package source so we don't
# have to check one in to the repo.
cat > "$TESTPROJECTS_DIR/NuGet.config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <!-- Local source containing the freshly-built SlnGen nupkg -->
    <add key="local" value="/packages" />
    <add key="dotnet-public" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json" />
    <add key="dotnet-tools" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/index.json" />
    <add key="dotnet10" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet10/nuget/v3/index.json" />
  </packageSources>
</configuration>
EOF
echo "=== Generated NuGet.config with local source ==="

# Find the slngen nupkg (Tool package — the one that provides the 'slngen' command)
TOOL_NUPKG=$(find "$PACKAGES_DIR" -iname 'Microsoft.VisualStudio.SlnGen.Tool.*.nupkg' | head -1)
if [ -z "$TOOL_NUPKG" ]; then
    echo "ERROR: Could not find Microsoft.VisualStudio.SlnGen.Tool nupkg in $PACKAGES_DIR"
    ls -la "$PACKAGES_DIR"
    exit 1
fi

echo ""
echo "=== Found SlnGen tool package ==="
echo "$TOOL_NUPKG"

# Extract version from the nupkg filename
# e.g. Microsoft.VisualStudio.SlnGen.Tool.12.0.123.nupkg -> 12.0.123
TOOL_VERSION=$(basename "$TOOL_NUPKG" | sed -E 's/Microsoft\.VisualStudio\.SlnGen\.Tool\.(.+)\.nupkg/\1/')
echo "Version: $TOOL_VERSION"

echo ""
echo "=== Installing SlnGen as a global dotnet tool ==="
dotnet tool install --global \
    --add-source "$PACKAGES_DIR" \
    --version "$TOOL_VERSION" \
    Microsoft.VisualStudio.SlnGen.Tool

# Ensure the tool is on PATH
export PATH="$PATH:$HOME/.dotnet/tools"

echo ""
echo "=== Verifying slngen is available ==="
slngen --version

echo ""
echo "=== Running slngen against dirs.proj ==="
cd "$TESTPROJECTS_DIR"

# Remove any pre-existing .sln
rm -f *.sln

# Run slngen: generate the .sln, don't try to launch VS
slngen dirs.proj --launch false --folders true -v:detailed

echo ""
echo "=== Checking generated .sln ==="
SLN_FILE=$(find . -maxdepth 1 -name '*.sln' | head -1)
if [ -z "$SLN_FILE" ]; then
    echo "FAIL: No .sln file was generated!"
    exit 1
fi

echo "Generated: $SLN_FILE"
echo ""
echo "--- .sln contents ---"
cat "$SLN_FILE"
echo "--- end .sln ---"

# Verify expected projects appear in the .sln
EXPECTED_PROJECTS=("Net8App" "Net9App" "Net10App" "Net11App" "Net8Lib" "Net9Lib" "Net10Lib" "Net11Lib")
MISSING=0
for PROJ in "${EXPECTED_PROJECTS[@]}"; do
    if grep -q "$PROJ" "$SLN_FILE"; then
        echo "  OK: $PROJ found in .sln"
    else
        echo "  FAIL: $PROJ NOT found in .sln"
        MISSING=1
    fi
done

echo ""
if [ "$MISSING" -ne 0 ]; then
    echo "FAIL: Some expected projects were not found in the generated .sln"
    exit 1
fi

echo "SUCCESS: SlnGen generated a valid .sln with all expected projects."
