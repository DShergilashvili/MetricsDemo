#!/bin/bash

set -e

GATEWAY_URL=${GATEWAY_URL:-http://localhost:5000}
REQUESTS_COUNT=${REQUESTS_COUNT:-100}


# Function to generate random email
random_email() {
  echo "user$(( RANDOM % 1000 ))@example.com"
}

# Function to generate random order
random_order() {
  cat <<EOF
{
  "items": [
    {
      "productId": "prod-$(( RANDOM % 100 ))",
      "quantity": $(( RANDOM % 10 + 1 )),
      "price": $(( RANDOM % 100 + 10 )).99
    }
  ]
}
EOF
}

for i in $(seq 1 $((REQUESTS_COUNT / 2))); do
  email=$(random_email)
  password="password123"

  # Successful login (80% of time)
  if [ $((RANDOM % 100)) -lt 80 ]; then
    status_code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$GATEWAY_URL/api/auth/login" \
      -H "Content-Type: application/json" \
      -d "{\"email\":\"$email\",\"password\":\"$password\"}" 2>/dev/null || echo "000")
  else
    # Failed login (wrong password)
    status_code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$GATEWAY_URL/api/auth/login" \
      -H "Content-Type: application/json" \
      -d "{\"email\":\"$email\",\"password\":\"wrongpassword\"}" 2>/dev/null || echo "000")
  fi

  if [ $((i % 10)) -eq 0 ]; then
    echo "  Generated $i login attempts..."
  fi
done

for i in $(seq 1 $((REQUESTS_COUNT / 2))); do
  order=$(random_order)

  status_code=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$GATEWAY_URL/api/orders" \
    -H "Content-Type: application/json" \
    -d "$order" 2>/dev/null || echo "000")

  if [ $((i % 10)) -eq 0 ]; then
    echo "  Generated $i orders..."
  fi
done


for i in $(seq 1 20); do
  curl -s -o /dev/null "$GATEWAY_URL/api/orders" 2>/dev/null || true
done

