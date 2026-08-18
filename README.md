# 图片与 PDF 本地转换

一款完全离线、无需安装、单 EXE 运行的 Windows 图片转 PDF / PDF 转图片工具。文件只在本机处理，不上传、不联网、无遥测，全部功能免费可用。

项目地址：[github.com/TrendPioneerAI/image-to-pdf](https://github.com/TrendPioneerAI/image-to-pdf)

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
- 设置齿轮内可由用户主动添加或移除当前用户“发送到”右键入口。

## PDF 转图片（v1.1）

- 在主界面点击“PDF 转图片”，或把一个或多个 PDF 直接拖到主窗口，即可进入转换界面。
- 支持“添加 PDF”“添加文件夹”和拖入三种方式；文件夹只读取当前层，不递归。
- 支持多个 PDF 批量转换，异步读取并显示每个文件的页数、大小和位置。
- 默认转换全部页面，也可输入 `1-3,5` 一类页码范围。
- 支持 PNG 无损输出，以及 JPEG 质量 50～100（默认 92）。
- 支持 150 DPI（推荐/快速）、220 DPI（标准）和 300 DPI（精细）。
- 图片按 `PDF名_第001页.png` 命名；不覆盖已有文件，同名自动追加 `(2)`、`(3)`。
- 转换在后台逐页执行，可随时取消；已完成图片保留，当前临时文件自动清理。
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

下载 [`图片转PDF-v1.1.0-Windows.zip`](release/图片转PDF-v1.1.0-Windows.zip) 后直接解压即可。压缩包内只有 `图片转PDF.exe` 和 `快速使用指南.pdf`；双击 EXE 就能使用，不需要安装，也不依赖同目录 DLL。

三页快速指南同时说明图片转 PDF 与 PDF 转图片。文件校验值见 [`release/SHA256SUMS.txt`](release/SHA256SUMS.txt)，后续版本也会发布到 [Releases](https://github.com/TrendPioneerAI/image-to-pdf/releases)。

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
.\tests\smoke-export.ps1
.\tests\smoke-separate-names.ps1
.\tests\smoke-pdf-to-images.ps1
.\tests\validate-v1.ps1
```

`validate-v1.ps1` 验证 JPEG DCT 字节直嵌、无水印、水印覆盖层、PNG 快速/无损编码、220/300 DPI 编码以及 EXIF 方向对照数据。`smoke-pdf-to-images.ps1` 验证三页 PNG、指定页 JPEG、分辨率、重名自动编号、非法页码和临时文件清理。PDF 视觉验收使用 Poppler 或 Windows 内置渲染器逐页检查。

## 配置与隐私

软件只在 `%LocalAppData%\ZenthZhang\ImageToPdf\settings.json` 保存最后一次成功输出目录和输出目标模式。配置缺失或损坏时会回退到“文档”目录。软件不会保存源图片路径，也不会自动修改右键菜单。

## 许可证

[MIT License](LICENSE) · Copyright (c) 2026 ZenthZhang

完整需求与验收口径见 [docs/PRD.md](docs/PRD.md)。

问题反馈请使用 [GitHub Issues](https://github.com/TrendPioneerAI/image-to-pdf/issues)。
