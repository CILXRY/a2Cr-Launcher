# Git 规范

> 硬规矩。和 `CONTRIBUTING.md` 的分工：那边讲"怎么参与"，这边讲"什么算合格"。

## 1. 分支

| 分支 | 用途 |
|---|---|
| `elegant` | 主干，受保护：必须 PR、线性历史、禁止强推 |
| `feat/<短语>` | 新功能 |
| `fix/<短语>` | 修 bug |
| `docs/<短语>` | 只改文档 |
| `chore/<短语>` | 构建、依赖、杂务 |

短语用小写英文或拼音，**不要用中文分支名**（有些工具和 CI 处理不好）。

## 2. 提交信息

Conventional Commits，中文正文：

```text
<type>(<scope>): <一句话说清做了什么>

<可选：为什么这么做、代价是什么>
```

- `type` 只用：`feat` `fix` `docs` `refactor` `test` `chore` `perf` `build` `ci`
- `scope` 可选，写模块名（`download`、`launch`、`instance`、`docs`…）
- **标题不超过 50 字**，说"做了什么"；正文说"为什么"，不要复述 diff
- 一个提交只做一件事。如果标题里出现"顺便"、"以及"，就该拆

反例：`fix: 修了一些问题` — 三个月后没人知道修了什么。

## 3. PR

- 合并方式：**Squash merge**，保持线性历史；PR 标题就是最终的 commit 标题
- 必须：CI 全绿、描述里贴验证结果（命令输出或截图）
- 一个 PR 只做一件事；改二十个文件的 PR 请拆
- 改了契约要在 PR 里说明对应 `13-决策记录.md` 的新增条目

## 4. tag

| 形态 | 含义 | 例子 |
|---|---|---|
| `v1-final` | v1 冻结点（已存在） | — |
| `docs-v2-spec` | 文档冻结：契约可以从这一版开始照着写 | 本次 |
| `v2-mN` | 里程碑完成（M1–M5） | `v2-m1` |
| `v2.x.y` | 正式发布（**由 Release Please 自动打，不要手打**） | `v2.0.0` |

规矩：

- 打 tag 前**工作区必须干净**，CI 必须绿；
- 里程碑 tag 只打**功能** tag，不打在"文档改完"的提交上；
- tag 一旦推上去不改指向，要改就发新 tag。

## 5. 换行与编码

- 仓库内统一 **UTF-8（无 BOM）、LF**；
- Windows 检出时的 CRLF 交给 git 处理（`.gitattributes` 里 `* text=auto`）；
- 不要在提交里混进整文件换行符变更——那会把真正的改动埋在几千行 diff 里。

## 6. 不提交的东西

`bin/` `obj/` `build/` `artifacts/` `.venv/` `*.DotSettings.user` 一律不进库（`.gitignore` 已挡住）。
如果它们出现在 `git status` 里，说明是在别的目录建了项目，或者忽略了规则。

## 7. 大文件

- `tests/fixtures/` 总大小控制在 **5 MB 以内**；
- 不放真实 jar / 整合包，需要时写生成脚本在测试里造；
- 真的超过 5 MB 再考虑 Git LFS，**不要一上来就 LFS**——LFS 会让 clone 变复杂，
  而且对个人项目通常没必要。

## 8. 提交前自检

```powershell
dotnet build          # 警告即错误
dotnet test
git status            # 不该有未跟踪的产物
```

改了文档的话，至少确认所有相对链接有效（`12-验收与回归.md` 里有 CI 闸门）。
