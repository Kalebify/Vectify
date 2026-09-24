#!/usr/bin/env bash
# HTTP only: requires Python 3; checks exact JSON fields and CORS.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "${PYTHON_EXECUTABLE:-python3}" "$SCRIPT_DIR/smoke_test.py" "$@"
