# Between Us / HUD Shader Studio

这是独立的桌面材质演示，用于预览、录屏和作品集截图。它使用脚本示例对话，不调用 AI、麦克风、头显相机或现有对话服务。原有 `StereoCaption`、`ConversationCaption` 和 PICO 主场景保持原样。

## 打开可视化界面

1. 打开 `PicoVRPrototype` Unity 工程，等待脚本导入完成。
2. 顶部菜单选择 **Prototype → Between Us → HUD Shader Studio**。
3. 窗口中会显示实时画面，不需要点击 Play。
4. 使用 **Edge glow / Sweep speed / Scan texture / Edge breathing / Opacity** 调节效果。
5. 关闭 **Animate** 后，可用 **Time** 固定动画时刻。
6. 点击 **Capture 1920 x 1080** 导出当前预览。图片位于工程根目录的 `PortfolioHudCaptures`。

**Panel material** 按钮会选中新建的 `HUD Panel - Coral.mat`，可截取 Inspector 参数界面。

## 新增效果

- `HolographicPanel.shader`：解析式圆角边界、外围柔光、沿边缘移动的高光、低强度扫描纹理、呼吸效果。
- `HolographicText.shader`：清晰字体核心、暗色描边、克制的红色光晕和扫光。
- 面板使用无纹理的 SDF 距离计算；不依赖 URP、GrabPass 或后处理 Bloom。
- 包含 Unity 实例化与立体渲染相关宏；此预览使用独立桌面相机，不代表头显双目显示已验证。
- `PortfolioHudPreviewDriver` 同步动态字体图集，支持固定截图时刻。

生成的场景名为 `BetweenUsHudShowcase.unity`。场景相机采用手动渲染，因此直接打开场景后请通过 **HUD Shader Studio** 看画面。

## 作品集标注

**Custom HUD Shader Study — Edge Glow, Animated Sweep & Text Readability**

**Desktop shader preview · scripted content**

这是可运行的材质与界面视觉实验；与正式头显 HUD 的接入、设备性能及双目可读性测试分别记录。
