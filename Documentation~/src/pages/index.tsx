import React from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Translate from '@docusaurus/Translate';
import styles from './index.module.css';

const features = [
  {
    icon: '01',
    titleId: 'home.install.title',
    title: '一个 UPM 包',
    bodyId: 'home.install.body',
    body: '导入 com.wjq.qhy-framework，依赖和项目骨架自动准备。',
  },
  {
    icon: '02',
    titleId: 'home.hotupdate.title',
    title: '代码与资源热更新',
    bodyId: 'home.hotupdate.body',
    body: 'QFramework 调用方式保持不变，资源统一交给 YooAsset。',
  },
  {
    icon: '03',
    titleId: 'home.client.title',
    title: '客户端整包升级',
    bodyId: 'home.client.body',
    body: 'AOT 变化时，Windows ZIP 与 Android APK 也能通过 Boot 升级。',
  },
];

export default function Home(): React.ReactNode {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout title={siteConfig.title} description={siteConfig.tagline}>
      <main>
        <header className={styles.hero}>
          <div className="container">
            <div className={styles.badge}>Unity 2022.3 LTS · QHY Framework v1.0.1</div>
            <Heading as="h1" className={styles.title}>
              <Translate id="home.hero.title">把三套框架，变成一条完整工作流</Translate>
            </Heading>
            <p className={styles.subtitle}>
              <Translate id="home.hero.subtitle">
                安装一个 UPM 包，即可获得 QFramework、HybridCLR、YooAsset 的启动、开发、热更新、发布与客户端升级能力。
              </Translate>
            </p>
            <div className={styles.actions}>
              <Link className="button button--primary button--lg" to="/docs/getting-started/installation">
                <Translate id="home.action.install">安装框架</Translate>
              </Link>
              <Link className="button button--secondary button--lg" to="/docs/getting-started/quick-start">
                <Translate id="home.action.quick">十分钟入门</Translate>
              </Link>
              <Link className={styles.textLink} to="/docs/release/choose-build">
                <Translate id="home.action.release">发布热更新 →</Translate>
              </Link>
            </div>
          </div>
        </header>
        <section className={styles.featureSection}>
          <div className={clsx('container', styles.featureGrid)}>
            {features.map((feature) => (
              <article className={styles.featureCard} key={feature.icon}>
                <span className={styles.featureIcon}>{feature.icon}</span>
                <Heading as="h2">
                  <Translate id={feature.titleId}>{feature.title}</Translate>
                </Heading>
                <p><Translate id={feature.bodyId}>{feature.body}</Translate></p>
              </article>
            ))}
          </div>
        </section>
        <section className={styles.flowSection}>
          <div className="container">
            <span className={styles.eyebrow}>WORKFLOW</span>
            <Heading as="h2">
              <Translate id="home.flow.title">从第一次运行，到可交付更新</Translate>
            </Heading>
            <div className={styles.flow}>
              {['导入 UPM', '编辑 Main', '热更新构建', '上传 CDN', 'Boot 自动更新'].map((item, index) => (
                <React.Fragment key={item}>
                  <div className={styles.flowItem}>
                    <span>{index + 1}</span>
                    <Translate id={`home.flow.${index}`}>{item}</Translate>
                  </div>
                  {index < 4 && <div className={styles.arrow}>→</div>}
                </React.Fragment>
              ))}
            </div>
          </div>
        </section>
      </main>
    </Layout>
  );
}
