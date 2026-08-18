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
            "B4（JIS）",
            "B5（JIS）",
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
                Text = "仅供参考",
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
                throw new InvalidOperationException("JPEG 编码器不可用。");

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
                data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                int rowBytes = bitmap.Width * 3;
                byte[] raw = new byte[(rowBytes + 1) * bitmap.Height];
                byte[] row = new byte[rowBytes];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int rawOffset = y * (rowBytes + 1);
                    raw[rawOffset] = 0;
                    IntPtr rowPointer = IntPtr.Add(data.Scan0, y * data.Stride);
                    Marshal.Copy(rowPointer, row, 0, rowBytes);
                    for (int x = 0; x < rowBytes; x += 3)
                    {
                        raw[rawOffset + 1 + x] = row[x + 2];
                        raw[rawOffset + 1 + x + 1] = row[x + 1];
                        raw[rawOffset + 1 + x + 2] = row[x];
                    }
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    using (DeflateStream deflate = new DeflateStream(stream, CompressionMode.Compress, true))
                    {
                        deflate.Write(raw, 0, raw.Length);
                    }
                    byte[] deflated = stream.ToArray();
                    byte[] zlib = new byte[deflated.Length + 6];
                    zlib[0] = 0x78;
                    zlib[1] = 0x9C;
                    Buffer.BlockCopy(deflated, 0, zlib, 2, deflated.Length);
                    uint checksum = Adler32(raw);
                    zlib[zlib.Length - 4] = (byte)(checksum >> 24);
                    zlib[zlib.Length - 3] = (byte)(checksum >> 16);
                    zlib[zlib.Length - 2] = (byte)(checksum >> 8);
                    zlib[zlib.Length - 1] = (byte)checksum;
                    return zlib;
                }
            }
            finally
            {
                if (data != null)
                    bitmap.UnlockBits(data);
            }
        }

        private static uint Adler32(byte[] bytes)
        {
            const uint Modulo = 65521;
            uint a = 1;
            uint b = 0;
            for (int index = 0; index < bytes.Length; index++)
            {
                a = (a + bytes[index]) % Modulo;
                b = (b + a) % Modulo;
            }
            return (b << 16) | a;
        }

        private static void PrepareGraphics(Graphics graphics)
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
    }

    internal sealed class SimplePdfWriter : IDisposable
    {
        private readonly FileStream _stream;
        private readonly List<long> _offsets = new List<long> { 0 };
        private readonly List<int> _pageObjects = new List<int>();
        private int _nextObject = 3;
        private bool _finished;

        public SimplePdfWriter(string path)
        {
            _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 64);
            WriteBytes(Encoding.ASCII.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));
            WriteAsciiObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        }

        public void AddPage(byte[] imageData, int imageWidth, int imageHeight, PageLayout layout, int pageNumber, bool lossless)
        {
            int pageObject = NextObject();
            int imageObject = NextObject();
            int contentObject = NextObject();
            _pageObjects.Add(pageObject);

            string imageName = "Im" + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string page = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.###} {1:0.###}] /Resources << /ProcSet [/PDF /ImageC] /XObject << /{2} {3} 0 R >> >> /Contents {4} 0 R >>",
                layout.PageWidthPt, layout.PageHeightPt, imageName, imageObject, contentObject);
            WriteAsciiObject(pageObject, page);

            WriteImageObject(imageObject, imageData, imageWidth, imageHeight, lossless);

            string content = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "q\n{0:0.###} 0 0 {1:0.###} {2:0.###} {3:0.###} cm\n/{4} Do\nQ\n",
                layout.WidthPt, layout.HeightPt, layout.XPt, layout.YPt, imageName);
            WriteStreamObject(contentObject, content);
        }

        public void Finish()
        {
            if (_finished)
                return;

            StringBuilder kids = new StringBuilder();
            kids.Append("[");
            foreach (int page in _pageObjects)
            {
                kids.Append(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
                kids.Append(" 0 R ");
            }
            kids.Append("]");
            WriteAsciiObject(2, String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "<< /Type /Pages /Kids {0} /Count {1} >>", kids, _pageObjects.Count));

            long xrefPosition = _stream.Position;
            int objectCount = _nextObject;
            WriteAscii("xref\n0 " + objectCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
            WriteAscii("0000000000 65535 f \n");
            for (int index = 1; index < objectCount; index++)
            {
                long offset = index < _offsets.Count ? _offsets[index] : 0;
                WriteAscii(offset.ToString("0000000000", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
            }
            WriteAscii(String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n", objectCount, xrefPosition));
            _stream.Flush(true);
            _finished = true;
        }

        private int NextObject()
        {
            return _nextObject++;
        }

        private void WriteAsciiObject(int number, string body)
        {
            RecordOffset(number);
            WriteAscii(number.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 obj\n");
            WriteAscii(body + "\nendobj\n");
        }

        private void WriteImageObject(int number, byte[] imageData, int width, int height, bool lossless)
        {
            RecordOffset(number);
            WriteAscii(number.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 obj\n");
            string dictionary;
            if (lossless)
            {
                dictionary = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {0} >> /Length {2} >>\nstream\n",
                    width, height, imageData.Length);
            }
            else
            {
                dictionary = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {2} >>\nstream\n",
                    width, height, imageData.Length);
            }
            WriteAscii(dictionary);
            WriteBytes(imageData);
            WriteAscii("\nendstream\nendobj\n");
        }

        private void WriteStreamObject(int number, string content)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(content);
            RecordOffset(number);
            WriteAscii(number.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 obj\n");
            WriteAscii("<< /Length " + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >>\nstream\n");
            WriteBytes(bytes);
            WriteAscii("endstream\nendobj\n");
        }

        private void RecordOffset(int number)
        {
            while (_offsets.Count <= number)
                _offsets.Add(0);
            _offsets[number] = _stream.Position;
        }

        private void WriteAscii(string value)
        {
            WriteBytes(Encoding.ASCII.GetBytes(value));
        }

        private void WriteBytes(byte[] bytes)
        {
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void Dispose()
        {
            if (!_finished)
            {
                try { _stream.Flush(); } catch { }
            }
            _stream.Dispose();
        }
    }

    internal static class LegacyPdfExporter
    {
        public static void ExportMerged(string targetPath, IList<ImageSnapshot> items, ExportOptions options, Action<int> progress, CancellationToken token)
        {
            string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (SimplePdfWriter writer = new SimplePdfWriter(temporaryPath))
                {
                    for (int index = 0; index < items.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        AddSnapshot(writer, items[index], options, index + 1);
                        if (progress != null)
                            progress((index + 1) * 100 / items.Count);
                    }
                    writer.Finish();
                }
                ReplaceFile(temporaryPath, targetPath);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        public static void ExportSeparate(string folder, IList<ImageSnapshot> items, ExportOptions options, Action<int> progress, CancellationToken token)
        {
            List<string> created = new List<string>();
            try
            {
                for (int index = 0; index < items.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    string requestedName = items[index].OutputName;
                    if (String.IsNullOrWhiteSpace(requestedName))
                        requestedName = System.IO.Path.GetFileNameWithoutExtension(items[index].Path);
                    requestedName = requestedName.Trim();
                    if (requestedName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        requestedName = requestedName.Substring(0, requestedName.Length - 4);
                    string baseName = SanitizeFileName(requestedName);
                    string targetPath = GetUniquePath(folder, baseName + ".pdf");
                    string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        using (SimplePdfWriter writer = new SimplePdfWriter(temporaryPath))
                        {
                            AddSnapshot(writer, items[index], options, 1);
                            writer.Finish();
                        }
                        ReplaceFile(temporaryPath, targetPath);
                        created.Add(targetPath);
                    }
                    catch
                    {
                        TryDelete(temporaryPath);
                        throw;
                    }
                    if (progress != null)
                        progress((index + 1) * 100 / items.Count);
                }
            }
            catch
            {
                // Completed files are intentional output and are retained for separate export.
                throw;
            }
        }

        private static void AddSnapshot(SimplePdfWriter writer, ImageSnapshot snapshot, ExportOptions options, int pageNumber)
        {
            using (Bitmap source = ImageTools.LoadTransformed(snapshot.Path, snapshot.ManualRotation, options.AutoRotate))
            {
                PageLayout layout = ImageTools.CalculateLayout(source.Width, source.Height, options.PaperSize, options.Orientation, options.MarginMm);
                int dpi = QualitySettings.GetDpi(options.Quality);
                long quality = QualitySettings.GetJpegQuality(options.Quality);
                int targetWidth = Math.Max(1, (int)Math.Round(layout.WidthPt / 72f * dpi));
                int targetHeight = Math.Max(1, (int)Math.Round(layout.HeightPt / 72f * dpi));
                targetWidth = Math.Min(targetWidth, Math.Max(1, source.Width));
                targetHeight = Math.Min(targetHeight, Math.Max(1, source.Height));

                using (Bitmap rendered = ImageTools.RenderImage(source, targetWidth, targetHeight))
                {
                    bool lossless = options.Quality == QualityPreset.Print;
                    byte[] imageData = lossless ? ImageTools.ToLosslessRgb(rendered) : ImageTools.ToJpeg(rendered, quality);
                    writer.AddPage(imageData, rendered.Width, rendered.Height, layout, pageNumber, lossless);
                }
            }
        }

        private static string GetUniquePath(string folder, string fileName)
        {
            string candidate = System.IO.Path.Combine(folder, fileName);
            if (!File.Exists(candidate))
                return candidate;
            string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
            string extension = System.IO.Path.GetExtension(fileName);
            int index = 2;
            while (File.Exists(candidate))
            {
                candidate = System.IO.Path.Combine(folder, stem + " (" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" + extension);
                index++;
            }
            return candidate;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(invalid.ToString(), "_");
            return String.IsNullOrWhiteSpace(name) ? "图片" : name;
        }

        private static void ReplaceFile(string temporaryPath, string targetPath)
        {
            if (File.Exists(targetPath))
                File.Replace(temporaryPath, targetPath, null);
            else
                File.Move(temporaryPath, targetPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }

    internal static class SendToManager
    {
        private static string ShortcutPath
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft\\Windows\\SendTo\\图片转PDF.lnk");
            }
        }

        public static void Add()
        {
            string directory = System.IO.Path.GetDirectoryName(ShortcutPath);
            Directory.CreateDirectory(directory);
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("系统未提供 Windows 快捷方式组件。");

            object shell = Activator.CreateInstance(shellType);
            object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { Application.ExecutablePath });
                shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { System.IO.Path.GetDirectoryName(Application.ExecutablePath) });
                shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "图片与 PDF 本地转换" });
                shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        public static void Remove()
        {
            if (File.Exists(ShortcutPath))
                File.Delete(ShortcutPath);
        }

        public static bool Exists()
        {
            return File.Exists(ShortcutPath);
        }
    }

    internal sealed class NaturalComparer : IComparer<ImageItem>
    {
        private static readonly Regex NumberPattern = new Regex("(\\d+)", RegexOptions.Compiled);

        public int Compare(ImageItem left, ImageItem right)
        {
            string a = left == null ? "" : left.FileName;
            string b = right == null ? "" : right.FileName;
            MatchCollection am = NumberPattern.Matches(a);
            MatchCollection bm = NumberPattern.Matches(b);
            int positionA = 0;
            int positionB = 0;
            int count = Math.Min(am.Count, bm.Count);
            for (int index = 0; index < count; index++)
            {
                int textCompare = StringComparer.CurrentCultureIgnoreCase.Compare(a.Substring(positionA, am[index].Index - positionA), b.Substring(positionB, bm[index].Index - positionB));
                if (textCompare != 0) return textCompare;
                long numberA;
                long numberB;
                if (!Int64.TryParse(am[index].Value, out numberA) || !Int64.TryParse(bm[index].Value, out numberB))
                    continue;
                if (numberA != numberB) return numberA < numberB ? -1 : 1;
                positionA = am[index].Index + am[index].Length;
                positionB = bm[index].Index + bm[index].Length;
            }
            return StringComparer.CurrentCultureIgnoreCase.Compare(a.Substring(positionA), b.Substring(positionB));
        }
    }

    internal sealed class SharpPreviewBox : PictureBox
    {
        public float Zoom { get; set; }

        public SharpPreviewBox()
        {
            DoubleBuffered = true;
            SizeMode = PictureBoxSizeMode.Normal;
            Zoom = 1f;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (Image == null)
                return;

            Rectangle area = new Rectangle(
                Padding.Left,
                Padding.Top,
                Math.Max(1, ClientSize.Width - Padding.Horizontal),
                Math.Max(1, ClientSize.Height - Padding.Vertical));
            float scale = Math.Min((float)area.Width / Image.Width, (float)area.Height / Image.Height) * Math.Max(0.1f, Zoom);
            int width = Math.Max(1, (int)Math.Round(Image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(Image.Height * scale));
            Rectangle destination = new Rectangle(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                width,
                height);

            Graphics graphics = e.Graphics;
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(Image, destination, 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel);
        }
    }

    internal sealed class LargePreviewForm : AdaptiveForm
    {
        private readonly Bitmap _image;
        private readonly SharpPreviewBox _preview;

        protected override Size MinimumLogicalSize
        {
            get { return new Size(600, 420); }
        }

        public LargePreviewForm(string fileName, Bitmap image, Icon applicationIcon)
        {
            _image = image;
            Text = "预览 - " + fileName;
            Icon = applicationIcon;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1100, 760);
            MinimumSize = new Size(640, 480);
            BackColor = Color.FromArgb(35, 38, 45);
            KeyPreview = true;
            _preview = new SharpPreviewBox
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28),
                BackColor = Color.FromArgb(35, 38, 45),
                Image = image,
                Cursor = Cursors.Hand
            };
            _preview.MouseEnter += delegate { _preview.Focus(); };
            _preview.MouseWheel += delegate (object sender, MouseEventArgs args)
            {
                _preview.Zoom = Math.Max(1f, Math.Min(4f, _preview.Zoom + (args.Delta > 0 ? 0.25f : -0.25f)));
                _preview.Invalidate();
            };
            Controls.Add(_preview);
            KeyDown += delegate (object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Escape)
                    Close();
            };
            FormClosed += delegate
            {
                _preview.Image = null;
                _image.Dispose();
            };
        }

        protected override void ApplyAdaptiveLayout()
        {
            if (_preview != null) _preview.Padding = new Padding(ScaleLogical(28));
        }
    }

    internal sealed class ImageCard : Panel
    {
        private readonly IImageCardOwner _owner;
        private readonly ImageItem _item;
        private Control _dragControl;
        private Point _dragStart;
        private bool _dragging;

        public ImageCard(IImageCardOwner owner, ImageItem item)
        {
            _owner = owner;
            _item = item;
            Width = 340;
            Height = 560;
            Margin = new Padding(10);
            BackColor = Color.White;
            BorderStyle = BorderStyle.FixedSingle;

            SharpPreviewBox preview = new SharpPreviewBox
            {
                Dock = DockStyle.Top,
                Height = 410,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(250, 250, 250),
                Image = item.Preview,
                Cursor = Cursors.Hand
            };
            preview.Click += delegate { _owner.ShowPreview(_item); };
            Label label = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Text = item.PreviewError == null ? item.FileName : item.FileName + "（无法读取）",
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(55, 65, 81),
                Padding = new Padding(6, 0, 6, 0)
            };
            if (item.PreviewError != null)
                label.ForeColor = Color.FromArgb(185, 28, 28);
            TextBox outputName = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 30,
                Text = item.OutputName,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(31, 41, 55),
                Padding = new Padding(5, 4, 5, 2),
                AccessibleName = "PDF 输出文件名",
                AccessibleDescription = "一图一个 PDF 模式使用的输出文件名，不含扩展名"
            };
            outputName.TextChanged += delegate { _item.OutputName = outputName.Text; };
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 46,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(8, 6, 8, 5)
            };
            Button left = MakeActionButton("↶");
            Button right = MakeActionButton("↷");
            Button remove = MakeActionButton("删除");
            left.Click += delegate { _owner.RotateItem(_item, -90); };
            right.Click += delegate { _owner.RotateItem(_item, 90); };
            remove.Click += delegate { _owner.RemoveItem(_item); };
            actions.Controls.Add(left);
            actions.Controls.Add(right);
            actions.Controls.Add(remove);

            Controls.Add(actions);
            Controls.Add(outputName);
            Controls.Add(label);
            Controls.Add(preview);
            AttachDrag(this);
            AttachDrag(preview);
            AttachDrag(label);
        }

        private static Button MakeActionButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = text == "删除" ? 72 : 46,
                Height = 29,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(247, 248, 250),
                FlatAppearance = { BorderColor = Color.FromArgb(220, 224, 230) }
            };
        }

        private void AttachDrag(Control control)
        {
            control.MouseDown += delegate (object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left)
                {
                    _dragControl = control;
                    _dragStart = args.Location;
                    _dragging = false;
                }
            };
            control.MouseMove += delegate (object sender, MouseEventArgs args)
            {
                if (_dragControl != control || _dragging || (Control.MouseButtons & MouseButtons.Left) == 0)
                    return;
                Rectangle dragRectangle = new Rectangle(
                    _dragStart.X - SystemInformation.DragSize.Width / 2,
                    _dragStart.Y - SystemInformation.DragSize.Height / 2,
                    SystemInformation.DragSize.Width,
                    SystemInformation.DragSize.Height);
                if (dragRectangle.Contains(args.Location))
                    return;
                _dragging = true;
                try { DoDragDrop(_item, DragDropEffects.Move); }
                finally { _dragging = false; }
            };
            control.MouseUp += delegate
            {
                if (_dragControl == control)
                {
                    _dragControl = null;
                    _dragging = false;
                }
            };
        }
    }

    internal sealed class LegacyMainForm : AdaptiveForm, IImageCardOwner
    {
        private const int PreviewPortraitWidth = 360;
        private const int PreviewPortraitHeight = 510;
        private const int PreviewLandscapeWidth = 510;
        private const int PreviewLandscapeHeight = 360;
        private readonly string[] _startupArgs;
        private readonly List<ImageItem> _items = new List<ImageItem>();
        private readonly HashSet<string> _paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private FlowLayoutPanel _cards;
        private Label _countLabel;
        private Label _statusLabel;
        private ComboBox _paperCombo;
        private ComboBox _orientationCombo;
        private CheckBox _autoRotateCheck;
        private ComboBox _marginCombo;
        private ComboBox _qualityCombo;
        private ComboBox _modeCombo;
        private TextBox _batchNameBox;
        private TextBox _fileNameBox;
        private Button _exportButton;
        private Button _cancelButton;
        private CancellationTokenSource _cancellation;
        private bool _refreshing;

        public LegacyMainForm(string[] startupArgs)
        {
            _startupArgs = startupArgs ?? new string[0];
            Text = "图片转PDF";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1240, 820);
            MinimumSize = new Size(980, 650);
            BackColor = Color.FromArgb(244, 246, 249);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AllowDrop = true;
            BuildUi();
            DragEnter += HandleDragEnter;
            DragDrop += HandleDragDrop;
            Shown += delegate { AddFiles(_startupArgs); };
        }

        public void RotateItem(ImageItem item, int delta)
        {
            if (item == null) return;
            item.ManualRotation = ImageTools.NormalizeRotation(item.ManualRotation + delta);
            RefreshCards();
        }

        public void ShowPreview(ImageItem item)
        {
            if (item == null) return;
            Bitmap display = null;
            try
            {
                bool autoRotate = _autoRotateCheck != null && _autoRotateCheck.Checked;
                using (Bitmap source = ImageTools.LoadTransformed(item, autoRotate))
                {
                    display = ImageTools.RenderImage(source, source.Width, source.Height);
                }
                using (LargePreviewForm dialog = new LargePreviewForm(item.FileName, display, Icon))
                {
                    display = null;
                    dialog.ShowDialog(this);
                }
            }
            catch (Exception error)
            {
                if (display != null) display.Dispose();
                MessageBox.Show(this, "无法打开大图预览：" + error.Message, "预览失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void RemoveItem(ImageItem item)
        {
            if (item == null) return;
            _items.Remove(item);
            _paths.Remove(item.Path);
            item.DisposePreview();
            RefreshCards();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = BackColor
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 355f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            Panel header = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 12, 8) };
            _countLabel = new Label { Text = "共 0 页", AutoSize = true, Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular), ForeColor = Color.FromArgb(31, 41, 55), Location = new Point(20, 19) };
            FlowLayoutPanel headerActions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 470, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button add = MakeHeaderButton("添加图片");
            Button sort = MakeHeaderButton("排序 ▾");
            Button clear = MakeHeaderButton("清空");
            add.Click += delegate { ChooseFiles(); };
            ContextMenuStrip sortMenu = new ContextMenuStrip();
            AddSortMenuItem(sortMenu, "文件名（升序）", SortMode.NameAscending);
            AddSortMenuItem(sortMenu, "文件名（降序）", SortMode.NameDescending);
            sortMenu.Items.Add(new ToolStripSeparator());
            AddSortMenuItem(sortMenu, "文件大小（大 → 小）", SortMode.SizeDescending);
            AddSortMenuItem(sortMenu, "文件大小（小 → 大）", SortMode.SizeAscending);
            AddSortMenuItem(sortMenu, "修改日期（新 → 旧）", SortMode.ModifiedDescending);
            AddSortMenuItem(sortMenu, "修改日期（旧 → 新）", SortMode.ModifiedAscending);
            AddSortMenuItem(sortMenu, "最近加入（新 → 旧）", SortMode.AddedDescending);
            AddSortMenuItem(sortMenu, "最近加入（旧 → 新）", SortMode.AddedAscending);
            sort.Click += delegate { sortMenu.Show(sort, 0, sort.Height); };
            clear.Click += delegate { ClearItems(); };
            headerActions.Controls.Add(add);
            headerActions.Controls.Add(clear);
            headerActions.Controls.Add(sort);
            header.Controls.Add(_countLabel);
            header.Controls.Add(headerActions);
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 2);

            _cards = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                AllowDrop = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(14),
                BackColor = Color.FromArgb(244, 246, 249)
            };
            _cards.DragEnter += CardsDragEnter;
            _cards.DragOver += CardsDragOver;
            _cards.DragDrop += CardsDragDrop;
            Panel leftArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 8) };
            leftArea.Controls.Add(_cards);
            root.Controls.Add(leftArea, 0, 1);

            Panel settings = BuildSettingsPanel();
            root.Controls.Add(settings, 1, 1);
            EnableExternalDrop(root);
        }

        private Panel BuildSettingsPanel()
        {
            Panel outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 12, 8), BackColor = Color.FromArgb(244, 246, 249) };
            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20, 18, 20, 16), AutoScroll = true };
            outer.Controls.Add(panel);
            int y = 12;
            Label title = MakeSectionTitle("输出设置");
            title.Location = new Point(20, y);
            panel.Controls.Add(title);
            y += 48;
            Label paperLabel = MakeFieldLabel("纸张大小");
            paperLabel.Location = new Point(20, y + 5);
            panel.Controls.Add(paperLabel);
            _paperCombo = MakeCombo(PaperSizes.DisplayNames);
            _paperCombo.SelectedIndex = 0;
            _paperCombo.Location = new Point(115, y);
            _paperCombo.SelectedIndexChanged += SettingsChanged;
            panel.Controls.Add(_paperCombo);
            y += 48;

            Label orientationLabel = MakeFieldLabel("纸张方向");
            orientationLabel.Location = new Point(20, y + 5);
            panel.Controls.Add(orientationLabel);
            _orientationCombo = MakeCombo(new[] { "竖向", "横向" });
            _orientationCombo.Location = new Point(115, y);
            _orientationCombo.SelectedIndex = 0;
            _orientationCombo.SelectedIndexChanged += SettingsChanged;
            panel.Controls.Add(_orientationCombo);
            y += 48;

            _autoRotateCheck = new CheckBox { Text = "横图自动转正（顺时针 90°）", Checked = true, AutoSize = true, Location = new Point(20, y + 2), ForeColor = Color.FromArgb(55, 65, 81) };
            _autoRotateCheck.CheckedChanged += SettingsChanged;
            panel.Controls.Add(_autoRotateCheck);
            y += 42;

            Label marginLabel = MakeFieldLabel("页面边距");
            marginLabel.Location = new Point(20, y + 5);
            panel.Controls.Add(marginLabel);
            _marginCombo = MakeCombo(new[] { "无边距（0 mm）", "窄边距（5 mm）", "标准边距（10 mm）" });
            _marginCombo.SelectedIndex = 2;
            _marginCombo.Location = new Point(115, y);
            _marginCombo.SelectedIndexChanged += SettingsChanged;
            panel.Controls.Add(_marginCombo);
            y += 48;

            Label qualityLabel = MakeFieldLabel("输出质量");
            qualityLabel.Location = new Point(20, y + 5);
            panel.Controls.Add(qualityLabel);
            _qualityCombo = MakeCombo(new[] { "清晰打印（300 DPI）", "标准（220 DPI）", "小文件（150 DPI）" });
            _qualityCombo.SelectedIndex = 0;
            _qualityCombo.Location = new Point(115, y);
            panel.Controls.Add(_qualityCombo);
            y += 48;

            Label modeLabel = MakeFieldLabel("导出方式");
            modeLabel.Location = new Point(20, y + 5);
            panel.Controls.Add(modeLabel);
            _modeCombo = MakeCombo(new[] { "合并为一个 PDF", "一图一个 PDF" });
            _modeCombo.SelectedIndex = 0;
            _modeCombo.Location = new Point(115, y);
            panel.Controls.Add(_modeCombo);
            y += 48;

            Label batchLabel = MakeFieldLabel("逐页批量命名");
            batchLabel.Location = new Point(20, y + 5);
            panel.Controls.Add(batchLabel);
            _batchNameBox = new TextBox { Location = new Point(115, y), Width = 140, Text = "图片", BorderStyle = BorderStyle.FixedSingle };
            panel.Controls.Add(_batchNameBox);
            Button applyBatch = new Button { Text = "应用", Location = new Point(260, y - 1), Width = 45, Height = 28, FlatStyle = FlatStyle.Flat };
            applyBatch.Click += ApplyBatchNamesClicked;
            panel.Controls.Add(applyBatch);
            Label batchHint = new Label
            {
                Text = "用于一图一个 PDF；应用后可逐张修改",
                AutoSize = false,
                Width = 285,
                Height = 34,
                Font = new Font("Microsoft YaHei UI", 8.5f),
                ForeColor = Color.FromArgb(107, 114, 128),
                Location = new Point(20, y + 32),
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel.Controls.Add(batchHint);
            y += 78;

            Label nameLabel = MakeFieldLabel("合并文件名");
            nameLabel.Location = new Point(20, y + 5);
            panel.Controls.Add(nameLabel);
            _fileNameBox = new TextBox { Location = new Point(115, y), Width = 190, Text = "图片合并_" + DateTime.Now.ToString("yyyyMMdd_HHmm"), BorderStyle = BorderStyle.FixedSingle };
            panel.Controls.Add(_fileNameBox);
            y += 58;

            _statusLabel = new Label { Text = "可添加图片开始转换", AutoSize = false, Width = 305, Height = 42, ForeColor = Color.FromArgb(107, 114, 128), Location = new Point(20, y), AutoEllipsis = true };
            panel.Controls.Add(_statusLabel);
            y += 50;

            _exportButton = new Button { Text = "导出 PDF", Location = new Point(20, y), Width = 305, Height = 43, BackColor = Color.FromArgb(79, 70, 229), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold) };
            _exportButton.FlatAppearance.BorderSize = 0;
            _exportButton.Click += ExportClicked;
            panel.Controls.Add(_exportButton);
            y += 50;
            _cancelButton = new Button { Text = "取消导出", Location = new Point(20, y), Width = 305, Height = 36, Visible = false, FlatStyle = FlatStyle.Flat };
            _cancelButton.Click += delegate { if (_cancellation != null) _cancellation.Cancel(); };
            panel.Controls.Add(_cancelButton);
            y += 54;

            Label menuTitle = MakeSectionTitle("右键入口");
            menuTitle.Location = new Point(20, y);
            panel.Controls.Add(menuTitle);
            y += 42;
            Button addMenu = new Button { Text = "添加到“发送到”菜单", Location = new Point(20, y), Width = 145, Height = 34, FlatStyle = FlatStyle.Flat };
            Button removeMenu = new Button { Text = "移除右键入口", Location = new Point(180, y), Width = 145, Height = 34, FlatStyle = FlatStyle.Flat };
            addMenu.Click += AddSendToClicked;
            removeMenu.Click += RemoveSendToClicked;
            panel.Controls.Add(addMenu);
            panel.Controls.Add(removeMenu);
            return outer;
        }

        private void ApplyBatchNamesClicked(object sender, EventArgs e)
        {
            string baseName = _batchNameBox == null ? "图片" : _batchNameBox.Text.Trim();
            if (String.IsNullOrWhiteSpace(baseName))
                baseName = "图片";
            for (int index = 0; index < _items.Count; index++)
                _items[index].OutputName = baseName + "_" + (index + 1).ToString("00");
            RefreshCards();
        }

        private void AddSortMenuItem(ContextMenuStrip menu, string text, SortMode mode)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += delegate { SortItems(mode); };
            menu.Items.Add(item);
        }

        private void SortItems(SortMode mode)
        {
            NaturalComparer natural = new NaturalComparer();
            _items.Sort(delegate (ImageItem left, ImageItem right)
            {
                int result = CompareBySortMode(left, right, mode);
                if (result != 0)
                    return result;
                result = natural.Compare(left, right);
                if (result != 0)
                    return result;
                return left.AddedOrder.CompareTo(right.AddedOrder);
            });
            RefreshCards();
        }

        private static int CompareBySortMode(ImageItem left, ImageItem right, SortMode mode)
        {
            if (mode == SortMode.NameAscending)
                return new NaturalComparer().Compare(left, right);
            if (mode == SortMode.NameDescending)
                return new NaturalComparer().Compare(right, left);

            FileInfo leftInfo = TryGetFileInfo(left == null ? null : left.Path);
            FileInfo rightInfo = TryGetFileInfo(right == null ? null : right.Path);
            if (leftInfo == null && rightInfo != null) return 1;
            if (leftInfo != null && rightInfo == null) return -1;
            if (leftInfo != null && rightInfo != null)
            {
                switch (mode)
                {
                    case SortMode.SizeDescending:
                        return rightInfo.Length.CompareTo(leftInfo.Length);
                    case SortMode.SizeAscending:
                        return leftInfo.Length.CompareTo(rightInfo.Length);
                    case SortMode.ModifiedDescending:
                        return rightInfo.LastWriteTime.CompareTo(leftInfo.LastWriteTime);
                    case SortMode.ModifiedAscending:
                        return leftInfo.LastWriteTime.CompareTo(rightInfo.LastWriteTime);
                }
            }
            if (mode == SortMode.AddedDescending)
                return right.AddedOrder.CompareTo(left.AddedOrder);
            if (mode == SortMode.AddedAscending)
                return left.AddedOrder.CompareTo(right.AddedOrder);
            return 0;
        }

        private static FileInfo TryGetFileInfo(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            try
            {
                return File.Exists(path) ? new FileInfo(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private void ChooseFiles()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择图片";
                dialog.Multiselect = true;
                dialog.Filter = "支持的图片|*.jpg;*.jpeg;*.png;*.bmp|JPG|*.jpg;*.jpeg|PNG|*.png|BMP|*.bmp";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    AddFiles(dialog.FileNames);
            }
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            List<string> rejected = new List<string>();
            int added = 0;
            foreach (string rawPath in paths ?? new string[0])
            {
                if (String.IsNullOrWhiteSpace(rawPath)) continue;
                string path;
                try { path = System.IO.Path.GetFullPath(rawPath); } catch { rejected.Add(rawPath + "（路径无效）"); continue; }
                if (!File.Exists(path)) { rejected.Add(System.IO.Path.GetFileName(path) + "（文件不存在）"); continue; }
                if (!ImageTools.IsSupportedPath(path)) { rejected.Add(System.IO.Path.GetFileName(path) + "（不支持的格式）"); continue; }
                if (_paths.Contains(path)) continue;
                try
                {
                    using (Image image = Image.FromFile(path))
                    {
                        if (image.Width <= 0 || image.Height <= 0) throw new InvalidDataException("尺寸无效");
                    }
                    ImageItem item = new ImageItem(path);
                    _items.Add(item);
                    _paths.Add(path);
                    added++;
                }
                catch (Exception error)
                {
                    rejected.Add(System.IO.Path.GetFileName(path) + "（无法读取：" + error.Message + "）");
                }
            }
            if (added > 0)
                RefreshCards();
            if (rejected.Count > 0)
            {
                StringBuilder message = new StringBuilder();
                message.AppendLine("以下文件未加入：");
                int limit = Math.Min(20, rejected.Count);
                for (int index = 0; index < limit; index++) message.AppendLine("• " + rejected[index]);
                if (rejected.Count > limit) message.AppendLine("……另有 " + (rejected.Count - limit).ToString() + " 个文件。");
                MessageBox.Show(this, message.ToString(), "图片导入提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ClearItems()
        {
            foreach (ImageItem item in _items) item.DisposePreview();
            _items.Clear();
            _paths.Clear();
            RefreshCards();
        }

        private void RefreshCards()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                _cards.SuspendLayout();
                foreach (Control control in _cards.Controls) control.Dispose();
                _cards.Controls.Clear();
                PaperSizeKind paperSize = GetPaperSize();
                PageOrientation orientation = GetOrientation();
                bool autoRotate = _autoRotateCheck != null && _autoRotateCheck.Checked;
                int margin = GetMarginMm();
                int previewWidth;
                int previewHeight;
                GetPreviewDimensions(paperSize, orientation, out previewWidth, out previewHeight);
                Cursor = Cursors.WaitCursor;
                foreach (ImageItem item in _items)
                {
                    item.DisposePreview();
                    try
                    {
                        item.PreviewError = null;
                        item.Preview = ImageTools.RenderPagePreview(item, paperSize, orientation, autoRotate, margin, previewWidth, previewHeight);
                    }
                    catch (Exception error)
                    {
                        item.PreviewError = error.Message;
                        item.Preview = new Bitmap(previewWidth, previewHeight, PixelFormat.Format24bppRgb);
                        using (Graphics graphics = Graphics.FromImage(item.Preview)) { graphics.Clear(Color.White); }
                    }
                    ImageCard card = new ImageCard(this, item);
                    EnableExternalDrop(card);
                    _cards.Controls.Add(card);
                }
            }
            finally
            {
                Cursor = Cursors.Default;
                _cards.ResumeLayout();
                _refreshing = false;
                UpdateCount();
            }
        }

        private void ExportClicked(object sender, EventArgs e)
        {
            if (_cancellation != null) return;
            if (_items.Count == 0)
            {
                MessageBox.Show(this, "请先添加图片。", "无法导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<string> unavailable = new List<string>();
            foreach (ImageItem item in _items)
            {
                if (!File.Exists(item.Path)) unavailable.Add(item.FileName);
            }
            if (unavailable.Count > 0)
            {
                MessageBox.Show(this, "以下源文件已经不存在，请重新添加后再导出：\n\n" + String.Join("\n", unavailable.ToArray()), "无法导出", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ExportOptions options = GetOptions();
            string target = null;
            string folder = null;
            if (options.Mode == ExportMode.Merge)
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "保存合并 PDF";
                    dialog.Filter = "PDF 文件|*.pdf";
                    dialog.FileName = EnsurePdfExtension(options.BaseName);
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    target = dialog.FileName;
                }
            }
            else
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "选择逐页 PDF 的输出文件夹";
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    folder = dialog.SelectedPath;
                }
            }

            List<ImageSnapshot> snapshots = new List<ImageSnapshot>();
            foreach (ImageItem item in _items)
            {
                snapshots.Add(new ImageSnapshot { Path = item.Path, ManualRotation = item.ManualRotation, OutputName = item.OutputName });
            }
            _cancellation = new CancellationTokenSource();
            _exportButton.Enabled = false;
            _cancelButton.Visible = true;
            _statusLabel.Text = "正在导出 0%...";
            CancellationToken token = _cancellation.Token;
            Task.Run(delegate
            {
                try
                {
                    Action<int> progress = delegate (int value)
                    {
                        if (!IsDisposed) BeginInvoke((Action)delegate { _statusLabel.Text = "正在导出 " + value.ToString() + "%..."; });
                    };
                    if (options.Mode == ExportMode.Merge)
                        PdfExporter.ExportMerged(target, snapshots, options, progress, token);
                    else
                        PdfExporter.ExportSeparate(folder, snapshots, options, progress, token);
                    if (!IsDisposed) BeginInvoke((Action)delegate
                    {
                        _statusLabel.Text = "导出完成。";
                        MessageBox.Show(this, "PDF 已成功导出。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                }
                catch (OperationCanceledException)
                {
                    if (!IsDisposed) BeginInvoke((Action)delegate { _statusLabel.Text = "已取消导出。"; });
                }
                catch (Exception error)
                {
                    if (!IsDisposed) BeginInvoke((Action)delegate { _statusLabel.Text = "导出失败。"; MessageBox.Show(this, error.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); });
                }
                finally
                {
                    if (!IsDisposed) BeginInvoke((Action)delegate { _cancelButton.Visible = false; _exportButton.Enabled = true; _cancellation.Dispose(); _cancellation = null; });
                }
            });
        }

        private ExportOptions GetOptions()
        {
            return new ExportOptions
            {
                PaperSize = GetPaperSize(),
                Orientation = GetOrientation(),
                AutoRotate = _autoRotateCheck.Checked,
                MarginMm = GetMarginMm(),
                Quality = (QualityPreset)_qualityCombo.SelectedIndex,
                Mode = _modeCombo.SelectedIndex == 1 ? ExportMode.Separate : ExportMode.Merge,
                BaseName = String.IsNullOrWhiteSpace(_fileNameBox.Text) ? "图片合并_" + DateTime.Now.ToString("yyyyMMdd_HHmm") : _fileNameBox.Text.Trim()
            };
        }

        private PageOrientation GetOrientation()
        {
            return _orientationCombo != null && _orientationCombo.SelectedIndex == 1 ? PageOrientation.Landscape : PageOrientation.Portrait;
        }

        private PaperSizeKind GetPaperSize()
        {
            if (_paperCombo == null || _paperCombo.SelectedIndex < 0)
                return PaperSizeKind.A4;
            return (PaperSizeKind)Math.Max(0, Math.Min(PaperSizes.DisplayNames.Length - 1, _paperCombo.SelectedIndex));
        }

        private void GetPreviewDimensions(PaperSizeKind paperSize, PageOrientation orientation, out int width, out int height)
        {
            int longSide;
            if (_items.Count <= 12)
                longSide = 1018;
            else if (_items.Count <= 60)
                longSide = 679;
            else
                longSide = PreviewPortraitHeight;

            float paperWidth = PaperSizes.GetWidthMm(paperSize);
            float paperHeight = PaperSizes.GetHeightMm(paperSize);
            if (orientation == PageOrientation.Landscape)
            {
                float swap = paperWidth;
                paperWidth = paperHeight;
                paperHeight = swap;
            }
            if (paperWidth >= paperHeight)
            {
                width = longSide;
                height = Math.Max(1, (int)Math.Round(longSide * paperHeight / paperWidth));
            }
            else
            {
                height = longSide;
                width = Math.Max(1, (int)Math.Round(longSide * paperWidth / paperHeight));
            }
        }

        private int GetMarginMm()
        {
            return _marginCombo == null ? 10 : (_marginCombo.SelectedIndex == 0 ? 0 : (_marginCombo.SelectedIndex == 1 ? 5 : 10));
        }

        private static string EnsurePdfExtension(string name)
        {
            return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
        }

        private void SettingsChanged(object sender, EventArgs e)
        {
            if (!_refreshing) RefreshCards();
        }

        private void UpdateCount()
        {
            if (_countLabel != null) _countLabel.Text = "共 " + _items.Count.ToString() + " 页";
            if (_statusLabel != null && _cancellation == null) _statusLabel.Text = _items.Count == 0 ? "可添加图片开始转换" : "已准备 " + _items.Count.ToString() + " 张图片";
        }

        private void EnableExternalDrop(Control control)
        {
            if (control == null) return;
            if (control != _cards)
            {
                control.AllowDrop = true;
                control.DragEnter += HandleDragEnter;
                control.DragDrop += HandleDragDrop;
            }
            foreach (Control child in control.Controls)
                EnableExternalDrop(child);
        }

        private void HandleDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else if (e.Data.GetDataPresent(typeof(ImageItem)))
                e.Effect = DragDropEffects.Move;
            else
                e.Effect = DragDropEffects.None;
        }

        private void HandleDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(ImageItem)))
            {
                CardsDragDrop(_cards, e);
                return;
            }
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) AddFiles(files);
        }

        private void CardsDragEnter(object sender, DragEventArgs e)
        {
            HandleDragEnter(sender, e);
        }

        private void CardsDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else if (e.Data.GetDataPresent(typeof(ImageItem)))
                e.Effect = DragDropEffects.Move;
            else
                e.Effect = DragDropEffects.None;
        }

        private void CardsDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null)
            {
                AddFiles(files);
                return;
            }
            ImageItem item = e.Data.GetData(typeof(ImageItem)) as ImageItem;
            if (item == null) return;
            Point location = _cards.PointToClient(new Point(e.X, e.Y));
            int targetIndex = _items.Count - 1;
            for (int index = 0; index < _cards.Controls.Count; index++)
            {
                if (_cards.Controls[index].Bounds.Contains(location)) { targetIndex = index; break; }
            }
            int sourceIndex = _items.IndexOf(item);
            if (sourceIndex < 0 || sourceIndex == targetIndex) return;
            _items.RemoveAt(sourceIndex);
            if (sourceIndex < targetIndex) targetIndex--;
            targetIndex = Math.Max(0, Math.Min(targetIndex, _items.Count));
            _items.Insert(targetIndex, item);
            RefreshCards();
        }

        private void AddSendToClicked(object sender, EventArgs e)
        {
            try { SendToManager.Add(); MessageBox.Show(this, "已添加到当前用户的“发送到”菜单。", "右键入口", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "添加失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void RemoveSendToClicked(object sender, EventArgs e)
        {
            try { SendToManager.Remove(); MessageBox.Show(this, "已移除右键入口。", "右键入口", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "移除失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static Button MakeHeaderButton(string text)
        {
            return new Button { Text = text, Width = 104, Height = 34, Margin = new Padding(6, 0, 0, 0), FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        }

        private static Label MakeSectionTitle(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(17, 24, 39) };
        }

        private static Label MakeFieldLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Color.FromArgb(75, 85, 99) };
        }

        private static ComboBox MakeCombo(string[] values)
        {
            ComboBox combo = new ComboBox { Width = 190, Height = 28, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            combo.Items.AddRange(values);
            return combo;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cancellation != null) _cancellation.Cancel();
            foreach (ImageItem item in _items) item.DisposePreview();
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        internal static bool ShouldLaunchPdfToImages(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            bool hasPdf = false;
            foreach (string rawPath in args)
            {
                if (String.IsNullOrWhiteSpace(rawPath)) return false;
                string path;
                try { path = Path.GetFullPath(rawPath); }
                catch { return false; }
                if (!File.Exists(path) || !PdfToImageExporter.IsSupportedPath(path)) return false;
                hasPdf = true;
            }
            return hasPdf;
        }

        internal static bool ShouldShowSendToOnboarding(AppSettings settings, bool shortcutExists)
        {
            return settings != null && !settings.SendToOnboardingCompleted && !shortcutExists;
        }

        [STAThread]
        private static void Main(string[] args)
        {
            if (PdfToImageCommandLine.TryRun(args)) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Icon startupIcon = null;
            try { startupIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            try
            {
                AppSettings settings = AppSettingsStore.Load();
                if (ShouldShowSendToOnboarding(settings, SendToManager.Exists()))
                {
                    using (SendToOnboardingForm onboarding = new SendToOnboardingForm(startupIcon))
                        onboarding.ShowDialog();
                }

                if (ShouldLaunchPdfToImages(args))
                {
                    using (PdfToImageForm form = new PdfToImageForm(args, startupIcon))
                    {
                        form.StartPosition = FormStartPosition.CenterScreen;
                        Application.Run(form);
                    }
                    return;
                }
            }
            finally
            {
                if (startupIcon != null) startupIcon.Dispose();
            }

            Application.Run(new MainForm(args));
        }
    }
}
