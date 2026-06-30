---
name: tag-new-release
description: Create a new annotated git tag with release notes extracted from CHANGELOG.md. Use this skill when preparing or tagging a new TinyClips release.
argument-hint: "[version] (optional, e.g., v1.2.3)"
user-invocable: true
---

# Tag New Release

Create a new annotated git tag for a release based on the current project's changelog.

## When to use

- When preparing a new TinyClips release tag.
- When the user asks to tag a release.
- When the user provides a version to tag, such as `v1.2.3`.
- When the user wants release notes extracted from `CHANGELOG.md` and included in the tag message.

## How to use

Request the release tag in chat:

```text
/tag-new-release v1.2.3
```

The version argument is optional. If no version is provided, determine the release version from the latest unreleased entry in `CHANGELOG.md` and the current app version settings.

## Step-by-step procedure

When asked to tag a new release:

1. **Check existing tags** - Inspect the most recent git tags to understand the repository's versioning scheme.
2. **Check the app version** - Inspect the app's version setting, such as project settings, build configuration, or `Info.plist`.
   - Only increment to a new minor version, such as `1.3.x` to `1.4.0`, if the app version has been updated in the codebase.
   - Otherwise, stay on a patch version, such as `1.3.2`.
3. **Read `CHANGELOG.md`** - Identify the latest unreleased version and its release notes.
   - If release notes do not exist for the target version, create them from the unreleased changes.
4. **Update `CHANGELOG.md`** - Mark the version as released and add the release date if it is not already present.
5. **Verify the working directory** - Ensure there are no uncommitted changes before creating the tag.
6. **Create an annotated git tag** with:
   - A tag name matching the version, such as `v1.2.3`.
   - A tag message containing the version and formatted release notes from `CHANGELOG.md`.
7. **Confirm the tag** - Show the created tag details to verify the tag name and message.
8. **Suggest pushing the tag** - Tell the user they can push with:

   ```bash
   git push origin <tag-name>
   ```

   Do not push the tag unless the user explicitly asks.

## Release notes format

The tag message should include cleanly formatted release notes from the `CHANGELOG.md` entry for the release. Include all relevant sections, such as:

- Added
- Improved
- Fixed
- Changed
- Deprecated
- Removed
- Security

## Safety checks

- Do not create a release tag from a dirty working directory.
- Do not overwrite an existing tag.
- Do not push tags without explicit user approval.
- If the requested version conflicts with the changelog, app version, or existing tags, stop and ask the user how to proceed.
