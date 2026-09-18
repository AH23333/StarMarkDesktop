# DeskBox 组件细节功能对比矩阵

> 目标：把 DeskBox（参考仓 `DeskBox-ref`）桌面组件层的**细节能力**逐条摊开，逐条核对 StarMarkDesktop 的实现现状，
> 明确「已实现 / 部分实现 / 未实现（有意）」，作为后续补齐的输入。
> 统计口径截至本次提交；每条都标注了 StarMark 侧的落点文件，便于回查。

图例：**✅ 已实现** ｜ **🟡 部分实现**（主路径有，细节缺） ｜ **⬜ 未实现** ｜ **🚫 有意不做**（附理由）

---

## 一、组件外壳 / 窗口层

| DeskBox 能力 | StarMark | 落点 | 说明 |
| --- | --- | --- | --- |
| 三种外壳模式（常规 / 紧凑胶囊 / 隐藏） | ✅ | `WidgetWindow.xaml.cs`（`WidgetChromeMode`） | 与 DeskBox 语义一致 |
| 胶囊稳定停靠位（悬停预览/收起/重启不漂移） | ✅ | `WidgetWindow.xaml.cs` `_capsuleRect` | 曾因每次重算堆叠导致同列胶囊被推出屏幕，改为首次计算并持久化（`CapsuleX/Y`） |
| 展开位与胶囊位分离保存 | ✅ | `_expandedRect` | 避免 Compact 态把展开位写成胶囊位，展开后跳到屏幕边缘 |
| 悬停预览（peek） | ✅ | `_peeking` | DeskBox `QuickLook` 同源思路 |
| 边缘缩放 grip（8 向） | ✅ | `WidgetWindow` 根 `Grid` grip | 注意 WinUI 3 里 `Border/Grid/Canvas` 均 sealed，需用 `Panel` 派生 |
| 缩放/拖动边缘磁吸对齐 + 总开关 | ✅ | `WidgetSnapCalculator` + `SettingsStore.WidgetSnapEnabled` | |
| 每显示器拓扑布局（DIP 相对 + 越界回收） | ✅ | `WidgetLayout` | 换分辨率/拔显示器后自动回到可见区 |
| 空闲期 Z 序策略（靠下组件保持在上） | ✅ | `WidgetZOrderPolicy`（移植自 `IdleWidgetZOrderPolicy`） | 纯函数，可单测 |
| 层级策略（置顶 / 取消置顶 / 沉底） | ✅ | `WidgetLayerService` | 取代二元 Topmost |
| 布局预设：保存 / 应用 / 删除 / 快捷键切换 | ✅ | `WidgetStorage` + 设置页 | |
| 多实例重复添加 + 按实例构造 | ✅ | `WidgetManager` / `WidgetWindow` | 同一品类可开多个 |
| 组件格子重命名（F2 / 双击标题） | ✅ | `RenameAsync()` | |
| Ctrl+拖动 = 同屏组件协同移动 | ✅ | `DragBar_PointerMoved` 同伴偏移 | 本次补齐，对齐 `WidgetManager.CoordinatedMove` |
| 键盘操作：Esc 收起 / Enter·Space 展开胶囊 / F2 重命名 | ✅ | `OnKeyDown` | 本次补齐 |
| 标题栏层级（grip 必须压在标题栏之上，按钮再压 grip） | ✅ | `Canvas.ZIndex` 分层 | 见踩坑记录：提升的是按钮本身而非其父 DragBar |
| 组件分组（多组件合并成一组 + 标题栏标签切换） | ⬜ | — | DeskBox `widget-group-navigation-ux` 是它的核心卖点之一；StarMark 走「多实例独立 + 布局预设」路线，暂不合并为组 |
| 拖放引导线浮层（ResizeGuideOverlay） | ⬜ | — | 引导线对磁吸的增益有限，优先做隐形网格吸附 |
| Windows 10 降级动效（motion contract） | ⬜ | — | 目标平台为 Win10 19041+ 最新 WinUI，暂不额外降级 |

---

## 二、外观 / 材质

