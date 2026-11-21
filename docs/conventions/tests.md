# 测试规范

> `12-验收与回归.md` 说"必须测什么"，这篇说"怎么测"。

## 1. 项目结构

```text
tests/
├─a2Cr.Core.Tests/          ← 单元 + 契约测试
│  ├─Download/
│  ├─Launch/
│  ├─Instance/
│  └─Fixtures/              ← 测试用的固定数据（小）
└─a2Cr.Integration.Tests/   ← 可选：起本地 HTTP 服务器，跑真实网络路径
```

- 测试项目**只能引用 Core**，不许引用 app；
- 测试名用英文（`Download_Resumes_WhenPartExists`），断言消息用中文。

## 2. 三种测试，别混着写

| 类型 | 特征 | 例子 |
|---|---|---|
| **单元** | 纯函数、无 IO、毫秒级 | 规则求值、参数拼装、`inheritsFrom` 合并 |
| **契约** | 读 fixtures，锁住"和 v1 行为一致" | argv 逐字符对比、golden meta |
| **集成** | 起本地服务器，跑真实 HTTP 路径 | 续传 / 416 / 忽略 Range 返回 200 |

一个测试里混两种，失败时你分不清是逻辑错还是环境错。

## 3. 命名

```csharp
public void Download_4xx_DoesNotRetry()          // 方法_场景_期望
public void Merge_ChildOverridesParentLibrary()
```

- 名字里要能看出**被破坏时的后果**，而不是 `Test1` / `TestDownload2`；
- 一个测试只验证一件事；断言多于一个时，它们必须属于同一个结论。

## 4. Fixtures

- 放 `tests/.../Fixtures/`，**总大小控制在 5 MB 以内**（见 `git.md` 第 7 节）；
- 不放真实 jar、不放完整整合包——需要时在测试里用代码生成最小样本；
- 每个 fixture 旁边写一句它是从哪个版本 / 哪个真实包裁剪来的，方便以后换；
- **不要"顺手更新"fixture**：它变了，说明行为变了，先搞清楚为什么再提交。

## 5. 不联网、不依赖时间

- 单元测试**绝不联网**；
- 集成测试用本地 `HttpListener` / 假 handler，端口随机；
- 不用 `Thread.Sleep` 等异步结果，用可控时钟 / `TaskCompletionSource`；
  真需要等待时用"轮询 + 超时上限"，而不是固定 sleep。

## 6. 必须覆盖的边界（按模块）

| 模块 | 边界 |
|---|---|
| 下载 | 空列表、单个文件、同名重复、`.part` 比目标大、416、200 覆盖、取消、4xx 不重试 |
| 合并 | 无 `inheritsFrom`、多层、环、子版本缺字段 |
| 参数 | 未知占位符、缺 `javaVersion`、Forge 新老两套 |
| 实例 | 缺 `instance.json`、损坏、`format` 过高、重名、`../` 逃逸 |
| 设置 | 非法值、未知字段、缺文件、BOM |
| 账号 | 离线 UUID 已知值、令牌提前 5 分钟续期、refresh 失效 |

## 7. 回归用例的来历

**每个修掉的 bug 都要补一条能复现它的用例**，并在用例注释里写清"当时怎么坏的"。

v1 的冒烟测试里有一半是这么来的：`DownloadCancelled` 重新导出、`_meta_define` 把 folder
写成元组、合并顺序——这些都是"改一处、悄悄坏另一处"，没有用例就会反复。

## 8. CI

```powershell
dotnet test --logger "console;verbosity=normal"
```

- 任何一个测试失败即阻断合并；
- 测试耗时要能在日志里看出来：单个测试超过 2 秒就要检查是不是在联网或者 sleep。
