---
title: Documentation maintenance
---

# Documentation maintenance {#docs-maintenance}

Chinese Next lives in `docs/`; English Next mirrors it under
`i18n/en/docusaurus-plugin-content-docs/current/`. Stable snapshots live in
`versioned_docs/version-x.y.z` and the matching English version directory.
Every relative path and explicit heading ID must match.

```bash
npm ci
npm run start
npm run start:en
npm run check
npm run build
```

Core tutorials include prerequisites, steps, expected results, common errors,
and next steps. Add both languages in one change, update `sidebars.ts`, and give
every screenshot descriptive alt text.

Do not change the UPM version or generate a stable snapshot during daily
development. Record changes under CHANGELOG `[Unreleased]` and maintain both
Next languages. At publication time, use **QHY Framework Developer/UPM Package
Publisher** to select a Major, Minor, or Patch target.

The publisher promotes `[Unreleased]`, updates README and every version file,
generates bilingual stable snapshots, and requires `npm run check` to pass. It
then publishes the same branch and tag to GitHub and Gitee. The new version is
written back locally only after both remotes succeed.

On first open, the publisher paints its UI and loading state immediately, then
performs lightweight stable-version inspection in a later Editor callback. Full
package scanning and documentation production builds run only when validating
Unreleased content or starting an actual publication.

The following manual command is reserved for publisher maintenance or
diagnostics and must not be used to prepare snapshots during daily development.
First set the UPM root
`package.json` to the target version, then run:

```bash
npm run docs:release -- 1.1.0
```

The script snapshots both languages and updates `versions.json`, the docs
package, and lock-file versions. It does not change the framework package and
refuses to overwrite an existing version.

GitHub Pull Requests run the complete check. Main deploys a Pages artifact.
Once, select **Settings → Pages → Source → GitHub Actions**.

The UPM publisher keeps documentation source and static assets but excludes
`node_modules`, `build`, `.docusaurus`, and caches. It refreshes the current
stable version from the UPM `package.json` when the window regains focus or the project
changes. Verify that host Assets, FTP credentials, and author-only developer
tools are absent.
