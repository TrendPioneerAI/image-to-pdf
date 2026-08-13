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
     ßÎ{¶‰Ëkºwµç@€€€€€€€€½¹Ñ•¹Ğ¹ÁÁ•¹ ˆµq¸½]´½q¹Eq¸ˆ¤ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÙ½¥ÁÁ•¹‘5…ÑÉ¥à¡MÑÉ¥¹	Õ¥±‘•È‰Õ¥±‘•È°U¹¥Ñ5…ÑÉ¥àµ…ÑÉ¥à¤(€€€€€€€ì(€€€€€€€€€€€‰Õ¥±‘•È¹ÁÁ•¹¡µ…ÑÉ¥à¹¹Q½MÑÉ¥¹œ ˆÀ¸ŒŒŒŒŒˆ°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤¤¹ÁÁ•¹ œ€œ¤(€€€€€€€€€€€€€€€€¹ÁÁ•¹¡µ…ÑÉ¥à¹¹Q½MÑÉ¥¹œ ˆÀ¸ŒŒŒŒŒˆ°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤¤¹ÁÁ•¹ œ€œ¤(€€€€€€€€€€€€€€€€¹ÁÁ•¹¡µ…ÑÉ¥à¹¹Q½MÑÉ¥¹œ ˆÀ¸ŒŒŒŒŒˆ°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤¤¹ÁÁ•¹ œ€œ¤(€€€€€€€€€€€€€€€€¹ÁÁ•¹¡µ…ÑÉ¥à¹¹Q½MÑÉ¥¹œ ˆÀ¸ŒŒŒŒŒˆ°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤¤¹ÁÁ•¹ œ€œ¤(€€€€€€€€€€€€€€€€¹ÁÁ•¹¡µ…ÑÉ¥à¹¹Q½MÑÉ¥¹œ ˆÀ¸ŒŒŒŒŒˆ°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤¤¹ÁÁ•¹ œ€œ¤(€€€€€€€€€€€€€€€€¹ÁÁ•¹¡µ…ÑÉ¥à¹¹Q½MÑÉ¥¹œ ˆÀ¸ŒŒŒŒŒˆ°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤¤ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥]É¥Ñ•)Á•=‰©•ÑÉ½µ¥±”¡¥¹Ğ¹Õµ‰•È°ÍÑÉ¥¹œÁ…Ñ °¥¹Ğİ¥‘Ñ °¥¹Ğ¡•¥¡Ğ°¥¹Ğ½µÁ½¹•¹ÑÌ¤(€€€€€€€ì(€€€€€€€€€€€¥±•%¹™¼™¥±”€ô¹•Ü¥±•%¹™¼¡Á…Ñ ¤ì(€€€€€€€€€€€I•½É‘=™™Í•Ğ¡¹Õµ‰•È¤ì(€€€€€€€€€€€]É¥Ñ•Í¥¤¡¹Õµ‰•È¹Q½MÑÉ¥¹œ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€¬€ˆ€À½‰©q¸ˆ¤ì(€€€€€€€€€€€ÍÑÉ¥¹œ½±½ÉMÁ…”€ô½µÁ½¹•¹ÑÌ€ôô€Ä€ü€ˆ½•Ù¥•É…äˆ€è€ˆ½•Ù¥•Iˆì(€€€€€€€€€€€]É¥Ñ•Í¥¤¡MÑÉ¥¹œ¹½Éµ…Ğ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”°(€€€€€€€€€€€€€€€€ˆğğ€½QåÁ”€½a=‰©•Ğ€½MÕ‰ÑåÁ”€½%µ…”€½]¥‘Ñ ìÁô€½!•¥¡ĞìÅô€½½±½ÉMÁ…”ìÉô€½	¥ÑÍA•É½µÁ½¹•¹Ğ€à€½¥±Ñ•È€½Q•½‘”€½1•¹Ñ ìÍô€øùq¹ÍÑÉ•…µq¸ˆ°(€€€€€€€€€€€€€€€İ¥‘Ñ °¡•¥¡Ğ°½±½ÉMÁ…”°™¥±”¹1•¹Ñ ¤¤ì(€€€€€€€€€€€ÕÍ¥¹œ€¡¥±•MÑÉ•…´¥¹ÁÕĞ€ô¹•Ü¥±•MÑÉ•…´¡Á…Ñ °¥±•5½‘”¹=Á•¸°¥±••ÍÌ¹I•…°¥±•M¡…É”¹I•…°€ÄÀÈĞ€¨€ÄÈà°¥±•=ÁÑ¥½¹Ì¹M•ÅÕ•¹Ñ¥…±M…¸¤¤(€€€€€€€€€€€€€€€¥¹ÁÕĞ¹½ÁåQ¼¡}ÍÑÉ•…´°€ÄÀÈĞ€¨€ÄÈà¤ì(€€€€€€€€€€€]É¥Ñ•Í¥¤ ‰q¹•¹‘ÍÑÉ•…µq¹•¹‘½‰©q¸ˆ¤ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥]É¥Ñ•%µ…•=‰©•Ğ¡¥¹Ğ¹Õµ‰•È°‰åÑ•mt‘…Ñ„°¥¹Ğİ¥‘Ñ °¥¹Ğ¡•¥¡Ğ°A‘™%µ…•¹½‘¥¹œ•¹½‘¥¹œ°¥¹Ğ½µÁ½¹•¹ÑÌ¤(€€€€€€€ì(€€€€€€€€€€€I•½É‘=™™Í•Ğ¡¹Õµ‰•È¤ì(€€€€€€€€€€€]É¥Ñ•Í¥¤¡¹Õµ‰•È¹Q½MÑÉ¥¹œ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€¬€ˆ€À½‰©q¸ˆ¤ì(€€€€€€€€€€€ÍÑÉ¥¹œ½±½ÉMÁ…”€ô½µÁ½¹•¹ÑÌ€ôô€Ä€ü€ˆ½•Ù¥•É…äˆ€è€ˆ½•Ù¥•Iˆì(€€€€€€€€€€€ÍÑÉ¥¹œ‘¥Ñ¥½¹…Éäì(€€€€€€€€€€€¥˜€¡•¹½‘¥¹œ€ôôA‘™%µ…•¹½‘¥¹œ¹1½ÍÍ±•ÍÍIˆ¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€‘¥Ñ¥½¹…Éä€ôMÑÉ¥¹œ¹½Éµ…Ğ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”°(€€€€€€€€€€€€€€€€€€€€ˆğğ€½QåÁ”€½a=‰©•Ğ€½MÕ‰ÑåÁ”€½%µ…”€½]¥‘Ñ ìÁô€½!•¥¡ĞìÅô€½½±½ÉMÁ…”€½•Ù¥•I€½	¥ÑÍA•É½µÁ½¹•¹Ğ€à€½¥±Ñ•È€½±…Ñ••½‘”€½•½‘•A…ÉµÌ€ğğ€½AÉ•‘¥Ñ½È€ÄÔ€½½±½ÉÌ€Ì€½	¥ÑÍA•É½µÁ½¹•¹Ğ€à€½½±Õµ¹ÌìÁô€øø€½1•¹Ñ ìÉô€øùq¹ÍÑÉ•…µq¸ˆ°(€€€€€€€€€€€€€€€€€€€İ¥‘Ñ °¡•¥¡Ğ°‘…Ñ„¹1•¹Ñ ¤ì(€€€€€€€€€€€ô(€€€€€€€€€€€•±Í”(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€‘¥Ñ¥½¹…Éä€ôMÑÉ¥¹œ¹½Éµ…Ğ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”°(€€€€€€€€€€€€€€€€€€€€ˆğğ€½QåÁ”€½a=‰©•Ğ€½MÕ‰ÑåÁ”€½%µ…”€½]¥‘Ñ ìÁô€½!•¥¡ĞìÅô€½½±½ÉMÁ…”ìÉô€½	¥ÑÍA•É½µÁ½¹•¹Ğ€à€½¥±Ñ•È€½Q•½‘”€½1•¹Ñ ìÍô€øùq¹ÍÑÉ•…µq¸ˆ°(€€€€€€€€€€€€€€€€€€€İ¥‘Ñ °¡•¥¡Ğ°½±½ÉMÁ…”°‘…Ñ„¹1•¹Ñ ¤ì(€€€€€€€€€€€ô(€€€€€€€€€€€]É¥Ñ•Í¥¤¡‘¥Ñ¥½¹…Éä¤ì(€€€€€€€€€€€]É¥Ñ•	åÑ•Ì¡‘…Ñ„¤ì(€€€€€€€€€€€]É¥Ñ•Í¥¤ ‰q¹•¹‘ÍÑÉ•…µq¹•¹‘½‰©q¸ˆ¤ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥]É¥Ñ•I…İ%µ…•=‰©•Ğ¡¥¹Ğ¹Õµ‰•È°‰åÑ•mt‘…Ñ„°¥¹Ğİ¥‘Ñ °¥¹Ğ¡•¥¡Ğ°ÍÑÉ¥¹œ‘¥Ñ¥½¹…ÉåQ…¥°°¥¹ĞÍ½™Ñ5…Í­=‰©•Ğ¤(€€€€€€€ì(€€€€€€€€€€€I•½É‘=™™Í•Ğ¡¹Õµ‰•È¤ì(€€€€€€€€€€€]É¥Ñ•Í¥¤¡¹Õµ‰•È¹Q½MÑÉ¥¹œ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€¬€ˆ€À½‰©q¸ˆ¤ì(€€€€€€€€€€€ÍÑÉ¥¹œÍ½™Ñ5…Í¬€ôÍ½™Ñ5…Í­=‰©•Ğ€ø€À€ü€ˆ€½M5…Í¬€ˆ€¬Í½™Ñ5…Í­=‰©•Ğ¹Q½MÑÉ¥¹œ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€¬€ˆ€ÀHˆ€èMÑÉ¥¹œ¹µÁÑäì(€€€€€€€€€€€]É¥Ñ•Í¥¤¡MÑÉ¥¹œ¹½Éµ…Ğ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”°(€€€€€€€€€€€€€€€€ˆğğ€½QåÁ”€½a=‰©•Ğ€½MÕ‰ÑåÁ”€½%µ…”€½]¥‘Ñ ìÁô€½!•¥¡ĞìÅôìÉõìÍô€½1•¹Ñ ìÑô€øùq¹ÍÑÉ•…µq¸ˆ°(€€€€€€€€€€€€€€€İ¥‘Ñ °¡•¥¡Ğ°‘¥Ñ¥½¹…ÉåQ…¥°°Í½™Ñ5…Í¬°‘…Ñ„¹1•¹Ñ ¤¤ì(€€€€€€€€€€€]É¥Ñ•	åÑ•Ì¡‘…Ñ„¤ì(€€€€€€€€€€€]É¥Ñ•Í¥¤ ‰q¹•¹‘ÍÑÉ•…µq¹•¹‘½‰©q¸ˆ¤ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥]É¥Ñ•MÑÉ•…µ=‰©•Ğ¡¥¹Ğ¹Õµ‰•È°ÍÑÉ¥¹œ½¹Ñ•¹Ğ¤(€€€€€€€ì(€€€€€€€€€€€‰åÑ•mt‰åÑ•Ì€ô¹½‘¥¹œ¹M%$¹•Ñ	åÑ•Ì¡½¹Ñ•¹Ğ¤ì(€€€€€€€€€€€I•½É‘=™™Í•Ğ¡¹Õµ‰•È¤ì(€€€€€€€€€€€]É¥Ñ•Í¥¤¡¹Õµ‰•È¹Q½MÑÉ¥¹œ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€¬€ˆ€À½‰©q¸ğğ€½1•¹Ñ €ˆ€¬‰åÑ•Ì¹1•¹Ñ ¹Q½MÑÉ¥¹œ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€¬€ˆ€øùq¹ÍÑÉ•…µq¸ˆ¤ì(€€€€€€€€€€€]É¥Ñ•	åÑ•Ì¡‰åÑ•Ì¤ì(€€€€€€€€€€€]É¥Ñ•Í¥¤ ‰•¹‘ÍÑÉ•…µq¹•¹‘½‰©q¸ˆ¤ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”¥¹Ğ9•áÑ=‰©•Ğ ¤(€€€€€€€ì(€€€€€€€€€€€É•ÑÕÉ¸}¹•áÑ=‰©•Ğ¬¬ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥]É¥Ñ•Í¥¥=‰©•Ğ¡¥¹Ğ¹Õµ‰•È°ÍÑÉ¥¹œ‰½‘ä¤(€€€€€€€ì(€€€€€€€€€€€I•½É‘=™™Í•Ğ¡¹Õµ‰•È¤ì(€€€€€€€€€€€]É¥Ñ•Í¥¤¡¹Õµ‰•È¹Q½MÑÉ¥¹œ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€¬€ˆ€À½‰©q¸ˆ€¬‰½‘ä€¬€‰q¹•¹‘½‰©q¸ˆ¤ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥I•½É‘=™™Í•Ğ¡¥¹Ğ¹Õµ‰•È¤(€€€€€€€ì(€€€€€€€€€€€İ¡¥±”€¡}½™™Í•ÑÌ¹½Õ¹Ğ€ğô¹Õµ‰•È¤}½™™Í•ÑÌ¹‘ À¤ì(€€€€€€€€€€€}½™™Í•ÑÍm¹Õµ‰•Ét€ô}ÍÑÉ•…´¹A½Í¥Ñ¥½¸ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥]É¥Ñ•Í¥¤¡ÍÑÉ¥¹œÑ•áĞ¤(€€€€€€€ì(€€€€€€€€€€€]É¥Ñ•	åÑ•Ì¡¹½‘¥¹œ¹M%$¹•Ñ	åÑ•Ì¡Ñ•áĞ¤¤ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”Ù½¥]É¥Ñ•	åÑ•Ì¡‰åÑ•mt‰åÑ•Ì¤(€€€€€€€ì(€€€€€€€€€€€}ÍÑÉ•…´¹]É¥Ñ”¡‰åÑ•Ì°€À°‰åÑ•Ì¹1•¹Ñ ¤ì(€€€€€€€ô((€€€€€€€ÁÕ‰±¥ŒÙ½¥¥ÍÁ½Í” ¤(€€€€€€€ì(€€€€€€€€€€€¥˜€ …}™¥¹¥Í¡•¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€ÑÉäì}ÍÑÉ•…´¹±ÕÍ  ¤ìô…Ñ ìô(€€€€€€€€€€€ô(€€€€€€€€€€€}ÍÑÉ•…´¹¥ÍÁ½Í” ¤ì(€€€€€€€ô(€€€ô((€€€¥¹Ñ•É¹…°ÍÑ…Ñ¥Œ±…ÍÌA‘™áÁ½ÉÑ•È(€€€ì(€€€€€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒÙ½¥áÁ½ÉÑ5•É•¡ÍÑÉ¥¹œÑ…É•ÑA…Ñ °%1¥ÍĞñ%µ…•M¹…ÁÍ¡½Ğø¥Ñ•µÌ°áÁ½ÉÑ=ÁÑ¥½¹Ì½ÁÑ¥½¹Ì°Ñ¥½¸ñ¥¹ĞøÁÉ½É•ÍÌ°…¹•±±…Ñ¥½¹Q½­•¸Ñ½­•¸¤(€€€€€€€ì(€€€€€€€€€€€¥˜€¡¥Ñ•µÌ€ôô¹Õ±°ñğ¥Ñ•µÌ¹½Õ¹Ğ€ôô€À¤Ñ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‹šÊ‡šr'–>¿–¾ó–ëj–nû&ˆ¤ì(€€€€€€€€€€€ÍÑÉ¥¹œÑ•µÁ½É…ÉåA…Ñ €ôÑ…É•ÑA…Ñ €¬€ˆ¹ÑµÀ´ˆ€¬Õ¥¹9•İÕ¥ ¤¹Q½MÑÉ¥¹œ ‰8ˆ¤ì(€€€€€€€€€€€ÑÉä(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€ÕÍ¥¹œ€¡A‘™]É¥Ñ•ÉXÈİÉ¥Ñ•È€ô¹•ÜA‘™]É¥Ñ•ÉXÈ¡Ñ•µÁ½É…ÉåA…Ñ °½ÁÑ¥½¹Ì¹]…Ñ•Éµ…É¬¤¤(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€AÉ½•ÍÍ%¹=É‘•È¡¥Ñ•µÌ°½ÁÑ¥½¹Ì°Ñ½­•¸°‘•±•…Ñ”€¡AÉ•Á…É•‘A…”Á…”°¥¹Ğ¥¹‘•à¤(€€€€€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€€€€€İÉ¥Ñ•È¹‘‘A…”¡Á…”°¥¹‘•à€¬€Ä¤ì(€€€€€€€€€€€€€€€€€€€€€€€¥˜€¡ÁÉ½É•ÍÌ€„ô¹Õ±°¤ÁÉ½É•ÍÌ ¡¥¹‘•à€¬€Ä¤€¨€ÄÀÀ€¼¥Ñ•µÌ¹½Õ¹Ğ¤ì(€€€€€€€€€€€€€€€€€€€ô¤ì(€€€€€€€€€€€€€€€€€€€İÉ¥Ñ•È¹¥¹¥Í  ¤ì(€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€I•Á±…•¥±”¡Ñ•µÁ½É…ÉåA…Ñ °Ñ…É•ÑA…Ñ ¤ì(€€€€€€€€€€€ô(€€€€€€€€€€€…Ñ (€€€€€€€€€€€ì(€€€€€€€€€€€€€€€QÉå•±•Ñ”¡Ñ•µÁ½É…ÉåA…Ñ ¤ì(€€€€€€€€€€€€€€€Ñ¡É½Üì(€€€€€€€€€€€ô(€€€€€€€ô((€€€€€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒÙ½¥áÁ½ÉÑM•Á…É…Ñ”¡ÍÑÉ¥¹œ™½±‘•È°%1¥ÍĞñ%µ…•M¹…ÁÍ¡½Ğø¥Ñ•µÌ°áÁ½ÉÑ=ÁÑ¥½¹Ì½ÁÑ¥½¹Ì°Ñ¥½¸ñ¥¹ĞøÁÉ½É•ÍÌ°…¹•±±…Ñ¥½¹Q½­•¸Ñ½­•¸¤(€€€€€€€ì(€€€€€€€€€€€¥˜€¡¥Ñ•µÌ€ôô¹Õ±°ñğ¥Ñ•µÌ¹½Õ¹Ğ€ôô€À¤Ñ¡É½Ü¹•Ü%¹Ù…±¥‘=Á•É…Ñ¥½¹á•ÁÑ¥½¸ ‹šÊ‡šr'–>¿–¾ó–ëj–nû&ˆ¤ì(€€€€€€€€€€€¥É•Ñ½Éä¹É•…Ñ•¥É•Ñ½Éä¡™½±‘•È¤ì(€€€€€€€€€€€AÉ½•ÍÍ%¹=É‘•È¡¥Ñ•µÌ°½ÁÑ¥½¹Ì°Ñ½­•¸°‘•±•…Ñ”€¡AÉ•Á…É•‘A…”Á…”°¥¹Ğ¥¹‘•à¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€ÍÑÉ¥¹œÉ•ÅÕ•ÍÑ•€ô¥Ñ•µÍm¥¹‘•át¹=ÕÑÁÕÑ9…µ”ì(€€€€€€€€€€€€€€€¥˜€¡MÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡É•ÅÕ•ÍÑ•¤¤É•ÅÕ•ÍÑ•€ôA…Ñ ¹•Ñ¥±•9…µ•]¥Ñ¡½ÕÑáÑ•¹Í¥½¸¡¥Ñ•µÍm¥¹‘•át¹A…Ñ ¤ì(€€€€€€€€€€€€€€€¥˜€¡É•ÅÕ•ÍÑ•¹¹‘Í]¥Ñ  ˆ¹Á‘˜ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤É•ÅÕ•ÍÑ•€ôÉ•ÅÕ•ÍÑ•¹MÕ‰ÍÑÉ¥¹œ À°É•ÅÕ•ÍÑ•¹1•¹Ñ €´€Ğ¤ì(€€€€€€€€€€€€€€€ÍÑÉ¥¹œÑ…É•ÑA…Ñ €ô•ÑU¹¥ÅÕ•A…Ñ ¡™½±‘•È°M…¹¥Ñ¥é•¥±•9…µ”¡É•ÅÕ•ÍÑ•¹QÉ¥´ ¤¤€¬€ˆ¹Á‘˜ˆ¤ì(€€€€€€€€€€€€€€€ÍÑÉ¥¹œÑ•µÁ½É…ÉåA…Ñ €ôÑ…É•ÑA…Ñ €¬€ˆ¹ÑµÀ´ˆ€¬Õ¥¹9•İÕ¥ ¤¹Q½MÑÉ¥¹œ ‰8ˆ¤ì(€€€€€€€€€€€€€€€ÑÉä(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€ÕÍ¥¹œ€¡A‘™]É¥Ñ•ÉXÈİÉ¥Ñ•È€ô¹•ÜA‘™]É¥Ñ•ÉXÈ¡Ñ•µÁ½É…ÉåA…Ñ °½ÁÑ¥½¹Ì¹]…Ñ•Éµ…É¬¤¤(€€€€€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€€€€€İÉ¥Ñ•È¹‘‘A…”¡Á…”°€Ä¤ì(€€€€€€€€€€€€€€€€€€€€€€€İÉ¥Ñ•È¹¥¹¥Í  ¤ì(€€€€€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€€€€€¥±”¹5½Ù”¡Ñ•µÁ½É…ÉåA…Ñ °Ñ…É•ÑA…Ñ ¤ì(€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€…Ñ (€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€QÉå•±•Ñ”¡Ñ•µÁ½É…ÉåA…Ñ ¤ì(€€€€€€€€€€€€€€€€€€€Ñ¡É½Üì(€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€¥˜€¡ÁÉ½É•ÍÌ€„ô¹Õ±°¤ÁÉ½É•ÍÌ ¡¥¹‘•à€¬€Ä¤€¨€ÄÀÀ€¼¥Ñ•µÌ¹½Õ¹Ğ¤ì(€€€€€€€€€€€ô¤ì(€€€€€€€ô((€€€€€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒÍÑÉ¥¹œ•ÑU¹¥ÅÕ•A…Ñ ¡ÍÑÉ¥¹œ™½±‘•È°ÍÑÉ¥¹œ™¥±•9…µ”¤(€€€€€€€ì(€€€€€€€€€€€ÍÑÉ¥¹œ…¹‘¥‘…Ñ”€ôA…Ñ ¹½µ‰¥¹”¡™½±‘•È°™¥±•9…µ”¤ì(€€€€€€€€€€€¥˜€ …¥±”¹á¥ÍÑÌ¡…¹‘¥‘…Ñ”¤¤É•ÑÕÉ¸…¹‘¥‘…Ñ”ì(€€€€€€€€€€€ÍÑÉ¥¹œÍÑ•´€ôA…Ñ ¹•Ñ¥±•9…µ•]¥Ñ¡½ÕÑáÑ•¹Í¥½¸¡™¥±•9…µ”¤ì(€€€€€€€€€€€ÍÑÉ¥¹œ•áÑ•¹Í¥½¸€ôA…Ñ ¹•ÑáÑ•¹Í¥½¸¡™¥±•9…µ”¤ì(€€€€€€€€€€€¥¹Ğ¥¹‘•à€ô€Èì(€€€€€€€€€€€İ¡¥±”€¡¥±”¹á¥ÍÑÌ¡…¹‘¥‘…Ñ”¤¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€…¹‘¥‘…Ñ”€ôA…Ñ ¹½µ‰¥¹”¡™½±‘•È°ÍÑ•´€¬€ˆ€ ˆ€¬¥¹‘•à¹Q½MÑÉ¥¹œ¡Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€¬€ˆ¤ˆ€¬•áÑ•¹Í¥½¸¤ì(€€€€€€€€€€€€€€€¥¹‘•à¬¬ì(€€€€€€€€€€€ô(€€€€€€€€€€€É•ÑÕÉ¸…¹‘¥‘…Ñ”ì(€€€€€€€ô((€€€€€€€ÁÕ‰±¥ŒÍÑ…Ñ¥ŒÍÑÉ¥¹œM…¹¥Ñ¥é•¥±•9…µ”¡ÍÑÉ¥¹œ¹…µ”¤(€€€€€€€ì(€€€€€€€€€€€™½É•… €¡¡…È¥¹Ù…±¥¥¸A…Ñ ¹•Ñ%¹Ù…±¥‘¥±•9…µ•¡…ÉÌ ¤¤¹…µ”€ô¹…µ”¹I•Á±…”¡¥¹Ù…±¥¹Q½MÑÉ¥¹œ ¤°€‰|ˆ¤ì(€€€€€€€€€€€É•ÑÕÉ¸MÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡¹…µ”¤€ü€‹–nû&ˆ€è¹…µ”ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÙ½¥AÉ½•ÍÍ%¹=É‘•È¡%1¥ÍĞñ%µ…•M¹…ÁÍ¡½Ğø¥Ñ•µÌ°áÁ½ÉÑ=ÁÑ¥½¹Ì½ÁÑ¥½¹Ì°…¹•±±…Ñ¥½¹Q½­•¸Ñ½­•¸°Ñ¥½¸ñAÉ•Á…É•‘A…”°¥¹Ğø½¹ÍÕµ”¤(€€€€€€€ì(€€€€€€€€€€€¥¹Ğİ½É­•ÉÌ€ô•Ñ]½É­•É½Õ¹Ğ¡½ÁÑ¥½¹Ì¤ì(€€€€€€€€€€€™½È€¡¥¹ĞÍÑ…ÉĞ€ô€ÀìÍÑ…ÉĞ€ğ¥Ñ•µÌ¹½Õ¹ĞìÍÑ…ÉĞ€¬ôİ½É­•ÉÌ¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€Ñ½­•¸¹Q¡É½İ%™…¹•±±…Ñ¥½¹I•ÅÕ•ÍÑ• ¤ì(€€€€€€€€€€€€€€€¥¹Ğ½Õ¹Ğ€ô5…Ñ ¹5¥¸¡İ½É­•ÉÌ°¥Ñ•µÌ¹½Õ¹Ğ€´ÍÑ…ÉĞ¤ì(€€€€€€€€€€€€€€€Q…Í¬ñAÉ•Á…É•‘A…”ùmtÑ…Í­Ì€ô¹•ÜQ…Í¬ñAÉ•Á…É•‘A…”ùm½Õ¹Ñtì(€€€€€€€€€€€€€€€™½È€¡¥¹Ğ½™™Í•Ğ€ô€Àì½™™Í•Ğ€ğ½Õ¹Ğì½™™Í•Ğ¬¬¤(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€¥¹Ğ¥Ñ•µ%¹‘•à€ôÍÑ…ÉĞ€¬½™™Í•Ğì(€€€€€€€€€€€€€€€€€€€%µ…•M¹…ÁÍ¡½ĞÍ¹…ÁÍ¡½Ğ€ô¥Ñ•µÍm¥Ñ•µ%¹‘•átì(€€€€€€€€€€€€€€€€€€€Ñ…Í­Ím½™™Í•Ñt€ôQ…Í¬¹…Ñ½Éä¹MÑ…ÉÑ9•Ü (€€€€€€€€€€€€€€€€€€€€€€€‘•±•…Ñ”ìÉ•ÑÕÉ¸AÉ•Á…É•A…”¡Í¹…ÁÍ¡½Ğ°½ÁÑ¥½¹Ì°Ñ½­•¸¤ìô°(€€€€€€€€€€€€€€€€€€€€€€€Ñ½­•¸°Q…Í­É•…Ñ¥½¹=ÁÑ¥½¹Ì¹9½¹”°Q…Í­M¡•‘Õ±•È¹•™…Õ±Ğ¤ì(€€€€€€€€€€€€€€€ô((€€€€€€€€€€€€€€€ÑÉä(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€Q…Í¬¹]…¥Ñ±°¡Ñ…Í­Ì¤ì(€€€€€€€€€€€€€€€€€€€™½È€¡¥¹Ğ½™™Í•Ğ€ô€Àì½™™Í•Ğ€ğ½Õ¹Ğì½™™Í•Ğ¬¬¤(€€€€€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€€€€€Ñ½­•¸¹Q¡É½İ%™…¹•±±…Ñ¥½¹I•ÅÕ•ÍÑ• ¤ì(€€€€€€€€€€€€€€€€€€€€€€€AÉ•Á…É•‘A…”Á…”€ôÑ…Í­Ím½™™Í•Ñt¹I•ÍÕ±Ğì(€€€€€€€€€€€€€€€€€€€€€€€ÑÉä(€€€€€€€€€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€€€€€€€€€½¹ÍÕµ”¡Á…”°ÍÑ…ÉĞ€¬½™™Í•Ğ¤ì(€€€€€€€€€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€€€€€€€€€™¥¹…±±ä(€€€€€€€€€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€€€€€€€€€Á…”¹¥ÍÁ½Í” ¤ì(€€€€€€€€€€€€€€€€€€€€€€€€€€€Ñ…Í­Ím½™™Í•Ñt€ô¹Õ±°ì(€€€€€€€€€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€…Ñ €¡É•…Ñ•á•ÁÑ¥½¸…É•…Ñ”¤(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€É•…Ñ•á•ÁÑ¥½¸™±…ÑÑ•¹•€ô…É•…Ñ”¹±…ÑÑ•¸ ¤ì(€€€€€€€€€€€€€€€€€€€¥˜€¡™±…ÑÑ•¹•¹%¹¹•Éá•ÁÑ¥½¹Ì¹½Õ¹Ğ€ôô€Ä¤Ñ¡É½Ü™±…ÑÑ•¹•¹%¹¹•Éá•ÁÑ¥½¹ÍlÁtì(€€€€€€€€€€€€€€€€€€€Ñ¡É½Üì(€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€™¥¹…±±ä(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€™½É•… €¡Q…Í¬ñAÉ•Á…É•‘A…”øÑ…Í¬¥¸Ñ…Í­Ì¤(€€€€€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€€€€€¥˜€¡Ñ…Í¬€„ô¹Õ±°€˜˜Ñ…Í¬¹MÑ…ÑÕÌ€ôôQ…Í­MÑ…ÑÕÌ¹I…¹Q½½µÁ±•Ñ¥½¸€˜˜Ñ…Í¬¹I•ÍÕ±Ğ€„ô¹Õ±°¤(€€€€€€€€€€€€€€€€€€€€€€€€€€€Ñ…Í¬¹I•ÍÕ±Ğ¹¥ÍÁ½Í” ¤ì(€€€€€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€ô(€€€€€€€€€€€ô(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ¥¹Ğ•Ñ]½É­•É½Õ¹Ğ¡áÁ½ÉÑ=ÁÑ¥½¹Ì½ÁÑ¥½¹Ì¤(€€€€€€€ì(€€€€€€€€€€€¥˜€¡½ÁÑ¥½¹Ì¹EÕ…±¥Ñä€ôôEÕ…±¥ÑåAÉ•Í•Ğ¹1½ÍÍ±•ÍÌ¤É•ÑÕÉ¸€Äì(€€€€€€€€€€€¥¹Ğ½É•Ì€ô¹Ù¥É½¹µ•¹Ğ¹AÉ½•ÍÍ½É½Õ¹Ğì(€€€€€€€€€€€¥˜€¡½É•Ì€øô€à¤É•ÑÕÉ¸€Ğì(€€€€€€€€€€€¥˜€¡½É•Ì€øô€Ğ¤É•ÑÕÉ¸€Èì(€€€€€€€€€€€É•ÑÕÉ¸€Äì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒAÉ•Á…É•‘A…”AÉ•Á…É•A…”¡%µ…•M¹…ÁÍ¡½ĞÍ¹…ÁÍ¡½Ğ°áÁ½ÉÑ=ÁÑ¥½¹Ì½ÁÑ¥½¹Ì°…¹•±±…Ñ¥½¹Q½­•¸Ñ½­•¸¤(€€€€€€€ì(€€€€€€€€€€€Ñ½­•¸¹Q¡É½İ%™…¹•±±…Ñ¥½¹I•ÅÕ•ÍÑ• ¤ì(€€€€€€€€€€€ÍÑÉ¥¹œ•áÑ•¹Í¥½¸€ôA…Ñ ¹•ÑáÑ•¹Í¥½¸¡Í¹…ÁÍ¡½Ğ¹A…Ñ ¤¹Q½1½İ•É%¹Ù…É¥…¹Ğ ¤ì(€€€€€€€€€€€‰½½°‘¥É•ÑAÉ•Í•Ğ€ô½ÁÑ¥½¹Ì¹EÕ…±¥Ñä€ôôEÕ…±¥ÑåAÉ•Í•Ğ¹Mµ…ÉÑ…ÍĞñğ½ÁÑ¥½¹Ì¹EÕ…±¥Ñä€ôôEÕ…±¥ÑåAÉ•Í•Ğ¹1½ÍÍ±•ÍÌì(€€€€€€€€€€€)Á•5•Ñ…‘…Ñ„©Á•œì(€€€€€€€€€€€¥˜€¡‘¥É•ÑAÉ•Í•Ğ€˜˜€¡•áÑ•¹Í¥½¸€ôô€ˆ¹©Áœˆñğ•áÑ•¹Í¥½¸€ôô€ˆ¹©Á•œˆ¤€˜˜)Á•%¹ÍÁ•Ñ½È¹QÉåI•…¡Í¹…ÁÍ¡½Ğ¹A…Ñ °½ÕĞ©Á•œ¤¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€¥¹Ğ½É¥•¹Ñ•‘]¥‘Ñ ì(€€€€€€€€€€€€€€€¥¹Ğ½É¥•¹Ñ•‘!•¥¡Ğì(€€€€€€€€€€€€€€€U¹¥Ñ5…ÑÉ¥à½É¥•¹Ñ…Ñ¥½¸€ô=É¥•¹Ñ…Ñ¥½¹QÉ…¹Í™½É´¹	Õ¥±¡©Á•œ¹]¥‘Ñ °©Á•œ¹!•¥¡Ğ°©Á•œ¹á¥™=É¥•¹Ñ…Ñ¥½¸°½ÁÑ¥½¹Ì¹ÕÑ½I½Ñ…Ñ”°Í¹…ÁÍ¡½Ğ¹5…¹Õ…±I½Ñ…Ñ¥½¸°½ÕĞ½É¥•¹Ñ•‘]¥‘Ñ °½ÕĞ½É¥•¹Ñ•‘!•¥¡Ğ¤ì(€€€€€€€€€€€€€€€A…•1…å½ÕĞ±…å½ÕĞ€ô%µ…•Q½½±Ì¹…±Õ±…Ñ•1…å½ÕĞ¡½É¥•¹Ñ•‘]¥‘Ñ °½É¥•¹Ñ•‘!•¥¡Ğ°½ÁÑ¥½¹Ì¹A…Á•ÉM¥é”°½ÁÑ¥½¹Ì¹=É¥•¹Ñ…Ñ¥½¸°½ÁÑ¥½¹Ì¹5…É¥¹5´¤ì(€€€€€€€€€€€€€€€É•ÑÕÉ¸¹•ÜAÉ•Á…É•‘A…”(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€¥É•Ñ)Á•A…Ñ €ôÍ¹…ÁÍ¡½Ğ¹A…Ñ °(€€€€€€€€€€€€€€€€€€€¹½‘¥¹œ€ôA‘™%µ…•¹½‘¥¹œ¹)Á•œ°(€€€€€€€€€€€€€€€€€€€]¥‘Ñ €ô©Á•œ¹]¥‘Ñ °(€€€€€€€€€€€€€€€€€€€!•¥¡Ğ€ô©Á•œ¹!•¥¡Ğ°(€€€€€€€€€€€€€€€€€€€½µÁ½¹•¹ÑÌ€ô©Á•œ¹½µÁ½¹•¹ÑÌ°(€€€€€€€€€€€€€€€€€€€1…å½ÕĞ€ô±…å½ÕĞ°(€€€€€€€€€€€€€€€€€€€%µ…•5…ÑÉ¥à€ô=É¥•¹Ñ…Ñ¥½¹QÉ…¹Í™½É´¹A±…”¡½É¥•¹Ñ…Ñ¥½¸°±…å½ÕĞ¤(€€€€€€€€€€€€€€€ôì(€€€€€€€€€€€ô((€€€€€€€€€€€ÕÍ¥¹œ€¡	¥Ñµ…ÀÍ½ÕÉ”€ô%µ…•Q½½±Ì¹1½…‘QÉ…¹Í™½Éµ•¡Í¹…ÁÍ¡½Ğ¹A…Ñ °Í¹…ÁÍ¡½Ğ¹5…¹Õ…±I½Ñ…Ñ¥½¸°½ÁÑ¥½¹Ì¹ÕÑ½I½Ñ…Ñ”¤¤(€€€€€€€€€€€ì(€€€€€€€€€€€€€€€Ñ½­•¸¹Q¡É½İ%™…¹•±±…Ñ¥½¹I•ÅÕ•ÍÑ• ¤ì(€€€€€€€€€€€€€€€A…•1…å½ÕĞ±…å½ÕĞ€ô%µ…•Q½½±Ì¹…±Õ±…Ñ•1…å½ÕĞ¡Í½ÕÉ”¹]¥‘Ñ °Í½ÕÉ”¹!•¥¡Ğ°½ÁÑ¥½¹Ì¹A…Á•ÉM¥é”°½ÁÑ¥½¹Ì¹=É¥•¹Ñ…Ñ¥½¸°½ÁÑ¥½¹Ì¹5…É¥¹5´¤ì(€€€€€€€€€€€€€€€¥¹ĞÑ…É•Ñ]¥‘Ñ ì(€€€€€€€€€€€€€€€¥¹ĞÑ…É•Ñ!•¥¡Ğì(€€€€€€€€€€€€€€€¥˜€¡½ÁÑ¥½¹Ì¹EÕ…±¥Ñä€ôôEÕ…±¥ÑåAÉ•Í•Ğ¹1½ÍÍ±•ÍÌ¤(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€Ñ…É•Ñ]¥‘Ñ €ôÍ½ÕÉ”¹]¥‘Ñ ì(€€€€€€€€€€€€€€€€€€€Ñ…É•Ñ!•¥¡Ğ€ôÍ½ÕÉ”¹!•¥¡Ğì(€€€€€€€€€€€€€€€ô(€€€€€€€€€€€€€€€•±Í”(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€¥¹Ğ‘Á¤€ôEÕ…±¥ÑåM•ÑÑ¥¹Ì¹•ÑÁ¤¡½ÁÑ¥½¹Ì¹EÕ…±¥Ñä¤ì(€€€€€€€€€€€€€€€€€€€Ñ…É•Ñ]¥‘Ñ €ô5…Ñ ¹5…à Ä°€¡¥¹Ğ¥5…Ñ ¹I½Õ¹¡±…å½ÕĞ¹]¥‘Ñ¡AĞ€¼€ÜÉ˜€¨‘Á¤¤¤ì(€€€€€€€€€€€€€€€€€€€Ñ…É•Ñ!•¥¡Ğ€ô5…Ñ ¹5…à Ä°€¡¥¹Ğ¥5…Ñ ¹I½Õ¹¡±…å½ÕĞ¹!•¥¡ÑAĞ€¼€ÜÉ˜€¨‘Á¤¤¤ì(€€€€€€€€€€€€€€€€€€€Ñ…É•Ñ]¥‘Ñ €ô5…Ñ ¹5¥¸¡Ñ…É•Ñ]¥‘Ñ °5…Ñ ¹5…à Ä°Í½ÕÉ”¹]¥‘Ñ ¤¤ì(€€€€€€€€€€€€€€€€€€€Ñ…É•Ñ!•¥¡Ğ€ô5…Ñ ¹5¥¸¡Ñ…É•Ñ!•¥¡Ğ°5…Ñ ¹5…à Ä°Í½ÕÉ”¹!•¥¡Ğ¤¤ì(€€€€€€€€€€€€€€€ô((€€€€€€€€€€€€€€€ÕÍ¥¹œ€¡	¥Ñµ…ÀÉ•¹‘•É•€ô%µ…•Q½½±Ì¹I•¹‘•É%µ…”¡Í½ÕÉ”°Ñ…É•Ñ]¥‘Ñ °Ñ…É•Ñ!•¥¡Ğ¤¤(€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€Ñ½­•¸¹Q¡É½İ%™…¹•±±…Ñ¥½¹I•ÅÕ•ÍÑ• ¤ì(€€€€€€€€€€€€€€€€€€€‰½½°±½ÍÍ±•ÍÌ€ô½ÁÑ¥½¹Ì¹EÕ…±¥Ñä€ôôEÕ…±¥ÑåAÉ•Í•Ğ¹1½ÍÍ±•ÍÌì(€€€€€€€€€€€€€€€€€€€‰åÑ•mt‘…Ñ„€ô±½ÍÍ±•ÍÌ€ü%µ…•Q½½±Ì¹Q½1½ÍÍ±•ÍÍIˆ¡É•¹‘•É•¤€è%µ…•Q½½±Ì¹Q½)Á•œ¡É•¹‘•É•°EÕ…±¥ÑåM•ÑÑ¥¹Ì¹•Ñ)Á•EÕ…±¥Ñä¡½ÁÑ¥½¹Ì¹EÕ…±¥Ñä¤¤ì(€€€€€€€€€€€€€€€€€€€É•ÑÕÉ¸¹•ÜAÉ•Á…É•‘A…”(€€€€€€€€€€€€€€€€€€€ì(€€€€€€€€€€€€€€€€€€€€€€€%µ…•…Ñ„€ô‘…Ñ„°(€€€€€€€€€€€€€€€€€€€€€€€¹½‘¥¹œ€ô±½ÍÍ±•ÍÌ€üA‘™%µ…•¹½‘¥¹œ¹1½ÍÍ±•ÍÍIˆ€èA‘™%µ…•¹½‘¥¹œ¹)Á•œ°(€€€€€€€€€€€€€€€€€€€€€€€]¥‘Ñ €ôÉ•¹‘•É•¹]¥‘Ñ °(€€€€€€€€€€€€€€€€€€€€€€€!•¥¡Ğ€ôÉ•¹‘•É•¹!•¥¡Ğ°(€€€€€€€€€€€€€€€€€€€€€€€½µÁ½¹•¹ÑÌ€ô€Ì°(€€€€€€€€€€€€€€€€€€€€€€€1…å½ÕĞ€ô±…å½ÕĞ°(€€€€€€€€€€€€€€€€€€€€€€€%µ…•5…ÑÉ¥à€ô¹•ÜU¹¥Ñ5…ÑÉ¥àì€ô±…å½ÕĞ¹]¥‘Ñ¡AĞ°€ô±…å½ÕĞ¹!•¥¡ÑAĞ°€ô±…å½ÕĞ¹aAĞ°€ô±…å½ÕĞ¹eAĞô(€€€€€€€€€€€€€€€€€€€ôì(€€€€€€€€€€€€€€€ô(€€€€€€€€€€€ô(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÙ½¥I•Á±…•¥±”¡ÍÑÉ¥¹œÑ•µÁ½É…ÉåA…Ñ °ÍÑÉ¥¹œÑ…É•ÑA…Ñ ¤(€€€€€€€ì(€€€€€€€€€€€¥˜€¡¥±”¹á¥ÍÑÌ¡Ñ…É•ÑA…Ñ ¤¤¥±”¹I•Á±…”¡Ñ•µÁ½É…ÉåA…Ñ °Ñ…É•ÑA…Ñ °¹Õ±°¤ì(€€€€€€€€€€€•±Í”¥±”¹5½Ù”¡Ñ•µÁ½É…ÉåA…Ñ °Ñ…É•ÑA…Ñ ¤ì(€€€€€€€ô((€€€€€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÙ½¥QÉå•±•Ñ”¡ÍÑÉ¥¹œÁ…Ñ ¤(€€€€€€€ì(€€€€€€€€€€€ÑÉäì¥˜€¡¥±”¹á¥ÍÑÌ¡Á…Ñ ¤¤¥±”¹•±•Ñ”¡Á…Ñ ¤ìô…Ñ ìô(€€€€€€€ô(€€€ô)ô(