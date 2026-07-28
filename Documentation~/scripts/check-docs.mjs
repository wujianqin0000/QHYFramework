import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import {fileURLToPath} from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const packageRoot = path.resolve(root, '..');
const zhRoot = path.join(root, 'docs');
const enRoot = path.join(root, 'i18n/en/docusaurus-plugin-content-docs/current');
const versions = JSON.parse(fs.readFileSync(path.join(root, 'versions.json'), 'utf8'));
const docsPackage = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
const docsPackageLock = JSON.parse(fs.readFileSync(path.join(root, 'package-lock.json'), 'utf8'));
const frameworkPackage = JSON.parse(fs.readFileSync(path.join(packageRoot, 'package.json'), 'utf8'));

const fail = (message) => {
  console.error(`Documentation check failed: ${message}`);
  process.exitCode = 1;
};

const markdownFiles = (directory) => {
  const result = [];
  const visit = (current) => {
    for (const entry of fs.readdirSync(current, {withFileTypes: true})) {
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) visit(full);
      else if (entry.name.endsWith('.md') || entry.name.endsWith('.mdx'))
        result.push(path.relative(directory, full).replaceAll('\\', '/'));
    }
  };
  visit(directory);
  return result.sort();
};

const zhFiles = markdownFiles(zhRoot);
const enFiles = markdownFiles(enRoot);
for (const file of zhFiles.filter((value) => !enFiles.includes(value)))
  fail(`missing English page: ${file}`);
for (const file of enFiles.filter((value) => !zhFiles.includes(value)))
  fail(`missing Chinese page: ${file}`);

for (const file of zhFiles.filter((value) => enFiles.includes(value))) {
  const anchors = (base) => [
    ...fs.readFileSync(path.join(base, file), 'utf8').matchAll(/\{#([a-z0-9-]+)\}/g),
  ].map((match) => match[1]).sort();
  const zhAnchors = anchors(zhRoot);
  const enAnchors = anchors(enRoot);
  if (JSON.stringify(zhAnchors) !== JSON.stringify(enAnchors))
    fail(`explicit heading IDs differ: ${file}`);
}

if (docsPackage.version !== frameworkPackage.version)
  fail(`docs package ${docsPackage.version} != framework package ${frameworkPackage.version}`);

if (docsPackageLock.version !== docsPackage.version ||
    docsPackageLock.packages?.['']?.version !== docsPackage.version)
  fail(`package-lock version does not match docs package ${docsPackage.version}`);

if (versions[0] !== frameworkPackage.version)
  fail(`latest stable documentation ${versions[0] ?? '(none)'} != framework package ${frameworkPackage.version}`);

for (const version of versions) {
  const zhVersion = path.join(root, `versioned_docs/version-${version}`);
  const enVersion = path.join(root, `i18n/en/docusaurus-plugin-content-docs/version-${version}`);
  if (!fs.existsSync(zhVersion)) fail(`missing Chinese stable snapshot ${version}`);
  if (!fs.existsSync(enVersion)) fail(`missing English stable snapshot ${version}`);
  if (!fs.existsSync(path.join(root, `versioned_sidebars/version-${version}-sidebars.json`)))
    fail(`missing versioned sidebar ${version}`);
  if (fs.existsSync(zhVersion) && fs.existsSync(enVersion)) {
    const zh = markdownFiles(zhVersion);
    const en = markdownFiles(enVersion);
    if (JSON.stringify(zh) !== JSON.stringify(en))
      fail(`stable language pages differ for ${version}`);
  }
}

if (!process.exitCode)
  console.log(`Documentation check passed: ${zhFiles.length} bilingual Next pages, ${versions.length} stable version(s).`);
