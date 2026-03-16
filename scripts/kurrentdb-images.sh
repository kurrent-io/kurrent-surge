#!/usr/bin/env bash
set -euo pipefail

REGISTRY="docker.kurrent.io"
CLOUDSMITH_ORG="eventstore"
ARCHS=(amd64 arm64)

declare -A IMAGES=(
    [latest]="kurrent-latest/kurrentdb:latest"
    [lts]="kurrent-latest/kurrentdb:lts"
    [nightly]="kurrent-staging/kurrentdb:nightly"
    [ci]="kurrent-staging/kurrentdb:ci"
)

declare -A TAG_TO_REPO=(
    [latest]="kurrent-latest"
    [lts]="kurrent-latest"
    [nightly]="kurrent-staging"
    [ci]="kurrent-staging"
)

TAG_ORDER=(latest lts nightly ci)

header() { printf "\n\033[1;36m%s\033[0m\n" "$1"; }
label()  { printf "  \033[1m%-14s\033[0m %s\n" "$1" "$2"; }
dim()    { printf "  \033[2m%-14s %s\033[0m\n" "$1" "$2"; }
ok()     { printf "  \033[1;32m%-14s %s\033[0m\n" "$1" "$2"; }
warn()   { printf "  \033[1;33m%-14s %s\033[0m\n" "$1" "$2"; }
alert()  { printf "  \033[1;31m%-14s %s\033[0m\n" "$1" "$2"; }
sep()    { printf "  %s\n" "──────────────────────────────────────────────────"; }

has_cloudsmith=false
if command -v cloudsmith &> /dev/null && [[ -n "${CLOUDSMITH_API_KEY:-}" ]]; then
    has_cloudsmith=true
fi

# resolve auth for public vs private repos
docker_auth_header() {
    local repo="$1"
    if [[ "$repo" == kurrent-staging* ]] && [[ -n "${CLOUDSMITH_API_KEY:-}" ]]; then
        local basic
        basic=$(printf "token:%s" "$CLOUDSMITH_API_KEY" | base64)
        echo "Authorization: Basic $basic"
    else
        echo ""
    fi
}

# get digest for a specific tag via registry v2 API
registry_digest() {
    local repo="$1" tag="$2"
    local auth
    auth=$(docker_auth_header "$repo")
    local args=(-s -I -H "Accept: application/vnd.docker.distribution.manifest.v2+json")
    [[ -n "$auth" ]] && args+=(-H "$auth")
    curl "${args[@]}" "https://$REGISTRY/v2/$repo/manifests/$tag" 2>/dev/null \
        | grep -i docker-content-digest | awk '{print $2}' | tr -d '\r' | sed 's/sha256://'
}

# get version by running a container
container_version() {
    local image="$1" arch="$2"
    local raw
    raw=$(docker run --rm --platform "linux/$arch" "$image" --version 2>&1 || true)
    echo "$raw" | grep -m1 'KurrentDB version' | sed 's/KurrentDB version //' || true
}

# list all tags for a repo
registry_tags() {
    local repo="$1"
    local auth
    auth=$(docker_auth_header "$repo")
    local args=(-s)
    [[ -n "$auth" ]] && args+=(-H "$auth")
    curl "${args[@]}" "https://$REGISTRY/v2/$repo/tags/list" 2>/dev/null \
        | python3 -c "import sys,json; [print(t) for t in json.load(sys.stdin).get('tags',[])]" 2>/dev/null
}

# find arch-specific tags for a given tag (e.g. nightly -> 26.1.0-nightly-arm64-10.0-noble)
find_arch_tags() {
    local repo="$1" tag="$2"
    local all_tags
    all_tags=$(registry_tags "$repo")

    for arch in "${ARCHS[@]}"; do
        local arch_label="$arch"
        [[ "$arch" == "amd64" ]] && arch_label="x64"

        local arch_tag
        arch_tag=$(echo "$all_tags" | grep -E "^[0-9]+\.[0-9]+\.[0-9]+-.*${arch_label}-" | sort -V | tail -1 || true)

        if [[ -n "$arch_tag" ]]; then
            echo "${arch}=${arch_tag}"
        fi
    done
}

#region Docker Registry: resolve per-tag and per-arch digests
header "Docker Registry ($REGISTRY)"
sep

declare -A D_DIGEST D_ARCH_DIGEST D_ARCH_TAG D_AVAILABLE D_VERSION

# cache tags per repo
declare -A REPO_ARCH_TAGS_QUERIED

