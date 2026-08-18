from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"
WORK = ROOT / "dist" / "使用说明" / "_work"
WORK.mkdir(parents=True, exist_ok=True)
FINAL = ROOT / "dist" / "使用说明"
FINAL.mkdir(parents=True, exist_ok=True)

WIDTH, HEIGHT = 2480, 3508  # a4-print preset
NAVY = "#152033"
BLUE = "#315EFB"
ORANGE = "#FF6A3D"
IVORY = "#F1EFE8"
SAGE = "#8DA89A"
BLACK = "#151515"
GREY = "#D8D5CD"
WHITE = "#F7F6F0"

FONT_REGULAR = Path(r"C:\Windows\Fonts\msyh.ttc")
FONT_BOLD = Path(r"C:\Windows\Fonts\msyhbd.ttc")


def f(size, bold=False):
    path = FONT_BOLD if bold and FONT_BOLD.exists() else FONT_REGULAR
    return ImageFont.truetype(str(path), size)


def put(draw, xy, value, size, fill, bold=False, anchor=None, spacing=16):
    draw.multiline_text(xy, value, font=f(size, bold), fill=fill, anchor=anchor, spacing=spacing)


def rule(draw, x1, y1, x2, y2, fill, width=4):
    draw.line((x1, y1, x2, y2), fill=fill, width=width)


def dot(draw, x, y, radius, fill):
    draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=fill)


def load_background(name):
    with Image.open(ASSETS / name) as opened:
        return opened.convert("RGB").resize((WIDTH, HEIGHT), Image.Resampling.LANCZOS)


def page_label(draw, page, fill):
    put(draw, (170, 150), f"FIELD NOTE  {page:02d} / 04", 42, fill, True)
    rule(draw, 170, 230, 520, 230, ORANGE, 7)


def numbered_note(draw, x, y, number, heading, body, fill=NAVY, line_color=BLUE, width=950):
    dot(draw, x + 30, y + 30, 26, line_color)
    put(draw, (x + 30, y + 30), number, 28, WHITE, True, anchor="mm")
    put(draw, (x + 88, y), heading, 48, fill, True)
    put(draw, (x + 88, y + 70), body, 38, fill, False, spacing=12)
    rule(draw, x + 88, y + 185, x + width, y + 185, GREY, 2)


def page_one():
    page = load_background("guide-bg-01.png")
    draw = ImageDraw.Draw(page)
    page_label(draw, 1, WHITE)
    put(draw, (170, 370), "图片与 PDF，\n右键自动分流。", 132, WHITE, True, spacing=18)
    put(draw, (180, 760), "一个本地工具，两个转换方向。", 58, "#D9E4FF", True)
    rule(draw, 180, 920, 760, 920, BLUE, 6)
    put(draw, (180, 1010), "01", 54, ORANGE, True)
    put(draw, (350, 1010), "同一个“发送到”入口", 58, WHITE, True)
    put(draw, (350, 1090), "图片发送 → 图片转 PDF\nPDF 发送 → PDF 转图片", 42, "#D9E4FF", spacing=13)
    put(draw, (180, 1370), "02", 54, ORANGE, True)
    put(draw, (350, 1370), "批量处理，也能快速完成", 58, WHITE, True)
    put(draw, (350, 1450), "支持多文件、文件夹和拖入；\nJPEG 快速档优先原图直嵌。", 42, "#D9E4FF", spacing=13)
    put(draw, (180, 1730), "03", 54, ORANGE, True)
    put(draw, (350, 1730), "完全离线，拿来就用", 58, WHITE, True)
    put(draw, (350, 1810), "单 EXE、无需安装、无需网络；\n文件留在本机，不上传。", 42, "#D9E4FF", spacing=13)
    put(draw, (180, 2200), "图片完整保留 · 不裁剪 · 不拉伸", 46, ORANGE, True)
    put(draw, (180, 2310), "同一窗口即时换页；PDF 页左上角可随时返回。", 42, "#D9E4FF")
    return page


def page_two():
    page = load_background("guide-bg-01.png")
    draw = ImageDraw.Draw(page)
    page_label(draw, 2, WHITE)
    put(draw, (170, 370), "选中文件，\n右键就转。", 142, WHITE, True, spacing=18)
    put(draw, (180, 790), "“发送到”入口，\n是最快的使用方式。", 72, "#D9E4FF", True, spacing=16)
    put(draw, (180, 1050), "首次只点一下，之后无需先打开软件", 48, "#D9E4FF")
    put(draw, (180, 1250), "01", 54, ORANGE, True)
    put(draw, (350, 1250), "首次点“一键开启”", 58, WHITE, True)
    put(draw, (350, 1325), "双击“图片转PDF.exe”，在首次引导中点一次即可。", 38, "#D9E4FF")
    put(draw, (180, 1515), "02", 54, ORANGE, True)
    put(draw, (350, 1515), "以后右键就转", 58, WHITE, True)
    put(draw, (350, 1590), "选择图片或 PDF，右击 →“发送到”→“图片转PDF”。", 38, "#D9E4FF")
    put(draw, (180, 1780), "03", 54, ORANGE, True)
    put(draw, (350, 1780), "随时可以移除", 58, WHITE, True)
    put(draw, (350, 1855), "右上角齿轮 →“设置与关于”→“移除右键入口”。\n设置页也可随时重新添加。", 38, "#D9E4FF", spacing=12)
    rule(draw, 180, 2115, 760, 2115, BLUE, 5)
    put(draw, (180, 2180), "图片 → 图片转 PDF\nPDF → PDF 转图片\nWindows 11 若看不到入口，请先点“显示更多选项”", 38, "#D9E4FF", spacing=14)
    put(draw, (180, 2560), "仅当前用户 · 随时可移除 · 本地处理", 42, ORANGE, True)
    return page


