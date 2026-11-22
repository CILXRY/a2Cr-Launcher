# 代码规范

> 硬规矩。目标只有一个：**让 Core 可以被测试，让分层不被绕过。**

## 1. 项目与引用方向

```text
a2Cr.app (Avalonia) ─┐
                     ├─→ a2Cr.Core
a2Cr.Cli (控制台)   ─┘
tests/a2Cr.Core.Tests ──→ a2Cr.Core
```

- **Core 不许引用 app / Cli**，也不许引用任何 UI 库。
- **app 和 Cli 之间不许互相引用**，一切共享能力都下沉到 Core。
- 新增项目前先问：它是不是 Core 的一个目录就够了。

## 2. 命名

| 对象 | 规则 | 例子 |
|---|---|---|
| 项目 / 程序集 | 按 `01-名称定义.md`，保持 `a2Cr` 的大小写 | `a2Cr.Core` |
| 命名空间 | C# 惯例的 PascalCase | `A2Cr.Core.Download` |
| 类型 / 方法 / 属性 | PascalCase | `DownloadAsync` |
| 局部变量 / 参数 | camelCase | `targetPath` |
| 私有字段 | `_camelCase` | `_httpClient` |
| 接口 | `I` 前缀 | `IFileStore` |
| 异步方法 | `Async` 后缀 | `InstallAsync` |

> `【待确认】` 命名空间用 `A2Cr`（C# 惯例）还是 `a2Cr`（和程序集完全一致）？
> C# 里命名空间 PascalCase 是硬惯例，且不面向用户；本文按 `A2Cr` 写。
> 如果你要严格一致，就把这条改成 `a2Cr`，一次性全局替换即可。

## 3. 可空与警告

- `<Nullable>enable</Nullable>`，`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`；
- 不用 `!`（null-forgiving）掩盖问题，除非紧跟着一行注释说明为什么安全；
- 对外 API 的引用类型参数，不允许"可能为 null 但没标注"。

## 4. 分层纪律

| 层 | 可以做 | 绝对不许 |
|---|---|---|
| Core | 纯计算、调 `IFileStore`、抛结构化异常 | `Console.Write*`、直接写游戏目录文件、读 `Environment.GetFolderPath` |
| Cli | 解析参数、打印、决定退出码 | 自己实现业务逻辑 |
| app | 组控件、调度后台任务 | 直接碰文件系统（走 Core） |

路径统一从 `LauncherPaths` 取；时间、随机数、网络都通过接口注入，方便测试。

## 5. 异步

- 所有 IO 用 `async`，禁止 `.Result` / `.Wait()` / `GetAwaiter().GetResult()`；
- 禁止 `async void`（事件处理器除外，且必须自己 try/catch）；
- 传递 `CancellationToken`，一路传到最底层；
- 不要在 `Task.Run` 里包同步 IO 当作"异步化"——那是把线程池当挡箭牌。

## 6. 异常

- 只抛 `LauncherException` 体系（见 `11-错误与日志.md`），每条带 `Code`；
- **不许吞异常**：`catch { }` 只有在"明确降级且记一条日志"时才允许；
- 取消用 `OperationCanceledException`，永远不包成业务异常。

## 7. 文件与目录

- 一个类型一个文件，文件名 = 类型名；
- 目录结构 = 命名空间结构；
- 单个文件超过 ~400 行就该想想是不是塞了两件事；
- 分部类（`partial`）只在生成代码或 UI 代码里用。

## 8. 依赖

- 引入新的 NuGet 包要在 PR 里说明：干什么用、能不能自己做、体积多大
  （发布产物 ≤ 60 MB / ≤ 20 个文件，见 `12-验收与回归.md`）；
- 已经确定的：`Downloader`（传输层）、`System.CommandLine`（CLI）、Avalonia。
- **不要为了一个工具函数引包**。

## 9. 注释

- 注释解释**为什么**，不复述代码；
- 面向用户的文案、注释、提交信息一律中文；
- 踩过的坑要写成注释（例如"Bootstraplauncher 不能加 client.jar"），
  并附一句原因——那是给三个月后的自己看的。

## 10. 提交前

```powershell
dotnet build      # 0 warnings
dotnet test
```

未跑过的不提 PR。
