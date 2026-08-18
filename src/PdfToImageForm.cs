using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LocalImageToPdf
{
    internal sealed class PdfSourceItem
    {
        public string Path { get; set; }
        public int? PageCount { get; set; }
        public string Error { get; set; }
        public ListViewItem Row { get; set; }
    }

    internal sealed class PdfToImageForm : Form
    {
        private readonly IEnumerable<string> _initialPaths;
        private readonly List<PdfSourceItem> _sources = new List<PdfSourceItem>();
        private readonly HashSet<string> _paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly AppSettings _settings;
        private readonly SemaphoreSlim _inspectionGate = new SemaphoreSlim(2, 2);
        private readonly CancellationTokenSource _inspectionCancellation = new CancellationTokenSource();
        private ListView _sourceList;
        private Label _countLabel;
        private Label _statusLabel;
        private CheckBox _allPagesCheck;
        private TextBox _pageRangeBox;
        private ComboBox _formatCombo;
        private ComboBox _dpiCombo;
        private Label _jpegQualityLabel;
        private NumericUpDown _jpegQuality;
        private TextBox _outputPathBox;
        private Button _exportButton;
        private Button _cancelButton;
        private CancellationTokenSource _exportCancellation;
        private readonly bool _showReturnToImages;

        internal event EventHandler ReturnToImagesRequested;

        public PdfToImageForm(IEnumerable<string> initialPaths, Icon icon)
            : this(initialPaths, icon, false)
        {
        }

        public PdfToImageForm(IEnumerable<string> initialPaths, Icon icon, bool showReturnToImages)
        {
            _initialPaths = initialPaths ?? new string[0];
            _showReturnToImages = showReturnToImages;
            _settings = AppSettingsStore.Load();
            Text = "PDF转图片";
            Icon = icon;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(940, 640);
            BackColor = UiTheme.Background;
            Font = UiTheme.Font(9.5f, FontStyle.Regular);
            DoubleBuffered = true;
            AllowDrop = true;
            BuildUi();
            DragEnter += HandleDragEnter;
            DragDrop += HandleDragDrop;
            Shown += delegate
            {
                List<string> paths = _initialPaths.Where(delegate (string path) { return !String.IsNullOrWhiteSpace(path); }).ToList();
                if (paths.Count > 0) AddInputs(paths);
            };
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                BackColor = UiTheme.Background,
                Padding = new Padding(16, 10, 16, 12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildContent(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
            Controls.Add(root);
            EnableDropRecursive(root);
            UpdateCount();
            UpdateFormatUi();
        }

        private Control BuildHeader()
        {
            Panel header = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
            Label title = new Label
            {
                Left = 10,
                Top = 10,
                AutoSize = true,
                MaximumSize = new Size(210, 0),
                Text = "PDF 转图片",
                Font = UiTheme.Font(16f, FontStyle.Bold),
                ForeColor = UiTheme.Text
            };
            _countLabel = new Label
            {
                Left = 12,
                Top = 52,
                AutoSize = true,
                MaximumSize = new Size(210, 0),
                Text = "共 0 个 PDF",
                ForeColor = UiTheme.Muted
            };
            LinkLabel returnToImages = null;
            if (_showReturnToImages)
            {
                returnToImages = new LinkLabel
                {
                    Left = 12,
                    Top = 82,
                    AutoSize = true,
                    Text = "← 返回图片转 PDF",
                    LinkColor = UiTheme.Primary,
                    ActiveLinkColor = UiTheme.Primary,
                    VisitedLinkColor = UiTheme.Primary,
                    Font = UiTheme.Font(9.2f, FontStyle.Regular)
                };
                returnToImages.LinkClicked += delegate
                {
                    EventHandler handler = ReturnToImagesRequested;
                    if (handler != null) handler(this, EventArgs.Empty); else Close();
                };
            }

            DropHintPanel dropHint = new DropHintPanel { Left = 225, Top = 13, Width = 350, Height = 64, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            Label dropTitle = new Label { Left = 22, Top = 9, Width = 305, Height = 23, Text = "拖入 PDF 文件或文件夹", TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.Text, Font = UiTheme.Font(10f, FontStyle.Regular) };
            Label dropSub = new Label { Left = 18, Top = 32, Width = 315, Height = 20, Text = "文件夹只读取当前层，不递归子文件夹", TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.Muted, Font = UiTheme.Font(8.7f, FontStyle.Regular) };
            dropHint.Controls.Add(dropTitle);
            dropHint.Controls.Add(dropSub);
            dropHint.Click += delegate { ChooseFiles(); };
            dropTitle.Click += delegate { ChooseFiles(); };
            dropSub.Click += delegate { ChooseFiles(); };

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Width = 500,
                Height = 56,
                Top = 16,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Button addFiles = UiTheme.Button("＋ 添加 PDF", 130, 44);
            Button addFolder = UiTheme.Button("添加文件夹", 120, 44);
            Button remove = UiTheme.Button("移除所选", 110, 44);
            Button clear = UiTheme.Button("清空", 82, 44);
            addFiles.BackColor = UiTheme.PrimarySoft;
            addFiles.ForeColor = UiTheme.Primary;
            addFiles.FlatAppearance.BorderColor = UiTheme.Primary;
            addFiles.Click += delegate { ChooseFiles(); };
            addFolder.Click += delegate { ChooseFolder(); };
            remove.Click += delegate { RemoveSelected(); };
            clear.Click += delegate { ClearSources(); };
            actions.Controls.Add(addFiles);
            actions.Controls.Add(addFolder);
            actions.Controls.Add(remove);
            actions.Controls.Add(clear);

            header.Controls.Add(title);
            header.Controls.Add(_countLabel);
            if (returnToImages != null) header.Controls.Add(returnToImages);
            header.Controls.Add(dropHint);
            header.Controls.Add(actions);
            header.Resize += delegate
            {
                actions.Left = Math.Max(590, header.ClientSize.Width - actions.Width);
                dropHint.Visible = actions.Left - dropHint.Right > 15;
            };
            return header;
        }

        private Control BuildContent()
        {
            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430f));

            Panel listShell = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(10), Margin = new Padding(0, 0, 6, 0), BorderStyle = BorderStyle.FixedSingle };
            _sourceList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = true,
                HideSelection = false,
                ShowItemToolTips = true,
                BorderStyle = BorderStyle.None,
                BackColor = UiTheme.Surface,
                ForeColor = UiTheme.Text,
                Font = UiTheme.Font(9.5f, FontStyle.Regular)
            };
            _sourceList.Columns.Add("PDF 文件", 260);
            _sourceList.Columns.Add("页数", 90);
            _sourceList.Columns.Add("大小", 100);
            _sourceList.Columns.Add("位置", 420);
            _sourceList.KeyDown += delegate (object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Delete) { RemoveSelected(); args.Handled = true; }
            };
            _sourceList.DoubleClick += delegate { OpenSelectedSource(); };
            _sourceList.Resize += delegate
            {
                if (_sourceList.Columns.Count == 4)
                    _sourceList.Columns[3].Width = Math.Max(180, _sourceList.ClientSize.Width - 470);
            };
            listShell.Controls.Add(_sourceList);
            content.Controls.Add(listShell, 0, 0);
            content.Controls.Add(BuildSettingsSidebar(), 1, 0);
            return content;
        }

        private Control BuildSettingsSidebar()
        {
            Panel shell = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Padding = new Padding(10), Margin = new Padding(6, 0, 0, 0), BorderStyle = BorderStyle.FixedSingle };
            FlowLayoutPanel list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = UiTheme.Surface,
                Padding = new Padding(4)
            };
            list.Controls.Add(BuildPageSection());
            list.Controls.Add(BuildImageSection());
            list.Controls.Add(BuildOutputSection());
            shell.Controls.Add(list);
            return shell;
        }

        private Control BuildPageSection()
        {
            Panel panel = SectionPanel(370, 170);
            panel.Controls.Add(SectionTitle("页面", 10));
            _allPagesCheck = new CheckBox { Left = 18, Top = 52, Width = 330, Height = 28, Text = "转换全部页面", Checked = true, ForeColor = UiTheme.Text };
            _allPagesCheck.CheckedChanged += delegate
            {
                _pageRangeBox.Enabled = !_allPagesCheck.Checked;
                if (!_allPagesCheck.Checked) _pageRangeBox.Focus();
            };
            panel.Controls.Add(_allPagesCheck);
            panel.Controls.Add(FieldLabel("指定页码", 92));
            _pageRangeBox = new TextBox { Left = 118, Top = 87, Width = 232, Height = 28, Enabled = false, Text = "1-3,5", BorderStyle = BorderStyle.FixedSingle };
            panel.Controls.Add(_pageRangeBox);
            panel.Controls.Add(new Label { Left = 18, Top = 126, Width = 332, Height = 34, Text = "支持 1-3,5；超过某个 PDF 总页数的页码会被忽略。", ForeColor = UiTheme.Muted, Font = UiTheme.Font(8.4f, FontStyle.Regular) });
            return panel;
        }

        private Control BuildImageSection()
        {
            Panel panel = SectionPanel(370, 224);
            panel.Controls.Add(SectionTitle("图片设置", 10));
            panel.Controls.Add(FieldLabel("图片格式", 59));
            _formatCombo = MakeCombo(new[] { "PNG（无损，文字清晰）", "JPEG（文件较小）" }, 118, 52, 232);
            _formatCombo.SelectedIndex = 0;
            _formatCombo.SelectedIndexChanged += delegate { UpdateFormatUi(); };
            panel.Controls.Add(_formatCombo);
            panel.Controls.Add(FieldLabel("输出分辨率", 109));
            _dpiCombo = MakeCombo(new[] { "150 DPI（推荐/快速）", "220 DPI（标准）", "300 DPI（精细）" }, 118, 102, 232);
            _dpiCombo.SelectedIndex = 0;
            panel.Controls.Add(_dpiCombo);
            _jpegQualityLabel = FieldLabel("JPEG 质量", 159);
            _jpegQuality = new NumericUpDown { Left = 118, Top = 153, Width = 112, Height = 28, Minimum = 50, Maximum = 100, Value = 92, Increment = 1 };
            panel.Controls.Add(_jpegQualityLabel);
            panel.Controls.Add(_jpegQuality);
            panel.Controls.Add(new Label { Left = 18, Top = 190, Width = 332, Height = 26, Text = "PNG 不二次压缩；JPEG 默认质量 92。", ForeColor = UiTheme.Muted, Font = UiTheme.Font(8.4f, FontStyle.Regular) });
            return panel;
        }

        private Control BuildOutputSection()
        {
            Panel panel = SectionPanel(370, 180);
            panel.Controls.Add(SectionTitle("输出", 10));
            panel.Controls.Add(FieldLabel("输出文件夹", 59));
            _outputPathBox = new TextBox { Left = 18, Top = 88, Width = 250, Height = 28, Text = _settings.LastOutputDirectory, BorderStyle = BorderStyle.FixedSingle };
            Button browse = UiTheme.Button("浏览", 76, 32);
            browse.Left = 274;
            browse.Top = 84;
            browse.Click += delegate { BrowseOutputFolder(); };
            panel.Controls.Add(_outputPathBox);
            panel.Controls.Add(browse);
            panel.Controls.Add(new Label
            {
                Left = 18,
                Top = 127,
                Width = 332,
                Height = 44,
                Text = "命名格式：PDF名_第001页.png；不会覆盖已有文件，同名自动追加序号。",
                ForeColor = UiTheme.Muted,
                Font = UiTheme.Font(8.4f, FontStyle.Regular)
            });
            return panel;
        }

        private Control BuildFooter()
        {
            Panel footer = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Margin = new Padding(0, 10, 0, 0), BorderStyle = BorderStyle.FixedSingle };
            Label privacy = new Label
            {
                Left = 20,
                Top = 21,
                Width = 430,
                Height = 28,
                Text = "▣  本地处理 · 不上传文件 · 免费开源 · 由 ZenthZhang 开发",
                ForeColor = UiTheme.Muted,
                Font = UiTheme.Font(9f, FontStyle.Regular)
            };
            _statusLabel = new Label { Top = 21, Width = 290, Height = 28, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.Muted };
            _cancelButton = UiTheme.Button("取消", 90, 44);
            _cancelButton.Top = 10;
            _cancelButton.Visible = false;
            _cancelButton.Click += delegate { if (_exportCancellation != null) _exportCancellation.Cancel(); };
            _exportButton = UiTheme.Button("导出图片", 250, 46);
            _exportButton.Top = 9;
            _exportButton.BackColor = UiTheme.Primary;
            _exportButton.ForeColor = Color.White;
            _exportButton.FlatAppearance.BorderColor = UiTheme.Primary;
            _exportButton.Font = UiTheme.Font(11f, FontStyle.Bold);
            _exportButton.Click += ExportClicked;
            footer.Controls.Add(privacy);
            footer.Controls.Add(_statusLabel);
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_exportButton);
            footer.Resize += delegate
            {
                _exportButton.Left = footer.ClientSize.Width - _exportButton.Width - 12;
                _cancelButton.Left = _exportButton.Left - _cancelButton.Width - 10;
                _statusLabel.Left = _cancelButton.Left - _statusLabel.Width - 12;
                privacy.Width = Math.Max(220, _statusLabel.Left - privacy.Left - 10);
            };
            return footer;
        }

        private void ChooseFiles()
        {
            if (_exportCancellation != null) return;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "添加 PDF 文件";
                dialog.Multiselect = true;
                dialog.Filter = "PDF 文件|*.pdf";
                if (dialog.ShowDialog(this) == DialogResult.OK) AddInputs(dialog.FileNames);
            }
        }

        private void ChooseFolder()
        {
            if (_exportCancellation != null) return;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 PDF 文件夹（只读取当前层，不读取子文件夹）";
                if (dialog.ShowDialog(this) == DialogResult.OK) AddInputs(new[] { dialog.SelectedPath });
            }
        }

        private void AddInputs(IEnumerable<string> inputs)
        {
            if (_exportCancellation != null) return;
            List<string> rejected = new List<string>();
            List<string> candidates = new List<string>();
            foreach (string raw in inputs ?? new string[0])
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                string path;
                try { path = Path.GetFullPath(raw); }
                catch { rejected.Add(raw + "（路径无效）"); continue; }
                if (Directory.Exists(path))
                {
                    try
                    {
                        string[] files = Directory.GetFiles(path, "*.pdf", SearchOption.TopDirectoryOnly);
                        Array.Sort(files, new PathNaturalComparer());
                        if (files.Length == 0) rejected.Add(Path.GetFileName(path) + "（当前层未找到 PDF）");
                        else candidates.AddRange(files);
                    }
                    catch (Exception error) { rejected.Add(Path.GetFileName(path) + "（无法读取文件夹：" + error.Message + "）"); }
                }
                else if (File.Exists(path))
                {
                    if (PdfToImageExporter.IsSupportedPath(path)) candidates.Add(path);
                    else rejected.Add(Path.GetFileName(path) + "（不是 PDF 文件）");
                }
                else rejected.Add(Path.GetFileName(path) + "（文件不存在）");
            }

            List<PdfSourceItem> added = new List<PdfSourceItem>();
            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                if (!_paths.Add(full)) continue;
                FileInfo info = new FileInfo(full);
                PdfSourceItem source = new PdfSourceItem { Path = full };
                ListViewItem row = new ListViewItem(info.Name);
                row.SubItems.Add("读取中…");
                row.SubItems.Add(FormatFileSize(info.Length));
                row.SubItems.Add(info.DirectoryName ?? String.Empty);
                row.Tag = source;
                source.Row = row;
                _sources.Add(source);
                _sourceList.Items.Add(row);
                added.Add(source);
            }
            UpdateCount();
            foreach (PdfSourceItem source in added) QueueInspection(source);
            ShowRejectedSummary(rejected);
        }

        internal void AddExternalInputs(IEnumerable<string> inputs)
        {
            if (inputs != null) AddInputs(inputs);
        }

        private void QueueInspection(PdfSourceItem source)
        {
            CancellationToken token = _inspectionCancellation.Token;
            Task.Run(delegate
            {
                int? pageCount = null;
                string error = null;
                bool entered = false;
                try
                {
                    _inspectionGate.Wait(token);
                    entered = true;
                    pageCount = PdfToImageExporter.GetPageCount(source.Path, token);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception failure) { error = failure.Message; }
                finally { if (entered) _inspectionGate.Release(); }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (IsDisposed || !_sources.Contains(source)) return;
                        source.PageCount = pageCount;
                        source.Error = error;
                        source.Row.SubItems[1].Text = error == null ? pageCount.Value.ToString() + " 页" : "无法读取";
                        source.Row.ForeColor = error == null ? UiTheme.Text : UiTheme.Danger;
                        source.Row.ToolTipText = error ?? source.Path;
                        UpdateCount();
                    });
                }
                catch { }
            });
        }

        private void RemoveSelected()
        {
            if (_exportCancellation != null || _sourceList.SelectedItems.Count == 0) return;
            List<PdfSourceItem> selected = _sourceList.SelectedItems.Cast<ListViewItem>().Select(delegate (ListViewItem row) { return row.Tag as PdfSourceItem; }).Where(delegate (PdfSourceItem item) { return item != null; }).ToList();
            foreach (PdfSourceItem source in selected)
            {
                _sources.Remove(source);
                _paths.Remove(source.Path);
                _sourceList.Items.Remove(source.Row);
            }
            UpdateCount();
        }

        private void ClearSources()
        {
            if (_exportCancellation != null || _sources.Count == 0) return;
            if (MessageBox.Show(this, "确定清空已添加的全部 PDF 吗？", "清空 PDF", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _sources.Clear();
            _paths.Clear();
            _sourceList.Items.Clear();
            UpdateCount();
        }

        private void OpenSelectedSource()
        {
            if (_sourceList.SelectedItems.Count != 1) return;
            PdfSourceItem source = _sourceList.SelectedItems[0].Tag as PdfSourceItem;
            if (source == null) return;
            try { Process.Start(new ProcessStartInfo(source.Path) { UseShellExecute = true }); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "无法打开 PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void BrowseOutputFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择转换后图片的输出文件夹";
                if (Directory.Exists(_outputPathBox.Text.Trim())) dialog.SelectedPath = _outputPathBox.Text.Trim();
                if (dialog.ShowDialog(this) == DialogResult.OK) _outputPathBox.Text = dialog.SelectedPath;
            }
        }

        private void ExportClicked(object sender, EventArgs e)
        {
            if (_exportCancellation != null) return;
            if (_sources.Count == 0)
            {
                MessageBox.Show(this, "请先添加 PDF 文件。", "无法导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string outputDirectory = _outputPathBox.Text.Trim();
            if (String.IsNullOrWhiteSpace(outputDirectory))
            {
                BrowseOutputFolder();
                outputDirectory = _outputPathBox.Text.Trim();
                if (String.IsNullOrWhiteSpace(outputDirectory)) return;
            }
            try
            {
                outputDirectory = Path.GetFullPath(outputDirectory);
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "输出文件夹无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PdfImageExportOptions options = new PdfImageExportOptions
            {
                OutputDirectory = outputDirectory,
                PageRange = _allPagesCheck.Checked ? "全部" : _pageRangeBox.Text.Trim(),
                Format = _formatCombo.SelectedIndex == 1 ? PdfRasterFormat.Jpeg : PdfRasterFormat.Png,
                Dpi = _dpiCombo.SelectedIndex == 1 ? 220 : (_dpiCombo.SelectedIndex == 2 ? 300 : 150),
                JpegQuality = (int)_jpegQuality.Value
            };
            List<string> sources = _sources.Select(delegate (PdfSourceItem item) { return item.Path; }).ToList();
            _exportCancellation = new CancellationTokenSource();
            CancellationToken token = _exportCancellation.Token;
            SetExportState(true, "正在准备转换…");
            Task.Run(delegate
            {
                try
                {
                    PdfImageExportResult result = PdfToImageExporter.Export(sources, options, delegate (PdfImageProgress progress)
                    {
                        try
                        {
                            BeginInvoke((Action)delegate
                            {
                                _statusLabel.Text = "正在转换 " + progress.CompletedPages.ToString() + "/" + progress.TotalPages.ToString() + " · " + progress.SourceName;
                            });
                        }
                        catch { }
                    }, token);
                    if (IsDisposed) return;
                    BeginInvoke((Action)delegate
                    {
                        if (result.OutputFiles.Count > 0) SaveSuccessfulOutputDirectory(options.OutputDirectory);
                        _statusLabel.Text = result.OutputFiles.Count > 0 ? "已导出 " + result.OutputFiles.Count.ToString() + " 张图片" : "没有图片导出";
                        MessageBox.Show(this, BuildCompletionMessage(result, options.OutputDirectory), result.OutputFiles.Count > 0 ? "转换完成" : "转换未完成", MessageBoxButtons.OK, result.Failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    });
                }
                catch (OperationCanceledException)
                {
                    if (!IsDisposed) try { BeginInvoke((Action)delegate { _statusLabel.Text = "已取消；已完成的图片已保留"; }); } catch { }
                }
                catch (Exception error)
                {
                    if (!IsDisposed) try { BeginInvoke((Action)delegate { _statusLabel.Text = "转换失败"; MessageBox.Show(this, error.Message, "转换失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }); } catch { }
                }
                finally
                {
                    if (!IsDisposed) try { BeginInvoke((Action)delegate { SetExportState(false, _statusLabel.Text); }); } catch { }
                }
            });
        }

        private void SetExportState(bool exporting, string status)
        {
            _exportButton.Enabled = !exporting && _sources.Count > 0;
            _cancelButton.Visible = exporting;
            _sourceList.Enabled = !exporting;
            _statusLabel.Text = status;
            UseWaitCursor = exporting;
            if (!exporting && _exportCancellation != null)
            {
                UseWaitCursor = false;
                _exportCancellation.Dispose();
                _exportCancellation = null;
            }
        }

        private void SaveSuccessfulOutputDirectory(string directory)
        {
            try
            {
                _settings.LastOutputDirectory = directory;
                AppSettingsStore.Save(_settings);
            }
            catch { }
        }

        private void UpdateFormatUi()
        {
            if (_jpegQuality == null) return;
            bool jpeg = _formatCombo.SelectedIndex == 1;
            _jpegQualityLabel.Visible = jpeg;
            _jpegQuality.Visible = jpeg;
        }

        private void UpdateCount()
        {
            if (_countLabel == null) return;
            int knownPages = _sources.Where(delegate (PdfSourceItem item) { return item.PageCount.HasValue; }).Sum(delegate (PdfSourceItem item) { return item.PageCount.Value; });
            bool pending = _sources.Any(delegate (PdfSourceItem item) { return !item.PageCount.HasValue && String.IsNullOrWhiteSpace(item.Error); });
            _countLabel.Text = "共 " + _sources.Count.ToString() + " 个 PDF" + (knownPages > 0 ? " · " + knownPages.ToString() + " 页" : String.Empty) + (pending ? "…" : String.Empty);
            if (_statusLabel != null && _exportCancellation == null)
                _statusLabel.Text = _sources.Count == 0 ? "拖入 PDF 开始转换" : "已添加 " + _sources.Count.ToString() + " 个 PDF";
            if (_exportButton != null && _exportCancellation == null) _exportButton.Enabled = _sources.Count > 0;
        }

        private static string BuildCompletionMessage(PdfImageExportResult result, string outputDirectory)
        {
            StringBuilder message = new StringBuilder();
            if (result.OutputFiles.Count > 0)
            {
                message.AppendLine("已导出 " + result.OutputFiles.Count.ToString() + " 张图片：");
                message.AppendLine(outputDirectory);
            }
            else message.AppendLine("没有成功导出图片。");
            if (result.Failures.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("以下 PDF 未完成：");
                int limit = Math.Min(8, result.Failures.Count);
                for (int index = 0; index < limit; index++)
                    message.AppendLine("• " + Path.GetFileName(result.Failures[index].SourcePath) + "：" + result.Failures[index].Message);
                if (result.Failures.Count > limit) message.AppendLine("……另有 " + (result.Failures.Count - limit).ToString() + " 个文件");
            }
            return message.ToString().TrimEnd();
        }

        private void ShowRejectedSummary(IList<string> rejected)
        {
            if (rejected == null || rejected.Count == 0) return;
            StringBuilder message = new StringBuilder("以下项目未加入：\r\n");
            int limit = Math.Min(12, rejected.Count);
            for (int index = 0; index < limit; index++) message.AppendLine("• " + rejected[index]);
            if (rejected.Count > limit) message.AppendLine("……另有 " + (rejected.Count - limit).ToString() + " 个项目");
            MessageBox.Show(this, message.ToString(), "PDF 导入提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024L) return (bytes / 1024d / 1024d).ToString("0.##") + " MB";
            if (bytes >= 1024L) return (bytes / 1024d).ToString("0.##") + " KB";
            return bytes.ToString() + " B";
        }

        private static Panel SectionPanel(int width, int height)
        {
            return new Panel { Width = width, Height = height, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 8) };
        }

        private static Label SectionTitle(string text, int top)
        {
            return new Label { Left = 18, Top = top, Width = 330, Height = 34, Text = text, Font = UiTheme.Font(13f, FontStyle.Bold), ForeColor = UiTheme.Text };
        }

        private static Label FieldLabel(string text, int top)
        {
            return new Label { Left = 18, Top = top, Width = 96, Height = 28, Text = text, ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft };
        }

        private static ComboBox MakeCombo(string[] values, int left, int top, int width)
        {
            ComboBox combo = new ComboBox { Left = left, Top = top, Width = width, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            combo.Items.AddRange(values);
            return combo;
        }

        private void EnableDropRecursive(Control control)
        {
            if (control == null) return;
            control.AllowDrop = true;
            if (control != this)
            {
                control.DragEnter += HandleDragEnter;
                control.DragDrop += HandleDragDrop;
            }
            foreach (Control child in control.Controls) EnableDropRecursive(child);
        }

        private void HandleDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void HandleDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) AddInputs(paths);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _inspectionCancellation.Cancel(); } catch { }
            if (_exportCancellation != null) try { _exportCancellation.Cancel(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