def page_three():
    page = load_background("guide-bg-02.png")
    draw = ImageDraw.Draw(page)
    page_label(draw, 3, NAVY)
    put(draw, (170, 370), "图片转 PDF：\n先定页面。", 138, NAVY, True, spacing=18)
    put(draw, (180, 745), "默认 A4 竖向、无水印；图片完整保留，\n不裁剪、不拉伸。", 48, NAVY, spacing=16)
    rule(draw, 180, 1040, 1030, 1040, BLUE, 7)
    numbered_note(draw, 180, 1130, "01", "加入图片", "点击“添加图片”选择文件或文件夹，也可从资源管理器直接拖入。")
    numbered_note(draw, 180, 1380, "02", "设置页面", "按需选择纸张、横竖方向和 0 / 5 / 10 mm 边距；单图可旋转、放大预览。")
    numbered_note(draw, 180, 1630, "03", "选择输出", "先选保存位置，再选择“合并为一个”或“一图一个”，确认名称后导出。")
    rule(draw, 180, 1935, 1030, 1935, GREY, 2)
    put(draw, (180, 2035), "推荐/快速 · 智能处理", 54, ORANGE, True)
    put(draw, (180, 2135), "JPEG 原图直嵌；PNG / BMP\n以 150 DPI 处理。\n打印小字可选 220 / 300 DPI，\n高级需求可选无损。", 38, NAVY, spacing=13)
    put(draw, (900, 2460), "文字水印", 54, SAGE, True)
    put(draw, (900, 2560), "默认无水印；也可选择“仅供参考”，\n或自定义文字、透明度、角度和布局。\n预览与最终 PDF 保持一致。", 40, NAVY, spacing=14)
    return page


def page_four():
    page = load_background("guide-bg-03.png")
    draw = ImageDraw.Draw(page)
    page_label(draw, 4, NAVY)
    put(draw, (170, 370), "PDF 转图片：\n三步完成。", 138, NAVY, True, spacing=18)
    put(draw, (180, 745), "在主界面顶部点击“PDF 转图片”，\n可一次处理一个或多个 PDF。", 48, NAVY, spacing=16)
    rule(draw, 180, 900, 1050, 900, BLUE, 7)
    numbered_note(draw, 180, 990, "01", "加入 PDF", "点击“添加 PDF”或“添加文件夹”，也可直接拖入；文件夹只读取当前层。")
    numbered_note(draw, 180, 1240, "02", "选择页面与格式", "默认转换全部页面；也可填 1-3,5。默认 PNG，也可选 JPEG、BMP、TIFF。")
    numbered_note(draw, 180, 1490, "03", "选择目录并转换", "选择 150 / 220 / 300 DPI、保存位置和命名方式，再点击“导出图片”。")
    rule(draw, 180, 1800, 1050, 1800, GREY, 2)
    put(draw, (180, 1900), "推荐默认值", 54, ORANGE, True)
    put(draw, (180, 2005), "PNG（默认、无损、文字清晰）\n150 DPI（推荐/快速）\n小文件选 JPEG。\nBMP / TIFF 用于兼容，文件较大。", 36, NAVY, spacing=12)
    put(draw, (180, 2305), "命名与结果入口", 54, SAGE, True)
    put(draw, (180, 2410), "默认建立“PDF名-转换后”，图片名为\n“PDF名_第001页.png”。也可自定义名称，\n非法字符自动替换。完成后点击页脚的\n“打开结果文件夹”；重名自动编号，\n已完成图片会保留。", 36, NAVY, spacing=12)
    return page


pages = [page_one(), page_two(), page_three(), page_four()]
final_names = ["01_核心亮点.png", "02_右键快速开始.png", "03_图片转PDF.png", "04_PDF转图片.png"]
for index, page in enumerate(pages, 1):
    target = WORK / f"{index:02d}_master.png"
    page.save(target, "PNG", compress_level=6)
    print(target)
    final_target = FINAL / final_names[index - 1]
    page.save(final_target, "PNG", compress_level=6)
    print(final_target)

pdf_target = ROOT / "dist" / "快速使用指南.pdf"
pages[0].save(
    pdf_target,
    "PDF",
    resolution=300.0,
    save_all=True,
    append_images=pages[1:],
    quality=84,
    subsampling=1,
    optimize=True,
)
print(pdf_target)
