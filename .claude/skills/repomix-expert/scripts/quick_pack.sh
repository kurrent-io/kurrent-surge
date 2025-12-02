#!/usr/bin/env bash
#
# Repomix Quick Pack
#
# Preset packing workflows for common use cases.
# Provides convenient shortcuts for frequent operations.
#
# Usage:
#   ./quick_pack.sh [preset] [directory]
#
# Presets:
#   overview     - Compressed overview with token tree (fast analysis)
#   review       - Code review with diffs and logs (detailed review)
#   docs         - Documentation with compression (API reference)
#   security     - Security audit without compression (vulnerability scan)
#   full         - Complete uncompressed pack (debugging)
#   typescript   - TypeScript project optimized (TS/TSX only)
#   python       - Python project optimized (PY only)
#   dotnet       - .NET project optimized (CS/CSHTML only)
#   web          - Web project optimized (HTML/CSS/JS/TS)
#

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Functions
print_usage() {
    echo -e "${BLUE}Repomix Quick Pack${NC}"
    echo ""
    echo "Usage: $0 [preset] [directory]"
    echo ""
    echo "Presets:"
    echo "  ${GREEN}overview${NC}     - Compressed overview with token tree"
    echo "  ${GREEN}review${NC}       - Code review with diffs and logs"
    echo "  ${GREEN}docs${NC}         - Documentation generation"
    echo "  ${GREEN}security${NC}     - Security audit scan"
    echo "  ${GREEN}full${NC}         - Complete uncompressed pack"
    echo "  ${GREEN}typescript${NC}   - TypeScript project (*.ts, *.tsx)"
    echo "  ${GREEN}python${NC}       - Python project (*.py)"
    echo "  ${GREEN}dotnet${NC}       - .NET project (*.cs, *.cshtml)"
    echo "  ${GREEN}web${NC}          - Web project (HTML/CSS/JS/TS)"
    echo ""
    echo "Directory: Path to pack (default: current directory)"
    echo ""
    echo "Examples:"
    echo "  $0 overview"
    echo "  $0 review /path/to/project"
    echo "  $0 typescript ./src"
}

print_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

# Check if repomix is available
check_repomix() {
    if ! command -v repomix &> /dev/null; then
        print_error "repomix not found"
        print_info "Install with: npm install -g repomix"
        print_info "Or use: npx repomix"
        exit 1
    fi
}

# Main function
main() {
    check_repomix

    # Parse arguments
    PRESET="${1:-overview}"
    DIRECTORY="${2:-.}"

    if [[ "$PRESET" == "-h" ]] || [[ "$PRESET" == "--help" ]]; then
        print_usage
        exit 0
    fi

    # Validate directory
    if [ ! -d "$DIRECTORY" ]; then
        print_error "Directory not found: $DIRECTORY"
        exit 1
    fi

    print_info "Preset: $PRESET"
    print_info "Directory: $DIRECTORY"
    echo ""

    # Execute preset
    case "$PRESET" in
        overview)
            print_info "Generating compressed overview with token tree..."
            repomix "$DIRECTORY" \
                --compress \
                --token-count-tree \
                --style markdown \
                --output repomix-overview.md
            print_success "Overview generated: repomix-overview.md"
            ;;

        review)
            print_info "Generating code review pack with git context..."
            repomix "$DIRECTORY" \
                --include-diffs \
                --include-logs \
                --include-logs-count 20 \
                --sort-by-changes \
                --style markdown \
                --output repomix-review.md
            print_success "Review pack generated: repomix-review.md"
            print_info "Includes uncommitted changes and last 20 commits"
            ;;

        docs)
            print_info "Generating documentation with compression..."
            repomix "$DIRECTORY" \
                --compress \
                --include "src/**,**/*.md" \
                --remove-comments \
                --style markdown \
                --output repomix-docs.md
            print_success "Documentation generated: repomix-docs.md"
            ;;

        security)
            print_info "Generating security audit pack..."
            repomix "$DIRECTORY" \
                --include "**/*.{js,ts,py,go,java,cs,rb,php}" \
                --style xml \
                --output repomix-security.xml
            print_success "Security pack generated: repomix-security.xml"
            print_warning "Security check enabled - will fail if sensitive data detected"
            ;;

        full)
            print_info "Generating complete uncompressed pack..."
            repomix "$DIRECTORY" \
                --style xml \
                --output repomix-full.xml
            print_success "Full pack generated: repomix-full.xml"
            print_warning "Large output - consider using compression for analysis"
            ;;

        typescript)
            print_info "Packing TypeScript project..."
            repomix "$DIRECTORY" \
                --include "**/*.{ts,tsx},**/*.json,**/*.md" \
                --ignore "**/*.test.ts,**/*.spec.ts,**/dist/**,**/build/**" \
                --compress \
                --style json \
                --output repomix-typescript.json
            print_success "TypeScript pack generated: repomix-typescript.json"
            ;;

        python)
            print_info "Packing Python project..."
            repomix "$DIRECTORY" \
                --include "**/*.py,requirements.txt,**/*.md" \
                --ignore "**/*_test.py,**/test_*.py,**/__pycache__/**,**/venv/**" \
                --compress \
                --style json \
                --output repomix-python.json
            print_success "Python pack generated: repomix-python.json"
            ;;

        dotnet)
            print_info "Packing .NET project..."
            repomix "$DIRECTORY" \
                --include "**/*.cs,**/*.cshtml,**/*.csproj,**/*.sln,**/*.md" \
                --ignore "**/bin/**,**/obj/**,**/*.Designer.cs" \
                --compress \
                --style json \
                --output repomix-dotnet.json
            print_success ".NET pack generated: repomix-dotnet.json"
            ;;

        web)
            print_info "Packing web project..."
            repomix "$DIRECTORY" \
                --include "**/*.{html,css,scss,js,ts,tsx,jsx,json,md}" \
                --ignore "**/node_modules/**,**/dist/**,**/build/**,**/*.min.js,**/*.min.css" \
                --compress \
                --style markdown \
                --output repomix-web.md
            print_success "Web pack generated: repomix-web.md"
            ;;

        *)
            print_error "Unknown preset: $PRESET"
            echo ""
            print_usage
            exit 1
            ;;
    esac

    echo ""
    print_success "Done!"
}

# Run main
main "$@"
