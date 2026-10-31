# Hextech Seekers · 海克斯捉迷藏

当前版本 **0.9.2-four-heroes.1**：保留第一阶段召唤师峡谷与盖伦探索，并新增 Issue 002 单局规则状态机与页面右上角“对局规则预演”。预演可手动推进计时、模拟抓获、检查积分；**现已新增原导航约束的 AI 移动和近战命中合法性逻辑，但尚未连接实际角色渲染、AI 自动作战或原版视野，因此不是完整可玩对局**。

使用来源固定的 Map11 原始地形、贴图和导航数据，以及盖伦原始骨骼动作。固定方向的斜俯视镜头支持缩放、拖动、跟随和回中。角色在下半区沿导航网格绕开岩壁，场景深度可遮挡英雄。

## 本地运行

需要 Node.js 20+，没有 npm 运行依赖：

```sh
git clone https://github.com/xia-ke/Hextech-Seekers.git
cd Hextech-Seekers
npm start
```

打开 `http://localhost:8000`。`dist/` 为完整静态网页，需通过 HTTP 提供，不能直接双击 HTML。新版使用 Canvas2D，不要求 WebGL；素材构建所用的 EGL 不属于网页运行依赖。

## 操作

| 操作 | 输入 |
| --- | --- |
| 移动盖伦 | 鼠标右键；触屏轻点 |
| 停止 | S |
| 拖动镜头 | 鼠标左键拖动 / 触屏拖动 |
| 缩放 | 鼠标滚轮 |
| 跟随镜头开关 | Y 或顶部按钮 |
| 镜头回到盖伦 | 空格或回中按钮 |
| 攻击动作预览，无伤害 | A 或底部按钮 |
| 查看地区，不移动英雄 | 小地图 / 左侧地点按钮 |

默认初始位置在红 Buff 营地。地图上半区可用镜头查看，但不允许角色进入。当前仍没有野怪实体、战争迷雾、四英雄技能、AI、真实抓捕或海克斯施法。规则预演支持计时与模拟结算，但不等于实际战斗对局。

## 规则与当前任务

已确定的独立版规则见 [V1 规则草案 v0.7](docs/implementation/v1-rules.md)，后续实现以它为准，包含 2–4 人、四名唯一英雄、独立视野、一次有效普攻抓捕和八条强化。

当前任务见 [Issue 001](docs/implementation/issue-001.md)，实际验证与限制见 [实施报告](docs/implementation/issue-001-report.md)。本阶段不提前进入完整对局开发。

GitHub `main` 为当前实现的源码入口。旧游戏入口、旧规则、旧测试及未使用素材已移除；需要查看历史版本时使用 Git 提交记录。

## 检查

```sh
npm test
npm run test:visual
```

当前有 4 项投影、真实地图寻路、墙体绕行与素材完整性检查，另增加 7 项 Issue 002 对局规则测试。画面需另行在真实浏览器验证；这些测试不代表 AI、抓捕和计分机制已经实现。

## 文件结构

- `dist/index.html`、`dist/rebuild/`：新版探索入口、界面、实时镜头、深度合成与寻路；`match.js` 为可测试的规则状态机，`round-lab.js` 为规则预演交互。
- `dist/assets/visual/`：随站点提供的场景图块、逐像素深度、动作图集与导航数据。
- `dist/assets/native/`：转换后的盖伦原始网格、骨骼、动画数据及纹理，供素材重建。
- `scripts/convert-native-champion.py`、`scripts/render-visual-assets.py`：资产构建脚本。
- `docs/implementation/`：当前规则、任务、验收记录及资产来源。

## 美术来源与范围

这是 Riot Games 的非官方同人原型。来源提交、文件哈希、许可证副本、素材复现方法及画面差异见 [资产来源记录](docs/implementation/asset-provenance.md)。来源仓库的 MIT 许可证不代表 Riot 美术资产的独立授权。

当前为固定镜头的预渲染素材与实时操控合成，英雄有 16 个朝向；水、草、动态着色器和元素峡谷变化尚未还原。镜头采用本项目设定的 55°斜俯视，未证明与原客户端参数逐项相等。全部运行素材本地提供，无外部字体、CDN 或模型服务依赖。

## 下一阶段状态

[Issue 002 记录](docs/implementation/issue-002.md) 标记 PARTIAL。GitHub 代码已加入规则模块和预演入口；尚未同步体验站点，真实浏览器联调未完成。Issue 001 的最终视觉与性能验收也仍未完成。

## Issue 003 导航与遭遇战逻辑

