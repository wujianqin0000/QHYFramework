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

To release docs, first set the UPM root `package.json` to the target version,
then run:

```bash
npm run docs:release -- 1.1.0
```

The script snapshots both languages and updates `versions.json`; it refuses to
overwrite an existing version.

GitHub Pull Requests run the complete check. Main deploys a Pages artifact.
Once, select **Settings → Pages → Source → GitHub Actions**.

The UPM publisher keeps documentation source and static assets but excludes
`node_modules`, `build`, `.docusaurus`, and caches. Verify that host Assets,
FTP credentials, and author-only developer tools are absent.
