# 图片转PDF

一款完全离线、无需安装、单 EXE 运行的 Windows 图片转 PDF 工具。图片只在本机处理，不上传、不联网、无遥测，全部功能免费可用。

项目地址：[github.com/TrendPioneerAI/image-to-pdf](https://github.com/TrendPioneerAI/image-to-pdf)

![图片转PDF界面原型](design/ui-concept-image2-v3-watermark.png)

## 主要功能

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

## 四档输出质量

| 档位 | 处理方式 |
|---|---|
| 推荐/快速 · 智能处理（默认） | JPEG 原始字节直接嵌入；PNG/BMP 以 150 DPI、JPEG 78 处理 |
| 标准（220 DPI） | 最高 220 DPI、JPEG 86 |
| 精细打印（300 DPI） | 最高 300 DPI、JPEG 92 |
| 无损（高级） | JPEG 原图直嵌；PNG/BMP 原始分辨率 Flate 无损压缩 |

所有重采样档位都不会生成超过源图片实际像素的位图。水印以独立 PDF 覆盖层写入，不会让快速档的 JPEG 再次压缩。

## 直接使用

从 [Releases](https://github.com/TrendPioneerAI/image-to-pdf/releases/latest) 下载 `图片转PDF.exe` 后双击即可。它不依赖同目录 DLL，单独发送这个 EXE 也能运行；[`dist\快速使用指南.pdf`](dist/快速使用指南.pdf) 可作为用户手册一并发送。

还可以把图片或文件夹作为启动参数传入：

```text
图片转PDF.exe "图片1.jpg" "图片2.png"
图片转PDF.exe "D:\待转换图片"
```

## 构建

在 Windows 10/11 中运行：

```powershell
.\build.ps1
```

也可双击 `build.cmd`。构建脚本使用系统 .NET Framework C# 编译器，嵌入 `assets\app.ico` 和 PerMonitorV2 清单，输出 `dist\图片转PDF.exe`，不产生额外运行库。

## 测试

```powershell
.\tests\make-fixtures.ps1
.\tests\smoke-export.ps1
.\tests\smoke-separate-names.ps1
.\tests\validate-v1.ps1
```

`validate-v1.ps1` 验证 JPEG DCT 字节直嵌、无水印、水印覆盖层、PNG 快速/无损编码、220/300 DPI 编码以及 EXIF 方向对照数据。PDF 视觉验收使用 Poppler 渲染后逐页检查。

## 配置与隐私

软件只在 `%LocalAppData%\ZenthZhang\ImageToPdf\settings.json` 保存最后一次成功输出目录和输出目标模式。配置缺失或损坏时会回退到“文档”目录。软件不会保存源图片路径，也不会自动修改右键菜单。

## 许可证

[MIT License](LICENSE) · Copyright (c) 2026 ZenthZhang

完整需求与验收口径见 [docs/PRD.md](docs/PRD.md)。

问题反馈请使用 [GitHub Issues](https://github.com/TrendPioneerAI/image-to-pdf/issues)。
