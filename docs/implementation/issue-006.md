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
