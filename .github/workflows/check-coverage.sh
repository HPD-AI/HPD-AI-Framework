#!/bin/bash
# Check code coverage against threshold
# Usage: ./check-coverage.sh <json-report-path> <threshold>

JSON_REPORT=$1
THRESHOLD=$2

if [ ! -f "$JSON_REPORT" ]; then
    echo "Coverage report not found: $JSON_REPORT"
    exit 1
fi

# Extract line coverage from JSON summary
LINE_COVERAGE=$(cat "$JSON_REPORT" | grep -o '"linecoverage":[0-9.]*' | head -1 | cut -d':' -f2)
THREAD_COVERAGE=$(cat "$JSON_REPORT" | grep -o '"threadcoverage":[0-9.]*' | head -1 | cut -d':' -f2)

echo "=================================="
echo "       Code Coverage Report       "
echo "=================================="
echo ""
printf "Line Coverage:   %.1f%%\n" "$LINE_COVERAGE"
printf "Thread Coverage: %.1f%%\n" "$THREAD_COVERAGE"
printf "Threshold:       %.1f%%\n" "$THRESHOLD"
echo ""

# Check if coverage meets threshold
PASSED=true

if (( $(echo "$LINE_COVERAGE < $THRESHOLD" | bc -l) )); then
    echo "Line coverage ($LINE_COVERAGE%) is below threshold ($THRESHOLD%)"
    PASSED=false
else
    echo "✓ Line coverage meets threshold"
fi

if (( $(echo "$THREAD_COVERAGE < $THRESHOLD" | bc -l) )); then
    echo "Thread coverage ($THREAD_COVERAGE%) is below threshold ($THRESHOLD%)"
    PASSED=false
else
    echo "✓ Thread coverage meets threshold"
fi

echo ""

if [ "$PASSED" = false ]; then
    echo "=================================="
    echo "      Coverage Check FAILED       "
    echo "=================================="
    exit 1
else
    echo "=================================="
    echo "      Coverage Check PASSED       "
    echo "=================================="
    exit 0
fi
