#!/usr/bin/env bash
#
# gencert.sh — Generate TLS certificates for KurrentDB development and testing.
#
# Downloads es-gencert-cli from https://github.com/kurrent-io/es-gencert-cli
# on first run, caches it at ~/.cache/es-gencert-cli/<version>/, and generates
# a CA + node certificate pair. Optionally generates user certificates for
# client certificate authentication and installs the CA into the OS trust store.
#
# Certs are only created if they don't already exist in the output directory.
# Use --force to regenerate everything (e.g., after changing DNS names).
#
# Usage:
#   ./gencert.sh                                      # CA + node (latest version)
#   ./gencert.sh --force                              # regenerate all certs
#   ./gencert.sh --trust                              # + install CA into OS trust store
#   ./gencert.sh --user-certs admin,invalid           # + user certs (comma-separated)
#   ./gencert.sh --user-certs admin --user-certs bob  # + user certs (repeated flag)
#   ./gencert.sh --version 1.3.0                      # pin a specific version
#   ./gencert.sh --dns "localhost,node1"              # custom DNS names for node cert
#   ./gencert.sh --output /tmp/certs                  # custom output directory
#
# Incremental usage (certs already exist):
#   ./gencert.sh --user-certs goofy # add a user cert, reuse existing CA
#   ./gencert.sh --trust            # just install existing CA into OS
#
# Options:
#   --version X.Y.Z       Pin es-gencert-cli version (default: latest GitHub release)
#   --dns names            Comma-separated DNS names for the node cert (default: localhost)
#   --output dir           Output directory for certs (default: ./certs)
#   --force                Regenerate all certs even if they already exist
#   --user-certs names     Generate user certs; comma-separated or pass the flag multiple times
#   --trust                Install the CA into the OS trust store (macOS/Linux; requires sudo)
#   -h, --help             Show help
#
set -euo pipefail

GENCERT_REPO="kurrent-io/es-gencert-cli"
GENCERT_CACHE_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/es-gencert-cli"

GENCERT_VERSION=""
DNS_NAMES="localhost"
CERTS_DIR="./certs"
TRUST=false
FORCE=false
USER_CERTS=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)     GENCERT_VERSION="$2"; shift 2 ;;
        --dns)         DNS_NAMES="$2"; shift 2 ;;
        --output)      CERTS_DIR="$2"; shift 2 ;;
        --trust)       TRUST=true; shift ;;
        --force)       FORCE=true; shift ;;
        --user-certs)
            IFS=',' read -ra names <<< "$2"
            USER_CERTS+=("${names[@]}")
            shift 2
            ;;
        --help|-h)
            echo "Usage: gencert.sh [options]"
            echo ""
            echo "  --version X.Y.Z        Pin es-gencert-cli version (default: latest release)"
            echo "  --dns names            DNS names for the node cert (default: localhost)"
            echo "  --output dir           Output directory for certs (default: ./certs)"
            echo "  --force                Regenerate all certs even if they already exist"
            echo "  --trust                Install the CA into the OS trust store (requires sudo)"
            echo "  --user-certs names     Generate user certs (comma-separated or repeated)"
            echo ""
            echo "Examples:"
            echo "  gencert.sh                                     # CA + node only"
            echo "  gencert.sh --force                             # regenerate everything"
            echo "  gencert.sh --trust                             # CA + node + install CA"
            echo "  gencert.sh --user-certs admin,invalid          # CA + node + two user certs"
            echo "  gencert.sh --user-certs admin --user-certs bob # same, repeated flag"
            exit 0
            ;;
        *) echo "error: unknown option: $1" >&2; exit 1 ;;
    esac
done

fetch() {
    if command -v curl &>/dev/null; then
        curl -fsSL "$@"
    elif command -v wget &>/dev/null; then
        wget -q -O- "$@"
    else
        echo "error: neither curl nor wget found" >&2
        exit 1
    fi
}

