import type {Config} from '@docusaurus/types';
import type {Options, ThemeConfig} from '@docusaurus/preset-classic';
import {themes as prismThemes} from 'prism-react-renderer';
import fs from 'node:fs';

const stableVersions = JSON.parse(
  fs.readFileSync(new URL('./versions.json', import.meta.url), 'utf8'),
) as string[];
const latestStableVersion = stableVersions[0];
if (!latestStableVersion) {
  throw new Error('versions.json must contain at least one stable documentation version.');
}
const docsVersions = Object.fromEntries(
  stableVersions.map((version, index) => [
    version,
    {label: `v${version}`, path: index === 0 ? '' : version},
  ]),
);

const config: Config = {
  title: 'QHY Framework',
  tagline: 'QFramework × HybridCLR × YooAsset production-ready Unity workflow',
  favicon: 'img/favicon.svg',
  url: 'https://wujianqin0000.github.io',
  baseUrl: '/QHYFramework/',
  organizationName: 'wujianqin0000',
  projectName: 'QHYFramework',
  trailingSlash: false,
  onBrokenLinks: 'throw',
  markdown: {
    mermaid: true,
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },
  themes: ['@docusaurus/theme-mermaid'],
  i18n: {
    defaultLocale: 'zh-Hans',
    locales: ['zh-Hans', 'en'],
    localeConfigs: {
      'zh-Hans': {label: '简体中文', htmlLang: 'zh-CN'},
      en: {label: 'English', htmlLang: 'en-US'},
    },
  },
  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          routeBasePath: 'docs',
          includeCurrentVersion: true,
          lastVersion: latestStableVersion,
          versions: {
            current: {label: 'Next', path: 'next'},
            ...docsVersions,
          },
          // Keep local/UPM source builds independent from Git metadata. GitHub
          // still exposes the complete history through the edit link.
          showLastUpdateAuthor: false,
          showLastUpdateTime: false,
          editUrl: 'https://github.com/wujianqin0000/QHYFramework/edit/main/Documentation~',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Options,
    ],
  ],
  plugins: [
    [
      require.resolve('@easyops-cn/docusaurus-search-local'),
      {
        hashed: true,
        language: ['en', 'zh'],
        docsRouteBasePath: '/docs',
        indexBlog: false,
        highlightSearchTermsOnTargetPage: true,
        explicitSearchResultPath: true,
      },
    ],
  ],
  themeConfig: {
    image: 'img/social-card.svg',
    colorMode: {
      defaultMode: 'dark',
      disableSwitch: false,
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'QHY Framework',
      logo: {alt: 'QHY Framework Logo', src: 'img/logo.svg'},
      items: [
        {type: 'docSidebar', sidebarId: 'tutorialSidebar', position: 'left', label: '文档'},
        {to: '/docs/getting-started/quick-start', label: '十分钟入门', position: 'left'},
        {to: '/docs/release/choose-build', label: '发布', position: 'left'},
        {type: 'docsVersionDropdown', position: 'right'},
        {type: 'localeDropdown', position: 'right'},
        {
          href: 'https://github.com/wujianqin0000/QHYFramework',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: '开始',
          items: [
            {label: '安装', to: '/docs/getting-started/installation'},
            {label: '十分钟入门', to: '/docs/getting-started/quick-start'},
            {label: '常见问题', to: '/docs/help/faq'},
          ],
        },
        {
          title: '参考',
          items: [
            {label: 'API', to: '/docs/reference/runtime-api'},
            {label: '配置字段', to: '/docs/reference/settings'},
            {label: '术语表', to: '/docs/reference/glossary'},
          ],
        },
        {
          title: '项目',
          items: [
            {label: 'GitHub', href: 'https://github.com/wujianqin0000/QHYFramework'},
            {label: '文档维护', to: '/docs/contributing/docs-maintenance'},
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} QHY Framework`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  } satisfies ThemeConfig,
};

export default config;