| DeskBox 能力 | StarMark | 落点 | 说明 |
| --- | --- | --- | --- |
| 六种材质（亚克力 / 云母 / 不透明 / MicaAlt / AcrylicBase / 纯色） | ✅ | `WidgetBackdropKind` + `WidgetAppearance` | 纯色为 DeskBox 移植项 |
| 背景不透明度调节（实时生效、全界面区域） | ✅ | `SettingsStore.WidgetOpacity` | 曾出现仅在部分区域生效，已统一走 `SurfaceBrush` |
| 材质浓度（material intensity） | ✅ | `WidgetMaterialVisualCalculator` | |
| 每组件独立外观（圆角 / 文本缩放 / 前景色） | ✅ | `WidgetAppearance` + 外观浮层 | 文本缩放改 `TextBlock.FontSize`，不用 RenderTransform（后者会把文本裁掉） |
| 「恢复全局」回滚到初始加载态文本大小 | ✅ | `ApplyAppearanceCore` | 需缓存「初始加载态」基准字号（`ConditionalWeakTable`） |
| 运行期主题跟随（材质/画笔随元素主题重解析） | ✅ | `ThemeBrush.For` / `ThemeManager` | `Application.Current.Resources` 首个窗口后冻结，不能直接用 |
| 外观编辑器：实时预览 + 取消确认 + 单例守护 | ✅ | 外观浮层 | |

---

## 三、设置与持久化

| DeskBox 能力 | StarMark | 落点 | 说明 |
| --- | --- | --- | --- |
| 组件数据持久化（位置/尺寸/模式/外观） | ✅ | `WidgetStorage` | 含每实例 `ChromeMode`、`CapsuleX/Y` |
| 设置立即生效（无保存按钮） | ✅ | `SettingsPage` | |
| 全局快捷键 + 冲突检测 + 多级分类 | ✅ | `HotkeyService`、`KeyboardHookService`、设置页 | 录制必须把 `KeyDown` 挂到被点击按钮本身，`Page.KeyDown` 不可靠 |
| 快捷键动作含 toggle 类（折叠/展开/显示隐藏） | ✅ | 快捷键动作表 | |
| 性能模式 / 内存门禁 | ✅ | `MemoryReclaimer`、`CacheBudgetMb` | |
| 诊断信息导出 | ✅ | 设置页诊断区块 | |
| 组件特性反射注册（Registry 自动发现） | 🟡 | `WidgetContentFactory` 为**单点注册**（枚举 + 描述符 + 工厂三处） | 尚未做反射自动注册；当前三处单点写法更利于编译期检查 |

---

## 四、内容组件层

### 4.1 品类覆盖

| DeskBox 内容 | StarMark 对应 | 状态 |
| --- | --- | --- |
| FileSurface（文件/快捷方式浮层） | 快捷启动 `QuickLaunch` | ✅ |
| Todo | 待办 `Todo` | ✅（本轮增强） |
| QuickCapture | 随记 `QuickNote` | ✅ |
| Search | 快捷搜索 `Search` | ✅ |
| Glance | 今日速览 `Glance`（含农历） | ✅ |
| Weather | 天气 `Weather` | ✅（本轮增强） |
| Music | 音乐 `Music` | ✅（本轮增强） |
| — | 时钟 `Clock` | ✅ StarMark 多出 |
| — | 标签格 / 搜索结果格 / 最近活动格 / 置顶条目格 | ✅ StarMark 多出（书签主业衍生，DeskBox 无对应） |

### 4.2 待办（Todo）

| 细节 | 状态 | 说明 |
| --- | --- | --- |
| 增删改 / 勾选完成 | ✅ | |
| 分段筛选（全部/未完成/已完成）+ 计数徽标 | ✅ | 本轮补齐 |
| 优先级颜色标记 | ✅ | 本轮补齐（`LocalItemState.SetColor`） |
| 截止日期 | ✅ | 本轮补齐（unix 秒，旧数据零值迁移） |
| 删除后行内撤销条 | ✅ | 本轮补齐 |
| 已完成项折叠 | ✅ | 本轮补齐 |
| 数据落到仓库而非本地 UI 集合 | ✅ | `ItemRepository` + `local` 源 |
| 系统通知提醒 / 重复日程 | ⬜ | DeskBox 有 `todo-notification` + `recurrence-reminder`；StarMark 尚未引入通知管线，属**下一阶段**（需常驻通知 + 权限，收益/成本比待评估） |
| 拖拽排序 | ✅ | 本轮补齐：`ListView` 的 `CanReorderItems` 直接调序 `ObservableCollection`，落盘写 `extra_json.order`；**仅「全部」筛选下开放**（子集顺序无法无歧义地映射回全量） |

### 4.3 天气（Weather）

