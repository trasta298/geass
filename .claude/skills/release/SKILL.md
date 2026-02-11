---
name: release
description: Analyze commits and trigger a GitHub Release
disable-model-invocation: true
---

# Release Skill

Create a new release for Geass.

## Steps

1. Get the latest tag:
   ```bash
   git describe --tags --abbrev=0 2>/dev/null
   ```

2. Get commit log since the last tag (or all commits if no tag exists):
   - If a tag exists: `git log <tag>..HEAD --oneline`
   - If no tag: `git log --oneline`

3. Analyze commits and recommend the next version based on emoji prefixes:
   - No previous tag → recommend `v1.0.0`
   - Breaking changes (💥) → bump **major**
   - New features (✨) → bump **minor**
   - Fixes, refactors, docs, etc. (🐛 ♻️ 📝 🧪 🔧) → bump **patch**

4. Use `AskUserQuestion` to propose the recommended version and let the user confirm or override.

5. After approval, trigger the release workflow:
   ```bash
   gh workflow run release.yml -f version=<version>
   ```

6. Watch the workflow run:
   ```bash
   gh run watch --exit-status
   ```
   Report the result to the user.
