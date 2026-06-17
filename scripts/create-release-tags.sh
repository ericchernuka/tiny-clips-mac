#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/create-release-tags.sh <version> [--push] [--dry-run]

Creates both release tags:
- macOS tag: <version> (example: v1.0.8)
- Windows tag: <version>-windows (example: v1.0.8-windows)

Options:
  --push     Push both tags to origin.
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
version=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --push)
      push_tags=true
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

if [[ ! "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must match v<major>.<minor>.<patch> (example: v1.0.8)." >&2
  exit 1
fi

if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "This script must run inside a git repository." >&2
  exit 1
fi

mac_tag="$version"
windows_tag="${version}-windows"

tag_exists_local() {
  git rev-parse -q --verify "refs/tags/$1" >/dev/null 2>&1
}

tag_exists_remote() {
  git ls-remote --exit-code --tags origin "refs/tags/$1" >/dev/null 2>&1
}

for tag in "$mac_tag" "$windows_tag"; do
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

run git tag -a "$mac_tag" -m "Release $mac_tag"
run git tag -a "$windows_tag" -m "Release $windows_tag"

if [[ "$push_tags" == "true" ]]; then
  run git push origin "$mac_tag" "$windows_tag"
fi

echo "Created tags:"
echo "- $mac_tag"
echo "- $windows_tag"
if [[ "$push_tags" != "true" ]]; then
  echo "Push with: git push origin $mac_tag $windows_tag"
fi
