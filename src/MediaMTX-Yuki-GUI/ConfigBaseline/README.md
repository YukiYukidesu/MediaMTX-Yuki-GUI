# 配置基准（ConfigBaseline）

每个**配置结构发生变化的版本**在这里留一份 `app.json`，供以后写 `ConfigMigrator` 迁移段时对照。
没有这份东西，以后就只能看着 `Models\AppConfig.cs` 的当前样子猜历史结构，写迁移段必然抓瞎。

## 文件

| 文件 | 版本 | configVersion | 说明 |
|---|---|---|---|
| `app-0.0.5.json` | 0.0.5 | 5 | 第一个留档版本，配置结构自此基线起算 |

## 什么时候新增快照

- **真的改了** `AppConfig` 的持久化字段就加：增字段、删字段、改名、改类型、改语义（默认值变了但含义不变不算）。
- 只是版本号上涨、结构没动就**不加** —— 迁移段区也保持为空，靠升级链链尾把 `configVersion` 同步到新版本。
- 加的时候：发版前把程序真实生成的 `bin\Debug\net10.0\config\app.json` 复制进来，命名 `app-<版本>.json`。

## 约定

- 快照是**只读历史**：不要回头修改它，它代表那个版本真实写出来的样子。
- 快照里的值是开发机上的测试数据（13 个输入源、39 个输出目标，主机名多为 `live-push.example.com`）。
  这是刻意的：真实配置能看出各字段实际怎么填，比一份全是默认值的样例有用。
- 与 `docs\` 不同，这个目录**会进版本库**（`docs\` 在 `.gitignore` 里），所以留档要放在这里。

## 相关

- 结构定义：`Models\AppConfig.cs`
- 升级链：`Services\ConfigMigrator.cs`，设计说明见 `docs\交接文档.md` §4.1
- 版本编码：`Services\ProgramVersion.cs`，`Code = major * 1_000_000 + minor * 1_000 + patch`