| 细节 | 状态 | 说明 |
| --- | --- | --- |
| 实况（温度/描述/图标/体感/湿度/风） | ✅ | Open-Meteo 单链路（DeskBox 默认 MSN Weather + 内嵌私有 key，StarMark **有意不照搬**第三方凭证） |
| 城市选择（地理编码 + 持久化） | ✅ | 中文可直接查 |
| 多日概览（未来三天） | ✅ | |
| 温度单位 °C / °F（风速同步 km/h ↔ mph） | ✅ | 本轮补齐（`WeatherUnits`） |
| 今日逐时视图 | ✅ | 本轮补齐（新增 hourly 字段解析） |
| 共享客户端与缓存（多实例只打一次接口） | ✅ | 静态 client + `SemaphoreSlim` 合并并发 |
| 附加指标：降水概率 / UV / 气压 / 日出日落 | ✅ | 本轮补齐：新增 `precipitation_probability_max` / `uv_index_max` / `surface_pressure` / `sunrise` / `sunset` 字段解析 |
| 按尺寸分级的指标显示阈值 | ✅ | 本轮补齐：`WeatherLayoutMath` 三档（Mini/Compact/Expanded）**带滞回**，阈值沿用 DeskBox `DetermineLayoutMode` |

### 4.4 音乐（Music）

| 细节 | 状态 | 说明 |
| --- | --- | --- |
| SMTC 读取曲目/艺术家/专辑 | ✅ | 不对接具体播放器 |
| 播放 / 暂停 / 上下曲 + 能力位禁用按钮 | ✅ | |
| 进度条 + 时间（1 秒一跳，事件驱动非轮询） | ✅ | 无会话时停表避免空转 |
| 多会话音源选择（跟随系统 / 指定播放器） | ✅ | 本轮补齐，对齐 DeskBox `MusicSessionService.GetSessionOptions` |
| 友好音源名（AUMID → QQ音乐 / Spotify…）+ 同名序号 | ✅ | 本轮补齐 |
| 音量条（CoreAudio 原生后端） | ⬜ | 路线图定为 v2（需要 Rust/C++ 原生后端 ABI，成本显著高于收益） |
| 进度拖拽定位（seek） | ✅ | 本轮补齐：外层 `SeekHost` 接管指针（3px 的条点不中），拖动期间关掉刷新闸门避免被拽回，松手才提交 |
| 随机 / 循环模式切换 | ✅ | 本轮补齐：SMTC 只有「随机开关 + 重复模式」两个独立开关，折叠成三态循环（普通→随机→列表循环），缺能力就跳过该态 |
| 专辑封面缩略图 | ⬜ | `MediaProperties.Thumbnail` 可取，等待 UI 版式确定 |

### 4.5 快捷启动 / 随记 / 速览 / 搜索

| 细节 | 状态 | 说明 |
| --- | --- | --- |
| 快捷启动复用主界面条目卡片 | ✅ | `ItemCard` + `IsLauncherMode` |
| 拖入文件/文件夹入库 | ✅ | 曾修复「无法打开」 |
| 溢出时滚动而非被标题栏遮挡 | ✅ | |
| 随记即时落库（local 源） | ✅ | |
| 今日速览含农历日期 | ✅ | `GlanceCalendar`（`ChineseLunisolarCalendar`） |
| 组件内快捷搜索（走主检索链路） | ✅ | |

---

## 五、有意不照搬的部分（附理由）

1. **MSN Weather 私有 API key**：DeskBox 把第三方凭证硬编码进客户端；StarMark 只保留 Open-Meteo（免费、无需 key）。
2. **Rust 原生音量后端**：引入原生 ABI 会显著增加构建与排障成本，当前播放控制的收益/成本比远高。
3. **组件分组（group navigation）**：DeskBox 的重交互范式；StarMark 走「多实例 + 布局预设」，更贴合书签工具的心智。
4. **组件特性反射注册**：编译期三处单点注册可让「忘记接线」在编译期暴露，优于运行时反射失败。

---

## 六、下一步建议（按性价比排序）

> 前三（音乐 seek + 模式 / 天气分级指标 / 待办拖拽排序）已在**第二十八轮**全部落地，见 §4.2–4.4。

1. **待办到期提醒**：需要先建通知管线（托盘 + Toast + 去重），体量最大。
2. **音乐专辑封面缩略图**：`MediaProperties.Thumbnail` 可取，等待 UI 版式确定。
3. **组件分组导航**：DeskBox 核心卖点，但 StarMark 走多实例路线，是否引入需产品决策。
4. **拖放引导线浮层**：对磁吸增益有限，优先级最低。
