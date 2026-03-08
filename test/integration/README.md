# SDK Integration Tests

End-to-end integration tests that validate SlnGen works correctly against
specific .NET SDK versions. Each test builds the SlnGen NuGet package, installs
it as a `dotnet tool` on the target SDK, and runs `slngen` against a traversal
project (`dirs.proj`) containing `.csproj` files for multiple .NET target
frameworks.

These tests are designed to catch compatibility regressions like those reported
in [#456](https://github.com/microsoft/slngen/issues/456).

## Tested SDKs

Each SDK has a corresponding `Dockerfile.sdk<N>` in this directory.
Exact versions are **pinned by `tag@sha256:digest`** in each Dockerfile —
check the `FROM` lines for current values. **Dependabot** automatically
proposes updates when new SDK images are published.

## Prerequisites

- Docker Desktop (Windows/macOS) or Docker Engine (Linux)
- The repo checked out locally

## Quick Start

### PowerShell (Windows / cross-platform)

```powershell
# Run all stable SDK tests
.\test\integration\run-docker-tests.ps1

# Test a single SDK
.\test\integration\run-docker-tests.ps1 -SdkVersions 9

# Include preview SDKs (may fail — informational only)
.\test\integration\run-docker-tests.ps1 -SdkVersions 8,9,10,11
```

### Bash (Linux / macOS)

```bash
# Run all stable SDK tests
./test/integration/run-docker-tests.sh

# Test a single SDK
./test/integration/run-docker-tests.sh 9

# Include preview SDKs
./test/integration/run-docker-tests.sh 8 9 10 11
```

### Manual Docker Build

You can also run a single Dockerfile directly:

```bash
docker build \
  -f test/integration/Dockerfile.sdk<N> \
  -t slngen-test-sdk<N> \
  .
```

A successful `docker build` (exit code 0) means the integration test passed.

## How It Works

Each Dockerfile is a **multi-stage build**:

### Stage 1 — Builder

Uses a known-good stable SDK to `dotnet restore` and `dotnet build` the SlnGen
solution, producing `.nupkg` files (via `GeneratePackageOnBuild`).

### Stage 2 — Test

Uses the **target SDK under test**:

1. Copies the `.nupkg` files from the builder stage
2. Copies the integration test fixtures from `testprojects/`
3. Runs `validate-slngen.sh` which:
   - Installs `Microsoft.VisualStudio.SlnGen.Tool` as a global dotnet tool
     from the local `.nupkg`
   - Runs `slngen dirs.proj --launch false --folders true` against a
     traversal project containing `NetXApp` / `NetXLib` sub-projects for
     each supported TFM
   - Verifies the generated `.sln` exists and contains all expected projects

A non-zero exit code from `docker build` means SlnGen failed on that SDK.

### Test Projects (`testprojects/`)

Each supported TFM has a `NetXApp` (Exe) and `NetXLib` (Lib) project.
These are wired together via `dirs.proj` (a
[`Microsoft.Build.Traversal`](https://github.com/microsoft/MSBuildSdks) project).

## Adding a New SDK Version

1. Copy an existing Dockerfile and update the test-stage `FROM` line with the
   new image tag and SHA digest (use
   `docker manifest inspect mcr.microsoft.com/dotnet/sdk:<tag>` to get the
   digest)
2. Add the new version to the lookup tables in `run-docker-tests.ps1` and
   `run-docker-tests.sh`
3. If testing a new TFM, add a corresponding `NetXApp/NetXLib` project pair
   under `testprojects/`, add them to `dirs.proj`, and add the project names
   to `EXPECTED_PROJECTS` in `validate-slngen.sh`
