#!/bin/bash

# Bump the version for the Swift app across all version files
# Usage: ./bump-swift-version.sh <new-version>
# Example: ./bump-swift-version.sh 1.6

set -e

if [ "$(uname -s)" != "Darwin" ]; then
    echo "❌ This script must be run on macOS because it uses PlistBuddy and BSD sed."
    exit 1
fi

if [ -z "$1" ]; then
    echo "Usage: $0 <new-version>"
    echo "Example: $0 1.6"
    exit 1
fi

NEW_VERSION="$1"

# Validate version format (X.Y)
if ! [[ "$NEW_VERSION" =~ ^[0-9]+\.[0-9]+$ ]]; then
    echo "❌ Invalid version format: $NEW_VERSION"
    echo "Version must be in format: X.Y (e.g., 1.5, 2.0)"
    exit 1
fi

# Find the repository root
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
MAC_DIR="$REPO_ROOT/mac"

# Files to update
INFO_PLIST="$MAC_DIR/TinyClips/Info.plist"
INFO_MAS_PLIST="$MAC_DIR/TinyClips/Info-MAS.plist"
PROJECT_PBXPROJ="$MAC_DIR/TinyClips.xcodeproj/project.pbxproj"

echo "Bumping Swift app version to $NEW_VERSION..."
echo ""

# Update Info.plist
if [ -f "$INFO_PLIST" ]; then
    /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $NEW_VERSION" "$INFO_PLIST"
    echo "✓ Updated $INFO_PLIST"
else
    echo "❌ File not found: $INFO_PLIST"
    exit 1
fi

# Update Info-MAS.plist
if [ -f "$INFO_MAS_PLIST" ]; then
    /usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $NEW_VERSION" "$INFO_MAS_PLIST"
    echo "✓ Updated $INFO_MAS_PLIST"
else
    echo "❌ File not found: $INFO_MAS_PLIST"
    exit 1
fi

# Update project.pbxproj
if [ -f "$PROJECT_PBXPROJ" ]; then
    sed -E -i '' "s/(MARKETING_VERSION = )[0-9]+\.[0-9]+/\1$NEW_VERSION/g" "$PROJECT_PBXPROJ"
    echo "✓ Updated $PROJECT_PBXPROJ"
else
    echo "❌ File not found: $PROJECT_PBXPROJ"
    exit 1
fi

echo ""
echo "✅ Version bump complete! Updated to $NEW_VERSION"
echo ""
echo "Verification:"
echo "Info.plist:"
grep -A1 "CFBundleShortVersionString" "$INFO_PLIST" | tail -2
echo ""
echo "Info-MAS.plist:"
grep -A1 "CFBundleShortVersionString" "$INFO_MAS_PLIST" | tail -2
echo ""
echo "project.pbxproj MARKETING_VERSION entries:"
grep "MARKETING_VERSION = " "$PROJECT_PBXPROJ" | sort -u
