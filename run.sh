#!/usr/bin/env bash
# Runner script for Tutumlu Price Aggregator
# Usage:
#   ./run.sh         -> Runs API & Frontend (http://localhost:5196)
#   ./run.sh all     -> Runs both API and Background Scraper Worker together
#   ./run.sh worker  -> Runs Background Scraper Worker only

set -e
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

if [ "$1" = "worker" ]; then
  echo "============================================="
  echo "  Starting Tutumlu Scraper Worker...        "
  echo "============================================="
  exec dotnet run --project PriceAggregator.Worker
elif [ "$1" = "all" ]; then
  echo "============================================="
  echo "  Starting Tutumlu API & Scraper Worker...  "
  echo "  Frontend: http://localhost:5196           "
  echo "============================================="
  dotnet run --project PriceAggregator.Api --launch-profile "http" &
  API_PID=$!
  trap 'kill $API_PID 2>/dev/null' EXIT
  dotnet run --project PriceAggregator.Worker
else
  echo "============================================="
  echo "  Tutumlu - Fiyat Karsilastirma Platformu   "
  echo "  API & Frontend: http://localhost:5196      "
  echo "============================================="
  exec dotnet run --project PriceAggregator.Api --launch-profile "http" "$@"
fi
