#!/usr/bin/env bash
set -euo pipefail

# Generates Kurrent.Client.{version}.json into docs/.vuepress/data/pkgs/
# by building Kurrent.Client and reflecting over its output assembly.

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "$SCRIPT_DIR/../../.." && pwd )"
DOCS_DIR="$REPO_ROOT/docs"
OUTPUT_DIR="$DOCS_DIR/.vuepress/data/pkgs"

CLIENT_PROJECT="$REPO_ROOT/src/Kurrent.Client/Kurrent.Client.csproj"
TOOL_PROJECT="$SCRIPT_DIR/PackageJsonGenerator.csproj"

CONFIG="${CONFIG:-Release}"
TFM="${TFM:-net10.0}"

mkdir -p "$OUTPUT_DIR"

echo "==> Building Kurrent.Client..."
# Generate XML docs (used by the analyzer for /// comments). Suppress the missing-doc
# warnings that would otherwise spam the output (CS1591 etc).
dotnet build "$CLIENT_PROJECT" -c "$CONFIG" -f "$TFM" --nologo -v quiet \
    -p:GenerateDocumentationFile=true \
    -p:NoWarn=CS1591%3BCS1573%3BCS1574%3BCS1580%3BCS1572

CLIENT_DLL="$REPO_ROOT/src/Kurrent.Client/bin/$CONFIG/$TFM/Kurrent.Client.dll"
if [[ ! -f "$CLIENT_DLL" ]]; then
    # Fall back to artifacts dir if used
    CLIENT_DLL=$(find "$REPO_ROOT/artifacts" -path "*Kurrent.Client*/$TFM/Kurrent.Client.dll" -type f 2>/dev/null | head -n 1 || true)
fi
if [[ -z "${CLIENT_DLL:-}" || ! -f "$CLIENT_DLL" ]]; then
    echo "ERROR: Kurrent.Client.dll not found after build." >&2
    exit 1
fi

CLIENT_DIR="$( dirname "$CLIENT_DLL" )"

# Resolve runtime ref-pack (.NET 10) for compilation references
DOTNET_ROOT="$( dirname "$(command -v dotnet)" )"
REF_PACK_BASE="$DOTNET_ROOT/packs/Microsoft.NETCore.App.Ref"
REF_PACK_VERSION="$( ls "$REF_PACK_BASE" 2>/dev/null | sort -V | tail -n 1 )"
REF_DIR="$REF_PACK_BASE/$REF_PACK_VERSION/ref/$TFM"
if [[ ! -d "$REF_DIR" ]]; then
    echo "ERROR: Reference assemblies not found at $REF_DIR" >&2
    exit 1
fi

# Read package version from the assembly (or fall back to a constant)
VERSION="${VERSION:-$( "$DOTNET_ROOT/dotnet" --version )}"
# Try to read from project (informational)
PKG_VERSION="$( grep -oE '<Version>[^<]+' "$CLIENT_PROJECT" | head -n1 | sed 's/<Version>//' || true )"
if [[ -z "$PKG_VERSION" ]]; then
    PKG_VERSION="dev"
fi

# Collect references: runtime ref-pack DLLs + every sibling DLL next to the client
REF_ARGS=()
while IFS= read -r dll; do
    REF_ARGS+=( --reference "$dll" )
done < <( find "$REF_DIR" -maxdepth 1 -name '*.dll' )

while IFS= read -r dll; do
    [[ "$dll" == "$CLIENT_DLL" ]] && continue
    REF_ARGS+=( --reference "$dll" )
done < <( find "$CLIENT_DIR" -maxdepth 1 -name '*.dll' )

OUTPUT_FILE="$OUTPUT_DIR/Kurrent.Client.$PKG_VERSION.json"

echo "==> Building PackageJsonGenerator..."
dotnet build "$TOOL_PROJECT" -c Release --nologo -v quiet

# Remove stale older outputs for the same package
find "$OUTPUT_DIR" -maxdepth 1 -type f -name 'Kurrent.Client.*.json' \
    ! -path "$OUTPUT_FILE" -delete 2>/dev/null || true

echo "==> Generating: $OUTPUT_FILE"
dotnet run --project "$TOOL_PROJECT" -c Release --no-build -- \
    --input "$CLIENT_DLL" \
    "${REF_ARGS[@]}" \
    --output "$OUTPUT_FILE" \
    --package-name "Kurrent.Client" \
    --package-version "$PKG_VERSION" \
    --target-framework "$TFM"

echo "Done."
