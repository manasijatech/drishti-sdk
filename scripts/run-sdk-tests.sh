#!/usr/bin/env bash
# Runs the SDK test suites the same way CI does (see .github/workflows/test.yml).
# Usage: scripts/run-sdk-tests.sh [all|python|js|php|java|dotnet|tooling]...
# Runs every requested suite, prints a summary, and exits non-zero if any failed.
set -uo pipefail
cd "$(dirname "$0")/.."

run_python() { (cd python && pip install -q -e ".[dev]" && pytest -q); }
run_js() { (cd js && npm ci --no-audit --no-fund && npm test); }
run_php() {
  composer install --no-interaction --no-progress &&
    composer test &&
    composer analyse
}
run_java() { (cd java && chmod +x mvnw && ./mvnw -B -q verify); }
run_dotnet() { (cd dotnet && dotnet test Drishti.sln -c Release --filter "TestCategory!=Live"); }
run_tooling() { node --test scripts/openapi-diff.test.mjs; }

targets=("$@")
if [ ${#targets[@]} -eq 0 ] || [ "${targets[0]}" = "all" ]; then
  targets=(python js php java dotnet tooling)
fi

failed=()
for target in "${targets[@]}"; do
  case "$target" in
    python | js | php | java | dotnet | tooling) ;;
    *)
      echo "Unknown suite: $target" >&2
      exit 2
      ;;
  esac
  echo "::group::$target"
  if "run_$target"; then
    echo "::endgroup::"
    echo "PASS $target"
  else
    echo "::endgroup::"
    echo "FAIL $target"
    failed+=("$target")
  fi
done

if [ ${#failed[@]} -gt 0 ]; then
  echo "Failed suites: ${failed[*]}"
  exit 1
fi
echo "All suites passed: ${targets[*]}"
