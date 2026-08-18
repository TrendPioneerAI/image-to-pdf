# 图片与 PDF 本地转换

一款完全离线、无需安装、单 EXE 运行的 Windows 图片转 PDF / PDF 转图片工具。文件只在本机处理，不上传、不联网、无遥测，全部功能免费可用。

项目地址：[github.com/TrendPioneerAI/image-to-pdf](https://github.com/TrendPioneerAI/image-to-pdf)

## 一眼看懂：同一个右键入口，自动选择转换方向

- **图片 → PDF**：选中一张或多张图片，右键发送后直接进入“图片转 PDF”。
- **PDF → 图片**：选中一个或多个 PDF，使用同一个右键入口，直接进入“PDF 转图片”，不会再先显示空白的图片窗口。
- **批量且快速**：两种方向均支持多文件、文件夹和拖入；图片快速档优先直嵌 JPEG，转换在后台执行并可取消。
- **完全离线**：单 EXE、无需安装、无需网络，文件始终留在本机。
- **主窗口自动适配**：跟随 Windows 100%～250% 显示缩放及当前屏幕工作区调整窗口，始终保留 v1.2.0 的左右栏布局。

首次只需点一下：

1. 首次运行 `图片转PDF.exe`，引导窗口会直接说明两个转换方向；点击 **一键开启**。
2. 以后在资源管理器中选择一个或多个同类文件，右键选择 **发送到 → 图片转PDF**，软件会按文件类型自动分流。
3. 不想保留时，在主界面点击 **右上角齿轮 → 设置与关于 → 移除右键入口**。设置页也始终保留重新添加入口的按钮。

引导只在尚未设置入口的首次启动显示；选择“暂不设置”后不会反复打扰。软件不会未经点击自动修改系统菜单。Windows 11 使用新版右键菜单时，如果第一层没有“发送到”，请先点击 **显示更多选项**。该入口只对当前用户生效，拖入、添加文件和添加文件夹三种传统入口仍然保留。

![图片转PDF界面原型](design/ui-concept-image2-v3-watermark.png)

## 图片转 PDF

- 支持 JPG/JPEG、PNG、BMP；不支持 TIFF、HEIC、WebP、GIF。
- 通过“添加文件”“添加文件夹”或拖入文件/文件夹导入；文件夹只读取当前层，不递归。
- 默认 A4 竖向，可选 A3、A4、A5、B4、B5、Letter、Legal，以及横向页面和 0/5/10 mm 边距。
- 图片按比例完整放入页面，不裁剪、不拉伸；支持横图自动转正和逐图左/右旋转。
- 支持异步清晰缩略图、点击放大、拖动重排、按名称/大小/修改时间/添加时间排序、删除与清空。
- 支持无水印、自定义文字水印和默认“仅供参考”水印；预览与 PDF 效果一致。
- 支持合并为一个 PDF，或一图一个 PDF；支持预选具体 PDF 文件或输出文件夹。
- 支持逐图命名、批量编号、非法字符替换和同名自动追加 `(2)`、`(3)`。
- 记住最后一次成功输出目录与文件/文件夹模式，不记录源图片路径。

## PDF 转图片（v1.2.1）

- 在主界面点击“PDF 转图片”，或把一个或多个 PDF 直接拖到主窗口，即可进入转换界面。
- 图片与 PDF 功能在同一个主窗口内即时换页，不再创建或叠放第二个窗口；PDF 页左上角可随时返回，并原样保留图片页内容和设置。
- 支持“添加 PDF”“添加文件夹”和拖入三种方式；文件夹只读取当前层，不递归。
- 支持多个 PDF 批量转换，异步读取并显示每个文件的页数、大小和位置。
- 默认转换全部页面，也可输入 `1-3,5` 一类页码范围。
- 支持 PNG、JPEG、BMP、TIFF，默认 PNG；JPEG 质量可选 50～100（默认 92），BMP/TIFF 适合兼容需求但文件更大。
- 支持 150 DPI（推荐/快速）、220 DPI（标准）和 300 DPI（精细）。
- 所选路径作为输出根目录；每个 PDF 自动建立 `PDF名-转换后` 独立文件夹，批量转换时不会把所有图片混在一起。
- 图片按 `PDF名_第001页.png` 命名；同名文件夹自动追加 `(2)`、`(3)`，不覆盖已有内容。
- 转换在后台使用最多两页的有界流水线，可随时取消；PDF 在导出阶段只加载一次，已完成图片保留，当前临时文件自动清理。
- PNG 直接原子写入；JPEG/BMP/TIFF 在内存中完成格式转换，不再为每页额外写入并重新读取临时 PNG。
- 使用 Windows 10/11 自带 PDF 渲染引擎，继续保持单 EXE、无额外 DLL、完全离线。

## 四档输出质量

| 档位 | 处理方式 |
|---|---|
| 推荐/快速 · 智能处理（默认） | JPEG 原始字节直接嵌入；PNG/BMP 以 150 DPI、JPEG 78 处理 |
| 标准（220 DPI） | 最高 220 DPI、JPEG 86 |
| 精细打印（300 DPI） | 最高 300 DPI、JPEG 92 |
| 无损（高级） | JPEG 原图直嵌；PNG/BMP 原始分辨率 Flate 无损压缩 |

所有重采样档位都不会生成超过源图片实际像素的位图。水印以独立 PDF 覆盖层写入，不会让快速档的 JPEG 再次压缩。

## 直接使用

下载 [`图片转PDF-v1.2.1-Windows.zip`](release/图片转PDF-v1.2.1-Windows.zip) 后直接解压即可。压缩包内只有 `图片转PDF.exe` 和 `快速使用指南.pdf`；双击 EXE 就能使用，不需要安装，也不依赖同目录 DLL。

四页快速指南先说明核心亮点与右键入口，再分别说明图片转 PDF 和 PDF 转图片。文件校验值见 [`release/SHA256SUMS.txt`](release/SHA256SUMS.txt)，后续版本也会发布到 [Releases](https://github.com/TrendPioneerAI/image-to-pdf/releases)。

还可以把图片或文件夹作为启动参数传入：

```text
图片转PDF.exe "图片1.jpg" "图片2.png"
图片转PDF.exe "D:\待转换图片"
图片转PDF.exe "D:\资料.pdf"
图片转PDF.exe --pdf-to-images "D:\资料.pdf" "D:\输出图片" png 150 all
```

## 构建

在 Windows 10/11 中运行：

```powershell
.\build.ps1
```

也可双击 `build.cmd`。构建脚本使用系统 .NET Framework C# 编译器和 Windows 10/11 SDK 元数据，嵌入 `assets\app.ico` 和 PerMonitorV2 清单，输出 `dist\图片转PDF.exe`，不产生额外运行库。SDK 只在构建时需要，接收方运行 EXE 不需要安装 SDK。

## 测试

```powershell
.\tests\make-fixtures.ps1
.\tests\smoke-main-window-scaling.ps1
.\tests\smoke-startup-routing.ps1
.\tests\smoke-view-switch.ps1
.\tests\smoke-export.ps1
.\tests\smoke-separate-names.ps1
.\tests\smoke-pdf-to-images.ps1
.\tests\validate-v1.ps1
```

`smoke-main-window-scaling.ps1` 验证 100%～250% 七档 DPI，以及 100%/150%/200%/250% 四组紧凑工作区；图片页与 PDF 页必须保持 v1.2.0 左右栏、单行页头和单行页脚，右侧设置区可滚动且控件不得压盖，并确认四类对话框未被改动。`smoke-startup-routing.ps1` 验证图片与 PDF 的启动分流，尤其确认纯 PDF 会直接进入 PDF 转图片。`smoke-view-switch.ps1` 验证两项功能使用同一窗口、返回入口存在，并要求预创建后的换页不超过 500 ms。`validate-v1.ps1` 验证 JPEG DCT 字节直嵌、无水印、水印覆盖层、PNG 快速/无损编码、220/300 DPI 编码以及 EXIF 方向对照数据。`smoke-pdf-to-images.ps1` 验证每个 PDF 的独立输出文件夹、PNG/JPEG/BMP/TIFF、分辨率、重名文件夹自动编号、并行导出的页序与逐页 SHA-256 稳定性、非法页码和临时文件清理。PDF 视觉验收使用 Poppler 或 Windows 内置渲染器逐页检查。

## 配置与隐私

软件只在 `%LocalAppData%\ZenthZhang\ImageToPdf\settings.json` 保存最后一次成功输出目录、输出目标模式和首次引导是否已经处理。配置缺失或损坏时会回退到“文档”目录。软件不会保存源图片路径，也不会未经用户点击自动修改右键菜单。

## 使用 GitHub Issues 反馈问题

[Issues](https://github.com/TrendPioneerAI/image-to-pdf/issues) 是公开的问题与建议清单，适合报告软件故障、提出新功能或指出文档遗漏。Issue 本身不会修改代码；开发者处理后会回复、关联修复记录，并在完成后关闭。

1. 进入 Issues 页面，点击 **New issue**。
2. 标题用一句话说明问题，例如：`[文档] 快速指南缺少“发送到”右键说明`。
3. 正文写清软件版本、操作步骤、实际结果、期望结果；必要时拖入截图。
4. 点击 **Submit new issue**。后续补充信息直接在该 Issue 下回复即可。

Issues 内容对所有人公开，请勿上传包含身份证、合同、印章、客户资料或其他敏感信息的原始文件。

## 许可证

[MIT License](LICENSE) · Copyright (c) 2026 ZenthZhang

完整需求与验收口径见 [docs/PRD.md](docs/PRD.md)。
