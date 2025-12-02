#!/bin/bash

# ============================================================================
# Test Environment Setup Script
# ============================================================================
# Automates Docker-based test infrastructure setup for integration testing
#
# Usage:
#   ./setup-test-environment.sh [OPTIONS]
#
# Options:
#   --all          Start all services (default)
#   --databases    Start only database services
#   --messaging    Start only messaging services
#   --caching      Start only caching services
#   --cloud        Start only cloud emulators
#   --minimal      Start minimal services (postgres, redis, rabbitmq)
#   --down         Stop and remove all containers
#   --clean        Stop containers and remove volumes
#   --status       Show status of all services
#   --logs [service]  Show logs for service (or all if not specified)
#   --help         Show this help message
#
# Examples:
#   ./setup-test-environment.sh --minimal
#   ./setup-test-environment.sh --databases
#   ./setup-test-environment.sh --down
#   ./setup-test-environment.sh --logs postgres
# ============================================================================

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.test.yml"

# ============================================================================
# Helper Functions
# ============================================================================

print_header() {
    echo -e "${BLUE}========================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}========================================${NC}"
}

print_success() {
    echo -e "${GREEN}✓ $1${NC}"
}

print_error() {
    echo -e "${RED}✗ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠ $1${NC}"
}

print_info() {
    echo -e "${BLUE}ℹ $1${NC}"
}

check_docker() {
    if ! command -v docker &> /dev/null; then
        print_error "Docker is not installed or not in PATH"
        exit 1
    fi

    if ! docker info &> /dev/null; then
        print_error "Docker daemon is not running"
        exit 1
    fi

    print_success "Docker is installed and running"
}

check_compose_file() {
    if [ ! -f "$COMPOSE_FILE" ]; then
        print_error "Docker Compose file not found: $COMPOSE_FILE"
        exit 1
    fi
    print_success "Docker Compose file found"
}

wait_for_service() {
    local service=$1
    local max_attempts=30
    local attempt=0

    print_info "Waiting for $service to be healthy..."

    while [ $attempt -lt $max_attempts ]; do
        if docker compose -f "$COMPOSE_FILE" ps "$service" | grep -q "healthy"; then
            print_success "$service is healthy"
            return 0
        fi

        attempt=$((attempt + 1))
        echo -n "."
        sleep 2
    done

    print_warning "$service did not become healthy within timeout"
    return 1
}

# ============================================================================
# Service Groups
# ============================================================================

DATABASES="postgres sqlserver mysql mongodb"
MESSAGING="rabbitmq kafka zookeeper"
CACHING="redis"
CLOUD="azurite localstack"
OBSERVABILITY="jaeger prometheus grafana"
SEARCH="elasticsearch"

MINIMAL="postgres redis rabbitmq"
ALL_SERVICES="$DATABASES $MESSAGING $CACHING $CLOUD $OBSERVABILITY $SEARCH"

# ============================================================================
# Operations
# ============================================================================

start_services() {
    local services=$1

    print_header "Starting Services"
    print_info "Services: $services"

    docker compose -f "$COMPOSE_FILE" up -d $services

    print_success "Services started"

    # Wait for services to be healthy
    for service in $services; do
        wait_for_service "$service" || true
    done
}

stop_services() {
    print_header "Stopping Services"

    docker compose -f "$COMPOSE_FILE" down

    print_success "Services stopped"
}

clean_services() {
    print_header "Cleaning Services"

    docker compose -f "$COMPOSE_FILE" down -v

    print_success "Services stopped and volumes removed"
}

show_status() {
    print_header "Service Status"

    docker compose -f "$COMPOSE_FILE" ps

    echo ""
    print_info "Container Details:"
    docker ps --filter "name=test-" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
}

show_logs() {
    local service=$1

    print_header "Service Logs"

    if [ -z "$service" ]; then
        docker compose -f "$COMPOSE_FILE" logs --tail=100 --follow
    else
        docker compose -f "$COMPOSE_FILE" logs --tail=100 --follow "$service"
    fi
}

