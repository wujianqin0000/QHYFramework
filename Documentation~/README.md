# QHY Framework Documentation

这里是 QHY Framework 的可离线阅读文档源文件，也是
[在线文档站](https://wujianqin0000.github.io/QHYFramework/) 的源码。

- 中文 Next：[`docs/intro.md`](docs/intro.md)
- English Next: [`i18n/en/docusaurus-plugin-content-docs/current/intro.md`](i18n/en/docusaurus-plugin-content-docs/current/intro.md)
- 稳定版列表：[`versions.json`](versions.json)
- 文档维护：[`docs/contributing/docs-maintenance.md`](docs/contributing/docs-maintenance.md)

## 本地预览

需要 Node.js 22 和 npm：

```bash
npm ci
npm run start
```

生产构建与完整检查：

```bash
npm run check
npm run build
```

使用框架开发工程的 UPM 发布器时，主版本、次版本或修订版本发布会自动更新框架与
文档版本，并生成中英文稳定快照，无需手动执行下面的命令。

仅单独维护文档版本时，先把框架根目录 `package.json` 的版本更新为目标版本，再运行：

```bash
npm run docs:release -- 1.1.0
```

生成的 `node_modules`、`build`、`.docusaurus` 和缓存目录不会进入 UPM 发布结果。