resolve_version() {
    if [[ -n "$GENCERT_VERSION" ]]; then
        return
    fi

    echo ">> Resolving latest es-gencert-cli version..."

    GENCERT_VERSION=$(fetch "https://api.github.com/repos/${GENCERT_REPO}/releases/latest" \
        | grep -o '"tag_name": *"[^"]*"' | cut -d'"' -f4)

    if [[ -z "$GENCERT_VERSION" ]]; then
        echo "error: could not resolve latest version from GitHub" >&2
        exit 1
    fi

    echo ">> Latest version: ${GENCERT_VERSION}"
}

detect_platform() {
    local os arch

    case "$(uname -s)" in
        Darwin) os="Darwin" ;;
        Linux)  os="Linux" ;;
        MINGW*|MSYS*|CYGWIN*) os="Windows" ;;
        *) echo "error: unsupported OS: $(uname -s)" >&2; exit 1 ;;
    esac

    case "$(uname -m)" in
        x86_64|amd64)  arch="x86_64" ;;
        arm64|aarch64) arch="arm64" ;;
        *) echo "error: unsupported architecture: $(uname -m)" >&2; exit 1 ;;
    esac

    echo "${os} ${arch}"
}

download_gencert() {
    read -r os arch <<< "$(detect_platform)"

    local ext="tar.gz"
    [[ "$os" == "Windows" ]] && ext="zip"

    local asset="es-gencert-cli_${GENCERT_VERSION}_${os}-${arch}.${ext}"
    local url="https://github.com/${GENCERT_REPO}/releases/download/${GENCERT_VERSION}/${asset}"
    local tmp_dir
    tmp_dir="$(mktemp -d)"

    echo ">> Downloading es-gencert-cli ${GENCERT_VERSION} (${os}/${arch})..."

    if command -v curl &>/dev/null; then
        curl -fsSL -o "${tmp_dir}/${asset}" "$url"
    elif command -v wget &>/dev/null; then
        wget -q -O "${tmp_dir}/${asset}" "$url"
    else
        echo "error: neither curl nor wget found" >&2
        exit 1
    fi

    mkdir -p "${GENCERT_CACHE_DIR}/${GENCERT_VERSION}"

    if [[ "$ext" == "zip" ]]; then
        unzip -qo "${tmp_dir}/${asset}" -d "${GENCERT_CACHE_DIR}/${GENCERT_VERSION}"
    else
        tar -xzf "${tmp_dir}/${asset}" -C "${GENCERT_CACHE_DIR}/${GENCERT_VERSION}"
    fi

    chmod +x "${GENCERT_CACHE_DIR}/${GENCERT_VERSION}/es-gencert-cli"
    rm -rf "$tmp_dir"

    echo ">> Installed es-gencert-cli to ${GENCERT_CACHE_DIR}/${GENCERT_VERSION}/es-gencert-cli"
}

ensure_gencert() {
    if [[ -n "$GENCERT_VERSION" ]]; then
        local bin="${GENCERT_CACHE_DIR}/${GENCERT_VERSION}/es-gencert-cli"
        if [[ -x "$bin" ]]; then
            GENCERT_BIN="$bin"
            return
        fi
        download_gencert
        GENCERT_BIN="${GENCERT_CACHE_DIR}/${GENCERT_VERSION}/es-gencert-cli"
        return
    fi

    local cached=""
    while IFS= read -r -d '' f; do
        [[ -x "$f" ]] && cached="$f" && break
    done < <(find "$GENCERT_CACHE_DIR" -name "es-gencert-cli" -type f -print0 2>/dev/null)
    if [[ -n "$cached" ]]; then
        GENCERT_BIN="$cached"
        GENCERT_VERSION=$(basename "$(dirname "$cached")")
        echo ">> Using cached es-gencert-cli ${GENCERT_VERSION}"
        return
    fi

    resolve_version
    download_gencert
    GENCERT_BIN="${GENCERT_CACHE_DIR}/${GENCERT_VERSION}/es-gencert-cli"
}

ensure_gencert

mkdir -p "$CERTS_DIR"

echo ">> Certs directory: ${CERTS_DIR}"
[[ "$FORCE" == true ]] && echo ">> Force mode: regenerating all certificates."

force_flag=()
[[ "$FORCE" == true ]] && force_flag=(-force)