show_help() {
    cat << EOF
Test Environment Setup Script

Usage:
  $0 [OPTIONS]

Options:
  --all          Start all services (default)
  --databases    Start only database services
  --messaging    Start only messaging services
  --caching      Start only caching services
  --cloud        Start only cloud emulators
  --minimal      Start minimal services (postgres, redis, rabbitmq)
  --down         Stop and remove all containers
  --clean        Stop containers and remove volumes
  --status       Show status of all services
  --logs [service]  Show logs for service (or all if not specified)
  --help         Show this help message

Service Groups:
  databases: $DATABASES
  messaging: $MESSAGING
  caching: $CACHING
  cloud: $CLOUD
  minimal: $MINIMAL

Examples:
  $0 --minimal             # Start minimal services
  $0 --databases           # Start only databases
  $0 --down                # Stop all services
  $0 --clean               # Stop and clean volumes
  $0 --status              # Show service status
  $0 --logs postgres       # Show postgres logs

Environment Variables:
  COMPOSE_FILE   Path to docker-compose file (default: ./docker-compose.test.yml)

EOF
}

# ============================================================================
# Connection Information
# ============================================================================

show_connection_info() {
    print_header "Connection Information"

    cat << EOF
PostgreSQL:
  Host: localhost
  Port: 5432
  Database: testdb
  Username: testuser
  Password: testpass
  Connection String: Host=localhost;Port=5432;Database=testdb;Username=testuser;Password=testpass

SQL Server:
  Host: localhost
  Port: 1433
  Database: testdb
  Username: sa
  Password: YourStrong!Passw0rd
  Connection String: Server=localhost,1433;Database=testdb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True

MySQL:
  Host: localhost
  Port: 3306
  Database: testdb
  Username: testuser
  Password: testpass
  Connection String: Server=localhost;Port=3306;Database=testdb;Uid=testuser;Pwd=testpass

Redis:
  Host: localhost
  Port: 6379
  Connection String: localhost:6379

RabbitMQ:
  Host: localhost
  Port: 5672 (AMQP)
  Management UI: http://localhost:15672
  Username: guest
  Password: guest
  Connection String: amqp://guest:guest@localhost:5672/

MongoDB:
  Host: localhost
  Port: 27017
  Database: testdb
  Username: testuser
  Password: testpass
  Connection String: mongodb://testuser:testpass@localhost:27017/testdb

Elasticsearch:
  Host: localhost
  Port: 9200
  URL: http://localhost:9200

Kafka:
  Bootstrap Servers: localhost:9092

Azurite (Azure Storage):
  Blob: http://localhost:10000
  Queue: http://localhost:10001
  Table: http://localhost:10002
  Account: devstoreaccount1
  Key: Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==

LocalStack (AWS):
  Endpoint: http://localhost:4566
  Services: s3, dynamodb, sqs, sns, lambda

Jaeger UI:
  URL: http://localhost:16686

Prometheus:
  URL: http://localhost:9090

Grafana:
  URL: http://localhost:3000
  Username: admin
  Password: admin

EOF
}

# ============================================================================
# Main Script
# ============================================================================

main() {
    print_header "Test Environment Setup"

    # Check prerequisites
    check_docker
    check_compose_file

    # Parse arguments
    if [ $# -eq 0 ]; then
        start_services "$MINIMAL"
        show_connection_info
        exit 0
    fi

    case "$1" in
        --all)
            start_services "$ALL_SERVICES"
            show_connection_info
            ;;
        --databases)
            start_services "$DATABASES"
            show_connection_info
            ;;
        --messaging)
            start_services "$MESSAGING"
            show_connection_info
            ;;
        --caching)
            start_services "$CACHING"
            show_connection_info
            ;;
        --cloud)
            start_services "$CLOUD"
            show_connection_info
            ;;
        --minimal)
            start_services "$MINIMAL"
            show_connection_info
            ;;
        --down)
            stop_services
            ;;
        --clean)
            clean_services
            ;;
        --status)
            show_status
            ;;
        --logs)
            show_logs "$2"
            ;;
        --help|-h)
            show_help
            ;;
        *)
            print_error "Unknown option: $1"
            echo ""
            show_help
            exit 1
            ;;
    esac
}

# Run main function
main "$@"
