#!/usr/bin/env bash
#
# run-docker-tests.sh — Run SlnGen Docker-based SDK compatibility tests on Linux/macOS.
#
# SDK versions are pinned by tag@sha256 digest directly in each Dockerfile
# so that Dependabot can automatically propose updates.
#
# Usage:
#   ./run-docker-tests.sh              # Test SDKs 8, 9, 10
#   ./run-docker-tests.sh 9            # Test only SDK 9
#   ./run-docker-tests.sh 8 9 10 11    # Include .NET 11 preview
#

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Default SDK versions to test (11 excluded by default — it's preview)
if [ $# -eq 0 ]; then
    VERSIONS=(8 9 10)
else
    VERSIONS=("$@")
fi

declare -A DOCKERFILES
DOCKERFILES[8]="Dockerfile.sdk8"
DOCKERFILES[9]="Dockerfile.sdk9"
DOCKERFILES[10]="Dockerfile.sdk10"
DOCKERFILES[11]="Dockerfile.sdk11-preview"

FAILED=0
RESULTS=""

for VER in "${VERSIONS[@]}"; do
    DOCKERFILE="${DOCKERFILES[$VER]}"
    IMAGE_NAME="slngen-test-sdk${VER}"

    echo ""
    echo "============================================================"
    echo " Testing .NET SDK ${VER} (Dockerfile: ${DOCKERFILE})"
    echo "============================================================"
    echo ""

    if docker build \
        -f "${SCRIPT_DIR}/${DOCKERFILE}" \
        -t "${IMAGE_NAME}" \
        "${REPO_ROOT}"; then
        STATUS="PASSED"
    else
        STATUS="FAILED"
        FAILED=1
    fi

    echo ""
    echo " .NET SDK ${VER}: ${STATUS}"
    echo ""
    RESULTS="${RESULTS}\n  SDK ${VER}: ${STATUS}"
done

echo ""
echo "============================================================"
echo " Summary"
echo "============================================================"
echo -e "${RESULTS}"
echo ""

if [ "$FAILED" -ne 0 ]; then
    echo "One or more SDK tests FAILED."
    exit 1
else
    echo "All SDK tests PASSED."
    exit 0
fi