for tag in "${TAG_ORDER[@]}"; do
    repo_image="${IMAGES[$tag]%%:*}"
    image_tag="${IMAGES[$tag]#*:}"

    # get the default tag digest
    D_DIGEST[$tag]=$(registry_digest "$repo_image" "$image_tag")

    if [[ -z "${D_DIGEST[$tag]}" ]]; then
        alert "[$tag]" "tag not found in registry"
        continue
    fi

    # find per-arch tags and their digests
    if [[ -z "${REPO_ARCH_TAGS_QUERIED[$repo_image]:-}" ]]; then
        REPO_ARCH_TAGS_QUERIED[$repo_image]=1
    fi

    for arch in "${ARCHS[@]}"; do
        key="${tag}_${arch}"
        arch_label="$arch"
        [[ "$arch" == "amd64" ]] && arch_label="x64"

        # check if there's an arch-specific tag
        all_tags=$(registry_tags "$repo_image")
        arch_tag=$(echo "$all_tags" | grep -E "^[0-9]+\.[0-9]+\.[0-9]+.*-${arch_label}-" | sort -V | tail -1 || true)

        if [[ -n "$arch_tag" ]]; then
            D_ARCH_TAG[$key]="$arch_tag"
            D_ARCH_DIGEST[$key]=$(registry_digest "$repo_image" "$arch_tag")
            D_AVAILABLE[$key]=true
        else
            # check if the default digest is this arch (single-arch image)
            D_ARCH_TAG[$key]=""
            D_ARCH_DIGEST[$key]=""
            D_AVAILABLE[$key]=false
        fi
    done

    # for single-arch images (no arch-specific tags found), the default tag IS the only arch
    has_any_arch=false
    for arch in "${ARCHS[@]}"; do
        [[ "${D_AVAILABLE[${tag}_${arch}]}" == true ]] && has_any_arch=true
    done

    if [[ "$has_any_arch" == false ]]; then
        # pull to detect actual arch
        full_image="$REGISTRY/${IMAGES[$tag]}"
        if docker pull -q "$full_image" > /dev/null 2>&1; then
            actual_arch=$(docker inspect --format='{{.Architecture}}' "$full_image" 2>/dev/null)
            key="${tag}_${actual_arch}"
            D_ARCH_TAG[$key]="$image_tag"
            D_ARCH_DIGEST[$key]="${D_DIGEST[$tag]}"
            D_AVAILABLE[$key]=true
        fi
    fi
done

# pull one image per unique digest to get versions
declare -A DIGEST_VERSION
for tag in "${TAG_ORDER[@]}"; do
    for arch in "${ARCHS[@]}"; do
        key="${tag}_${arch}"
        [[ "${D_AVAILABLE[$key]:-false}" != true ]] && continue
        d="${D_ARCH_DIGEST[$key]}"
        [[ -n "${DIGEST_VERSION[$d]:-}" ]] && continue

        # pull by arch-specific tag if available, else default
        full_image="$REGISTRY/${IMAGES[$tag]%%:*}:${D_ARCH_TAG[$key]:-${IMAGES[$tag]#*:}}"
        if docker pull -q --platform "linux/$arch" "$full_image" > /dev/null 2>&1; then
            version=$(container_version "$full_image" "$arch")
            DIGEST_VERSION[$d]="${version:-unknown}"
        else
            DIGEST_VERSION[$d]="unknown"
        fi
    done
done

# display
for tag in "${TAG_ORDER[@]}"; do
    [[ -z "${D_DIGEST[$tag]:-}" ]] && continue
    label "$tag" "$REGISTRY/${IMAGES[$tag]}"
    dim "" "default digest: ${D_DIGEST[$tag]:0:16}..."

    for arch in "${ARCHS[@]}"; do
        key="${tag}_${arch}"
        if [[ "${D_AVAILABLE[$key]:-false}" == true ]]; then
            d="${D_ARCH_DIGEST[$key]}"
            v="${DIGEST_VERSION[$d]:-unknown}"
            arch_tag="${D_ARCH_TAG[$key]}"
            if [[ -n "$arch_tag" && "$arch_tag" != "${IMAGES[$tag]#*:}" ]]; then
                dim "" "$arch  ${v%%\ *}  ${d:0:16}...  (tag: $arch_tag)"
            else
                dim "" "$arch  ${v%%\ *}  ${d:0:16}..."
            fi
        else
            warn "" "$arch  not available"
        fi
    done
done
#endregion

#region Cloudsmith: query per-arch
if [[ "$has_cloudsmith" == true ]]; then
    header "Cloudsmith Registry ($CLOUDSMITH_ORG)"
    sep

    declare -A C_DIGEST C_TAGS C_UPLOADED C_AVAILABLE

    declare -A QUERIED_REPOS
    for tag in "${TAG_ORDER[@]}"; do
        repo="${TAG_TO_REPO[$tag]}"
        [[ -n "${QUERIED_REPOS[$repo]:-}" ]] && continue
        QUERIED_REPOS[$repo]=1

        result=$(cloudsmith list packages "$CLOUDSMITH_ORG/$repo" \
            --query "name:^kurrentdb$ format:docker" \
            --page-size 30 \
            -k "$CLOUDSMITH_API_KEY" \
            -F json 2>&1) || true

        if echo "$result" | grep -q '"data"'; then
            echo "$result" | python3 -c "
