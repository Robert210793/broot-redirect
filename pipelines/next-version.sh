#!/usr/bin/env bash
set -euo pipefail

existing=$(git tag --points-at HEAD --list 'v[0-9]*' | sort -V | tail -1)

if [ -n "$existing" ]; then
    echo "version=${existing#v}"
    echo "release=existing"
    exit 0
fi

last=$(git describe --tags --abbrev=0 --match 'v[0-9]*' 2>/dev/null || echo "v0.0.0")

if [ "$last" = "v0.0.0" ]; then
    range="HEAD"
else
    range="$last..HEAD"
fi

messages=$(git log --format='%B' "$range")
bump="none"

if printf '%s\n' "$messages" | grep -qE '^[a-z]+(\([^)]*\))?!:|^BREAKING[ -]CHANGE'; then
    bump="major"
elif printf '%s\n' "$messages" | grep -qE '^feat(\([^)]*\))?:'; then
    bump="minor"
elif printf '%s\n' "$messages" | grep -qE '^(fix|perf)(\([^)]*\))?:'; then
    bump="patch"
fi

IFS=. read -r major minor patch <<< "${last#v}"

case "$bump" in
    major) major=$((major + 1)); minor=0; patch=0 ;;
    minor) minor=$((minor + 1)); patch=0 ;;
    patch) patch=$((patch + 1)) ;;
    none)
        echo "version=$major.$minor.$patch"
        echo "release=none"
        exit 0
        ;;
esac

echo "version=$major.$minor.$patch"
echo "release=new"
