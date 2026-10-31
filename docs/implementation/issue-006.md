# Issue 006 — 四英雄原始素材流水线与多角色渲染

日期：2026-10-31。状态：**PARTIAL**，未通过“全部四英雄正式模型已在游戏中显示”的验收。

## 这次实际完成

1. **固定原始素材来源与核验**：已在 `tengge1/lol-model-viewer` 固定提交 `ef5635c5fbe0c315275c9bcccf83558cfe7ee436` 的目录中核对布里茨 `53_0`、锐雯 `92_0`、亚索 `157_0` 的 `.lmesh` / `.lanim` 和三张原始贴图确实存在。 `scripts/fetch-visual-sources.py --issue006` 下载这些来源文件，并与核对到的 **9 个 Git blob SHA-1** 对照；构建缓存还会记录每个文件 SHA-256。
2. **真实动画生成**：`scripts/render-visual-assets.py --hero` 现在可使用同一 EGL + 原始骨骼路径渲染不同英雄。使用可用的原始待机／移动动作，且必须有原始 `attack1`；若缺少必要动作即明确失败，不复制其他英雄动作或合成假素材。
3. **一键构建入口**：`scripts/build-issue006-champions.py` 串联下载、转换、离线渲染和资源完整性校验。输出固定目录 `dist/assets/visual/{blitzcrank,riven,yasuo}/`，包含 `sprite.json`、原始动作颜色图集与对应深度图。
4. **网页渲染集成**：新建 `dist/rebuild/champion-renderer.js`，以每位英雄自己的图集、朝向与动画帧为基础，按照原始地图导航高度与逐像素地形深度遮挡渲染。仅绘制**玩家本人的独立视野中**仍存活的敌方角色；被抓者、视野外角色不可见。
5. **真实资源与失败态区分**：`app.js` 接入三英雄图集动态加载。资源未构建／加载失败时保留带“原始模型未安装”文字的开发诊断标记，并在 `round-lab.js` 显示缺失的英雄。**诊断标记不属于正式画面，也不冒充真实英雄。**
6. 新增 `tests/champion-renderer.test.mjs` 3 项检查：四英雄资源目录全局唯一、隐藏／淘汰角色不进入渲染名单、缺少原始素材时拒绝生成伪模型。

## 复现命令

需要可访问 GitHub 原始文件的网络、Python 和 EGL/OpenGL 3.3（与 Issue 001 资产构建相同）：

```sh
python3 -m pip install -r scripts/visual-requirements.txt
python3 scripts/build-issue006-champions.py --source /tmp/hextech-issue006
node --test
npm start
```

资产构建完成后，检查 `dist/assets/visual/{blitzcrank,riven,yasuo}/sprite.json` 的 clips 与资源文件，进入游戏右上角“对局规则预演 → 实时演练（盖伦）”，观察真实角色、深度遮挡及隐藏效果。

## 执行证据和限制

- 从 GitHub 回读本轮源码与测试，在 JavaScript 环境重跑现有规则、实时、视线及新增渲染边界测试：**31/31 PASS**；`app.js`、`round-lab.js` 语法解析通过。上述不是完整 Chromium 游戏的视觉验收。
- 本回合运行环境无法解析 `github.com`，因此无法从固定来源下载九个二进制原始文件、执行 EGL 素材生成、生成截图或验证四英雄在浏览器实际显示。**本次 GitHub 仅提交构建代码和渲染代码，没有提交三个英雄的实际图集二进制。**
- 角色渲染目前是 Garen 玩家 + 其视野内其他英雄；AI 角色可见时若素材已安装则显示自己的动作图集。首版尚未支持玩家选择其他英雄进行实时操作，仍未真正完成四英雄的可玩能力。
- 源动作名和姿势比例要在真实文件转换／渲染中确认；若 `attack1` 缺失，脚本会显式失败，需核对同一英雄的其他合法攻击动作后更新映射，不要伪造动作。
- 视野仍为基于原始 walkable/bush ID 的基础近似；尚无完整原版战争迷雾、草丛精确遮挡。主场景渲染并不等于所有《英雄联盟》视觉规则验收。
- GitHub 已提交，Sites 体验站点未发布。Issue 001–006 均仍有待验证部分。

