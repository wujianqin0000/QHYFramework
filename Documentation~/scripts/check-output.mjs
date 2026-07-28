import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import {fileURLToPath} from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const versions = JSON.parse(fs.readFileSync(path.join(root, 'versions.json'), 'utf8'));
const required = [
  'build/index.html',
  'build/docs/intro.html',
  'build/docs/next/intro.html',
  'build/search-index.json',
  'build/en/index.html',
  'build/en/docs/intro.html',
  'build/en/docs/next/intro.html',
  'build/en/search-index.json',
];

for (const version of versions.slice(1)) {
  required.push(`build/docs/${version}/intro.html`);
  required.push(`build/en/docs/${version}/intro.html`);
}

for (const relative of required) {
  if (!fs.existsSync(path.join(root, relative))) {
    console.error(`Built output is missing: ${relative}`);
    process.exitCode = 1;
  }
}

const englishHome = fs.readFileSync(path.join(root, 'build/en/index.html'), 'utf8');
const untranslatedUi = [
  '一套可发布的 Unity 热更新工作流',
  '<div class="footer__title">开始</div>',
  '<div class="footer__title">参考</div>',
  '<div class="footer__title">项目</div>',
  '>十分钟入门<',
  '>文档维护<',
];
for (const text of untranslatedUi) {
  if (englishHome.includes(text)) {
    console.error(`English home contains untranslated UI: ${text}`);
    process.exitCode = 1;
  }
}

if (!englishHome.includes('/QHYFramework/en/assets/')) {
  console.error('English assets do not use the GitHub Pages project base path.');
  process.exitCode = 1;
}

if (!process.exitCode)
  console.log('Built output check passed: stable/Next, zh/en, search, translations, and base paths.');
