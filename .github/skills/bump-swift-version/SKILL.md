---
name: bump-swift-version
description: Bump the version for the TinyClips Swift app across Info.plist, Info-MAS.plist, and project.pbxproj. Use this skill when you need to update the app version (e.g., from 1.4 to 1.5).
argument-hint: "[version] (e.g., 1.5)"
user-invocable: true
---

# Bump Swift App Version

This skill updates the TinyClips macOS app version across all version configuration files in a single command.

## What it does

The skill automatically updates the following files with the new version number:

- `mac/TinyClips/Info.plist` - `CFBundleShortVersionString`
- `mac/TinyClips/Info-MAS.plist` - `CFBundleShortVersionString` (Mac App Store variant)
- `mac/TinyClips.xcodeproj/project.pbxproj` - All 4 `MARKETING_VERSION` entries (Debug/Release for Direct and MAS builds)

## When to use

- When preparing a new release of the TinyClips macOS app
- When you need to update the version before building or submitting to the Mac App Store
- To keep all version references in sync across the project

## How to use

1. Request the version bump in chat:
   ```
   /bump-swift-version 1.5
   ```

2. Provide the new version in format `X.Y` (e.g., `1.5`, `2.0`)

## Files included

- `bump-swift-version.sh` - Shell script that performs the version updates

## Step-by-step procedure

When asked to bump the Swift version:

1. **Validate the version format** - Ensure it follows the pattern `X.Y` (major.minor)
2. **Update Info.plist** - Replace `CFBundleShortVersionString` value
3. **Update Info-MAS.plist** - Replace `CFBundleShortVersionString` value for the Mac App Store build
4. **Update project.pbxproj** - Replace all 4 `MARKETING_VERSION` entries (one for each build configuration)
5. **Verify changes** - Display the updated values to confirm all files were modified correctly

## Example

**Request:**
```
Bump the Swift app version to 1.6
```

**Result:**
- Info.plist: CFBundleShortVersionString = 1.6
- Info-MAS.plist: CFBundleShortVersionString = 1.6
- project.pbxproj: All MARKETING_VERSION = 1.6

## Notes

- The version format must be `X.Y` (e.g., 1.5, 2.0, 1.10)
- This updates both the direct distribution and Mac App Store variant
- All 4 build configurations (Debug Direct, Release Direct, Debug MAS, Release MAS) are updated
- Changes are made directly to the files; no commit is created automatically
