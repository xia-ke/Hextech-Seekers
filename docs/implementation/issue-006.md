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


## 四英雄玩家操控验收结果（2026-10-31）

- 最新完整 CI：[run 41160403395](https://github.com/xia-ke/Hextech-Seekers/actions/runs/41160403395) **SUCCESS**。在 Linux EGL 真实构建原始英雄图集后执行 `node --test`：**36/36 PASS**，Chromium 实际操作四名玩家英雄、逐一触发 A 攻击并验证 UI 名称／头像，`page_errors=[]`；四英雄原始模型资源加载为 true。
- 逐英雄 Chromium 截图保存在 `docs/implementation/issue006-visual/playable-{garen,blitzcrank,riven,yasuo}.png`；同步保存峡谷同屏图、原始模型展示和浏览器验收 JSON。检查脚本采用本地 `visualQA`，不是正式对局的 AI 布位。
- 视野内目标名单为布里茨、锐雯、亚索；无头浏览器取样：`FPS 9`、`draw p95 2.8ms`、`RAF p95 266.6ms`。对比此前 5 FPS 的环境观察，并不足以隔离浏览器调度、截图条件和缓存优化的作用，故不宣称性能已达到目标。
- 已解决 Actions 证据提交的并发竞态：分支并发取消、重新基于最新 `main` 变基后提交；本轮对应证据已真正写入 GitHub。体验站点仍未独立发布。
- Issue 006 的“原始四英雄图集、玩家角色选择、基本动画渲染、基础视野隐藏、Chromium 冒烟验收”已落实；**尚未完成**原版战争迷雾、英雄技能、完整攻击命中帧、真正泉水与性能硬验收。因此 Issue 006 整体标记 PARTIAL，不称作完整 V1。


## 普攻动画接触帧驱动抓捕（2026-10-31 后续）

- 将玩家实时普攻从“按下 A 当帧立即判定抓捕”改为两阶段动作：按键启动该英雄的原始 `attack1` 图集动画；模拟时钟推进到接触帧后，才重新检查目标存活、阵营、可见性、距离、朝向和墙体 LOS，并调用既有有效普攻抓捕逻辑。
- 接触帧目前暂定为 12 帧攻击图集中的第 7 帧（`7/12 × clip.duration`），恢复阶段持续至原始动作片段结束；同一动作期间禁止重叠普攻，启动后 0.8 秒设置下一次攻击的最早开始时间。目标在挥击过程中离开范围时，接触帧重新判定并不会隔空抓捕。
- 动画显示时间与单局模拟使用相同的累计 `animTime`／`tick(dt)` 时钟，避免使用 `performance.now()` 导致低帧率或 RAF 长停顿时视觉动作和逻辑接触时刻漂移。
- 新增测试覆盖：接触帧前不抓捕、接触帧时抓捕、挥击过程中目标离开距离则失败、动作恢复期禁止重复起手。保留原 `playerAttack()` 直接逻辑接口用于旧逻辑测试，正式 UI 改用 `beginPlayerAttack()`。
- **准确性边界**：第 7 帧是跨英雄统一的临时接触点，尚未根据各英雄原始动画逐帧标定真实武器接触时刻；各英雄基础攻击距离、攻击前摇／后摇、朝向判定角度和攻击冷却也仍是临时数值。不得将此阶段描述为已完全还原英雄联盟攻击帧。
- 下一轮应对四名英雄的 12 帧 `attack1` 做逐帧图像/武器轨迹检查，分别标定接触帧及攻击盒；之后在真实浏览器中测试贴身、斜向、背向、墙后、草丛、目标位移和同时攻击等边界。


## 逐英雄攻击接触帧与模型比例修正（2026-10-31）

- 回看上一轮四张 Chromium 截图后发现锐雯模型的实际可见高度明显小于盖伦／亚索，四英雄“均已渲染”不代表尺寸比例合格。构建脚本现从各英雄 idle1 第一帧 alpha bbox 测量可见高度，以盖伦为基准；布里茨目标高度额外乘 1.15，其余目标与盖伦一致，写入各 `sprite.json` 的 `visualScale` 和 `scaleReference`。渲染器对颜色图像和逐像素地形深度采样使用同一世界比例，避免只放大颜色层造成遮挡错位。
- 现有旧验收数据的 idle 第一帧可见高度：盖伦 141 px、布里茨 94 px、锐雯 62 px、亚索 150 px；由此估算比例约为 1.000、1.725、2.274、0.940。新构建会重新从原始图集计算，而不是永久硬编码上述数值。此归一化是预览视觉尺寸基线，不代表四英雄在原游戏中的真实碰撞体或身高完全相同。
- 普攻逻辑新增 `beginPlayerAttack`：起手后在原始攻击图集第 7/12 帧调用有效命中检查；攻击期间不允许重叠起手，命中帧重新检查目标距离、视野、朝向与 LOS，动作播放到原片段结束后才解除恢复锁。UI 动画和模拟时钟统一使用累计 `animTime`／`tick(dt)`，不再用墙钟 `performance.now()` 驱动攻击动画进度。
- 发现并修正了上一版 Chromium QA 的实质缺陷：此前测试在准备阶段点击 A，截图中实际出现 `not-hunter-or-not-chasing`，没有触发普攻。新 QA 专用接口仅允许 localhost `?visualQA=1`，将模拟推进到追捕阶段、固定 AI 目标、验证合法视野与近战距离，点击 A 后断言攻击接触帧成功抓捕。线上路径不能调用这些布位／冻结接口。
- 新增 Node 测试：接触帧前不抓捕、接触帧合法命中、目标挥击期间离开范围则失败、恢复期阻止重叠起手、四英雄比例元数据校验。Chromium 还会检查四英雄 scale 数值范围及逐个实际攻击结果。
- 仍未完成：四名英雄各自真实武器接触帧／攻击盒的逐帧标定、原版攻击前摇后摇与攻击速度规则、正式战争迷雾、英雄技能和海克斯。第 7/12 帧是统一临时接触点，不得称作原版精确帧。


## 2026-10-31 下一阶段：逐英雄攻击接触帧配置（视觉估算版）

- 新增 `scripts/issue006-attack-contact-sheet.py` 的 CI 产物，使用四英雄原始 `attack1` 图集、12 个时间采样帧和 D0/D4/D8/D12 四个朝向生成逐帧总览。已检查接触图：盖伦挥剑进入下劈/前挥姿势、布里茨拳臂伸展、锐雯横向斩击、亚索横向出刀的姿势出现时间不同。
- 新增 `dist/rebuild/attack-profile.js`，首轮按原始帧姿势分别标记：盖伦第 4 帧、布里茨第 8 帧、锐雯第 4 帧、亚索第 4 帧。命中时间严格映射到该帧开始时刻：`duration × (contactFrame − 1) / 12`，修正此前将 7/12 直接作为“第 7 帧”而实际落在第 8 帧的偏移。
- 实时 UI 在各英雄攻击动画起手时读取对应 profile，将各自的接触帧时间传入统一模拟时钟；接触帧触发时仍重新验证目标存活、阵营、视野、距离、朝向和墙体 LOS。攻击冷却、恢复锁和有效抓捕规则保持不变。
- 新增测试验证四英雄独立帧配置、采样帧时间映射无 off-by-one、未知英雄／错误帧数／非法片段时长拒绝启动。
- **重要边界：这是基于 12 帧渲染图集的姿势观察估算，不是原始骨骼动画事件标记，也不是 Riot 原版攻击碰撞盒。** 当前只有一个离散接触帧；还没有各英雄独立攻击范围、武器扫掠体积、前摇/后摇规则与攻击速度成长。CAPTURE_RANGE 仍为共享临时值 180。必须通过下一轮贴身、边缘距离、不同朝向、移动目标与墙体阻挡浏览器验收后，才能进一步收敛参数。


## 2026-10-31 下一项：英雄独立普攻武器命中区域（第一轮可调参数）

- 修复上一提交 CI 暴露的帧边界浮点误差：攻击接触时刻增加极小的片段归一化 epsilon，确保渲染器的 `floor(time / duration × frames)` 实际显示所选接触帧，而非因浮点舍入仍显示前一帧。
- `meleeHit` 增加可选前向胶囊命中区域：以攻击者朝向为轴，`range` 为前向长度、`halfWidth` 为横向半宽；目标必须落在该胶囊内，同时继续通过存活、阵营、玩家独立视野、LOS 与抓捕规则。旧 AI/测试调用未传 hitbox 时保留原近战距离与扇形判定。
- 四英雄首轮可调参数：盖伦 180/42、布里茨 165/52、锐雯 190/58、亚索 185/46（前向长度/半宽，地图世界单位）。这些是本项目初始平衡参数，不是从 Riot 服务器数据或原版碰撞体提取的数值。
- UI 从对应英雄 profile 同时传入接触帧和 hitbox；接触帧重新选择最近可见目标并逐一尝试胶囊命中，避免最近目标在武器轨迹外时屏蔽后方另一个有效目标。
- 新增测试覆盖胶囊正前方命中、侧向擦空、四英雄参数独立性，以及帧选择浮点边界。原有未指定 hitbox 的 AI 与兼容调用保持旧判定。
- 限制：该胶囊是 2D 世界平面上的攻击代理，不是骨骼武器扫掠体积；命中发生在单一接触帧，没有连续 sweep。参数必须由实际浏览器距离/朝向/动画验收进一步调优。


## 2026-10-31 胶囊命中与边界回归补充

- 修复上轮 `node --test` 失败的错误断言：目标仍在视野内、但移出攻击距离时，精确失败原因为 `out-of-range`。
- 将早期仅有矩形侧边的前向命中区域改为真正带圆形前端的二维胶囊。保留攻击者前向约束：背后目标不会因胶囊底端圆角被捕获。近战命中范围仍是项目临时平衡值，不宣称 Riot 真实碰撞数据。
- 新增回归覆盖：前端圆弧边界、斜向目标、背向目标、极限距离、侧边临界位置、视野/墙体阻挡、无效坐标。以上为规则单元测试，并非真实 Chromium 实机动画验收。


## 下一阶段：真实峡谷 Chromium 攻击边界场景（2026-10-31）

- 最新已通过的 CI [run 41175050208](https://github.com/xia-ke/Hextech-Seekers/actions/runs/41175050208) 完成之前的胶囊形碰撞单元测试与旧版 Chromium 冒烟检查；本阶段新增更严格的真实峡谷场景验收。
- 仅 localhost + `?visualQA=1` 允许调试布位：`stageAttackForVisualQA(mode)` 支持 `front/back/side/escape`，在当前地图的真实可通行且独立视野可见位置放置目标，并将其他 AI 目标从测试画面暂时排除。 `relocateAttackTargetForVisualQA()` 在攻击前摇开始后，将目标移到视野内的远处可通行网格。
- Chromium 使用真实的页面、英雄攻击按钮、模拟时钟和原始动画图集，分别验证正面命中、背向目标不命中、侧面偏离攻击轨迹不命中、目标在命中接触帧之前撤出距离后不命中。检查结果写入 `artifacts/issue006/combat-boundary-check.json`；成功 CI 将其提交到 `docs/implementation/issue006-visual/`。
- Node 补充对“目标进入另一组草丛”和“墙体斜角遮挡”的回归检查，确保即使几何距离满足条件，视野禁止时依然不能抓捕。这两个是网格逻辑测试，**不属于 Chromium 实机墙体与草丛视觉验收**。
- 核心规则未修改：仍然是玩家独立视野、接触帧重新验证、地图线段遮挡、既有得分和阶段规则。测试用布位／冻结 AI 接口通过主机名与 URL 参数双重限制，不对正式站点开放。
- **后续仍需：**真实峡谷特殊墙体地形及各草丛细节的 Chromium 可见性场景、攻击范围在实际视觉比例下的调参、性能硬验收；本阶段不会把临时 2D 胶囊代理描述为原版真实武器碰撞。


## 实际验收结果：GitHub Actions run 41175482257（SUCCESS）

- 真实 Ubuntu + EGL 原始素材重建成功，`node --test` **55/55 PASS**；Chromium 四英雄素材加载和页面操作通过，没有 `pageerror`。
- 已提交 [真实峡谷战斗边界检查 JSON](issue006-visual/combat-boundary-check.json)。实测（地图坐标，测试起始目标距离均为 100）：`front → captured`、`back → outside-facing`、`side → outside-hitbox`、`escape → out-of-range`。逃离测试在起手后、接触帧前把目标移到视野内但攻击距离外，故正确判定未命中。
- 单独通过的 Node 网格回归包含墙体对角遮挡和目标进入另一组草丛时禁止抓捕；这两项**尚无对应真实峡谷 Chromium 视觉布位验收**，不可混称。
- 此次无头 Chromium 1600×900 样本：性能报告 `fpsText=17 FPS`、`rafP95≈150.1ms`、`drawP95≈2ms`。RAF 仍存在严重长尾，**未达到流畅度硬验收**。
- 运行证据：[Actions #41175482257](https://github.com/xia-ke/Hextech-Seekers/actions/runs/41175482257)；Issue 006 整体维持 PARTIAL。下一阶段优先补齐真实地图墙体与草丛的浏览器布位验收，并定位 RAF 长停顿，然后完善 AI 追捕行为。


## 2026-10-31 实际峡谷渲染与帧率稳定性优化（0.9.3）

本轮交付内容：

1. `depth-codec.js` 用纯函数复现原来的 RGBA→16 位深度解码和透明哨兵值，提供回归测试。`tile-depth-worker.js` 在独立 Worker 中执行 PNG/WebP 资源获取、`createImageBitmap` 解码、`OffscreenCanvas` 深度 readback 和 Uint16 转换，通过可转移 `ImageBitmap`／`ArrayBuffer` 向主线程交付；`tile-depth-loader.js` 有错误回退到原图像加载路线。WebP 彩色贴图和深度图使用相同的原始美术资源，没有降分辨率。
2. `app.js` 将 Rift 瓦片加载接入异步解码；通过视口整数地图片块边界复用可见列表，避免每个 RAF 都重新计算和调用图片加载；缓存淘汰时关闭被转移的 ImageBitmap；标签页隐藏时停止无意义绘制。
3. 增加 `__hextechPerf()` 的 `tileCache.workerColors`、`workerDecoded`、回退数、解码错误、逻辑 P95、主线程 long task 数与 RAF P95，并通过仅 localhost 的 `visualQA` 环境重置采样。
4. 试验 `overview-viewport.js` 在 CPU 端裁剪大地图底图源矩形；A/B 验证没表现出改善，**生产默认已恢复原来的整张底图绘制**，裁剪功能仅能在 localhost 调试时临时打开。新增裁剪坐标边界测试。
5. Chromium 自动测试现在区分空闲视角、同页裁剪试验、恢复正常绘制和跨三个峡谷区域的镜头移动，并将数据保存为 `docs/implementation/issue006-visual/render-performance.json`。

### 真实执行及结论

- 成功 CI：[Worker 深度图验证 run 41176184817](https://github.com/xia-ke/Hextech-Seekers/actions/runs/41176184817)；空闲视角 47 FPS、RAF P95 16.8ms、Canvas draw P95 1.2ms，跨区域跳转 10 FPS、RAF P95 200ms；深度 Worker 解码 14 张，错误及回退 0。
- 最新成功 CI：[双 Worker + 底图 A/B run 41176994917](https://github.com/xia-ke/Hextech-Seekers/actions/runs/41176994917)，完整 `node --test` **61/61 PASS**，Chromium 视觉/抓捕回归通过、`page_errors=[]`。空闲 34 FPS、RAF P95 16.8ms；三地区跳转样本 9 FPS、RAF P95 316.7ms、Canvas draw P95 5.5ms、`logicP95≈0.2ms`、`longTaskCount=0`。14 张彩色图及 14 张深度图均通过 Worker 处理（`workerErrors=0`，`fallbackColors=0`，`fallbackDecoded=0`）。
- 同一次浏览器运行的裁剪 A/B：裁剪时约 10 FPS、恢复整图时约 8 FPS，后续样本持续变慢，说明无头 Chromium/SwiftShader 运行过程中存在与渲染模式切换无关的帧调度波动。这些有限样本**不能单独证明整图与裁剪的 GPU 性能优劣**，因此不默认启用没有正面证据的优化。
- 本轮确认：原始峡谷美术、四英雄与地形深度遮挡仍能通过 Chromium 测试，CPU 端图像解码已移出主线程。与此同时，**仍未达到实际镜头移动时稳定 60FPS 的目标**。由于绘制和游戏逻辑耗时远小于 RAF 长尾，更高优先级的后续工作是有真实 GPU 加速的 Chrome/Firefox 复测、记录浏览器 Compositor/GPU trace，确认软件渲染与图片纹理上传或屏幕捕获对帧调度的影响，再决定是否实施 WebGL 纹理分块绘制或异步资源预热；不能将无头 SwiftShader 的 FPS 直接归因于游戏 JavaScript。
- GitHub 源码和 CI 已更新；独立 Sites 没有部署，Issue 006 的正式流畅度与完整游戏验收继续为 PARTIAL。
