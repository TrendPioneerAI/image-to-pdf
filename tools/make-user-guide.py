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
    put(draw, (170, 150), f"FIELD NOTE  {page:02d} / 03", 42, fill, True)
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
    put(draw, (170, 370), "图片转 PDF", 184, WHITE, True)
    put(draw, (178, 635), "本地处理，\n清晰输出。", 96, WHITE, True, spacing=18)
    put(draw, (180, 930), "一套不需要解释的\n三步操作路径", 54, "#D9E4FF", False, spacing=14)
    put(draw, (180, 1250), "01", 54, ORANGE, True)
    put(draw, (350, 1250), "加入图片", 58, WHITE, True)
    put(draw, (350, 1325), "添加文件、添加文件夹，或从资源管理器直接拖入。", 38, "#D9E4FF")
    put(draw, (180, 1515), "02", 54, ORANGE, True)
    put(draw, (350, 1515), "调整页面", 58, WHITE, True)
    put(draw, (350, 1590), "默认 A4 竖向；需要时再改纸张、方向、边距。", 38, "#D9E4FF")
    put(draw, (180, 1780), "03", 54, ORANGE, True)
    put(draw, (350, 1780), "导出文件", 58, WHITE, True)
    put(draw, (350, 1855), "选择合并 PDF，或一图一个 PDF，然后导出。", 38, "#D9E4FF")
    rule(draw, 180, 2115, 760, 2115, BLUE, 5)
    put(draw, (180, 2180), "支持 JPG / JPEG / PNG / BMP。\n不支持 TIFF、HEIC、WebP、GIF。", 38, "#D9E4FF", spacing=14)
    put(draw, (180, 2490), "原图只读 · 不联网 · 不上传", 42, ORANGE, True)
    return page


def page_two():
    page = load_background("guide-bg-02.png")
    draw = ImageDraw.Draw(page)
    page_label(draw, 2, NAVY)
    put(draw, (170, 370), "先定页面，\n再导出。", 154, NAVY, True, spacing=18)
    put(draw, (180, 760), "默认设置兼顾速度与清晰度；\n页面和水印的选择都会同步到预览与最终 PDF。", 48, NAVY, spacing=16)
    rule(draw, 180, 1040, 1030, 1040, BLUE, 7)
    numbered_note(draw, 180, 1130, "01", "纸张大小", "默认 A4，也可选择 A3、A5、B4、B5、Letter、Legal。")
    numbered_note(draw, 180, 1380, "02", "纸张方向", "竖向是默认方向；选择横向后，页面和预览同步变为横向。")
    numbered_note(draw, 180, 1630, "03", "页面边距", "支持 0 / 5 / 10 mm；图片始终完整保留，不裁剪、不拉伸。")
    rule(draw, 180, 1935, 1030, 1935, GREY, 2)
    put(draw, (180, 2035), "推荐/快速 · 智能处理", 54, ORANGE, True)
    put(draw, (180, 2135), "JPEG 原图直嵌；PNG / BMP 以 150 DPI 处理。\n需要时再选 220 DPI、300 DPI 或无损。", 42, NAVY, spacing=16)
    put(draw, (180, 2460), "文字水印", 54, SAGE, True)
    put(draw, (180, 2560), "默认无水印；也可选择“仅供参考”，\n或自定义文字、透明度、角度和布局。", 42, NAVY, spacing=16)
    return page


def page_three():
    page = load_background("guide-bg-03.png")
    draw = ImageDraw.Draw(page)
    page_label(draw, 3, NAVY)
    put(draw, (170, 370), "导出方式决定\n文件形态。", 142, NAVY, True, spacing=18)
    put(draw, (180, 745), "先选择存放位置，再确认文件形态和名称。", 50, NAVY)
    rule(draw, 180, 900, 1050, 900, BLUE, 7)
    numbered_note(draw, 180, 990, "01", "选择输出位置", "合并模式可选具体 PDF 文件或文件夹；逐图模式使用文件夹。")
    numbered_note(draw, 180, 1240, "02", "决定文件形态", "合并为一个多页 PDF，或为每张图片分别生成一个 PDF。")
    numbered_note(draw, 180, 1490, "03", "批量命名后再微调", "输入前缀并点“应用”，再逐张编辑；同名会自动加序号。")
    rule(draw, 180, 1800, 1050, 1800, GREY, 2)
    put(draw, (180, 1900), "排序 ▾", 54, ORANGE, True)
    put(draw, (180, 2005), "文件名、文件大小、修改日期、最近加入，\n每一项都可以选择升序或降序。", 42, NAVY, spacing=16)
    put(draw, (180, 2305), "设置齿轮", 54, SAGE, True)
    put(draw, (180, 2410), "可主动添加或移除“发送到”右键入口；\n软件不会自行修改系统菜单。", 42, NAVY, spacing=16)
    return page


pages = [page_one(), page_two(), page_three()]
final_names = ["01_快速开始.png", "02_页面设置.png", "03_导出命名排序.png"]
for index, page in enumerate(pages, 1):
    target = WORK / f"{index:02d}_master.png"
    page.save(target, "PNG", optimize=True)
    print(target)
    final_target = FINAL / final_names[index - 1]
    page.save(final_target, "PNG", optimize=True)
    print(final_target)

pdf_target = ROOT / "dist" / "快速使用指南.pdf"
pages[0].save(pdf_target, "PDF", resolution=300.0, save_all=True, append_images=pages[1:], quality=92)
print(pdf_target)
