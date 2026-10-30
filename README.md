# Hextech Seekers · 海克斯捉迷藏

当前版本 **0.5.0-round-preview.1**：保留第一阶段召唤师峡谷与盖伦探索，并新增 Issue 002 单局规则状态机与页面右上角“对局规则预演”。预演可手动推进计时、模拟抓获、检查积分；**没有真实 AI 或普攻命中判定，不是完整可玩对局**。

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
