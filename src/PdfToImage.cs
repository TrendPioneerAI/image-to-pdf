using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LocalImageToPdf
{
    internal enum PdfRasterFormat
    {
        Png,
        Jpeg
    }

    internal sealed class PdfImageExportOptions
    {
        public string OutputDirectory { get; set; }
        public string PageRange { get; set; }
        public PdfRasterFormat Format { get; set; }
        public int Dpi { get; set; }
        public int JpegQuality { get; set; }
    }

    internal sealed class PdfImageProgress
    {
        public int CompletedPages { get; set; }
        public int TotalPages { get; set; }
        public string SourceName { get; set; }
        public int PageNumber { get; set; }
    }

    internal sealed class PdfImageFailure
    {
        public string SourcePath { get; set; }
        public string Message { get; set; }
    }

    internal sealed class PdfImageExportResult
    {
        public PdfImageExportResult()
        {
            OutputFiles = new List<string>();
            Failures = new List<PdfImageFailure>();
        }

        public List<string> OutputFiles { get; private set; }
        public List<PdfImageFailure> Failures { get; private set; }
    }

    internal static class PdfToImageExporter
    {
        private const long MaximumPixelsPerPage = 100000000L;

        private sealed class RenderPlan
        {
            public string Path { get; set; }
            public uint PageCount { get; set; }
            public List<int> PageIndexes { get; set; }
        }

        public static bool IsSupportedPath(string path)
        {
            return !String.IsNullOrWhiteSpace(path) &&
                String.Equals(System.IO.Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        public static int GetPageCount(string path, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("PDF 路径不能为空。", "path");
            token.ThrowIfCancellationRequested();
            StorageFile file = StorageFile.GetFileFromPathAsync(System.IO.Path.GetFullPath(path)).AsTask(token).GetAwaiter().GetResult();
            PdfDocument document = PdfDocument.LoadFromFileAsync(file).AsTask(token).GetAwaiter().GetResult();
            token.ThrowIfCancellationRequested();
            if (document.PageCount > Int32.MaxValue) throw new InvalidOperationException("PDF 页数超过支持范围。");
            return (int)document.PageCount;
        }

        public static PdfImageExportResult Export(
            IList<string> sourcePaths,
            PdfImageExportOptions options,
            Action<PdfImageProgress> progress,
            CancellationToken token)
        {
            if (sourcePaths == null || sourcePaths.Count == 0) throw new InvalidOperationException("请先添加 PDF 文件。");
            if (options == null) throw new ArgumentNullException("options");
            ValidateOptions(options);
            Directory.CreateDirectory(options.OutputDirectory);

            PdfImageExportResult result = new PdfImageExportResult();
            List<RenderPlan> plans = PreparePlans(sourcePaths, options.PageRange, result, token);
            int totalPages = plans.Sum(delegate (RenderPlan plan) { return plan.PageIndexes.Count; });
            int completed = 0;

            foreach (RenderPlan plan in plans)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    StorageFile sourceFile = StorageFile.GetFileFromPathAsync(plan.Path).AsTask(token).GetAwaiter().GetResult();
                    PdfDocument document = PdfDocument.LoadFromFileAsync(sourceFile).AsTask(token).GetAwaiter().GetResult();
                    string baseName = SanitizeFileName(System.IO.Path.GetFileNameWithoutExtension(plan.Path));
                    int digits = Math.Max(3, plan.PageCount.ToString(CultureInfo.InvariantCulture).Length);

                    foreach (int pageIndex in plan.PageIndexes)
                    {
                        token.ThrowIfCancellationRequested();
                        using (PdfPage page = document.GetPage((uint)pageIndex))
                        {
                            uint width;
                            uint height;
                            GetRenderDimensions(page, options.Dpi, out width, out height);
                            string extension = options.Format == PdfRasterFormat.Png ? ".png" : ".jpg";
                            string pageName = baseName + "_第" + (pageIndex + 1).ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + "页" + extension;
                            string targetPath = GetUniquePath(options.OutputDirectory, pageName);
                            RenderPage(page, targetPath, width, height, options, token);
                            result.OutputFiles.Add(targetPath);
                        }

                        completed++;
                        if (progress != null)
                        {
                            progress(new PdfImageProgress
                            {
                                CompletedPages = completed,
                                TotalPages = totalPages,
                                SourceName = System.IO.Path.GetFileName(plan.Path),
                                PageNumber = pageIndex + 1
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    result.Failures.Add(new PdfImageFailure { SourcePath = plan.Path, Message = FriendlyError(error) });
                }
            }

            return result;
        }

        public static List<int> ParsePageRange(string text, int pageCount)
        {
            if (pageCount < 1) throw new InvalidOperationException("PDF 不包含可转换页面。");
            string value = (text ?? String.Empty).Trim();
            if (value.Length == 0 || String.Equals(value, "全部", StringComparison.OrdinalIgnoreCase) || String.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
                return Enumerable.Range(0, pageCount).ToList();

            value = value.Replace('，', ',').Replace('－', '-').Replace('—', '-');
            List<int> pages = new List<int>();
            HashSet<int> seen = new HashSet<int>();
            string[] tokens = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) throw new FormatException("页码范围不能为空。");

            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();
                if (token.Length == 0) continue;
                int dash = token.IndexOf('-');
                if (dash >= 0)
                {
                    if (dash == 0 || dash != token.LastIndexOf('-') || dash == token.Length - 1)
                        throw new FormatException("页码范围格式不正确：" + token);
                    int start = ParsePositivePage(token.Substring(0, dash).Trim(), token);
                    int end = ParsePositivePage(token.Substring(dash + 1).Trim(), token);
                    if (start > end) throw new FormatException("页码范围起始页不能大于结束页：" + token);
                    int cappedEnd = Math.Min(end, pageCount);
                    for (int page = start; page <= cappedEnd; page++)
                    {
                        int index = page - 1;
                        if (seen.Add(index)) pages.Add(index);
                    }
                }
                else
                {
                    int page = ParsePositivePage(token, token);
                    if (page <= pageCount && seen.Add(page - 1)) pages.Add(page - 1);
                }
            }

            if (pages.Count == 0) throw new FormatException("所选页码不在此 PDF 的有效页数内（共 " + pageCount.ToString(CultureInfo.InvariantCulture) + " 页）。");
            return pages;
        }

        private static List<RenderPlan> PreparePlans(IList<string> sourcePaths, string pageRange, PdfImageExportResult result, CancellationToken token)
        {
            List<RenderPlan> plans = new List<RenderPlan>();
            foreach (string rawPath in sourcePaths)
            {
                token.ThrowIfCancellationRequested();
                string path;
                try
                {
                    path = System.IO.Path.GetFullPath(rawPath);
                    if (!File.Exists(path)) throw new FileNotFoundException("PDF 文件不存在。", path);
                    if (!IsSupportedPath(path)) throw new InvalidOperationException("仅支持 PDF 文件。");
                    int pageCount = GetPageCount(path, token);
                    plans.Add(new RenderPlan
                    {
                        Path = path,
                        PageCount = (uint)pageCount,
                        PageIndexes = ParsePageRange(pageRange, pageCount)
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    result.Failures.Add(new PdfImageFailure { SourcePath = rawPath, Message = FriendlyError(error) });
                }
            }
            return plans;
        }

        private static void ValidateOptions(PdfImageExportOptions options)
        {
            if (String.IsNullOrWhiteSpace(options.OutputDirectory)) throw new InvalidOperationException("请选择图片输出文件夹。");
            options.OutputDirectory = System.IO.Path.GetFullPath(options.OutputDirectory);
            if (options.Dpi != 150 && options.Dpi != 220 && options.Dpi != 300)
                throw new InvalidOperationException("分辨率仅支持 150、220 或 300 DPI。");
            if (options.JpegQuality < 50 || options.JpegQuality > 100)
                throw new InvalidOperationException("JPEG 质量应在 50～100 之间。");
        }

        private static int ParsePositivePage(string value, string token)
        {
            int page;
            if (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1)
                throw new FormatException("页码范围格式不正确：" + token);
            return page;
        }

        private static void GetRenderDimensions(PdfPage page, int dpi, out uint width, out uint height)
        {
            double desiredWidth = Math.Max(1.0, Math.Round(page.Size.Width * dpi / 96.0));
            double desiredHeight = Math.Max(1.0, Math.Round(page.Size.Height * dpi / 96.0));
            if (desiredWidth > UInt32.MaxValue || desiredHeight > UInt32.MaxValue || desiredWidth * desiredHeight > MaximumPixelsPerPage)
                throw new InvalidOperationException("此页在所选 DPI 下超过 1 亿像素，请降低输出分辨率。");
            width = (uint)desiredWidth;
            height = (uint)desiredHeight;
        }

        private static void RenderPage(PdfPage page, string targetPath, uint width, uint height, PdfImageExportOptions options, CancellationToken token)
        {
            string directory = System.IO.Path.GetDirectoryName(targetPath);
            string pngTemporary = System.IO.Path.Combine(directory, ".pdf-render-" + Guid.NewGuid().ToString("N") + ".png");
            string outputTemporary = options.Format == PdfRasterFormat.Png
                ? pngTemporary
                : System.IO.Path.Combine(directory, ".pdf-render-" + Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                RenderPngToFile(page, pngTemporary, width, height, token);
                token.ThrowIfCancellationRequested();
                if (options.Format == PdfRasterFormat.Jpeg)
                    EncodeJpeg(pngTemporary, outputTemporary, options.JpegQuality, options.Dpi, token);
                token.ThrowIfCancellationRequested();
                File.Move(outputTemporary, targetPath);
            }
            finally
            {
                TryDelete(pngTemporary);
                if (!String.Equals(outputTemporary, pngTemporary, StringComparison.OrdinalIgnoreCase)) TryDelete(outputTemporary);
            }
        }

        private static void RenderPngToFile(PdfPage page, string path, uint width, uint height, CancellationToken token)
        {
            using (FileStream seed = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            StorageFile outputFile = StorageFile.GetFileFromPathAsync(path).AsTask(token).GetAwaiter().GetResult();
            using (IRandomAccessStream stream = outputFile.OpenAsync(FileAccessMode.ReadWrite).AsTask(token).GetAwaiter().GetResult())
            {
                stream.Size = 0;
                stream.Seek(0);
                PdfPageRenderOptions renderOptions = new PdfPageRenderOptions
                {
                    DestinationWidth = width,
                    DestinationHeight = height,
                    BackgroundColor = Windows.UI.Colors.White,
                    IsIgnoringHighContrast = true
                };
                page.RenderToStreamAsync(stream, renderOptions).AsTask(token).GetAwaiter().GetResult();
                stream.FlushAsync().AsTask(token).GetAwaiter().GetResult();
            }
        }

        private static void EncodeJpeg(string pngPath, string jpegPath, int quality, int dpi, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (Image source = Image.FromFile(pngPath))
            using (Bitmap flattened = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb))
            {
                flattened.SetResolution(dpi, dpi);
                using (Graphics graphics = Graphics.FromImage(flattened))
                {
                    graphics.Clear(Color.White);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(source, 0, 0);
                }
                ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(delegate (ImageCodecInfo item) { return item.FormatID == ImageFormat.Jpeg.Guid; });
                using (EncoderParameters parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                    flattened.Save(jpegPath, codec, parameters);
                }
            }
        }

        private static string GetUniquePath(string directory, string fileName)
        {
            string first = System.IO.Path.Combine(directory, fileName);
            if (!File.Exists(first)) return first;
            string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            string extension = System.IO.Path.GetExtension(fileName);
            for (int index = 2; index < Int32.MaxValue; index++)
            {
                string candidate = System.IO.Path.Combine(directory, baseName + "(" + index.ToString(CultureInfo.InvariantCulture) + ")" + extension);
                if (!File.Exists(candidate)) return candidate;
            }
            throw new IOException("无法生成不重复的输出文件名。");
        }

        private static string SanitizeFileName(string value)
        {
            string result = value ?? String.Empty;
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            result = result.Trim().TrimEnd('.');
            return String.IsNullOrWhiteSpace(result) ? "PDF" : result;
        }

        private static string FriendlyError(Exception error)
        {
            Exception current = error;
            while (current.InnerException != null) current = current.InnerException;
            string message = current.Message;
            if (String.IsNullOrWhiteSpace(message)) message = current.GetType().Name;
            return message;
        }

        private static void TryDelete(string path)
        {
            try { if (!String.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }

    internal static class PdfToImageCommandLine
    {
        public static bool TryRun(string[] args)
        {
            if (args == null || args.Length == 0 || !String.Equals(args[0], "--pdf-to-images", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                if (args.Length < 3) throw new ArgumentException("用法：--pdf-to-images input.pdf outputFolder [png|jpg] [150|220|300] [pages]");
                PdfRasterFormat format = args.Length > 3 && String.Equals(args[3], "jpg", StringComparison.OrdinalIgnoreCase) ? PdfRasterFormat.Jpeg : PdfRasterFormat.Png;
                int dpi = 150;
                if (args.Length > 4 && !Int32.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out dpi)) throw new ArgumentException("DPI 必须是 150、220 或 300。");
                string range = args.Length > 5 ? args[5] : "全部";
                PdfImageExportResult result = PdfToImageExporter.Export(
                    new[] { args[1] },
                    new PdfImageExportOptions { OutputDirectory = args[2], PageRange = range, Format = format, Dpi = dpi, JpegQuality = 92 },
                    null,
                    CancellationToken.None);
                if (result.Failures.Count > 0) throw new InvalidOperationException(result.Failures[0].Message);
                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
            return true;
        }
    }
}
