# 决策 L4：`file://` UNC 凭据泄漏 —— 路径守卫层拒绝网络命名空间，保留本地盘符

- **来历**：2026-11-03 只读质量审查报告条目 L4（见 [LaunchGuard.cs](../src/StarMark.Abstractions/LaunchGuard.cs) 档头「另列待决策」那条注释）。
- **决策方**：用户（2026-11-03 定案，由审查会话代为落盘）。
- **状态**：已决策，待实现。本篇是给接手代理的正本。

## 问题

`file://` 指向 UNC 路径时（`\\server\share`、`//server/share`、`file://host/...`），Windows 会**主动发起 SMB 协商并向对方主机出示 NTLM 凭据**——路径本身就触发认证，不需要用户执行任何东西。攻击者只需让一颗带 UNC 的 uri 进库（污染过的备份/快照即可做到），用户一点「在文件夹中显示／打开位置」，凭据哈希就出网了。

## 决策

**在路径守卫层拒绝网络命名空间，保留本地盘符。**

### 拒绝清单

- `\\server\share`（裸 UNC）
- `//server/share`（正斜杠 UNC）
- `file://host/...`（host 非空且非 localhost）
- `\\.\` 设备命名空间
- `\\?\UNC\...`

### 放行清单

- 盘符路径（`C:\...`）
- `file:///C:/...`（host 为空或 localhost）
- `\\?\C:\...`（本地长路径）
- **盘符映射**（如 `Z:\` 指向网络共享）**继续放行**：用户显式映射过、凭据走既有会话，且攻击者需先知道映射关系；未映射的裸 UNC 才是「点一下就泄漏」的入口。

## 理由

误拒代价＝用户改用映射盘（一步操作）；误放代价＝凭据泄漏（不可撤销）。按「默认拒绝、显式路径放行」处理。

## 修复要点（给接手的代理）

- 位置推断在 `LaunchGuard`（[src/StarMark.Abstractions/LaunchGuard.cs](../src/StarMark.Abstractions/LaunchGuard.cs)）；已有 `IsSafeShellSelectTarget` 的「拒 `"` ＋磁盘存在性」双闸形状可照抄（另一调用点在 [ItemCardActions.cs](../src/StarMark.UI/Helpers/ItemCardActions.cs) 的 `IsShellSelectTarget`）。
- 新增一个本地性判定（例如 `IsLocalOnlyPath`），**与既有双闸串联**，不要另起一套路径解析。
- `\\?\` 只在后接盘符时放行（`\\?\C:\` 本地长路径），`\\?\UNC\` 一律拒。
- 落地时顺手把 `LaunchGuard.cs` 档头「UNC 泄漏面另列待决策」的注释改为指向本篇并销账。

## 门测清单（新增证人须覆盖八颗）

| # | 输入 | 预期 |
|---|------|------|
| 1 | `\\server\share\...` | 拒 |
| 2 | `//server/share/...` | 拒 |
| 3 | `file://host/...`（host 非空非 localhost） | 拒 |
| 4 | `\\.\PhysicalDrive0` 等设备命名空间 | 拒 |
| 5 | `\\?\UNC\server\share\...` | 拒 |
| 6 | `C:\...` | 放 |
| 7 | `\\?\C:\...` | 放 |
| 8 | 映射盘符（如 `Z:\...`） | 放 |
