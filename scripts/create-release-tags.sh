#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/create-release-tags.sh <version> [--mac] [--windows] [--push] [--dry-run]

Creates selected release tags:
- --mac: <version>-mac (example: v1.0.8-mac)
- --windows: <version>-windows (example: v1.0.8-windows)

Options:
  --mac      Create only the macOS tag.
  --windows  Create only the Windows tag.
  --push     Push selected tags to origin.
  --dry-run  Print commands without running them.
  -h, --help Show this help text.
EOF
}

if [[ $# -eq 0 ]]; then
  usage
  exit 1
fi

push_tags=false
dry_run=false
create_mac=false
create_windows=false
version=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --push)
      push_tags=true
      shift
      ;;
    --mac)
      create_mac=true
      shift
      ;;
    --windows)
      create_windows=true
      shift
      ;;
    --dry-run)
      dry_run=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    -*)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
    *)
      if [[ -n "$version" ]]; then
        echo "Only one version argument is supported." >&2
        usage
        exit 1
      fi
      version="$1"
      shift
      ;;
  esac
done

if [[ -z "$version" ]]; then
  echo "Version is required." >&2
  usage
  exit 1
fi

if [[ ! "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$ ]]; then
  echo "Version must match v<major>.<minor>.<patch> or v<major>.<minor>.<patch>.<revision> (example: v1.0.8 or v1.0.8.1)." >&2
  exit 1
fi

if [[ "$create_mac" != "true" && "$create_windows" != "true" ]]; then
  echo "Select at least one platform with --mac and/or --windows." >&2
  exit 1
fi

if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "This script must run inside a git repository." >&2
  exit 1
fi

selected_tags=()
if [[ "$create_mac" == "true" ]]; then
  selected_tags+=("${version}-mac")
fi
if [[ "$create_windows" == "true" ]]; then
  selected_tags+=("${version}-windows")
fi

tag_exists_local() {
  git rev-parse -q --verify "refs/tags/$1" >/dev/null 2>&1
}

tag_exists_remote() {
  git ls-remote --exit-code --tags origin "refs/tags/$1" >/dev/null 2>&1
}

for tag in "${selected_tags[@]}"; do
  if tag_exists_local "$tag"; then
    echo "Tag '$tag' already exists locally." >&2
    exit 1
  fi
  if tag_exists_remote "$tag"; then
    echo "Tag '$tag' already exists on origin." >&2
    exit 1
  fi
done

run() {
  if [[ "$dry_run" == "true" ]]; then
    echo "[dry-run] $*"
  else
    "$@"
  fi
}

for tag in "${selected_tags[@]}"; do
  run git tag -a "$tag" -m "Release $tag"
done

if [[ "$push_tags" == "true" ]]; then
  run git push origin "${selected_tags[@]}"
fi

echo "Created tags:"
for tag in "${selected_tags[@]}"; do
  echo "- $tag"
done
if [[ "$push_tags" != "true" ]]; then
  echo "Push with: git push origin ${selected_tags[*]}"
fi
