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
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LocalImageToPdf
{
    internal enum PdfRasterFormat
    {
        Png,
        Jpeg,
        Bmp,
        Tiff
    }

    internal sealed class PdfImageExportOptions
    {
        public string OutputDirectory { get; set; }
        public string PageRange { get; set; }
        public PdfRasterFormat Format { get; set; }
        public int Dpi { get; set; }
        public int JpegQuality { get; set; }
        public string CustomBaseName { get; set; }
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
            public PdfDocument Document { get; set; }
        }

        private sealed class PageWork
        {
            public int PageIndex { get; set; }
            public string TargetPath { get; set; }
            public Task RenderTask { get; set; }
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
                string planOutputDirectory = null;
                try
                {
                    PdfDocument document = plan.Document;
                    if (document == null) throw new InvalidOperationException("PDF 文档尚未准备完成。");
                    string sourceBaseName = SanitizeFileName(System.IO.Path.GetFileNameWithoutExtension(plan.Path));
                    string baseName = String.IsNullOrWhiteSpace(options.CustomBaseName)
                        ? sourceBaseName
                        : SanitizeFileName(options.CustomBaseName);
                    planOutputDirectory = CreateUniqueDirectory(options.OutputDirectory, baseName + "-转换后");
                    int digits = Math.Max(3, plan.PageCount.ToString(CultureInfo.InvariantCulture).Length);

                    int workerCount = Environment.ProcessorCount >= 4 ? 2 : 1;
                    for (int batchStart = 0; batchStart < plan.PageIndexes.Count; batchStart += workerCount)
                    {
                        token.ThrowIfCancellationRequested();
                        List<PageWork> batch = new List<PageWork>();
                        int batchEnd = Math.Min(plan.PageIndexes.Count, batchStart + workerCount);
                        for (int batchIndex = batchStart; batchIndex < batchEnd; batchIndex++)
                        {
                            int pageIndex = plan.PageIndexes[batchIndex];
                            PdfPage page = document.GetPage((uint)pageIndex);
                            uint width;
                            uint height;
                            try
                            {
                                GetRenderDimensions(page, options.Dpi, out width, out height);
                                string extension = GetFileExtension(options.Format);
                                string pageName = baseName + "_第" + (pageIndex + 1).ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + "页" + extension;
                                string targetPath = GetUniquePath(planOutputDirectory, pageName);
                                PdfPage pageForTask = page;
                                PageWork work = new PageWork { PageIndex = pageIndex, TargetPath = targetPath };
                                work.RenderTask = Task.Run(delegate
                                {
                                    try { RenderPage(pageForTask, targetPath, width, height, options, token); }
                                    finally { pageForTask.Dispose(); }
                                });
                                batch.Add(work);
                            }
                            catch
                            {
                                page.Dispose();
                                throw;
                            }
                        }

                        try { Task.WaitAll(batch.Select(delegate (PageWork item) { return item.RenderTask; }).ToArray()); }
                        catch (AggregateException) { }

                        Exception batchFailure = null;
                        bool batchCanceled = token.IsCancellationRequested;
                        foreach (PageWork work in batch)
                        {
                            if (work.RenderTask.Status == TaskStatus.RanToCompletion)
                            {
                                result.OutputFiles.Add(work.TargetPath);
                                completed++;
                                if (progress != null)
                                {
                                    progress(new PdfImageProgress
                                    {
                                        CompletedPages = completed,
                                        TotalPages = totalPages,
                                        SourceName = System.IO.Path.GetFileName(plan.Path),
                                        PageNumber = work.PageIndex + 1
                                    });
                                }
                                continue;
                            }
                            if (work.RenderTask.IsCanceled)
                            {
                                batchCanceled = true;
                                continue;
                            }
                            if (work.RenderTask.Exception != null && batchFailure == null)
                            {
                                AggregateException flattened = work.RenderTask.Exception.Flatten();
                                foreach (Exception failure in flattened.InnerExceptions)
                                {
                                    if (failure is OperationCanceledException) batchCanceled = true;
                                    else if (batchFailure == null) batchFailure = failure;
                                }
                            }
                        }
                        if (batchCanceled) throw new OperationCanceledException(token);
                        if (batchFailure != null) throw batchFailure;
                    }
                    plan.Document = null;
                }
                catch (OperationCanceledException)
                {
                    plan.Document = null;
                    TryDeleteEmptyDirectory(planOutputDirectory);
                    throw;
                }
                catch (Exception error)
                {
                    plan.Document = null;
                    TryDeleteEmptyDirectory(planOutputDirectory);
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
                    StorageFile sourceFile = StorageFile.GetFileFromPathAsync(path).AsTask(token).GetAwaiter().GetResult();
                    PdfDocument document = PdfDocument.LoadFromFileAsync(sourceFile).AsTask(token).GetAwaiter().GetResult();
                    if (document.PageCount > Int32.MaxValue) throw new InvalidOperationException("PDF 页数超过支持范围。");
                    int pageCount = (int)document.PageCount;
                    plans.Add(new RenderPlan
                    {
                        Path = path,
                        PageCount = (uint)pageCount,
                        PageIndexes = ParsePageRange(pageRange, pageCount),
                        Document = document
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
            if (!String.IsNullOrWhiteSpace(options.CustomBaseName) && options.CustomBaseName.Trim().Length > 64)
                throw new InvalidOperationException("自定义名称不能超过 64 个字符。");
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
            string outputTemporary = System.IO.Path.Combine(directory, ".pdf-render-" + Guid.NewGuid().ToString("N") + GetFileExtension(options.Format));
            try
            {
                if (options.Format == PdfRasterFormat.Png)
                    RenderPngToFile(page, outputTemporary, width, height, token);
                else
                {
                    using (InMemoryRandomAccessStream renderedPng = RenderPngToMemory(page, width, height, token))
                    using (Stream renderedStream = renderedPng.AsStreamForRead())
                    {
                        if (options.Format == PdfRasterFormat.Jpeg)
                            EncodeJpeg(renderedStream, outputTemporary, options.JpegQuality, options.Dpi, token);
                        else if (options.Format == PdfRasterFormat.Bmp)
                            EncodeLosslessRaster(renderedStream, outputTemporary, ImageFormat.Bmp, options.Dpi, token);
                        else if (options.Format == PdfRasterFormat.Tiff)
                            EncodeLosslessRaster(renderedStream, outputTemporary, ImageFormat.Tiff, options.Dpi, token);
                    }
                }
                token.ThrowIfCancellationRequested();
                File.Move(outputTemporary, targetPath);
            }
            finally
            {
                TryDelete(outputTemporary);
            }
        }

        private static InMemoryRandomAccessStream RenderPngToMemory(PdfPage page, uint width, uint height, CancellationToken token)
        {
            InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
            try
            {
                PdfPageRenderOptions renderOptions = CreateRenderOptions(width, height);
                page.RenderToStreamAsync(stream, renderOptions).AsTask(token).GetAwaiter().GetResult();
                stream.Seek(0);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
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
                PdfPageRenderOptions renderOptions = CreateRenderOptions(width, height);
                page.RenderToStreamAsync(stream, renderOptions).AsTask(token).GetAwaiter().GetResult();
                stream.FlushAsync().AsTask(token).GetAwaiter().GetResult();
            }
        }

        private static PdfPageRenderOptions CreateRenderOptions(uint width, uint height)
        {
            return new PdfPageRenderOptions
            {
                DestinationWidth = width,
                DestinationHeight = height,
                BackgroundColor = Windows.UI.Colors.White,
                IsIgnoringHighContrast = true
            };
        }

        private static void EncodeJpeg(Stream pngStream, string jpegPath, int quality, int dpi, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (Image source = Image.FromStream(pngStream, true, true))
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

        private static void EncodeLosslessRaster(Stream pngStream, string outputPath, ImageFormat format, int dpi, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (Image source = Image.FromStream(pngStream, true, true))
            using (Bitmap flattened = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb))
            {
                flattened.SetResolution(dpi, dpi);
                using (Graphics graphics = Graphics.FromImage(flattened))
                {
                    graphics.Clear(Color.White);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(source, 0, 0);
                }
                token.ThrowIfCancellationRequested();
                flattened.Save(outputPath, format);
            }
        }

        private static string GetFileExtension(PdfRasterFormat format)
        {
            if (format == PdfRasterFormat.Png) return ".png";
            if (format == PdfRasterFormat.Jpeg) return ".jpg";
            if (format == PdfRasterFormat.Bmp) return ".bmp";
            if (format == PdfRasterFormat.Tiff) return ".tif";
            throw new InvalidOperationException("不支持所选图片格式。");
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

        private static string CreateUniqueDirectory(string parentDirectory, string folderName)
        {
            string first = System.IO.Path.Combine(parentDirectory, folderName);
            if (!Directory.Exists(first) && !File.Exists(first))
            {
                Directory.CreateDirectory(first);
                return first;
            }

            for (int index = 2; index < Int32.MaxValue; index++)
            {
                string candidate = System.IO.Path.Combine(parentDirectory, folderName + "(" + index.ToString(CultureInfo.InvariantCulture) + ")");
                if (Directory.Exists(candidate) || File.Exists(candidate)) continue;
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            throw new IOException("无法生成不重复的输出文件夹名称。");
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

        private static void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                    Directory.Delete(path, false);
            }
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
                if (args.Length < 3) throw new ArgumentException("用法：--pdf-to-images input.pdf outputFolder [png|jpg|bmp|tif] [150|220|300] [pages] [customName]");
                PdfRasterFormat format = ParseFormat(args.Length > 3 ? args[3] : "png");
                int dpi = 150;
                if (args.Length > 4 && !Int32.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out dpi)) throw new ArgumentException("DPI 必须是 150、220 或 300。");
                string range = args.Length > 5 ? args[5] : "全部";
                PdfImageExportResult result = PdfToImageExporter.Export(
                    new[] { args[1] },
                    new PdfImageExportOptions { OutputDirectory = args[2], PageRange = range, Format = format, Dpi = dpi, JpegQuality = 92, CustomBaseName = args.Length > 6 ? args[6] : null },
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

        private static PdfRasterFormat ParseFormat(string value)
        {
            if (String.Equals(value, "png", StringComparison.OrdinalIgnoreCase)) return PdfRasterFormat.Png;
            if (String.Equals(value, "jpg", StringComparison.OrdinalIgnoreCase) || String.Equals(value, "jpeg", StringComparison.OrdinalIgnoreCase)) return PdfRasterFormat.Jpeg;
            if (String.Equals(value, "bmp", StringComparison.OrdinalIgnoreCase)) return PdfRasterFormat.Bmp;
            if (String.Equals(value, "tif", StringComparison.OrdinalIgnoreCase) || String.Equals(value, "tiff", StringComparison.OrdinalIgnoreCase)) return PdfRasterFormat.Tiff;
            throw new ArgumentException("图片格式必须是 PNG、JPEG、BMP 或 TIFF。");
        }
    }
}
