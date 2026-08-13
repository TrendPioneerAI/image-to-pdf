using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LocalImageToPdf
{
    internal interface IImageCardOwner
    {
        void RotateItem(ImageItem item, int delta);
        void ShowPreview(ImageItem item);
        void RemoveItem(ImageItem item);
    }

    internal enum PdfImageEncoding
    {
        Jpeg,
        LosslessRgb
    }

    internal sealed class JpegMetadata
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Components { get; set; }
        public int ExifOrientation { get; set; }
        public long Length { get; set; }
    }

    internal struct UnitMatrix
    {
        public double A;
        public double B;
        public double C;
        public double D;
        public double E;
        public double F;

        public static UnitMatrix Identity
        {
            get { return new UnitMatrix { A = 1, D = 1 }; }
        }

        public static UnitMatrix Compose(UnitMatrix after, UnitMatrix before)
        {
            return new UnitMatrix
            {
                A = after.A * before.A + after.C * before.B,
                B = after.B * before.A + after.D * before.B,
                C = after.A * before.C + after.C * before.D,
                D = after.B * before.C + after.D * before.D,
                E = after.A * before.E + after.C * before.F + after.E,
                F = after.B * before.E + after.D * before.F + after.F
            };
        }
    }

    internal sealed class PreparedPage : IDisposable
    {
        public string DirectJpegPath { get; set; }
        public byte[] ImageData { get; set; }
        public PdfImageEncoding Encoding { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Components { get; set; }
        public PageLayout Layout { get; set; }
        public UnitMatrix ImageMatrix { get; set; }

        public void Dispose()
        {
            ImageData = null;
        }
    }

    internal sealed class WatermarkAsset
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] RgbData { get; set; }
        public byte[] AlphaData { get; set; }
        public WatermarkOptions Options { get; set; }
    }

    internal static class JpegInspector
    {
        public static bool TryRead(string path, out JpegMetadata metadata)
        {
            metadata = null;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
                {
                    if (ReadByte(stream) != 0xFF || ReadByte(stream) != 0xD8)
                        return false;

                    int width = 0;
                    int height = 0;
                    int components = 0;
                    int precision = 0;
                    int orientation = 1;
                    while (stream.Position < stream.Length)
                    {
                        int prefix;
                        do { prefix = ReadByte(stream); } while (prefix != 0xFF && stream.Position < stream.Length);
                        int marker;
                        do { marker = ReadByte(stream); } while (marker == 0xFF && stream.Position < stream.Length);
                        if (marker < 0 || marker == 0xD9 || marker == 0xDA)
                            break;
                        if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8))
                            continue;

                        int segmentLength = ReadUInt16BigEndian(stream);
                        if (segmentLength < 2 || stream.Position + segmentLength - 2 > stream.Length)
                            return false;
                        int dataLength = segmentLength - 2;
                        long dataStart = stream.Position;

                        if (IsStartOfFrame(marker) && dataLength >= 6)
                        {
                            precision = ReadByte(stream);
                            height = ReadUInt16BigEndian(stream);
                            width = ReadUInt16BigEndian(stream);
                            components = ReadByte(stream);
                        }
                        else if (marker == 0xE1 && dataLength >= 14)
                        {
                            byte[] app1 = new byte[dataLength];
                            int read = stream.Read(app1, 0, app1.Length);
                            if (read == app1.Length)
                                orientation = ReadExifOrientation(app1);
                        }
                        stream.Position = dataStart + dataLength;
                    }

                    if (width <= 0 || height <= 0 || precision != 8 || (components != 1 && components != 3) || !HasEndOfImage(stream))
                        return false;
                    metadata = new JpegMetadata
                    {
                        Width = width,
                        Height = height,
                        Components = components,
                        ExifOrientation = orientation,
                        Length = stream.Length
                    };
                    return true;
                }
            }
            catch
            {
                metadata = null;
                return false;
            }
        }

        private static bool IsStartOfFrame(int marker)
        {
            return (marker >= 0xC0 && marker <= 0xC3) ||
                   (marker >= 0xC5 && marker <= 0xC7) ||
                   (marker >= 0xC9 && marker <= 0xCB) ||
                   (marker >= 0xCD && marker <= 0xCF);
        }

        private static int ReadExifOrientation(byte[] bytes)
        {
            try
            {
                if (bytes.Length < 14 || bytes[0] != (byte)'E' || bytes[1] != (byte)'x' || bytes[2] != (byte)'i' || bytes[3] != (byte)'f')
                    return 1;
                int tiff = 6;
                bool little = bytes[tiff] == (byte)'I' && bytes[tiff + 1] == (byte)'I';
                bool big = bytes[tiff] == (byte)'M' && bytes[tiff + 1] == (byte)'M';
                if (!little && !big) return 1;
                uint ifdOffset = ReadUInt32(bytes, tiff + 4, little);
                int ifd = checked(tiff + (int)ifdOffset);
                if (ifd < 0 || ifd + 2 > bytes.Length) return 1;
                int count = ReadUInt16(bytes, ifd, little);
                for (int index = 0; index < count; index++)
                {
                    int entry = ifd + 2 + index * 12;
                    if (entry + 12 > bytes.Length) break;
                    int tag = ReadUInt16(bytes, entry, little);
                    if (tag != 0x0112) continue;
                    int type = ReadUInt16(bytes, entry + 2, little);
                    uint valueCount = ReadUInt32(bytes, entry + 4, little);
                    if (type != 3 || valueCount < 1) return 1;
                    int value = ReadUInt16(bytes, entry + 8, little);
                    return value >= 1 && value <= 8 ? value : 1;
                }
            }
            catch { }
            return 1;
        }

        private static int ReadUInt16(byte[] bytes, int offset, bool little)
        {
            return little ? bytes[offset] | (bytes[offset + 1] << 8) : (bytes[offset] << 8) | bytes[offset + 1];
        }

        private static uint ReadUInt32(byte[] bytes, int offset, bool little)
        {
            if (little)
                return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
            return (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);
        }

        private static int ReadUInt16BigEndian(Stream stream)
        {
            int high = ReadByte(stream);
            int low = ReadByte(stream);
            if (high < 0 || low < 0) throw new EndOfStreamException();
            return (high << 8) | low;
        }

        private static int ReadByte(Stream stream)
        {
            return stream.ReadByte();
        }

        private static bool HasEndOfImage(FileStream stream)
        {
            long original = stream.Position;
            try
            {
                int tailLength = (int)Math.Min(64 * 1024, stream.Length);
                if (tailLength < 2) return false;
                byte[] tail = new byte[tailLength];
                stream.Position = stream.Length - tailLength;
                int read = stream.Read(tail, 0, tail.Length);
                for (int index = read - 2; index >= 0; index--)
                    if (tail[index] == 0xFF && tail[index + 1] == 0xD9)
                        return true;
                return false;
            }
            finally
            {
                stream.Position = original;
            }
        }
    }

    internal static class OrientationTransform
    {
        private static readonly UnitMatrix FlipX = new UnitMatrix { A = -1, D = 1, E = 1 };
        private static readonly UnitMatrix FlipY = new UnitMatrix { A = 1, D = -1, F = 1 };
        private static readonly UnitMatrix Rotate90 = new UnitMatrix { B = -1, C = 1, F = 1 };
        private static readonly UnitMatrix Rotate180 = new UnitMatrix { A = -1, D = -1, E = 1, F = 1 };
        private static readonly UnitMatrix Rotate270 = new UnitMatrix { B = 1, C = -1, E = 1 };

        public static UnitMatrix Build(int sourceWidth, int sourceHeight, int exifOrientation, bool autoRotate, int manualRotation, out int finalWidth, out int finalHeight)
        {
            UnitMatrix matrix = UnitMatrix.Identity;
            int width = sourceWidth;
            int height = sourceHeight;
            switch (exifOrientation)
            {
                case 2: Apply(ref matrix, FlipX, ref width, ref height, false); break;
                case 3: Apply(ref matrix, Rotate180, ref width, ref height, false); break;
                case 4: Apply(ref matrix, FlipY, ref width, ref height, false); break;
                case 5:
                    Apply(ref matrix, Rotate90, ref width, ref height, true);
                    Apply(ref matrix, FlipX, ref width, ref height, false);
                    break;
                case 6: Apply(ref matrix, Rotate90, ref width, ref height, true); break;
                case 7:
                    Apply(ref matrix, Rotate270, ref width, ref height, true);
                    Apply(ref matrix, FlipX, ref width, ref height, false);
                    break;
                case 8: Apply(ref matrix, Rotate270, ref width, ref height, true); break;
            }

            if (autoRotate && width > height)
                Apply(ref matrix, Rotate90, ref width, ref height, true);

            int normalized = ImageTools.NormalizeRotation(manualRotation);
            if (normalized == 90) Apply(ref matrix, Rotate90, ref width, ref height, true);
            else if (normalized == 180) Apply(ref matrix, Rotate180, ref width, ref height, false);
            else if (normalized == 270) Apply(ref matrix, Rotate270, ref width, ref height, true);

            finalWidth = width;
            finalHeight = height;
            return matrix;
        }

        public static UnitMatrix Place(UnitMatrix orientation, PageLayout layout)
        {
            UnitMatrix placement = new UnitMatrix
            {
                A = layout.WidthPt,
                D = layout.HeightPt,
                E = layout.XPt,
                F = layout.YPt
            };
            return UnitMatrix.Compose(placement, orientation);
        }

        private static void Apply(ref UnitMatrix current, UnitMatrix operation, ref int width, ref int height, bool swapsDimensions)
        {
            current = UnitMatrix.Compose(operation, current);
            if (swapsDimensions)
            {
                int swap = width;
                width = height;
                height = swap;
            }
        }
    }

    internal static class WatermarkRenderer
    {
        private static readonly Color WatermarkColor = Color.FromArgb(107, 114, 128);

        public static bool IsEnabled(WatermarkOptions options)
        {
            return options != null && options.Mode != WatermarkMode.None && !String.IsNullOrWhiteSpace(options.Text);
        }

        public static WatermarkAsset CreateAsset(WatermarkOptions options)
        {
            if (!IsEnabled(options)) return null;
            string text = options.Text.Trim();
            FontFamily family = ResolveFontFamily();
            const float emSize = 78f;
            SizeF measured;
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(probe))
            using (Font font = new Font(family, emSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                measured = graphics.MeasureString(text, font, Int32.MaxValue, StringFormat.GenericTypographic);
            }

            int width = Math.Max(32, Math.Min(2400, (int)Math.Ceiling(measured.Width) + 32));
            int height = Math.Max(32, Math.Min(400, (int)Math.Ceiling(measured.Height) + 24));
            byte[] alpha = new byte[width * height];
            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (Font font = new Font(family, emSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush brush = new SolidBrush(Color.White))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    graphics.DrawString(text, font, brush, new PointF(16, 10), StringFormat.GenericTypographic);
                }
                Rectangle rectangle = new Rectangle(0, 0, width, height);
                BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[Math.Abs(data.Stride)];
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr pointer = IntPtr.Add(data.Scan0, y * data.Stride);
                        Marshal.Copy(pointer, row, 0, row.Length);
                        for (int x = 0; x < width; x++)
                            alpha[y * width + x] = row[x * 4 + 3];
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }

            byte[] rgb = new byte[width * height * 3];
            for (int index = 0; index < width * height; index++)
            {
                int offset = index * 3;
                rgb[offset] = WatermarkColor.R;
                rgb[offset + 1] = WatermarkColor.G;
                rgb[offset + 2] = WatermarkColor.B;
            }
            int opacity = Math.Max(5, Math.Min(60, options.OpacityPercent));
            for (int index = 0; index < alpha.Length; index++)
                alpha[index] = (byte)(alpha[index] * opacity / 100);

            return new WatermarkAsset
            {
                Width = width,
                Height = height,
                RgbData = Zlib.Compress(rgb),
                AlphaData = Zlib.Compress(alpha),
                Options = options.Clone()
            };
        }

        public static void DrawPreview(Bitmap page, WatermarkOptions options)
        {
            if (page == null || !IsEnabled(options)) return;
            using (Graphics graphics = Graphics.FromImage(page))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int opacity = Math.Max(5, Math.Min(60, options.OpacityPercent));
                using (Brush brush = new SolidBrush(Color.FromArgb((int)Math.Round(255 * opacity / 100.0), WatermarkColor)))
                using (Font font = new Font(ResolveFontFamily(), Math.Max(18f, page.Width / 18f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    string text = options.Text.Trim();
                    if (options.Layout == WatermarkLayout.Tile)
                    {
                        int stepX = Math.Max(180, page.Width / 2);
                        int stepY = Math.Max(130, page.Height / 4);
                        for (int y = stepY / 2; y < page.Height + stepY / 2; y += stepY)
                            for (int x = stepX / 2; x < page.Width + stepX / 2; x += stepX)
                                DrawRotatedText(graphics, text, font, brush, format, x, y, options.AngleDegrees);
                    }
                    else if (options.Layout == WatermarkLayout.BottomRight)
                    {
                        DrawRotatedText(graphics, text, font, brush, format, page.Width * 0.78f, page.Height * 0.9f, options.AngleDegrees);
                    }
                    else
                    {
                        DrawRotatedText(graphics, text, font, brush, format, page.Width / 2f, page.Height / 2f, options.AngleDegrees);
                    }
                }
            }
        }

        private static void DrawRotatedText(Graphics graphics, string text, Font font, Brush brush, StringFormat format, float x, float y, float angle)
        {
            GraphicsState state = graphics.Save();
            try
            {
                graphics.TranslateTransform(x, y);
                graphics.RotateTransform(angle);
                graphics.DrawString(text, font, brush, new PointF(0, 0), format);
            }
            finally { graphics.Restore(state); }
        }

        private static FontFamily ResolveFontFamily()
        {
            string[] names = { "Microsoft YaHei UI", "Microsoft YaHei", "SimSun", FontFamily.GenericSansSerif.Name };
            foreach (string name in names)
            {
                try { return new FontFamily(name); } catch { }
            }
            return FontFamily.GenericSansSerif;
        }
    }

    internal static class Zlib
    {
        public static byte[] Compress(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                stream.WriteByte(0x78);
                stream.WriteByte(0x9C);
                using (DeflateStream deflate = new DeflateStream(stream, CompressionMode.Compress, true))
                    deflate.Write(bytes, 0, bytes.Length);
                uint checksum = Adler32(bytes);
                stream.WriteByte((byte)(checksum >> 24));
                stream.WriteByte((byte)(checksum >> 16));
                stream.WriteByte((byte)(checksum >> 8));
                stream.WriteByte((byte)checksum);
                return stream.ToArray();
            }
        }

        private static uint Adler32(byte[] bytes)
        {
            const uint modulo = 65521;
            uint a = 1;
            uint b = 0;
            int offset = 0;
            while (offset < bytes.Length)
            {
                int end = Math.Min(offset + 5552, bytes.Length);
                for (; offset < end; offset++)
                {
                    a += bytes[offset];
                    b += a;
                }
                a %= modulo;
                b %= modulo;
            }
            return (b << 16) | a;
        }
    }

    internal sealed class PdfWriterV2 : IDisposable
    {
        private readonly FileStream _stream;
        private readonly List<long> _offsets = new List<long> { 0 };
        private readonly List<int> _pageObjects = new List<int>();
        private readonly WatermarkAsset _watermark;
        private int _nextObject = 3;
        private int _watermarkMaskObject;
        private int _watermarkImageObject;
        private bool _watermarkWritten;
        private bool _finished;

        public PdfWriterV2(string path, WatermarkOptions watermark)
        {
            _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.SequentialScan);
            _watermark = WatermarkRenderer.CreateAsset(watermark);
            WriteBytes(Encoding.ASCII.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n"));
            WriteAsciiObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        }

        public void AddPage(PreparedPage prepared, int pageNumber)
        {
            EnsureWatermarkObjects();
            int pageObject = NextObject();
            int imageObject = NextObject();
            int contentObject = NextObject();
            _pageObjects.Add(pageObject);
            string imageName = "Im" + pageNumber.ToString(CultureInfo.InvariantCulture);
            string resources = "<< /ProcSet [/PDF /ImageC] /XObject << /" + imageName + " " + imageObject.ToString(CultureInfo.InvariantCulture) + " 0 R";
            if (_watermarkImageObject > 0)
                resources += " /Wm " + _watermarkImageObject.ToString(CultureInfo.InvariantCulture) + " 0 R";
            resources += " >> >>";
            string page = String.Format(CultureInfo.InvariantCulture,
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:0.###} {1:0.###}] /Resources {2} /Contents {3} 0 R >>",
                prepared.Layout.PageWidthPt, prepared.Layout.PageHeightPt, resources, contentObject);
            WriteAsciiObject(pageObject, page);

            if (!String.IsNullOrEmpty(prepared.DirectJpegPath))
                WriteJpegObjectFromFile(imageObject, prepared.DirectJpegPath, prepared.Width, prepared.Height, prepared.Components);
            else
                WriteImageObject(imageObject, prepared.ImageData, prepared.Width, prepared.Height, prepared.Encoding, prepared.Components);

            StringBuilder content = new StringBuilder();
            content.Append("q\n");
            AppendMatrix(content, prepared.ImageMatrix);
            content.Append(" cm\n/").Append(imageName).Append(" Do\nQ\n");
            if (_watermark != null)
                AppendWatermarkContent(content, prepared.Layout.PageWidthPt, prepared.Layout.PageHeightPt);
            WriteStreamObject(contentObject, content.ToString());
        }

        public void Finish()
        {
            if (_finished) return;
            StringBuilder kids = new StringBuilder("[");
            foreach (int page in _pageObjects)
                kids.Append(page.ToString(CultureInfo.InvariantCulture)).Append(" 0 R ");
            kids.Append("]");
            WriteAsciiObject(2, "<< /Type /Pages /Kids " + kids + " /Count " + _pageObjects.Count.ToString(CultureInfo.InvariantCulture) + " >>");
            long xref = _stream.Position;
            int objectCount = _nextObject;
            WriteAscii("xref\n0 " + objectCount.ToString(CultureInfo.InvariantCulture) + "\n");
            WriteAscii("0000000000 65535 f \n");
            for (int index = 1; index < objectCount; index++)
                WriteAscii(_offsets[index].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
            WriteAscii("trailer\n<< /Size " + objectCount.ToString(CultureInfo.InvariantCulture) + " /Root 1 0 R >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
            _stream.Flush(true);
            _finished = true;
        }

        private void EnsureWatermarkObjects()
        {
            if (_watermark == null || _watermarkWritten) return;
            _watermarkMaskObject = NextObject();
            _watermarkImageObject = NextObject();
            WriteRawImageObject(_watermarkMaskObject, _watermark.AlphaData, _watermark.Width, _watermark.Height,
                "/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", 0);
            WriteRawImageObject(_watermarkImageObject, _watermark.RgbData, _watermark.Width, _watermark.Height,
                "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode", _watermarkMaskObject);
            _watermarkWritten = true;
        }

        private void AppendWatermarkContent(StringBuilder content, float pageWidth, float pageHeight)
        {
            WatermarkOptions options = _watermark.Options;
            if (options.Layout == WatermarkLayout.Tile)
            {
                float stepX = pageWidth * 0.46f;
                float stepY = pageHeight * 0.28f;
                for (float y = stepY * 0.45f; y < pageHeight + stepY * 0.2f; y += stepY)
                    for (float x = stepX * 0.45f; x < pageWidth + stepX * 0.2f; x += stepX)
                        AppendWatermarkAt(content, x, y, pageWidth * 0.34f, options.AngleDegrees);
            }
            else if (options.Layout == WatermarkLayout.BottomRight)
            {
                AppendWatermarkAt(content, pageWidth * 0.79f, pageHeight * 0.09f, pageWidth * 0.26f, options.AngleDegrees);
            }
            else
            {
                AppendWatermarkAt(content, pageWidth / 2f, pageHeight / 2f, pageWidth * 0.56f, options.AngleDegrees);
            }
        }

        private void AppendWatermarkAt(StringBuilder content, float centerX, float centerY, float desiredWidth, int angleDegrees)
        {
            double desiredHeight = desiredWidth * _watermark.Height / Math.Max(1.0, _watermark.Width);
            double radians = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            double a = desiredWidth * cos;
            double b = desiredWidth * sin;
            double c = -desiredHeight * sin;
            double d = desiredHeight * cos;
            double e = centerX - (a + c) / 2.0;
            double f = centerY - (b + d) / 2.0;
            content.Append("q\n");
            AppendMatrix(content, new UnitMatrix { A = a, B = b, C = c, D = d, E = e, F = f });
            content.Append(" cm\n/Wm Do\nQ\n");
        }

        private static void AppendMatrix(StringBuilder builder, UnitMatrix matrix)
        {
            builder.Append(matrix.A.ToString("0.#####", CultureInfo.InvariantCulture)).Append(' ')
                .Append(matrix.B.ToString("0.#####", CultureInfo.InvariantCulture)).Append(' ')
                .Append(matrix.C.ToString("0.#####", CultureInfo.InvariantCulture)).Append(' ')
                .Append(matrix.D.ToString("0.#####", CultureInfo.InvariantCulture)).Append(' ')
                .Append(matrix.E.ToString("0.#####", CultureInfo.InvariantCulture)).Append(' ')
                .Append(matrix.F.ToString("0.#####", CultureInfo.InvariantCulture));
        }

        private void WriteJpegObjectFromFile(int number, string path, int width, int height, int components)
        {
            FileInfo file = new FileInfo(path);
            RecordOffset(number);
            WriteAscii(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            string colorSpace = components == 1 ? "/DeviceGray" : "/DeviceRGB";
            WriteAscii(String.Format(CultureInfo.InvariantCulture,
                "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace {2} /BitsPerComponent 8 /Filter /DCTDecode /Length {3} >>\nstream\n",
                width, height, colorSpace, file.Length));
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.SequentialScan))
                input.CopyTo(_stream, 1024 * 128);
            WriteAscii("\nendstream\nendobj\n");
        }

        private void WriteImageObject(int number, byte[] data, int width, int height, PdfImageEncoding encoding, int components)
        {
            RecordOffset(number);
            WriteAscii(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            string colorSpace = components == 1 ? "/DeviceGray" : "/DeviceRGB";
            string dictionary;
            if (encoding == PdfImageEncoding.LosslessRgb)
            {
                dictionary = String.Format(CultureInfo.InvariantCulture,
                    "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {0} >> /Length {2} >>\nstream\n",
                    width, height, data.Length);
            }
            else
            {
                dictionary = String.Format(CultureInfo.InvariantCulture,
                    "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace {2} /BitsPerComponent 8 /Filter /DCTDecode /Length {3} >>\nstream\n",
                    width, height, colorSpace, data.Length);
            }
            WriteAscii(dictionary);
            WriteBytes(data);
            WriteAscii("\nendstream\nendobj\n");
        }

        private void WriteRawImageObject(int number, byte[] data, int width, int height, string dictionaryTail, int softMaskObject)
        {
            RecordOffset(number);
            WriteAscii(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            string softMask = softMaskObject > 0 ? " /SMask " + softMaskObject.ToString(CultureInfo.InvariantCulture) + " 0 R" : String.Empty;
            WriteAscii(String.Format(CultureInfo.InvariantCulture,
                "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} {2}{3} /Length {4} >>\nstream\n",
                width, height, dictionaryTail, softMask, data.Length));
            WriteBytes(data);
            WriteAscii("\nendstream\nendobj\n");
        }

        private void WriteStreamObject(int number, string content)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(content);
            RecordOffset(number);
            WriteAscii(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n<< /Length " + bytes.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
            WriteBytes(bytes);
            WriteAscii("endstream\nendobj\n");
        }

        private int NextObject()
        {
            return _nextObject++;
        }

        private void WriteAsciiObject(int number, string body)
        {
            RecordOffset(number);
            WriteAscii(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + body + "\nendobj\n");
        }

        private void RecordOffset(int number)
        {
            while (_offsets.Count <= number) _offsets.Add(0);
            _offsets[number] = _stream.Position;
        }

        private void WriteAscii(string text)
        {
            WriteBytes(Encoding.ASCII.GetBytes(text));
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

    internal static class PdfExporter
    {
        public static void ExportMerged(string targetPath, IList<ImageSnapshot> items, ExportOptions options, Action<int> progress, CancellationToken token)
        {
            if (items == null || items.Count == 0) throw new InvalidOperationException("没有可导出的图片。");
            string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (PdfWriterV2 writer = new PdfWriterV2(temporaryPath, options.Watermark))
                {
                    ProcessInOrder(items, options, token, delegate (PreparedPage page, int index)
                    {
                        writer.AddPage(page, index + 1);
                        if (progress != null) progress((index + 1) * 100 / items.Count);
                    });
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
            if (items == null || items.Count == 0) throw new InvalidOperationException("没有可导出的图片。");
            Directory.CreateDirectory(folder);
            ProcessInOrder(items, options, token, delegate (PreparedPage page, int index)
            {
                string requested = items[index].OutputName;
                if (String.IsNullOrWhiteSpace(requested)) requested = Path.GetFileNameWithoutExtension(items[index].Path);
                if (requested.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) requested = requested.Substring(0, requested.Length - 4);
                string targetPath = GetUniquePath(folder, SanitizeFileName(requested.Trim()) + ".pdf");
                string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (PdfWriterV2 writer = new PdfWriterV2(temporaryPath, options.Watermark))
                    {
                        writer.AddPage(page, 1);
                        writer.Finish();
                    }
                    File.Move(temporaryPath, targetPath);
                }
                catch
                {
                    TryDelete(temporaryPath);
                    throw;
                }
                if (progress != null) progress((index + 1) * 100 / items.Count);
            });
        }

        public static string GetUniquePath(string folder, string fileName)
        {
            string candidate = Path.Combine(folder, fileName);
            if (!File.Exists(candidate)) return candidate;
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            int index = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(folder, stem + " (" + index.ToString(CultureInfo.InvariantCulture) + ")" + extension);
                index++;
            }
            return candidate;
        }

        public static string SanitizeFileName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid.ToString(), "_");
            return String.IsNullOrWhiteSpace(name) ? "图片" : name;
        }

        private static void ProcessInOrder(IList<ImageSnapshot> items, ExportOptions options, CancellationToken token, Action<PreparedPage, int> consume)
        {
            int workers = GetWorkerCount(options);
            for (int start = 0; start < items.Count; start += workers)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(workers, items.Count - start);
                Task<PreparedPage>[] tasks = new Task<PreparedPage>[count];
                for (int offset = 0; offset < count; offset++)
                {
                    int itemIndex = start + offset;
                    ImageSnapshot snapshot = items[itemIndex];
                    tasks[offset] = Task.Factory.StartNew(
                        delegate { return PreparePage(snapshot, options, token); },
                        token, TaskCreationOptions.None, TaskScheduler.Default);
                }

                try
                {
                    Task.WaitAll(tasks);
                    for (int offset = 0; offset < count; offset++)
                    {
                        token.ThrowIfCancellationRequested();
                        PreparedPage page = tasks[offset].Result;
                        try
                        {
                            consume(page, start + offset);
                        }
                        finally
                        {
                            page.Dispose();
                            tasks[offset] = null;
                        }
                    }
                }
                catch (AggregateException aggregate)
                {
                    AggregateException flattened = aggregate.Flatten();
                    if (flattened.InnerExceptions.Count == 1) throw flattened.InnerExceptions[0];
                    throw;
                }
                finally
                {
                    foreach (Task<PreparedPage> task in tasks)
                    {
                        if (task != null && task.Status == TaskStatus.RanToCompletion && task.Result != null)
                            task.Result.Dispose();
                    }
                }
            }
        }

        private static int GetWorkerCount(ExportOptions options)
        {
            if (options.Quality == QualityPreset.Lossless) return 1;
            int cores = Environment.ProcessorCount;
            if (cores >= 8) return 4;
            if (cores >= 4) return 2;
            return 1;
        }

        private static PreparedPage PreparePage(ImageSnapshot snapshot, ExportOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string extension = Path.GetExtension(snapshot.Path).ToLowerInvariant();
            bool directPreset = options.Quality == QualityPreset.SmartFast || options.Quality == QualityPreset.Lossless;
            JpegMetadata jpeg;
            if (directPreset && (extension == ".jpg" || extension == ".jpeg") && JpegInspector.TryRead(snapshot.Path, out jpeg))
            {
                int orientedWidth;
                int orientedHeight;
                UnitMatrix orientation = OrientationTransform.Build(jpeg.Width, jpeg.Height, jpeg.ExifOrientation, options.AutoRotate, snapshot.ManualRotation, out orientedWidth, out orientedHeight);
                PageLayout layout = ImageTools.CalculateLayout(orientedWidth, orientedHeight, options.PaperSize, options.Orientation, options.MarginMm);
                return new PreparedPage
                {
                    DirectJpegPath = snapshot.Path,
                    Encoding = PdfImageEncoding.Jpeg,
                    Width = jpeg.Width,
                    Height = jpeg.Height,
                    Components = jpeg.Components,
                    Layout = layout,
                    ImageMatrix = OrientationTransform.Place(orientation, layout)
                };
            }

            using (Bitmap source = ImageTools.LoadTransformed(snapshot.Path, snapshot.ManualRotation, options.AutoRotate))
            {
                token.ThrowIfCancellationRequested();
                PageLayout layout = ImageTools.CalculateLayout(source.Width, source.Height, options.PaperSize, options.Orientation, options.MarginMm);
                int targetWidth;
                int targetHeight;
                if (options.Quality == QualityPreset.Lossless)
                {
                    targetWidth = source.Width;
                    targetHeight = source.Height;
                }
                else
                {
                    int dpi = QualitySettings.GetDpi(options.Quality);
                    targetWidth = Math.Max(1, (int)Math.Round(layout.WidthPt / 72f * dpi));
                    targetHeight = Math.Max(1, (int)Math.Round(layout.HeightPt / 72f * dpi));
                    targetWidth = Math.Min(targetWidth, Math.Max(1, source.Width));
                    targetHeight = Math.Min(targetHeight, Math.Max(1, source.Height));
                }

                using (Bitmap rendered = ImageTools.RenderImage(source, targetWidth, targetHeight))
                {
                    token.ThrowIfCancellationRequested();
                    bool lossless = options.Quality == QualityPreset.Lossless;
                    byte[] data = lossless ? ImageTools.ToLosslessRgb(rendered) : ImageTools.ToJpeg(rendered, QualitySettings.GetJpegQuality(options.Quality));
                    return new PreparedPage
                    {
                        ImageData = data,
                        Encoding = lossless ? PdfImageEncoding.LosslessRgb : PdfImageEncoding.Jpeg,
                        Width = rendered.Width,
                        Height = rendered.Height,
                        Components = 3,
                        Layout = layout,
                        ImageMatrix = new UnitMatrix { A = layout.WidthPt, D = layout.HeightPt, E = layout.XPt, F = layout.YPt }
                    };
                }
            }
        }

        private static void ReplaceFile(string temporaryPath, string targetPath)
        {
            if (File.Exists(targetPath)) File.Replace(temporaryPath, targetPath, null);
            else File.Move(temporaryPath, targetPath);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
