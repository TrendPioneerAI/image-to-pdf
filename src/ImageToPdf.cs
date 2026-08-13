using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LocalImageToPdf
{
    internal enum PageOrientation
    {
        Portrait,
        Landscape
    }

    internal enum PaperSizeKind
    {
        A4,
        A3,
        A5,
        B4,
        B5,
        Letter,
        Legal
    }

    internal static class PaperSizes
    {
        public static readonly string[] DisplayNames = new[]
        {
            "A4",
            "A3",
            "A5",
            "B4ï¼ˆJISï¼‰",
            "B5ï¼ˆJISï¼‰",
            "Letter",
            "Legal"
        };

        public static float GetWidthMm(PaperSizeKind kind)
        {
            switch (kind)
            {
                case PaperSizeKind.A3: return 297f;
                case PaperSizeKind.A5: return 148f;
                case PaperSizeKind.B4: return 257f;
                case PaperSizeKind.B5: return 182f;
                case PaperSizeKind.Letter: return 215.9f;
                case PaperSizeKind.Legal: return 215.9f;
                default: return 210f;
            }
        }

        public static float GetHeightMm(PaperSizeKind kind)
        {
            switch (kind)
            {
                case PaperSizeKind.A3: return 420f;
                case PaperSizeKind.A5: return 210f;
                case PaperSizeKind.B4: return 364f;
                case PaperSizeKind.B5: return 257f;
                case PaperSizeKind.Letter: return 279.4f;
                case PaperSizeKind.Legal: return 355.6f;
                default: return 297f;
            }
        }
    }

    internal enum ExportMode
    {
        Merge,
        Separate
    }

    internal enum QualityPreset
    {
        SmartFast = 0,
        Standard = 1,
        FinePrint = 2,
        Lossless = 3,

        // Compatibility aliases retained for the legacy implementation.
        Small = SmartFast,
        Print = FinePrint
    }

    internal enum WatermarkMode
    {
        None,
        Custom,
        Default
    }

    internal enum WatermarkLayout
    {
        Center,
        Tile,
        BottomRight
    }

    internal enum OutputTargetMode
    {
        File,
        Folder
    }

    internal enum SortMode
    {
        NameAscending,
        NameDescending,
        SizeDescending,
        SizeAscending,
        ModifiedDescending,
        ModifiedAscending,
        AddedDescending,
        AddedAscending
    }

    internal sealed class ImageItem
    {
        private static long _nextAddedOrder;

        public ImageItem(string path)
        {
            Path = path;
            FileName = System.IO.Path.GetFileName(path);
            OutputName = System.IO.Path.GetFileNameWithoutExtension(path);
            AddedOrder = Interlocked.Increment(ref _nextAddedOrder);
        }

        public string Path { get; private set; }
        public string FileName { get; private set; }
        public string OutputName { get; set; }
        public long AddedOrder { get; private set; }
        public int ManualRotation { get; set; }
        public Bitmap Preview { get; set; }
        public string PreviewError { get; set; }

        public void DisposePreview()
        {
            if (Preview != null)
            {
                Preview.Dispose();
                Preview = null;
            }
        }
    }

    internal sealed class ExportOptions
    {
        public PaperSizeKind PaperSize { get; set; }
        public PageOrientation Orientation { get; set; }
        public bool AutoRotate { get; set; }
        public int MarginMm { get; set; }
        public QualityPreset Quality { get; set; }
        public ExportMode Mode { get; set; }
        public string BaseName { get; set; }
        public WatermarkOptions Watermark { get; set; }
        public OutputTargetMode TargetMode { get; set; }
    }

    internal sealed class WatermarkOptions
    {
        public WatermarkMode Mode { get; set; }
        public string Text { get; set; }
        public int OpacityPercent { get; set; }
        public int AngleDegrees { get; set; }
        public WatermarkLayout Layout { get; set; }

        public static WatermarkOptions None()
        {
            return new WatermarkOptions
            {
                Mode = WatermarkMode.None,
                Text = String.Empty,
                OpacityPercent = 18,
                AngleDegrees = 45,
                Layout = WatermarkLayout.Tile
            };
        }

        public static WatermarkOptions DefaultPreset()
        {
            return new WatermarkOptions
            {
                Mode = WatermarkMode.Default,
                Text = "ä»…ä¾›å‚è€ƒ",
                OpacityPercent = 18,
                AngleDegrees = 45,
                Layout = WatermarkLayout.Tile
            };
        }

        public WatermarkOptions Clone()
        {
            return new WatermarkOptions
            {
                Mode = Mode,
                Text = Text,
                OpacityPercent = OpacityPercent,
                AngleDegrees = AngleDegrees,
                Layout = Layout
            };
        }
    }

    internal sealed class ImageSnapshot
    {
        public string Path { get; set; }
        public int ManualRotation { get; set; }
        public string OutputName { get; set; }
    }

    internal sealed class PageLayout
    {
        public float PageWidthPt { get; set; }
        public float PageHeightPt { get; set; }
        public float XPt { get; set; }
        public float YPt { get; set; }
        public float WidthPt { get; set; }
        public float HeightPt { get; set; }
    }

    internal static class QualitySettings
    {
        public static int GetDpi(QualityPreset preset)
        {
            if (preset == QualityPreset.Standard) return 220;
            if (preset == QualityPreset.FinePrint) return 300;
            if (preset == QualityPreset.Lossless) return 0;
            return 150;
        }

        public static long GetJpegQuality(QualityPreset preset)
        {
            if (preset == QualityPreset.Standard) return 86;
            if (preset == QualityPreset.FinePrint) return 92;
            if (preset == QualityPreset.Lossless) return 100;
            return 78;
        }
    }

    internal static class ImageTools
    {
        private const int ExifOrientationId = 0x0112;

        public static bool IsSupportedPath(string path)
        {
            string extension = System.IO.Path.GetExtension(path);
            if (String.IsNullOrEmpty(extension))
                return false;
            extension = extension.ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".bmp";
        }

        public static Bitmap LoadTransformed(ImageItem item, bool autoRotate)
        {
            return LoadTransformed(item.Path, item.ManualRotation, autoRotate);
        }

        public static Bitmap LoadTransformed(string path, int manualRotation, bool autoRotate)
        {
            using (Image loaded = Image.FromFile(path))
            {
                int exifOrientation = ReadExifOrientation(loaded);
                Bitmap bitmap = new Bitmap(loaded);
                ApplyExifOrientation(bitmap, exifOrientation);

                if (autoRotate && bitmap.Width > bitmap.Height)
                    bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);

                ApplyManualRotation(bitmap, manualRotation);
                return bitmap;
            }
        }

        private static int ReadExifOrientation(Image image)
        {
            try
            {
                if (!Array.Exists(image.PropertyIdList, delegate (int id) { return id == ExifOrientationId; }))
                    return 1;
                PropertyItem item = image.GetPropertyItem(ExifOrientationId);
                if (item == null || item.Value == null || item.Value.Length < 2)
                    return 1;
                return BitConverter.ToUInt16(item.Value, 0);
            }
            catch
            {
                return 1;
            }
        }

        private static void ApplyExifOrientation(Bitmap bitmap, int orientation)
        {
            RotateFlipType type;
            switch (orientation)
            {
                case 2:
                    type = RotateFlipType.RotateNoneFlipX;
                    break;
                case 3:
                    type = RotateFlipType.Rotate180FlipNone;
                    break;
                case 4:
                    type = RotateFlipType.RotateNoneFlipY;
                    break;
                case 5:
                    type = RotateFlipType.Rotate90FlipX;
                    break;
                case 6:
                    type = RotateFlipType.Rotate90FlipNone;
                    break;
                case 7:
                    type = RotateFlipType.Rotate270FlipX;
                    break;
                case 8:
                    type = RotateFlipType.Rotate270FlipNone;
                    break;
                default:
                    type = RotateFlipType.RotateNoneFlipNone;
                    break;
            }
            if (type != RotateFlipType.RotateNoneFlipNone)
                bitmap.RotateFlip(type);
        }

        private static void ApplyManualRotation(Bitmap bitmap, int degrees)
        {
            int normalized = NormalizeRotation(degrees);
            if (normalized == 90)
                bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
            else if (normalized == 180)
                bitmap.RotateFlip(RotateFlipType.Rotate180FlipNone);
            else if (normalized == 270)
                bitmap.RotateFlip(RotateFlipType.Rotate270FlipNone);
        }

        public static int NormalizeRotation(int degrees)
        {
            int result = degrees % 360;
            if (result < 0)
                result += 360;
            return result;
        }

        public static PageLayout CalculateLayout(int sourceWidth, int sourceHeight, PageOrientation orientation, int marginMm)
        {
            return CalculateLayout(sourceWidth, sourceHeight, PaperSizeKind.A4, orientation, marginMm);
        }

        public static PageLayout CalculateLayout(int sourceWidth, int sourceHeight, PaperSizeKind paperSize, PageOrientation orientation, int marginMm)
        {
            float baseWidth = PaperSizes.GetWidthMm(paperSize) * 72f / 25.4f;
            float baseHeight = PaperSizes.GetHeightMm(paperSize) * 72f / 25.4f;
            float pageWidth = orientation == PageOrientation.Portrait ? baseWidth : baseHeight;
            float pageHeight = orientation == PageOrientation.Portrait ? baseHeight : baseWidth;
            float margin = Math.Max(0, Math.Min(50, marginMm)) * 72f / 25.4f;
            float innerWidth = Math.Max(1f, pageWidth - margin * 2f);
            float innerHeight = Math.Max(1f, pageHeight - margin * 2f);
            float scale = Math.Min(innerWidth / Math.Max(1, sourceWidth), innerHeight / Math.Max(1, sourceHeight));
            float drawWidth = Math.Max(1f, sourceWidth * scale);
            float drawHeight = Math.Max(1f, sourceHeight * scale);

            return new PageLayout
            {
                PageWidthPt = pageWidth,
                PageHeightPt = pageHeight,
                XPt = margin + (innerWidth - drawWidth) / 2f,
                YPt = margin + (innerHeight - drawHeight) / 2f,
                WidthPt = drawWidth,
                HeightPt = drawHeight
            };
        }

        public static Bitmap RenderPagePreview(ImageItem item, PageOrientation orientation, bool autoRotate, int marginMm, int pageWidthPx, int pageHeightPx)
        {
            return RenderPagePreview(item, PaperSizeKind.A4, orientation, autoRotate, marginMm, pageWidthPx, pageHeightPx);
        }

        public static Bitmap RenderPagePreview(ImageItem item, PaperSizeKind paperSize, PageOrientation orientation, bool autoRotate, int marginMm, int pageWidthPx, int pageHeightPx)
        {
            using (Bitmap source = LoadTransformed(item, autoRotate))
            {
                Bitmap page = new Bitmap(pageWidthPx, pageHeightPx, PixelFormat.Format24bppRgb);
                using (Graphics graphics = Graphics.FromImage(page))
                {
                    PrepareGraphics(graphics);
                    graphics.Clear(Color.White);
                    PageLayout layout = CalculateLayout(source.Width, source.Height, paperSize, orientation, marginMm);
                    float sx = pageWidthPx / layout.PageWidthPt;
                    float sy = pageHeightPx / layout.PageHeightPt;
                    RectangleF target = new RectangleF(layout.XPt * sx, pageHeightPx - (layout.YPt + layout.HeightPt) * sy, layout.WidthPt * sx, layout.HeightPt * sy);
                    graphics.DrawImage(source, target);
                }
                return page;
            }
        }

        public static Bitmap RenderImage(Bitmap source, int width, int height)
        {
            Bitmap result = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                PrepareGraphics(graphics);
                graphics.Clear(Color.White);
                graphics.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
            }
            return result;
        }

        public static byte[] ToJpeg(Bitmap bitmap, long quality)
        {
            ImageCodecInfo codec = null;
            foreach (ImageCodecInfo candidate in ImageCodecInfo.GetImageEncoders())
            {
                if (candidate.MimeType == "image/jpeg")
                {
                    codec = candidate;
                    break;
                }
            }
            if (codec == null)
                throw new InvalidOperationException("JPEG ç¼–ç å™¨ä¸å¯ç”¨ã€‚");

            using (MemoryStream stream = new MemoryStream())
            using (EncoderParameters parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Math.Max(1, Math.Min(100, quality)));
                bitmap.Save(stream, codec, parameters);
                return stream.ToArray();
            }
        }

        public static byte[] ToLosslessRgb(Bitmap bitmap)
        {
            Rectangle rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = null;
            try
            {
                data = bitmap.Lock×<âÚ$z{-®éÜj×“°¢–b‡&V¦V7FVBä6÷VçBâ¢°¢7G&–æt'V–ÆFW"ÖW76vRÒæWr7G&–æt'V–ÆFW"‚“°¢ÖW76vRäVæDÆ–æR‚.Kº^Kˆ¾ih~K»niÊ®XªXZ^ûÉ¢"“°¢–çBÆ–Ö—BÒÖF‚äÖ–âƒ#Â&V¦V7FVBä6÷VçB“°¢f÷"†–çB–æFW‚Ò²–æFW‚ÂÆ–Ö—C²–æFW‚²²’ÖW76vRäVæDÆ–æR‚.(
""²&V¦V7FVE¶–æFW…Ò“°¢–b‡&V¦V7FVBä6÷VçBâÆ–Ö—B’ÖW76vRäVæDÆ–æR‚.(
n(
nXúniÈ’"²‡&V¦V7FVBä6÷VçBÒÆ–Ö—B’åFõ7G&–ær‚’²"KŠ®ih~K»n8""“°¢ÖW76vT&÷‚å6†÷r‡F†—2ÂÖW76vRåFõ7G&–ær‚’Â.Y»îx˜~ZûÎXZ^hùzK¢"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâåv&æ–ær“°¢Ğ¢Ğ ¢&—fFRfö–B6ÆV$—FV×2‚¢°¢f÷&V6‚„–ÖvT—FVÒ—FVÒ–âö—FV×2’—FVÒäF—7÷6U&Wf–Wr‚“°¢ö—FV×2ä6ÆV"‚“°¢÷F‡2ä6ÆV"‚“°¢&Vg&W6„6&G2‚“°¢Ğ ¢&—fFRfö–B&Vg&W6„6&G2‚¢°¢–b…÷&Vg&W6†–ær’&WGW&ã°¢÷&Vg&W6†–ærÒG'VS°¢G'¢°¢ö6&G2å7W7VæDÆ–÷WB‚“°¢f÷&V6‚„6öçG&öÂ6öçG&öÂ–âö6&G2ä6öçG&öÇ2’6öçG&öÂäF—7÷6R‚“°¢ö6&G2ä6öçG&öÇ2ä6ÆV"‚“°¢W%6—¦T¶–æBW%6—¦RÒvWEW%6—¦R‚“°¢vT÷&–VçFF–öâ÷&–VçFF–öâÒvWD÷&–VçFF–öâ‚“°¢&ööÂWFõ&÷FFRÒöWFõ&÷FFT6†V6²ÒçVÆÂbböWFõ&÷FFT6†V6²ä6†V6¶VC°¢–çBÖ&v–âÒvWDÖ&v–äÖÒ‚“°¢–çB&Wf–Wuv–GFƒ°¢–çB&Wf–Wt†V–v‡C°¢vWE&Wf–WtF–ÖVç6–öç2‡W%6—¦RÂ÷&–VçFF–öâÂ÷WB&Wf–Wuv–GF‚Â÷WB&Wf–Wt†V–v‡B“°¢7W'6÷"Ò7W'6÷'2åv—D7W'6÷#°¢f÷&V6‚„–ÖvT—FVÒ—FVÒ–âö—FV×2¢°¢—FVÒäF—7÷6U&Wf–Wr‚“°¢G'¢°¢—FVÒå&Wf–WtW'&÷"ÒçVÆÃ°¢—FVÒå&Wf–WrÒ–ÖvUFööÇ2å&VæFW%vU&Wf–Wr†—FVÒÂW%6—¦RÂ÷&–VçFF–öâÂWFõ&÷FFRÂÖ&v–âÂ&Wf–Wuv–GF‚Â&Wf–Wt†V–v‡B“°¢Ğ¢6F6‚„W†6WF–öâW'&÷"¢°¢—FVÒå&Wf–WtW'&÷"ÒW'&÷"äÖW76vS°¢—FVÒå&Wf–WrÒæWr&—FÖ‡&Wf–Wuv–GF‚Â&Wf–Wt†V–v‡BÂ—†VÄf÷&ÖBäf÷&ÖC#F'&v"“°¢W6–ær„w&†–72w&†–72Òw&†–72äg&öÔ–ÖvR†—FVÒå&Wf–Wr’’²w&†–72ä6ÆV"„6öÆ÷"åv†—FR“²Ğ¢Ğ¢–ÖvT6&B6&BÒæWr–ÖvT6&B‡F†—2Â—FVÒ“°¢Væ&ÆTW‡FW&æÄG&÷†6&B“°¢ö6&G2ä6öçG&öÇ2äFB†6&B“°¢Ğ¢Ğ¢f–æÆÇ¢°¢7W'6÷"Ò7W'6÷'2äFVfVÇC°¢ö6&G2å&W7VÖTÆ–÷WB‚“°¢÷&Vg&W6†–ærÒfÇ6S°¢WFFT6÷VçB‚“°¢Ğ¢Ğ ¢&—fFRfö–BW‡÷'D6Æ–6¶VB†ö&¦V7B6VæFW"ÂWfVçD&w2R¢°¢–b…ö6æ6VÆÆF–öâÒçVÆÂ’&WGW&ã°¢–b…ö—FV×2ä6÷VçBÓÒ¢°¢ÖW76vT&÷‚å6†÷r‡F†—2Â.Šû~XXk{¾XªY»îx˜~8""Â.izk9^ZûÎX{¢"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°¢&WGW&ã°¢Ğ¢Æ—7CÇ7G&–æsâVæf–Æ&ÆRÒæWrÆ—7CÇ7G&–æsâ‚“°¢f÷&V6‚„–ÖvT—FVÒ—FVÒ–âö—FV×2¢°¢–b‚f–ÆRäW†—7G2†—FVÒåF‚’’Væf–Æ&ÆRäFB†—FVÒäf–ÆTæÖR“°¢Ğ¢–b‡Væf–Æ&ÆRä6÷VçBâ¢°¢ÖW76vT&÷‚å6†÷r‡F†—2Â.Kº^Kˆ¾k©ih~K»n[{.{¸şKˆŞZÙYÊûÈÎŠû~˜xŞikk{¾XªYîXhŞZûÎX{®ûÉ¥ÆåÆâ"²7G&–ærä¦ö–â‚%Æâ"ÂVæf–Æ&ÆRåFô'&’‚’’Â.izk9^ZûÎX{¢"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâåv&æ–ær“°¢&WGW&ã°¢Ğ ¢W‡÷'D÷F–öç2÷F–öç2ÒvWD÷F–öç2‚“°¢7G&–ærF&vWBÒçVÆÃ°¢7G&–ærföÆFW"ÒçVÆÃ°¢–b†÷F–öç2äÖöFRÓÒW‡÷'DÖöFRäÖW&vR¢°¢W6–ær…6fTf–ÆTF–ÆörF–ÆörÒæWr6fTf–ÆTF–Æör‚’¢°¢F–ÆöråF—FÆRÒ.KùŞZÙY[›bDb#°¢F–Æöräf–ÇFW"Ò%Dbih~K»gÂ¢çFb#°¢F–Æöräf–ÆTæÖRÒVç7W&UFdW‡FVç6–öâ†÷F–öç2ä&6TæÖR“°¢–b†F–Æörå6†÷tF–Æör‡F†—2’ÒF–Æöu&W7VÇBäô²’&WGW&ã°¢F&vWBÒF–Æöräf–ÆTæÖS°¢Ğ¢Ğ¢VÇ6P¢°¢W6–ær„föÆFW$'&÷w6W$F–ÆörF–ÆörÒæWrföÆFW$'&÷w6W$F–Æör‚’¢°¢F–ÆöräFW67&—F–öâÒ.˜hº˜	šRDby¨N‹é>X{®ih~K»nZK’#°¢–b†F–Æörå6†÷tF–Æör‡F†—2’ÒF–Æöu&W7VÇBäô²’&WGW&ã°¢föÆFW"ÒF–Æörå6VÆV7FVEFƒ°¢Ğ¢Ğ ¢Æ—7CÄ–ÖvU6æ6†÷Câ6æ6†÷G2ÒæWrÆ—7CÄ–ÖvU6æ6†÷Câ‚“°¢f÷&V6‚„–ÖvT—FVÒ—FVÒ–âö—FV×2¢°¢6æ6†÷G2äFB†æWr–ÖvU6æ6†÷B²F‚Ò—FVÒåF‚ÂÖçVÅ&÷FF–öâÒ—FVÒäÖçVÅ&÷FF–öâÂ÷WGWDæÖRÒ—FVÒä÷WGWDæÖRÒ“°¢Ğ¢ö6æ6VÆÆF–öâÒæWr6æ6VÆÆF–öåFö¶Vå6÷W&6R‚“°¢öW‡÷'D'WGFöâäVæ&ÆVBÒfÇ6S°¢ö6æ6VÄ'WGFöâåf—6–&ÆRÒG'VS°¢÷7FGW4Æ&VÂåFW‡BÒ.jÚ>YÊZûÎX{¢Râââ#°¢6æ6VÆÆF–öåFö¶VâFö¶VâÒö6æ6VÆÆF–öâåFö¶Vã°¢F6²å'Vâ†FVÆVvFP¢°¢G'¢°¢7F–öãÆ–çCâ&öw&W72ÒFVÆVvFR†–çBfÇVR¢°¢–b‚—4F—7÷6VB’&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFR²÷7FGW4Æ&VÂåFW‡BÒ.jÚ>YÊZûÎX{¢"²fÇVRåFõ7G&–ær‚’²"Râââ#²Ò“°¢Ó°¢–b†÷F–öç2äÖöFRÓÒW‡÷'DÖöFRäÖW&vR¢FdW‡÷'FW"äW‡÷'DÖW&vVB‡F&vWBÂ6æ6†÷G2Â÷F–öç2Â&öw&W72ÂFö¶Vâ“°¢VÇ6P¢FdW‡÷'FW"äW‡÷'E6W&FR†föÆFW"Â6æ6†÷G2Â÷F–öç2Â&öw&W72ÂFö¶Vâ“°¢–b‚—4F—7÷6VB’&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFP¢°¢÷7FGW4Æ&VÂåFW‡BÒ.ZûÎX{®ZèÎh‰8"#°¢ÖW76vT&÷‚å6†÷r‡F†—2Â%Db[{.h‰X©şZûÎX{®8""Â.ZèÎh‰"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°¢Ò“°¢Ğ¢6F6‚„÷W&F–öä6æ6VÆVDW†6WF–öâ¢°¢–b‚—4F—7÷6VB’&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFR²÷7FGW4Æ&VÂåFW‡BÒ.[{.XùnkhZûÎX{®8"#²Ò“°¢Ğ¢6F6‚„W†6WF–öâW'&÷"¢°¢–b‚—4F—7÷6VB’&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFR²÷7FGW4Æ&VÂåFW‡BÒ.ZûÎX{®ZK‹J^8"#²ÖW76vT&÷‚å6†÷r‡F†—2ÂW'&÷"äÖW76vRÂ.ZûÎX{®ZK‹JR"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâäW'&÷"“²Ò“°¢Ğ¢f–æÆÇ¢°¢–b‚—4F—7÷6VB’&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFR²ö6æ6VÄ'WGFöâåf—6–&ÆRÒfÇ6S²öW‡÷'D'WGFöâäVæ&ÆVBÒG'VS²ö6æ6VÆÆF–öâäF—7÷6R‚“²ö6æ6VÆÆF–öâÒçVÆÃ²Ò“°¢Ğ¢Ò“°¢Ğ ¢&—fFRW‡÷'D÷F–öç2vWD÷F–öç2‚¢°¢&WGW&âæWrW‡÷'D÷F–öç0¢°¢W%6—¦RÒvWEW%6—¦R‚’À¢÷&–VçFF–öâÒvWD÷&–VçFF–öâ‚’À¢WFõ&÷FFRÒöWFõ&÷FFT6†V6²ä6†V6¶VBÀ¢Ö&v–äÖÒÒvWDÖ&v–äÖÒ‚’À¢VÆ—G’Ò…VÆ—G•&W6WB•÷VÆ—G”6öÖ&òå6VÆV7FVD–æFW‚À¢ÖöFRÒöÖöFT6öÖ&òå6VÆV7FVD–æFW‚ÓÒòW‡÷'DÖöFRå6W&FR¢W‡÷'DÖöFRäÖW&vRÀ¢&6TæÖRÒ7G&–ærä—4çVÆÄ÷%v†—FU76R…öf–ÆTæÖT&÷‚åFW‡B’ò.Y»îx˜~Y[›eò"²FFUF–ÖRäæ÷råFõ7G&–ær‚'———”ÔÖFEô„†ÖÒ"’¢öf–ÆTæÖT&÷‚åFW‡BåG&–Ò‚¢Ó°¢Ğ ¢&—fFRvT÷&–VçFF–öâvWD÷&–VçFF–öâ‚¢°¢&WGW&âö÷&–VçFF–öä6öÖ&òÒçVÆÂbbö÷&–VçFF–öä6öÖ&òå6VÆV7FVD–æFW‚ÓÒòvT÷&–VçFF–öâäÆæG66R¢vT÷&–VçFF–öâå÷'G&—C°¢Ğ ¢&—fFRW%6—¦T¶–æBvWEW%6—¦R‚¢°¢–b…÷W$6öÖ&òÓÒçVÆÂÇÂ÷W$6öÖ&òå6VÆV7FVD–æFW‚Â¢&WGW&âW%6—¦T¶–æBäC°¢&WGW&â…W%6—¦T¶–æB”ÖF‚äÖ‚ƒÂÖF‚äÖ–â…W%6—¦W2äF—7Æ”æÖW2äÆVæwF‚ÒÂ÷W$6öÖ&òå6VÆV7FVD–æFW‚’“°¢Ğ ¢&—fFRfö–BvWE&Wf–WtF–ÖVç6–öç2…W%6—¦T¶–æBW%6—¦RÂvT÷&–VçFF–öâ÷&–VçFF–öâÂ÷WB–çBv–GF‚Â÷WB–çB†V–v‡B¢°¢–çBÆöæu6–FS°¢–b…ö—FV×2ä6÷VçBÃÒ"¢Æöæu6–FRÒƒ°¢VÇ6R–b…ö—FV×2ä6÷VçBÃÒc¢Æöæu6–FRÒcs“°¢VÇ6P¢Æöæu6–FRÒ&Wf–Wu÷'G&—D†V–v‡C° ¢fÆöBW%v–GF‚ÒW%6—¦W2ävWEv–GF„ÖÒ‡W%6—¦R“°¢fÆöBW$†V–v‡BÒW%6—¦W2ävWD†V–v‡DÖÒ‡W%6—¦R“°¢–b†÷&–VçFF–öâÓÒvT÷&–VçFF–öâäÆæG66R¢°¢fÆöB7vÒW%v–GFƒ°¢W%v–GF‚ÒW$†V–v‡C°¢W$†V–v‡BÒ7v°¢Ğ¢–b‡W%v–GF‚ãÒW$†V–v‡B¢°¢v–GF‚ÒÆöæu6–FS°¢†V–v‡BÒÖF‚äÖ‚ƒÂ†–çB”ÖF‚å&÷VæB†Æöæu6–FR¢W$†V–v‡BòW%v–GF‚’“°¢Ğ¢VÇ6P¢°¢†V–v‡BÒÆöæu6–FS°¢v–GF‚ÒÖF‚äÖ‚ƒÂ†–çB”ÖF‚å&÷VæB†Æöæu6–FR¢W%v–GF‚òW$†V–v‡B’“°¢Ğ¢Ğ ¢&—fFR–çBvWDÖ&v–äÖÒ‚¢°¢&WGW&âöÖ&v–ä6öÖ&òÓÒçVÆÂò¢…öÖ&v–ä6öÖ&òå6VÆV7FVD–æFW‚ÓÒò¢…öÖ&v–ä6öÖ&òå6VÆV7FVD–æFW‚ÓÒòR¢’“°¢Ğ ¢&—fFR7FF–27G&–ærVç7W&UFdW‡FVç6–öâ‡7G&–æræÖR¢°¢&WGW&âæÖRäVæG5v—F‚‚"çFb"Â7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R’òæÖR¢æÖR²"çFb#°¢Ğ ¢&—fFRfö–B6WGF–æw46†ævVB†ö&¦V7B6VæFW"ÂWfVçD&w2R¢°¢–b‚÷&Vg&W6†–ær’&Vg&W6„6&G2‚“°¢Ğ ¢&—fFRfö–BWFFT6÷VçB‚¢°¢–b…ö6÷VçDÆ&VÂÒçVÆÂ’ö6÷VçDÆ&VÂåFW‡BÒ.X["²ö—FV×2ä6÷VçBåFõ7G&–ær‚’²"šR#°¢–b…÷7FGW4Æ&VÂÒçVÆÂbbö6æ6VÆÆF–öâÓÒçVÆÂ’÷7FGW4Æ&VÂåFW‡BÒö—FV×2ä6÷VçBÓÒò.Xúşk{¾XªY»îx˜~[ÈZx¾‹ÚÎhÚ""¢.[{.XxnZHr"²ö—FV×2ä6÷VçBåFõ7G&–ær‚’²"[ÊY»îx˜r#°¢Ğ ¢&—fFRfö–BVæ&ÆTW‡FW&æÄG&÷„6öçG&öÂ6öçG&öÂ¢°¢–b†6öçG&öÂÓÒçVÆÂ’&WGW&ã°¢–b†6öçG&öÂÒö6&G2¢°¢6öçG&öÂäÆÆ÷tG&÷ÒG'VS°¢6öçG&öÂäG&tVçFW"³Ò†æFÆTG&tVçFW#°¢6öçG&öÂäG&tG&÷³Ò†æFÆTG&tG&÷°¢Ğ¢f÷&V6‚„6öçG&öÂ6†–ÆB–â6öçG&öÂä6öçG&öÇ2¢Væ&ÆTW‡FW&æÄG&÷†6†–ÆB“°¢Ğ ¢&—fFRfö–B†æFÆTG&tVçFW"†ö&¦V7B6VæFW"ÂG&tWfVçD&w2R¢°¢–b†RäFFävWDFF&W6VçB„FFf÷&ÖG2äf–ÆTG&÷’¢RäVffV7BÒG&tG&÷VffV7G2ä6÷“°¢VÇ6R–b†RäFFävWDFF&W6VçB‡G—Vöb„–ÖvT—FVÒ’’¢RäVffV7BÒG&tG&÷VffV7G2äÖ÷fS°¢VÇ6P¢RäVffV7BÒG&tG&÷VffV7G2äæöæS°¢Ğ ¢&—fFRfö–B†æFÆTG&tG&÷†ö&¦V7B6VæFW"ÂG&tWfVçD&w2R¢°¢–b†RäFFävWDFF&W6VçB‡G—Vöb„–ÖvT—FVÒ’’¢°¢6&G4G&tG&÷…ö6&G2ÂR“°¢&WGW&ã°¢Ğ¢7G&–æuµÒf–ÆW2ÒRäFFävWDFF„FFf÷&ÖG2äf–ÆTG&÷’27G&–æuµÓ°¢–b†f–ÆW2ÒçVÆÂ’FDf–ÆW2†f–ÆW2“°¢Ğ ¢&—fFRfö–B6&G4G&tVçFW"†ö&¦V7B6VæFW"ÂG&tWfVçD&w2R¢°¢†æFÆTG&tVçFW"‡6VæFW"ÂR“°¢Ğ ¢&—fFRfö–B6&G4G&t÷fW"†ö&¦V7B6VæFW"ÂG&tWfVçD&w2R¢°¢–b†RäFFävWDFF&W6VçB„FFf÷&ÖG2äf–ÆTG&÷’¢RäVffV7BÒG&tG&÷VffV7G2ä6÷“°¢VÇ6R–b†RäFFävWDFF&W6VçB‡G—Vöb„–ÖvT—FVÒ’’¢RäVffV7BÒG&tG&÷VffV7G2äÖ÷fS°¢VÇ6P¢RäVffV7BÒG&tG&÷VffV7G2äæöæS°¢Ğ ¢&—fFRfö–B6&G4G&tG&÷†ö&¦V7B6VæFW"ÂG&tWfVçD&w2R¢°¢7G&–æuµÒf–ÆW2ÒRäFFävWDFF„FFf÷&ÖG2äf–ÆTG&÷’27G&–æuµÓ°¢–b†f–ÆW2ÒçVÆÂ¢°¢FDf–ÆW2†f–ÆW2“°¢&WGW&ã°¢Ğ¢–ÖvT—FVÒ—FVÒÒRäFFävWDFF‡G—Vöb„–ÖvT—FVÒ’’2–ÖvT—FVÓ°¢–b†—FVÒÓÒçVÆÂ’&WGW&ã°¢ö–çBÆö6F–öâÒö6&G2åö–çEFô6Æ–VçB†æWrö–çB†Rå‚ÂRå’’“°¢–çBF&vWD–æFW‚Òö—FV×2ä6÷VçBÒ°¢f÷"†–çB–æFW‚Ò²–æFW‚Âö6&G2ä6öçG&öÇ2ä6÷VçC²–æFW‚²²¢°¢–b…ö6&G2ä6öçG&öÇ5¶–æFW…Òä&÷VæG2ä6öçF–ç2†Æö6F–öâ’’²F&vWD–æFW‚Ò–æFWƒ²'&V³²Ğ¢Ğ¢–çB6÷W&6T–æFW‚Òö—FV×2ä–æFW„öb†—FVÒ“°¢–b‡6÷W&6T–æFW‚ÂÇÂ6÷W&6T–æFW‚ÓÒF&vWD–æFW‚’&WGW&ã°¢ö—FV×2å&VÖ÷fTB‡6÷W&6T–æFW‚“°¢–b‡6÷W&6T–æFW‚ÂF&vWD–æFW‚’F&vWD–æFW‚ÒÓ°¢F&vWD–æFW‚ÒÖF‚äÖ‚ƒÂÖF‚äÖ–â‡F&vWD–æFW‚Âö—FV×2ä6÷VçB’“°¢ö—FV×2ä–ç6W'B‡F&vWD–æFW‚Â—FVÒ“°¢&Vg&W6„6&G2‚“°¢Ğ ¢&—fFRfö–BFE6VæEFô6Æ–6¶VB†ö&¦V7B6VæFW"ÂWfVçD&w2R¢°¢G'’²6VæEFôÖævW"äFB‚“²ÖW76vT&÷‚å6†÷r‡F†—2Â.[{.k{¾XªX‹[Ù>X˜ŞyJh‹~y¨N(	ÎXù˜X‹(	ŞˆùÎXÙ^8""Â.Xû>™JîXZ^Xú2"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“²Ğ¢6F6‚„W†6WF–öâW'&÷"’²ÖW76vT&÷‚å6†÷r‡F†—2ÂW'&÷"äÖW76vRÂ.k{¾XªZK‹JR"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâäW'&÷"“²Ğ¢Ğ ¢&—fFRfö–B&VÖ÷fU6VæEFô6Æ–6¶VB†ö&¦V7B6VæFW"ÂWfVçD&w2R¢°¢G'’²6VæEFôÖævW"å&VÖ÷fR‚“²ÖW76vT&÷‚å6†÷r‡F†—2Â.[{.z{¾™šNXû>™JîXZ^Xú>8""Â.Xû>™JîXZ^Xú2"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“²Ğ¢6F6‚„W†6WF–öâW'&÷"’²ÖW76vT&÷‚å6†÷r‡F†—2ÂW'&÷"äÖW76vRÂ.z{¾™šNZK‹JR"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâäW'&÷"“²Ğ¢Ğ ¢&—fFR7FF–2'WGFöâÖ¶T†VFW$'WGFöâ‡7G&–ærFW‡B¢°¢&WGW&âæWr'WGFöâ²FW‡BÒFW‡BÂv–GF‚ÒBÂ†V–v‡BÒ3BÂÖ&v–âÒæWrFF–ærƒbÂÂÂ’ÂfÆE7G–ÆRÒfÆE7G–ÆRäfÆBÂ&6´6öÆ÷"Ò6öÆ÷"åv†—FRÓ°¢Ğ ¢&—fFR7FF–2Æ&VÂÖ¶U6V7F–öåF—FÆR‡7G&–ærFW‡B¢°¢&WGW&âæWrÆ&VÂ²FW‡BÒFW‡BÂWFõ6—¦RÒG'VRÂföçBÒæWrföçB‚$Ö–7&÷6ögB–†V’T’"Â&bÂföçE7G–ÆRä&öÆB’Âf÷&T6öÆ÷"Ò6öÆ÷"äg&öÔ&v"ƒrÂ#BÂ3’’Ó°¢Ğ ¢&—fFR7FF–2Æ&VÂÖ¶Tf–VÆDÆ&VÂ‡7G&–ærFW‡B¢°¢&WGW&âæWrÆ&VÂ²FW‡BÒFW‡BÂWFõ6—¦RÒG'VRÂf÷&T6öÆ÷"Ò6öÆ÷"äg&öÔ&v"ƒsRÂƒRÂ“’’Ó°¢Ğ ¢&—fFR7FF–26öÖ&ô&÷‚Ö¶T6öÖ&ò‡7G&–æuµÒfÇVW2¢°¢6öÖ&ô&÷‚6öÖ&òÒæWr6öÖ&ô&÷‚²v–GF‚Ò“Â†V–v‡BÒ#‚ÂG&÷F÷vå7G–ÆRÒ6öÖ&ô&÷…7G–ÆRäG&÷F÷väÆ—7BÂfÆE7G–ÆRÒfÆE7G–ÆRäfÆBÓ°¢6öÖ&òä—FV×2äFE&ævR‡fÇVW2“°¢&WGW&â6öÖ&ó°¢Ğ ¢&÷FV7FVB÷fW'&–FRfö–Böäf÷&Ô6Æ÷6–ær„f÷&Ô6Æ÷6–ætWfVçD&w2R¢°¢–b…ö6æ6VÆÆF–öâÒçVÆÂ’ö6æ6VÆÆF–öâä6æ6VÂ‚“°¢f÷&V6‚„–ÖvT—FVÒ—FVÒ–âö—FV×2’—FVÒäF—7÷6U&Wf–Wr‚“°¢&6Räöäf÷&Ô6Æ÷6–ær†R“°¢Ğ¢Ğ ¢–çFW&æÂ7FF–26Æ72&öw&Ğ¢°¢µ5DF‡&VEĞ¢&—fFR7FF–2fö–BÖ–â‡7G&–æuµÒ&w2¢°¢Æ–6F–öâäVæ&ÆUf—7VÅ7G–ÆW2‚“°¢Æ–6F–öâå6WD6ö×F–&ÆUFW‡E&VæFW&–ætFVfVÇB†fÇ6R“°¢Æ–6F–öâå'Vâ†æWrÖ–äf÷&Ò†&w2’“°¢Ğ¢Ğ§Ğ