## 继续开发优先级

在具备可用 EGL 和资源下载的环境执行构建，上传实际三英雄动画图集到 GitHub；真实 Chromium 对比盖伦与新增英雄的身高比例、方向、移动、待机、攻击与遮挡，然后接入其他英雄玩家操控和攻击命中帧。之后继续原版视野、泉水与完整对局。

## 2026-10-31 实际执行补充：原始图集和 Chromium 自动视觉验收已完成

- GitHub Actions 成功运行：[Issue 006 Chromium 验收](https://github.com/xia-ke/Hextech-Seekers/actions/runs/41097612767)。在 Ubuntu EGL llvmpipe 上实际下载并校验 9 个固定原始源文件，成功生成布里茨、锐雯、亚索各自的 idle1/run/attack1 彩色与深度图集，资源已提交到 main。首次旧版骨骼 local 矩阵兼容性错误已修复。
- 真实 `node --test` 完整仓库测试：**35/35 PASS**；Chromium 真实加载：`盖伦/布里茨/锐雯/亚索` 四套图集成功，无 JavaScript pageerror，原始动作帧非空，颜色／深度图尺寸相符。
- 视觉验收图片（已保存到仓库）：[四英雄图集展示](issue006-visual/champion-gallery.png)、[峡谷 Chromium 原图](issue006-visual/chromium-game.png)、[峡谷缩略图](issue006-visual/chromium-game-preview.jpg)、[浏览器验收 JSON](issue006-visual/browser-check.json)。四英雄图集展示已人工查看，人物外形可辨；峡谷图可见盖伦、附近布里茨，其他英雄是否完全露出需进一步针对地形和景深逐个核对，不将脚本中可见性判定直接等同于完整英雄像素可见。
- 浏览器脚本仅在 `localhost/?visualQA=1` 允许对局截图布位与冻结模拟，用真实的地图/模型/深度合成显示视野内三名 AI 角色。这是验证场景，不修改线上游戏平常的 AI 行为。
- Chromium 数据（1600×900，无头 SwiftShader）：`fpsText=5 FPS`、`drawP95=7.0ms`、`rafP95≈350ms`、`rafP50≈16.7ms`、`page_errors=[]`。RAF 调度有明显间歇性长停顿，**不能宣称原先 Issue 001 的游戏流畅度问题已经解决**。
- 本次已完成的是 Issue 006 的**三英雄资产生成、提交和 Chromium 资源/基础画面冒烟验收**；原版画风细节、三位新英雄作为玩家可操作、攻击命中帧、全量实时对局仍需单独验收。Issue 006 整体继续标记 PARTIAL。


## 2026-10-31 后续完善：四英雄玩家操控

- 盖伦、布里茨、锐雯、亚索均可作为实时演练玩家进入 2–4 人对局；每场唯一英雄选择和两阵营规则保持不变。
- `app.js` 已按 `playerHero` 使用各自生成的原始动作图集；`champion-renderer.js` 支持玩家触发各英雄 `attack1` 动画，底部名字、头像与操作说明跟随选择更新。
- 视野内的 AI 仍使用独立原始英雄模型，视野外与淘汰目标不渲染。地形遮挡图像缓存已改为逐 actor 独立，避免角色间覆盖缓存。
- `tests/live-session.test.mjs` 增加四英雄实时配置用例；已在 GitHub Actions 的完整仓库 Node 环境观察到 **36/36 PASS**。Chromium 自动化新增分别启动四英雄、触发攻击、核对英雄名称与头像，并输出逐英雄场景截图。
- 保持限制：仅基础视线而非原版战争迷雾、没有完整英雄主动技能／海克斯、没有泉水起点、没有精确普攻命中动画帧；Issue 006 仍为 PARTIAL。无头 Chromium 历史约 5 FPS，不可宣称已修复流畅度。
- GitHub Actions 的早期作业存在并发推送截图的 non-fast-forward 失败。已加入并发互斥和证据提交前的 `git rebase`；最终以最新版 CI 和实际截图文件为准。
