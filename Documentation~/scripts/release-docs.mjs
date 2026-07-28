import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import {fileURLToPath} from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const target = (process.argv[2] ?? '').trim().replace(/^v/, '');
const semver = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
if (!semver.test(target)) {
  console.error('Usage: npm run docs:release -- 1.1.0');
  process.exit(1);
}

const frameworkPackagePath = path.resolve(root, '../package.json');
const frameworkPackage = JSON.parse(fs.readFileSync(frameworkPackagePath, 'utf8'));
if (frameworkPackage.version !== target) {
  console.error(`Framework package version is ${frameworkPackage.version}, expected ${target}. Update package.json first.`);
  process.exit(1);
}

const versionsPath = path.join(root, 'versions.json');
const versions = JSON.parse(fs.readFileSync(versionsPath, 'utf8'));
if (versions.includes(target)) {
  console.error(`Documentation version ${target} already exists; refusing to overwrite it.`);
  process.exit(1);
}

const markdownFiles = (directory) => {
  const output = [];
  const visit = (current) => {
    for (const entry of fs.readdirSync(current, {withFileTypes: true})) {
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) visit(full);
      else if (entry.name.endsWith('.md') || entry.name.endsWith('.mdx'))
        output.push(path.relative(directory, full).replaceAll('\\', '/'));
    }
  };
  visit(directory);
  return output.sort();
};

const zhSource = path.join(root, 'docs');
const enSource = path.join(root, 'i18n/en/docusaurus-plugin-content-docs/current');
if (JSON.stringify(markdownFiles(zhSource)) !== JSON.stringify(markdownFiles(enSource))) {
  console.error('Chinese and English Next pages do not match. Run npm run check and add the missing translation.');
  process.exit(1);
}

const copies = [
  [path.join(root, 'docs'), path.join(root, `versioned_docs/version-${target}`)],
  [
    path.join(root, 'i18n/en/docusaurus-plugin-content-docs/current'),
    path.join(root, `i18n/en/docusaurus-plugin-content-docs/version-${target}`),
  ],
];
for (const [source, destination] of copies) {
  if (fs.existsSync(destination)) {
    console.error(`Destination already exists: ${destination}`);
    process.exit(1);
  }
  fs.cpSync(source, destination, {recursive: true, errorOnExist: true});
}

const versionedSidebar = path.join(root, `versioned_sidebars/version-${target}-sidebars.json`);
fs.mkdirSync(path.dirname(versionedSidebar), {recursive: true});
fs.copyFileSync(path.join(root, 'sidebars.json'), versionedSidebar);

versions.unshift(target);
fs.writeFileSync(versionsPath, `${JSON.stringify(versions, null, 2)}\n`);

const docsPackagePath = path.join(root, 'package.json');
const docsPackage = JSON.parse(fs.readFileSync(docsPackagePath, 'utf8'));
docsPackage.version = target;
fs.writeFileSync(docsPackagePath, `${JSON.stringify(docsPackage, null, 2)}\n`);

const packageLockPath = path.join(root, 'package-lock.json');
const packageLock = JSON.parse(fs.readFileSync(packageLockPath, 'utf8'));
packageLock.version = target;
if (packageLock.packages?.['']) packageLock.packages[''].version = target;
fs.writeFileSync(packageLockPath, `${JSON.stringify(packageLock, null, 2)}\n`);

console.log(`Created Chinese and English documentation snapshots for ${target}.`);
