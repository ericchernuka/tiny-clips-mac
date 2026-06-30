#!/bin/bash

# Bump the version for the Swift app across all version files
# Usage: ./scripts/bump-swift-version.sh <new-version>
# Example: ./scripts/bump-swift-version.sh 1.6

set -e

if [ -z "$1" ]; then
    echo "Usage: $0 <new-version>"
    echo "Example: $0 1.6"
    exit 1
fi

NEW_VERSION="$1"
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
MAC_DIR="$REPO_ROOT/mac"

# Files to update
INFO_PLIST="$MAC_DIR/TinyClips/Info.plist"
INFO_MAS_PLIST="$MAC_DIR/TinyClips/Info-MAS.plist"
PROJECT_PBXPROJ="$MAC_DIR/TinyClips.xcodeproj/project.pbxproj"

echo "Bumping Swift app version to $NEW_VERSION..."

# Update Info.plist
if [ -f "$INFO_PLIST" ]; then
    sed -i '' "s/<string>[0-9]*\.[0-9]*<\/string>/<string>$NEW_VERSION<\/string>/g" "$INFO_PLIST" | grep -A1 "CFBundleShortVersionString" || true
    echo "✓ Updated $INFO_PLIST"
fi

# Update Info-MAS.plist
if [ -f "$INFO_MAS_PLIST" ]; then
    sed -i '' "s/<string>[0-9]*\.[0-9]*<\/string>/<string>$NEW_VERSION<\/string>/g" "$INFO_MAS_PLIST" | grep -A1 "CFBundleShortVersionString" || true
    echo "✓ Updated $INFO_MAS_PLIST"
fi

# Update project.pbxproj - replace all MARKETING_VERSION = X.Y with the new version
if [ -f "$PROJECT_PBXPROJ" ]; then
    sed -i '' "s/MARKETING_VERSION = [0-9]*\.[0-9]*/MARKETING_VERSION = $NEW_VERSION/g" "$PROJECT_PBXPROJ"
    echo "✓ Updated $PROJECT_PBXPROJ"
fi

echo ""
echo "Version bump complete! Updated to $NEW_VERSION"
echo ""
echo "Verification:"
grep "CFBundleShortVersionString" "$INFO_PLIST" | tail -1
grep "CFBundleShortVersionString" "$INFO_MAS_PLIST" | tail -1
echo "MARKETING_VERSION entries in project.pbxproj:"
grep "MARKETING_VERSION = " "$PROJECT_PBXPROJ" | sort -u
