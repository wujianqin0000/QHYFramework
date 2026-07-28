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

Prefer **QHY Framework Developer/UPM Package Publisher** in the framework
authoring project. Its Major, Minor, and Patch actions synchronize the UPM
version, documentation version, bilingual snapshots, and Git tag in a staging
copy. Local files change only after the remote publication succeeds.

To create only a documentation snapshot manually, first set the UPM root
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
`node_modules`, `build`, `.docusaurus`, and caches. Verify that host Assets,
FTP credentials, and author-only developer tools are absent.