if [[ "$FORCE" != true && -f "${CERTS_DIR}/ca/ca.crt" && -f "${CERTS_DIR}/ca/ca.key" ]]; then
    echo ">> CA already exists ($(ls -lh "${CERTS_DIR}/ca/ca.crt" | awk '{print $5, $6, $7, $8}')), skipping."
else
    echo ">> Generating CA certificate..."
    "$GENCERT_BIN" create-ca \
        -out "${CERTS_DIR}/ca" \
        "${force_flag[@]}"
fi

if [[ "$FORCE" != true && -f "${CERTS_DIR}/node/node.crt" && -f "${CERTS_DIR}/node/node.key" ]]; then
    echo ">> Node cert already exists ($(ls -lh "${CERTS_DIR}/node/node.crt" | awk '{print $5, $6, $7, $8}')), skipping."
else
    echo ">> Generating node certificate (DNS: ${DNS_NAMES})..."
    "$GENCERT_BIN" create-node \
        -ca-certificate "${CERTS_DIR}/ca/ca.crt" \
        -ca-key "${CERTS_DIR}/ca/ca.key" \
        -out "${CERTS_DIR}/node" \
        -ip-addresses 127.0.0.1 \
        -dns-names "$DNS_NAMES" \
        "${force_flag[@]}"
fi

for username in "${USER_CERTS[@]}"; do
    user_dir="${CERTS_DIR}/user-${username}"
    if [[ "$FORCE" != true && -f "${user_dir}/user-${username}.crt" && -f "${user_dir}/user-${username}.key" ]]; then
        echo ">> User cert '${username}' already exists ($(ls -lh "${user_dir}/user-${username}.crt" | awk '{print $5, $6, $7, $8}')), skipping."
    else
        echo ">> Generating user certificate for '${username}'..."
        "$GENCERT_BIN" create-user \
            -username "$username" \
            -ca-certificate "${CERTS_DIR}/ca/ca.crt" \
            -ca-key "${CERTS_DIR}/ca/ca.key" \
            -out "${CERTS_DIR}/user-${username}" \
            "${force_flag[@]}"
    fi
done

find "$CERTS_DIR" -type f -exec chmod 666 {} +
find "$CERTS_DIR" -type d -exec chmod 777 {} +

if [[ "$TRUST" == true ]]; then
    install_trust_store() {
        local ca_crt="${CERTS_DIR}/ca/ca.crt"

        case "$(uname -sr)" in
            Darwin*)
                echo ">> Installing CA into macOS System keychain..."

                local cert_name
                cert_name=$(openssl x509 -noout -subject -in "$ca_crt" | sed -n 's/.*CN *= *\([^,]*\).*/\1/p')

                sudo cp "$ca_crt" /tmp/ca.crt
                sudo chmod 644 /tmp/ca.crt
                sudo security import /tmp/ca.crt -k /Library/Keychains/System.keychain -T /usr/bin/codesign
                sudo security add-trusted-cert -d -r trustRoot -p ssl -p basic -k /Library/Keychains/System.keychain /tmp/ca.crt
                sudo rm /tmp/ca.crt

                if security find-certificate -a -c "$cert_name" -p /Library/Keychains/System.keychain > /dev/null 2>&1; then
                    echo ">> Certificate '${cert_name}' installed successfully."
                else
                    echo ">> Certificate installation failed. Check Keychain Access manually."
                    echo ">> You can try opening ${ca_crt} in Keychain Access."
                fi
                ;;
            Linux*Microsoft*)
                echo ">> Installing CA on WSL..."
                sudo cp "$ca_crt" /usr/local/share/ca-certificates/kurrentdb_ca.crt
                sudo update-ca-certificates
                ;;
            Linux*)
                echo ">> Installing CA on Linux..."
                sudo cp "$ca_crt" /usr/local/share/ca-certificates/kurrentdb_ca.crt
                sudo update-ca-certificates
                ;;
            *)
                echo ">> Unknown platform. Please install the CA certificate manually: ${ca_crt}"
                ;;
        esac
    }

    install_trust_store
fi