import sys, json
data = json.load(sys.stdin)['data']
for p in data:
    vtags = p.get('tags', {}).get('version', [])
    digest = p['version']
    arch = ','.join(a['name'] for a in p.get('architectures', []))
    uploaded = p.get('uploaded_at', '')[:10]
    all_tags = '|'.join(vtags)
    for t in vtags:
        print(f'{t}\t{arch}\t{digest}\t{uploaded}\t{all_tags}')
" 2>/dev/null | while IFS=$'\t' read -r ctag carch cdigest cuploaded calltags; do
                for t in "${TAG_ORDER[@]}"; do
                    if [[ "$ctag" == "$t" ]]; then
                        echo "$cdigest" > "/tmp/kdb_c_${t}_${carch}_digest"
                        echo "$calltags" > "/tmp/kdb_c_${t}_${carch}_tags"
                        echo "$cuploaded" > "/tmp/kdb_c_${t}_${carch}_uploaded"
                    fi
                done
            done
        fi
    done

    for tag in "${TAG_ORDER[@]}"; do
        for arch in "${ARCHS[@]}"; do
            key="${tag}_${arch}"
            C_DIGEST[$key]=$(cat "/tmp/kdb_c_${tag}_${arch}_digest" 2>/dev/null || echo "")
            C_TAGS[$key]=$(cat "/tmp/kdb_c_${tag}_${arch}_tags" 2>/dev/null || echo "")
            C_UPLOADED[$key]=$(cat "/tmp/kdb_c_${tag}_${arch}_uploaded" 2>/dev/null || echo "")
            C_AVAILABLE[$key]=$([[ -n "${C_DIGEST[$key]}" ]] && echo true || echo false)
            rm -f "/tmp/kdb_c_${tag}_${arch}_digest" "/tmp/kdb_c_${tag}_${arch}_tags" "/tmp/kdb_c_${tag}_${arch}_uploaded"
        done
    done

    for tag in "${TAG_ORDER[@]}"; do
        label "$tag" "${TAG_TO_REPO[$tag]}"
        for arch in "${ARCHS[@]}"; do
            key="${tag}_${arch}"
            if [[ "${C_AVAILABLE[$key]}" == true ]]; then
                dim "" "$arch  ${C_DIGEST[$key]:0:16}...  uploaded ${C_UPLOADED[$key]}"
                dim "" "      tags: $(echo "${C_TAGS[$key]}" | tr '|' ', ')"
            else
                warn "" "$arch  not available"
            fi
        done
    done

    #region Compare Docker vs Cloudsmith per-arch
    header "Docker vs Cloudsmith"
    sep

    mismatches=0
    for tag in "${TAG_ORDER[@]}"; do
        label "$tag" ""
        for arch in "${ARCHS[@]}"; do
            key="${tag}_${arch}"
            d_avail="${D_AVAILABLE[$key]:-false}"
            c_avail="${C_AVAILABLE[$key]:-false}"

            if [[ "$d_avail" == false && "$c_avail" == false ]]; then
                dim "" "$arch  not available on either"
            elif [[ "$d_avail" == false ]]; then
                warn "" "$arch  only in cloudsmith"
            elif [[ "$c_avail" == false ]]; then
                warn "" "$arch  only in docker registry"
            elif [[ "${D_ARCH_DIGEST[$key]}" == "${C_DIGEST[$key]}" ]]; then
                ok "" "$arch  digests match"
            else
                alert "" "$arch  MISMATCH!"
                dim  "" "  docker:     ${D_ARCH_DIGEST[$key]:0:24}..."
                dim  "" "  cloudsmith: ${C_DIGEST[$key]:0:24}..."
                ((mismatches++)) || true
            fi
        done
    done

    if [[ $mismatches -eq 0 ]]; then
        echo ""
        ok "result:" "all comparable digests match"
    else
        echo ""
        alert "result:" "$mismatches digest mismatch(es) found!"
    fi
    #endregion
else
    header "Cloudsmith"
    if ! command -v cloudsmith &> /dev/null; then
        dim "skipped:" "cloudsmith CLI not installed (brew install cloudsmith-io/cloudsmith-cli/cloudsmith-cli)"
    else
        dim "skipped:" "CLOUDSMITH_API_KEY not set"
    fi
fi
#endregion

#region Duplicate analysis
header "Duplicate Analysis"
sep

for arch in "${ARCHS[@]}"; do
    declare -A SEEN=()
    for tag in "${TAG_ORDER[@]}"; do
        key="${tag}_${arch}"
        [[ "${D_AVAILABLE[$key]:-false}" != true ]] && continue
        d="${D_ARCH_DIGEST[$key]}"
        if [[ -n "${SEEN[$d]:-}" ]]; then
            label "same:" "$tag = ${SEEN[$d]} ($arch)"
        else
            SEEN[$d]="$tag"
        fi
    done
    unset SEEN
done
#endregion

echo ""