新增 [Issue 003 实施报告](docs/implementation/issue-003.md)。规则预演界面提供基于原地图导航的 AI 路径移动诊断，只显示逻辑坐标，**尚未实现可视化敌方英雄或可玩的自动追捕**。7 项机制测试覆盖角色合法生成、准备期追捕限制、移动、近战合法性、可见性要求及非全知巡逻。完整游戏需要接入英雄资源、原版式独立视野与真实攻击动画事件；目前的探索攻击仍只是动作预览。

## Issue 004 独立基础视线与 AI 目标发现

新增 [Issue 004 报告](docs/implementation/issue-004.md)。基于原始地图网格和草丛 ID 的独立视线计算已接入 AI 规则诊断：每名角色只依靠自己的观察结果选择追逐或逃离，未看见敌人时只巡逻。新增 8 项视野测试，总计 22 项规则／移动／视线检查已在隔离 JS 执行环境中通过。**仍仅为视线基线，尚无正式战争迷雾与其他英雄模型渲染。**

## Issue 005 — 实时盖伦对局诊断

[实施报告](docs/implementation/issue-005.md)：右上角规则面板可启动“实时演练（盖伦）”，以真实经过时间驱动准备／追捕计时、AI 基础寻路与抓捕、盖伦右键移动和 A 键判定；视野内敌人仅用**开发诊断圈**表示，尚非英雄模型。仅支持盖伦玩家演练（追捕者或躲藏者），仍不构成完整可玩 V1。新增 6 项逻辑测试，合计 28 项规则／移动／视野／实时集成检查在 JS 执行环境通过。尚未完成真实浏览器全页面验收或同步 Sites。

## Issue 006 — 原始三英雄资源与可见实体渲染（PARTIAL）

已实现 [Issue 006 实施报告](docs/implementation/issue-006.md) 所述的原始资源下载、哈希核验、骨骼动画图集生成脚本和视野过滤的多角色 Canvas2D 渲染接入。布里茨、锐雯、亚索的原始模型与动画固定来源已确认，构建命令如下：

```sh
python3 -m pip install -r scripts/visual-requirements.txt
python3 scripts/build-issue006-champions.py --source /tmp/hextech-issue006
```

**三位英雄的真实动作图集和深度图已在 GitHub Actions 中生成并提交到 main；Chromium 已完成四套资源加载和基础视觉冒烟验收。** 运行素材缺失时会提示“原始模型未安装”，不会拿盖伦或几何占位替代。当前实时演练可选择盖伦、布里茨、锐雯、亚索四名英雄，使用各自原始模型与攻击动作；完整仓库 Node 测试 35/35 通过，EGL 构建与 Chromium 自动视觉冒烟验收已完成。无头 Chromium RAF 存在长停顿，性能和原版画风最终验收仍未通过。验收证据见 [Issue 006 浏览器截图](docs/implementation/issue006-visual/chromium-game-preview.jpg) 与 [四英雄图集](docs/implementation/issue006-visual/champion-gallery.png)。GitHub 与体验站点未同步部署。

## Issue 006 四英雄玩家操控与 Chromium 实机复核（0.9.2）

可在右上角“对局规则预演”中配置 2–4 人、阵营和**四名英雄任意一名**，创建对局后进入实时演练。所选英雄可右键移动、A 键触发本人的原始攻击动画；底部英雄名称及模型头像随选择更新。NPC 仍按各自视野显示，已淘汰角色不渲染。不同演员使用独立遮挡缓存，避免图像互相覆盖。

[Issue 006 最新 CI（36/36 Node 测试、Chromium 通过）](https://github.com/xia-ke/Hextech-Seekers/actions/runs/41160403395)。四英雄玩家截图： [盖伦](docs/implementation/issue006-visual/playable-garen.png) · [布里茨](docs/implementation/issue006-visual/playable-blitzcrank.png) · [锐雯](docs/implementation/issue006-visual/playable-riven.png) · [亚索](docs/implementation/issue006-visual/playable-yasuo.png)。[浏览器性能与可见目标数据](docs/implementation/issue006-visual/browser-check.json)。本次无头软件渲染测得 9 FPS、draw p95 2.8ms、RAF p95 266.6ms；仅证明此环境的实测值，**不等于正式游戏流畅度验收已通过**。

当前仍没有英雄主动技能、海克斯施放、原版级战争迷雾和逐帧普攻命中判定。Issue 006 继续为 PARTIAL；Github 主分支更新不代表体验站点已经发布。
