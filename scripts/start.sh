#!/bin/bash

set -e

echo "========================================="
echo "Starting Observability System"
echo "========================================="

# Check if docker-compose is installed
if ! command -v docker-compose &> /dev/null; then
    echo "docker-compose not found. Please install docker-compose first."
    exit 1
fi

# Pull latest images
echo "Pulling latest Docker images..."
docker-compose pull

# Start infrastructure first
echo "Starting infrastructure services..."
docker-compose up -d postgres rabbitmq prometheus loki promtail jaeger grafana

# Wait for infrastructure to be healthy
echo "Waiting for infrastructure to be ready..."
sleep 10

# Check infrastructure health
echo "Checking infrastructure health..."
until docker-compose exec -T postgres pg_isready -U postgres > /dev/null 2>&1; do
  echo "  Waiting for PostgreSQL..."
  sleep 2
done
echo "  PostgreSQL is ready"

until curl -sf http://localhost:3100/ready > /dev/null 2>&1; do
  echo "  Waiting for Loki..."
  sleep 2
done
echo "  Loki is ready"

until curl -sf http://localhost:9090/-/ready > /dev/null 2>&1; do
  echo "  Waiting for Prometheus..."
  sleep 2
done
echo "  Prometheus is ready"

# Start application services
echo "Starting application services..."
docker-compose up -d gateway auth-service order-service

# Wait for services
echo "Waiting for application services..."
sleep 5

# Check application health
echo "Checking application health..."
for service in gateway:5000 auth-service:5001 order-service:5002; do
  name=$(echo $service | cut -d: -f1)
  port=$(echo $service | cut -d: -f2)

  retries=0
  max_retries=30
  until curl -sf http://localhost:$port/health > /dev/null 2>&1; do
    retries=$((retries + 1))
    if [ $retries -eq $max_retries ]; then
      echo "  $name failed to start"
      docker-compose logs $name
      exit 1
    fi
    echo "  Waiting for $name..."
    sleep 2
  done
  echo "  $name is healthy"
done

