#!/usr/bin/env bash
# Prueba de humo end-to-end para el flujo React -> ASP.NET Core -> FastAPI.
#
# Requiere que la pila esté arriba (por ejemplo, "docker compose up --build"
# desde la raíz del repo) y que curl esté disponible. No reemplaza a las
# suites de xUnit (backend/Vectify.Api.Tests) ni pytest
# (services/python-engine/tests); confirma que los tres servicios están
# realmente conectados entre sí, tal como pide la Definition of Done.
#
# Uso:
#   BACKEND_URL=http://localhost:5080 PYTHON_URL=http://localhost:8001 \
#     ./tests/e2e/smoke-test.sh

set -euo pipefail

BACKEND_URL="${BACKEND_URL:-http://localhost:5080}"
PYTHON_URL="${PYTHON_URL:-http://localhost:8001}"

failures=0

check() {
  local description="$1"
  local url="$2"
  local expected_substring="$3"

  local body
  if ! body="$(curl -sS --fail --max-time 5 "$url")"; then
    echo "FALLÓ: $description ($url no respondió)"
    failures=$((failures + 1))
    return
  fi

  if [[ "$body" == *"$expected_substring"* ]]; then
    echo "OK: $description"
  else
    echo "FALLÓ: $description (respuesta inesperada: $body)"
    failures=$((failures + 1))
  fi
}

echo "== Smoke test end-to-end: React -> ASP.NET Core -> FastAPI =="

check "Backend /health responde ok" "$BACKEND_URL/health" '"status":"ok"'
check "Python /health responde ok (acceso directo, solo para diagnóstico)" "$PYTHON_URL/health" '"status":"ok"'
check "Backend /api/v1/system/health reporta status online (Python arriba)" "$BACKEND_URL/api/v1/system/health" '"status":"online"'

if [[ $failures -eq 0 ]]; then
  echo "Todas las verificaciones pasaron."
  exit 0
else
  echo "$failures verificación(es) fallaron."
  exit 1
fi
