# 视觉资源来源与复现

本次只接入资产及格式数据，没有复制来源项目的游戏逻辑或规则。运行时无第三方请求。站点中的英雄联盟美术与动作权利归 Riot Games；来源仓库的 MIT 许可证不等于这些美术资产的独立授权。本项目为非官方同人原型。

| 内容 | 固定来源 | 本项目处理 |
| --- | --- | --- |
| Map11 Base_SRX 场景、原始导航网格 | [iamywl/Web_game](https://github.com/iamywl/Web_game/tree/557a9d5352af12bee8bf3db879c22d04d0a0954e/public/assets/maps/summoners-rift)；提交 `557a9d5352af12bee8bf3db879c22d04d0a0954e` | 读取 198 个原始场景网格、645491 三角形和 137 张内嵌贴图；按固定镜头渲染图块、逐像素深度和俯视小地图 |
| 盖伦模型、骨骼、原始动作、贴图 | [tengge1/lol-model-viewer](https://github.com/tengge1/lol-model-viewer/tree/ef5635c5fbe0c315275c9bcccf83558cfe7ee436)；提交 `ef5635c5fbe0c315275c9bcccf83558cfe7ee436` | 解码 `86_0.lmesh` / `.lanim`；3932 顶点、6336 三角形、68 骨骼；生成 16 朝向待机、行走、攻击图集及深度 |
| 盖伦攻击预览按钮图标 | [chenzihao0731/rift-moba-3d](https://github.com/chenzihao0731/rift-moba-3d/tree/bd5b98d1eb3c104999ebb2b1cc91a6687c453221/public/assets)；提交 `bd5b98d1eb3c104999ebb2b1cc91a6687c453221` 的 `garen-Q.png` | 本地保存；只是攻击动作预览的图标，未实现 Q 技能 |
| 英雄头像 | 既有仓库中的 Riot Data Dragon 16.17.1 素材 | 沿用本地头像 |

场景原文件 SHA-256：`bdabbf3eb0c37dd2bb8567035c018d0da0f79c849c9353e5192a58279369a69e`。英雄模型与动画哈希保存在 `dist/assets/native/garen.json` 及 `dist/assets/visual/garen/sprite.json`。来源工程的 MIT 文本保存在 `licenses/`。

## 运行方式与保真范围

本测试浏览器禁用了 WebGL。资产构建使用独立 EGL 渲染原始几何，再由 Canvas2D 处理实时移动、动画帧、镜头、寻路与逐像素深度合成。不是运行时截屏播放器，也没有把程序几何占位物当成原始英雄。

场景与英雄使用相同的 55°固定斜俯视投影。这是本项目的镜头设定，并未取得 Riot 原客户端镜头参数的数值等同性证明。英雄有 16 个离散朝向；素材来源中的风、草、动态地表、水面及特殊着色器未执行。源 GLB 已将这些材质转换为静态材质，并使用压缩纹理；本项目进一步将其栅格化为固定视角素材。因此这次不宣称逐像素复刻原游戏或完成全部画面细节。

原始导航坐标按 `(X,Y,Z) → (X,Y,−Z)` 转换，保留高度、墙体标志与草丛 ID；清除单独的建筑阻挡标志，移动区域按下半区边界裁切。角色增加一格通行余量，A* 路径禁止对角穿角并做视线简化。草丛 ID 仅保留数据，本次尚未实现任何阵营视野逻辑。

## 复现资产构建

Node 运行网页不需安装依赖。仅重新生成美术素材时，需要 Python 与 EGL/OpenGL 3.3 环境：

```sh
python3 -m pip install -r scripts/visual-requirements.txt
python3 scripts/fetch-visual-sources.py /tmp/hextech-visual-source
python3 scripts/convert-native-champion.py /tmp/hextech-visual-source/champion dist/assets/native
python3 scripts/render-visual-assets.py /tmp/hextech-visual-source/rift dist/assets/visual --champion-source dist/assets/native
```

下载脚本只获取固定版本的数据文件并核对哈希，不运行来源仓库的程序。原始 88.8 MB GLB 不随网页分发；已经生成的 56 个图块、动作图集和导航数据随站点完整提供。运行时资源约 33 MB，按需读取地图图块。
