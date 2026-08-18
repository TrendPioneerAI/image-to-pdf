using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LocalImageToPdf
{
    internal static class UiTheme
    {
        public static readonly Color Background = Color.FromArgb(247, 248, 250);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 230, 236);
        public static readonly Color Text = Color.FromArgb(22, 29, 42);
        public static readonly Color Muted = Color.FromArgb(113, 123, 141);
        public static readonly Color Primary = Color.FromArgb(20, 105, 245);
        public static readonly Color PrimarySoft = Color.FromArgb(236, 244, 255);
        public static readonly Color Danger = Color.FromArgb(190, 48, 48);

        public static Font Font(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static Button Button(string text, int width, int height)
        {
            Button button = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = Text,
                Font = Font(9.5f, FontStyle.Regular),
                Cursor = Cursors.Hand,
                Margin = new Padding(5)
            };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(248, 250, 253);
            return button;
        }

        public static void StyleSegment(Button button, bool selected)
        {
            button.BackColor = selected ? PrimarySoft : Surface;
            button.ForeColor = selected ? Primary : Text;
            button.FlatAppearance.BorderColor = selected ? Primary : Border;
            button.Font = Font(9.5f, selected ? FontStyle.Bold : FontStyle.Regular);
        }
    }

    internal sealed class DropHintPanel : Panel
    {
        public DropHintPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle rectangle = ClientRectangle;
            rectangle.Inflate(-1, -1);
            using (Pen pen = new Pen(Color.FromArgb(199, 207, 219), 1f))
            {
                pen.DashStyle = DashStyle.Dash;
                e.Graphics.DrawRectangle(pen, rectangle);
            }
        }
    }

    internal sealed class PreviewSurface : Control
    {
        private Bitmap _preview;
        private string _message;
        private bool _error;

        public PreviewSurface()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(248, 249, 251);
            Cursor = Cursors.Hand;
            _message = "正在生成预览…";
        }

        public void SetPreview(Bitmap preview, string error)
        {
            _preview = preview;
            _error = !String.IsNullOrWhiteSpace(error);
            _message = _error ? "无法读取图片" : (preview == null ? "正在生成预览…" : String.Empty);
            Invalidate();
        }

        public void ReleaseImage()
        {
            _preview = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (_preview != null)
            {
                Rectangle area = new Rectangle(12, 12, Math.Max(1, Width - 24), Math.Max(1, Height - 24));
                float scale = Math.Min((float)area.Width / _preview.Width, (float)area.Height / _preview.Height);
                int width = Math.Max(1, (int)Math.Round(_preview.Width * scale));
                int height = Math.Max(1, (int)Math.Round(_preview.Height * scale));
                Rectangle destination = new Rectangle(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
                e.Graphics.DrawImage(_preview, destination, 0, 0, _preview.Width, _preview.Height, GraphicsUnit.Pixel);
                using (Pen pen = new Pen(Color.FromArgb(218, 222, 229)))
                    e.Graphics.DrawRectangle(pen, destination.X, destination.Y, destination.Width - 1, destination.Height - 1);
                return;
            }

            using (Font font = UiTheme.Font(10f, FontStyle.Regular))
            using (Brush brush = new SolidBrush(_error ? UiTheme.Danger : UiTheme.Muted))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                e.Graphics.DrawString(_message, font, brush, ClientRectangle, format);
        }
    }

    internal sealed class ModernImageCard : Panel
    {
        private readonly IImageCardOwner _owner;
        private readonly ImageItem _item;
        private readonly PreviewSurface _preview;
        private readonly Label _fileName;
        private Point _dragStart;

        public ModernImageCard(IImageCardOwner owner, ImageItem item, int cardWidth, int previewHeight)
        {
            _owner = owner;
            _item = item;
            Width = cardWidth;
            Height = previewHeight + 185;
            Margin = new Padding(10);
            BackColor = UiTheme.Surface;
            BorderStyle = BorderStyle.FixedSingle;

            _preview = new PreviewSurface
            {
                Dock = DockStyle.Top,
                Height = previewHeight
            };
            _preview.SetPreview(item.Preview, item.PreviewError);
            _preview.Click += delegate { _owner.ShowPreview(_item); };

            _fileName = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Text = item.FileName,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 12, 0),
                ForeColor = UiTheme.Text,
                Font = UiTheme.Font(9.5f, FontStyle.Regular)
            };

            Panel naming = new Panel { Dock = DockStyle.Top, Height = 68, Padding = new Padding(12, 0, 12, 8) };
            Label nameLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "PDF 文件名",
                ForeColor = UiTheme.Muted,
                Font = UiTheme.Font(8.5f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };
            TextBox outputName = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = EnsurePdfDisplayName(item.OutputName),
                BorderStyle = BorderStyle.FixedSingle,
                Font = UiTheme.Font(9.5f, FontStyle.Regular),
                AccessibleName = "PDF 输出文件名"
            };
            outputName.TextChanged += delegate
            {
                string value = outputName.Text.Trim();
                if (value.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    value = value.Substring(0, value.Length - 4);
                _item.OutputName = value;
            };
            naming.Controls.Add(outputName);
            naming.Controls.Add(nameLabel);

            TableLayoutPanel actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(7, 4, 7, 8)
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            Button left = UiTheme.Button("↶  左旋转", 92, 38);
            Button right = UiTheme.Button("↷  右旋转", 92, 38);
            Button remove = UiTheme.Button("删除", 92, 38);
            left.Dock = right.Dock = remove.Dock = DockStyle.Fill;
            left.Click += delegate { _owner.RotateItem(_item, -90); };
            right.Click += delegate { _owner.RotateItem(_item, 90); };
            remove.Click += delegate { _owner.RemoveItem(_item); };
            actions.Controls.Add(left, 0, 0);
            actions.Controls.Add(right, 1, 0);
            actions.Controls.Add(remove, 2, 0);

            Controls.Add(actions);
            Controls.Add(naming);
            Controls.Add(_fileName);
            Controls.Add(_preview);
            AttachDrag(this);
            AttachDrag(_fileName);
        }

        public ImageItem Item { get { return _item; } }

        public void SetPreview(Bitmap preview, string error)
        {
            _fileName.ForeColor = String.IsNullOrWhiteSpace(error) ? UiTheme.Text : UiTheme.Danger;
            _fileName.Text = String.IsNullOrWhiteSpace(error) ? _item.FileName : _item.FileName + "（无法读取）";
            _preview.SetPreview(preview, error);
        }

        public void ReleasePreviewReference()
        {
            _preview.ReleaseImage();
        }

        private void AttachDrag(Control control)
        {
            control.MouseDown += delegate (object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left) _dragStart = args.Location;
            };
            control.MouseMove += delegate (object sender, MouseEventArgs args)
            {
                if ((args.Button & MouseButtons.Left) == 0) return;
                if (Math.Abs(args.X - _dragStart.X) < SystemInformation.DragSize.Width / 2 &&
                    Math.Abs(args.Y - _dragStart.Y) < SystemInformation.DragSize.Height / 2) return;
                DoDragDrop(_item, DragDropEffects.Move);
            };
        }

        private static string EnsurePdfDisplayName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            return value.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? value : value + ".pdf";
        }
    }

    internal sealed class WatermarkDialog : Form
    {
        private readonly TextBox _text;
        private readonly NumericUpDown _opacity;
        private readonly ComboBox _angle;
        private readonly ComboBox _layout;

        public WatermarkDialog(WatermarkOptions current, Icon icon)
        {
            Text = "自定义文字水印";
            Icon = icon;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(480, 330);
            BackColor = UiTheme.Surface;
            Font = UiTheme.Font(9.5f, FontStyle.Regular);

            Label title = new Label { Left = 28, Top = 24, Width = 420, Height = 30, Text = "自定义文字水印", Font = UiTheme.Font(14f, FontStyle.Bold), ForeColor = UiTheme.Text };
            Label helper = new Label { Left = 28, Top = 55, Width = 420, Height = 38, Text = "水印会显示在缩略图、大图预览和最终 PDF 中。", ForeColor = UiTheme.Muted };
            AddFieldLabel("文字（1～64 个字符）", 28, 96);
            _text = new TextBox { Left = 180, Top = 92, Width = 260, MaxLength = 64, Text = current == null ? String.Empty : current.Text };
            AddFieldLabel("透明度", 28, 139);
            _opacity = new NumericUpDown { Left = 180, Top = 135, Width = 110, Minimum = 5, Maximum = 60, Increment = 1, Value = current == null ? 18 : Math.Max(5, Math.Min(60, current.OpacityPercent)) };
            Label percent = new Label { Left = 298, Top = 139, AutoSize = true, Text = "%", ForeColor = UiTheme.Muted };
            AddFieldLabel("倾斜角度", 28, 182);
            _angle = MakeCombo(180, 178, 130, new object[] { "-45°", "0°", "45°" });
            int angle = current == null ? 45 : current.AngleDegrees;
            _angle.SelectedIndex = angle < 0 ? 0 : (angle == 0 ? 1 : 2);
            AddFieldLabel("布局", 28, 225);
            _layout = MakeCombo(180, 221, 180, new object[] { "居中", "全页平铺", "右下角" });
            WatermarkLayout layout = current == null ? WatermarkLayout.Tile : current.Layout;
            _layout.SelectedIndex = layout == WatermarkLayout.Center ? 0 : (layout == WatermarkLayout.Tile ? 1 : 2);

            Button cancel = UiTheme.Button("取消", 94, 38);
            cancel.Left = 244;
            cancel.Top = 274;
            cancel.DialogResult = DialogResult.Cancel;
            Button confirm = UiTheme.Button("保存", 94, 38);
            confirm.Left = 346;
            confirm.Top = 274;
            confirm.BackColor = UiTheme.Primary;
            confirm.ForeColor = Color.White;
            confirm.FlatAppearance.BorderColor = UiTheme.Primary;
            confirm.Click += ConfirmClicked;

            Controls.Add(title);
            Controls.Add(helper);
            Controls.Add(_text);
            Controls.Add(_opacity);
            Controls.Add(percent);
            Controls.Add(_angle);
            Controls.Add(_layout);
            Controls.Add(cancel);
            Controls.Add(confirm);
            AcceptButton = confirm;
            CancelButton = cancel;
        }

        public WatermarkOptions Result { get; private set; }

        private void ConfirmClicked(object sender, EventArgs e)
        {
            string text = _text.Text.Trim();
            if (text.Length < 1)
            {
                MessageBox.Show(this, "请输入 1～64 个字符的水印文字。", "水印文字为空", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _text.Focus();
                return;
            }
            Result = new WatermarkOptions
            {
                Mode = WatermarkMode.Custom,
                Text = text,
                OpacityPercent = (int)_opacity.Value,
                AngleDegrees = _angle.SelectedIndex == 0 ? -45 : (_angle.SelectedIndex == 1 ? 0 : 45),
                Layout = _layout.SelectedIndex == 0 ? WatermarkLayout.Center : (_layout.SelectedIndex == 1 ? WatermarkLayout.Tile : WatermarkLayout.BottomRight)
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void AddFieldLabel(string text, int left, int top)
        {
            Controls.Add(new Label { Left = left, Top = top, Width = 145, Height = 25, Text = text, ForeColor = UiTheme.Text });
        }

        private static ComboBox MakeCombo(int left, int top, int width, object[] items)
        {
            ComboBox combo = new ComboBox { Left = left, Top = top, Width = width, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            combo.Items.AddRange(items);
            return combo;
        }
    }

    internal sealed class SendToOnboardingForm : Form
    {
        private bool _decisionSaved;

        public SendToOnboardingForm(Icon icon)
        {
            Text = "开启右键快速转换";
            Icon = icon;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(640, 440);
            BackColor = UiTheme.Surface;
            Font = UiTheme.Font(9.5f, FontStyle.Regular);

            Label title = new Label
            {
                Left = 30,
                Top = 24,
                Width = 560,
                Height = 38,
                Text = "开启右键快速转换",
                Font = UiTheme.Font(16f, FontStyle.Bold),
                ForeColor = UiTheme.Text
            };
            Label helper = new Label
            {
                Left = 30,
                Top = 66,
                Width = 560,
                Height = 30,
                Text = "只需设置一次，以后选中文件后右键发送即可。",
                ForeColor = UiTheme.Muted,
                Font = UiTheme.Font(10f, FontStyle.Regular)
            };
            Panel routes = new Panel
            {
                Left = 30,
                Top = 108,
                Width = 580,
                Height = 132,
                BackColor = UiTheme.PrimarySoft,
                BorderStyle = BorderStyle.FixedSingle
            };
            routes.Controls.Add(new Label
            {
                Left = 24,
                Top = 22,
                Width = 520,
                Height = 34,
                Text = "图片发送  →  图片转 PDF",
                ForeColor = UiTheme.Text,
                Font = UiTheme.Font(12f, FontStyle.Bold)
            });
            routes.Controls.Add(new Label
            {
                Left = 24,
                Top = 72,
                Width = 520,
                Height = 34,
                Text = "PDF 发送  →  PDF 转图片",
                ForeColor = UiTheme.Text,
                Font = UiTheme.Font(12f, FontStyle.Bold)
            });
            Label privacy = new Label
            {
                Left = 30,
                Top = 260,
                Width = 580,
                Height = 28,
                Text = "仅添加当前用户的“发送到”快捷入口 · 不联网 · 不改变文件关联",
                ForeColor = UiTheme.Muted
            };
            Label removal = new Label
            {
                Left = 30,
                Top = 298,
                Width = 580,
                Height = 48,
                Text = "以后需要清除：主界面右上角齿轮 → 设置与关于 → 移除右键入口。",
                ForeColor = UiTheme.Text
            };
            Button defer = UiTheme.Button("暂不设置", 130, 44);
            defer.Left = 300;
            defer.Top = 365;
            defer.Click += delegate { CompleteAndClose(); };
            Button enable = UiTheme.Button("一键开启", 170, 44);
            enable.Left = 440;
            enable.Top = 365;
            enable.BackColor = UiTheme.Primary;
            enable.ForeColor = Color.White;
            enable.FlatAppearance.BorderColor = UiTheme.Primary;
            enable.Font = UiTheme.Font(10.5f, FontStyle.Bold);
            enable.Click += EnableClicked;

            Controls.Add(title);
            Controls.Add(helper);
            Controls.Add(routes);
            Controls.Add(privacy);
            Controls.Add(removal);
            Controls.Add(defer);
            Controls.Add(enable);
            AcceptButton = enable;
            CancelButton = defer;
        }

        private void EnableClicked(object sender, EventArgs e)
        {
            try
            {
                SendToManager.Add();
                CompleteAndClose();
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "开启失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CompleteAndClose()
        {
            SaveDecision();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SaveDecision()
        {
            if (_decisionSaved) return;
            try { AppSettingsStore.MarkSendToOnboardingCompleted(); }
            catch { }
            _decisionSaved = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveDecision();
            base.OnFormClosing(e);
        }
    }

    internal sealed class SettingsForm : Form
    {
        private const string ProjectUrl = "https://github.com/TrendPioneerAI/image-to-pdf";
        private readonly Label _sendToState;

        public SettingsForm(Icon icon)
        {
            Text = "设置与关于";
            Icon = icon;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 490);
            BackColor = UiTheme.Surface;
            Font = UiTheme.Font(9.5f, FontStyle.Regular);

            Label title = new Label { Left = 30, Top = 24, Width = 470, Height = 32, Text = "设置", Font = UiTheme.Font(15f, FontStyle.Bold), ForeColor = UiTheme.Text };
            Label sendTitle = new Label { Left = 30, Top = 79, Width = 470, Height = 28, Text = "当前用户“发送到”右键入口", Font = UiTheme.Font(11f, FontStyle.Bold), ForeColor = UiTheme.Text };
            Label sendHelper = new Label { Left = 30, Top = 110, Width = 470, Height = 42, Text = "首次启动引导可一键开启；这里始终保留添加和移除入口。", ForeColor = UiTheme.Muted };
            _sendToState = new Label { Left = 30, Top = 156, Width = 470, Height = 25, ForeColor = UiTheme.Muted };
            Button add = UiTheme.Button("添加到“发送到”", 160, 40);
            add.Left = 30;
            add.Top = 189;
            Button remove = UiTheme.Button("移除右键入口", 150, 40);
            remove.Left = 200;
            remove.Top = 189;
            add.Click += delegate { ChangeSendTo(true); };
            remove.Click += delegate { ChangeSendTo(false); };

            Panel divider = new Panel { Left = 30, Top = 255, Width = 480, Height = 1, BackColor = UiTheme.Border };
            Label aboutTitle = new Label { Left = 30, Top = 277, Width = 470, Height = 28, Text = "关于", Font = UiTheme.Font(11f, FontStyle.Bold), ForeColor = UiTheme.Text };
            Label about = new Label
            {
                Left = 30,
                Top = 311,
                Width = 500,
                Height = 72,
                Text = "图片与PDF转换  v1.2.0\r\n由 ZenthZhang 开发\r\nMIT License · 免费开源",
                ForeColor = UiTheme.Muted
            };
            LinkLabel projectLink = new LinkLabel
            {
                Left = 30,
                Top = 386,
                Width = 500,
                Height = 26,
                Text = "GitHub：TrendPioneerAI/image-to-pdf",
                LinkColor = UiTheme.Primary,
                ActiveLinkColor = UiTheme.Primary,
                VisitedLinkColor = UiTheme.Primary,
                TabStop = true
            };
            projectLink.LinkClicked += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, ProjectUrl + "\r\n\r\n" + error.Message, "无法打开 GitHub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            Button close = UiTheme.Button("关闭", 96, 38);
            close.Left = 434;
            close.Top = 438;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            close.DialogResult = DialogResult.OK;

            Controls.Add(title);
            Controls.Add(sendTitle);
            Controls.Add(sendHelper);
            Controls.Add(_sendToState);
            Controls.Add(add);
            Controls.Add(remove);
            Controls.Add(divider);
            Controls.Add(aboutTitle);
            Controls.Add(about);
            Controls.Add(projectLink);
            Controls.Add(close);
            AcceptButton = close;
            UpdateSendToState();
        }

        private void ChangeSendTo(bool add)
        {
            try
            {
                if (add) SendToManager.Add(); else SendToManager.Remove();
                try { AppSettingsStore.MarkSendToOnboardingCompleted(); }
                catch { }
                UpdateSendToState();
                MessageBox.Show(this, add ? "已添加到当前用户的“发送到”菜单。" : "已移除右键入口。", "右键入口", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, add ? "添加失败" : "移除失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateSendToState()
        {
            _sendToState.Text = SendToManager.Exists() ? "当前状态：已添加" : "当前状态：未添加";
        }
    }

    internal sealed class PathNaturalComparer : IComparer<string>
    {
        private static readonly Regex NumberPattern = new Regex("(\\d+)", RegexOptions.Compiled);

        public int Compare(string left, string right)
        {
            return CompareText(Path.GetFileName(left ?? String.Empty), Path.GetFileName(right ?? String.Empty));
        }

        private static int CompareText(string left, string right)
        {
            MatchCollection a = NumberPattern.Matches(left);
            MatchCollection b = NumberPattern.Matches(right);
            int positionA = 0;
            int positionB = 0;
            int count = Math.Min(a.Count, b.Count);
            for (int index = 0; index < count; index++)
            {
                int text = StringComparer.CurrentCultureIgnoreCase.Compare(left.Substring(positionA, a[index].Index - positionA), right.Substring(positionB, b[index].Index - positionB));
                if (text != 0) return text;
                long numberA;
                long numberB;
                if (Int64.TryParse(a[index].Value, out numberA) && Int64.TryParse(b[index].Value, out numberB) && numberA != numberB)
                    return numberA < numberB ? -1 : 1;
                positionA = a[index].Index + a[index].Length;
                positionB = b[index].Index + b[index].Length;
            }
            return StringComparer.CurrentCultureIgnoreCase.Compare(left.Substring(positionA), right.Substring(positionB));
        }
    }

    internal sealed class MainForm : Form, IImageCardOwner
    {
        private readonly string[] _startupArgs;
        private readonly List<ImageItem> _items = new List<ImageItem>();
        private readonly HashSet<string> _paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ImageItem, ModernImageCard> _cardMap = new Dictionary<ImageItem, ModernImageCard>();
        private readonly AppSettings _settings;
        private FlowLayoutPanel _cards;
        private Label _countLabel;
        private Label _statusLabel;
        private ComboBox _paperCombo;
        private Button _portraitButton;
        private Button _landscapeButton;
        private CheckBox _autoRotateCheck;
        private ComboBox _marginCombo;
        private ComboBox _watermarkCombo;
        private Label _watermarkSummary;
        private Button _editWatermarkButton;
        private ComboBox _qualityCombo;
        private Label _qualityHelper;
        private Button _targetFileButton;
        private Button _targetFolderButton;
        private TextBox _outputPathBox;
        private Button _mergeButton;
        private Button _separateButton;
        private Label _mergeNameLabel;
        private TextBox _mergeNameBox;
        private Label _batchNameLabel;
        private TextBox _batchNameBox;
        private Button _applyBatchButton;
        private Button _exportButton;
        private Button _cancelButton;
        private PageOrientation _orientation = PageOrientation.Portrait;
        private ExportMode _exportMode = ExportMode.Merge;
        private OutputTargetMode _targetMode;
        private WatermarkMode _watermarkMode = WatermarkMode.None;
        private WatermarkOptions _customWatermark = new WatermarkOptions
        {
            Mode = WatermarkMode.Custom,
            Text = String.Empty,
            OpacityPercent = 18,
            AngleDegrees = 45,
            Layout = WatermarkLayout.Tile
        };
        private CancellationTokenSource _previewCancellation;
        private CancellationTokenSource _exportCancellation;
        private Control _imageToPdfView;
        private PdfToImageForm _pdfConverter;
        private int _previewGeneration;
        private bool _buildingUi;

        public MainForm(string[] startupArgs)
        {
            _startupArgs = startupArgs ?? new string[0];
            _settings = AppSettingsStore.Load();
            _targetMode = _settings.LastTargetMode;
            Text = "图片转PDF";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1440, 900);
            MinimumSize = new Size(1080, 700);
            BackColor = UiTheme.Background;
            Font = UiTheme.Font(9.5f, FontStyle.Regular);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            AllowDrop = true;
            BuildUi();
            PreparePdfConverter();
            DragEnter += HandleDragEnter;
            DragDrop += HandleDragDrop;
            Shown += delegate
            {
                HandleStartupInputs();
                if (_imageToPdfView.Visible && _pdfConverter != null) _pdfConverter.Hide();
            };
        }

        public void RotateItem(ImageItem item, int delta)
        {
            if (item == null || _exportCancellation != null) return;
            item.ManualRotation = ImageTools.NormalizeRotation(item.ManualRotation + delta);
            RebuildCardsAndQueuePreviews(false);
        }

        public void ShowPreview(ImageItem item)
        {
            if (item == null) return;
            Bitmap display = null;
            try
            {
                int width;
                int height;
                GetPagePixels(GetPaperSize(), _orientation, 1500, out width, out height);
                display = ImageTools.RenderPagePreview(item, GetPaperSize(), _orientation, _autoRotateCheck.Checked, GetMarginMm(), width, height);
                WatermarkRenderer.DrawPreview(display, GetWatermarkOptions());
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
            if (item == null || _exportCancellation != null) return;
            _items.Remove(item);
            _paths.Remove(item.Path);
            item.DisposePreview();
            RebuildCardsAndQueuePreviews(false);
        }

        private void BuildUi()
        {
            _buildingUi = true;
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                BackColor = UiTheme.Background,
                Padding = new Padding(16, 10, 16, 12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));
            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildContent(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
            _imageToPdfView = root;
            Controls.Add(_imageToPdfView);
            EnableDropRecursive(root);
            _buildingUi = false;
            UpdatePageButtons();
            UpdateWatermarkUi();
            UpdateQualityHelper();
            UpdateOutputUi();
            UpdateCount();
        }

        private Control BuildHeader()
        {
            Panel header = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
            _countLabel = new Label
            {
                Left = 10,
                Top = 22,
                Width = 125,
                Height = 36,
                Text = "共 0 页",
                Font = UiTheme.Font(16f, FontStyle.Bold),
                ForeColor = UiTheme.Text
            };

            DropHintPanel dropHint = new DropHintPanel { Left = 135, Top = 7, Width = 350, Height = 60, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            Label dropTitle = new Label { Left = 22, Top = 9, Width = 305, Height = 23, Text = "拖入文件或文件夹", TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.Text, Font = UiTheme.Font(10f, FontStyle.Regular) };
            Label dropSub = new Label { Left = 18, Top = 32, Width = 315, Height = 20, Text = "支持图片文件、文件夹，或直接拖入", TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.Muted, Font = UiTheme.Font(8.7f, FontStyle.Regular) };
            dropHint.Controls.Add(dropTitle);
            dropHint.Controls.Add(dropSub);
            dropHint.Click += delegate { ChooseFiles(); };
            dropTitle.Click += delegate { ChooseFiles(); };
            dropSub.Click += delegate { ChooseFiles(); };

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Width = 650,
                Height = 56,
                Top = 10,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Button pdfToImages = UiTheme.Button("PDF 转图片", 148, 44);
            Button sort = UiTheme.Button("⇅  排序  ▾", 118, 44);
            Button clear = UiTheme.Button("清空", 92, 44);
            Button add = UiTheme.Button("＋  添加图片  ▾", 142, 44);
            Button settings = UiTheme.Button("⚙", 52, 44);
            settings.Font = UiTheme.Font(15f, FontStyle.Regular);
            add.BackColor = UiTheme.PrimarySoft;
            add.ForeColor = UiTheme.Primary;
            add.FlatAppearance.BorderColor = UiTheme.Primary;
            pdfToImages.Click += delegate { OpenPdfConverter(new string[0]); };

            ContextMenuStrip sortMenu = new ContextMenuStrip();
            AddSortItem(sortMenu, "按名称升序", SortMode.NameAscending);
            AddSortItem(sortMenu, "按名称降序", SortMode.NameDescending);
            sortMenu.Items.Add(new ToolStripSeparator());
            AddSortItem(sortMenu, "按文件大小：大到小", SortMode.SizeDescending);
            AddSortItem(sortMenu, "按文件大小：小到大", SortMode.SizeAscending);
            AddSortItem(sortMenu, "按修改时间：最近优先", SortMode.ModifiedDescending);
            AddSortItem(sortMenu, "按修改时间：最早优先", SortMode.ModifiedAscending);
            AddSortItem(sortMenu, "按添加顺序：最近优先", SortMode.AddedDescending);
            AddSortItem(sortMenu, "按添加顺序：最早优先", SortMode.AddedAscending);
            sort.ContextMenuStrip = sortMenu;
            sort.Click += delegate { sortMenu.Show(sort, new Point(0, sort.Height)); };

            ContextMenuStrip addMenu = new ContextMenuStrip();
            ToolStripMenuItem addFiles = new ToolStripMenuItem("添加文件");
            ToolStripMenuItem addFolder = new ToolStripMenuItem("添加文件夹");
            addFiles.Click += delegate { ChooseFiles(); };
            addFolder.Click += delegate { ChooseFolder(); };
            addMenu.Items.Add(addFiles);
            addMenu.Items.Add(addFolder);
            add.ContextMenuStrip = addMenu;
            add.Click += delegate { addMenu.Show(add, new Point(0, add.Height)); };

            clear.Click += delegate
            {
                if (_items.Count == 0) return;
                if (MessageBox.Show(this, "确定清空已添加的全部图片吗？", "清空图片", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    ClearItems();
            };
            settings.Click += delegate { using (SettingsForm dialog = new SettingsForm(Icon)) dialog.ShowDialog(this); };
            actions.Controls.Add(pdfToImages);
            actions.Controls.Add(sort);
            actions.Controls.Add(clear);
            actions.Controls.Add(add);
            actions.Controls.Add(settings);
            header.Controls.Add(_countLabel);
            header.Controls.Add(dropHint);
            header.Controls.Add(actions);
            header.Resize += delegate
            {
                actions.Left = Math.Max(135, header.ClientSize.Width - actions.Width);
                dropHint.Visible = actions.Left - dropHint.Right > 15;
            };
            return header;
        }

        private Control BuildContent()
        {
            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460f));
            _cards = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.Background,
                Padding = new Padding(0, 0, 8, 12),
                AllowDrop = true
            };
            _cards.DragEnter += HandleDragEnter;
            _cards.DragOver += HandleDragOver;
            _cards.DragDrop += CardsDragDrop;
            content.Controls.Add(_cards, 0, 0);
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
            list.HorizontalScroll.Enabled = false;
            list.Controls.Add(BuildPageSection());
            list.Controls.Add(BuildWatermarkSection());
            list.Controls.Add(BuildOutputSection());
            shell.Controls.Add(list);
            return shell;
        }

        private Control BuildPageSection()
        {
            Panel panel = SectionPanel(400, 260);
            panel.Controls.Add(SectionTitle("页面", 12));
            panel.Controls.Add(FieldLabel("纸张大小", 58));
            _paperCombo = MakeCombo(PaperSizes.DisplayNames, 136, 52, 250);
            _paperCombo.SelectedIndex = 0;
            _paperCombo.SelectedIndexChanged += PreviewSettingsChanged;
            panel.Controls.Add(_paperCombo);
            panel.Controls.Add(FieldLabel("纸张方向", 108));
            _portraitButton = UiTheme.Button("▯  竖向", 120, 38);
            _portraitButton.Left = 136;
            _portraitButton.Top = 96;
            _landscapeButton = UiTheme.Button("▭  横向", 120, 38);
            _landscapeButton.Left = 266;
            _landscapeButton.Top = 96;
            _portraitButton.Click += delegate { SetOrientation(PageOrientation.Portrait); };
            _landscapeButton.Click += delegate { SetOrientation(PageOrientation.Landscape); };
            panel.Controls.Add(_portraitButton);
            panel.Controls.Add(_landscapeButton);
            _autoRotateCheck = new CheckBox { Left = 20, Top = 151, Width = 366, Height = 28, Text = "横图自动转正（顺时针 90°）", Checked = true, ForeColor = UiTheme.Text };
            _autoRotateCheck.CheckedChanged += PreviewSettingsChanged;
            panel.Controls.Add(_autoRotateCheck);
            panel.Controls.Add(FieldLabel("页面边距", 205));
            _marginCombo = MakeCombo(new[] { "无边距（0 mm）", "窄边距（5 mm）", "标准边距（10 mm）" }, 136, 199, 250);
            _marginCombo.SelectedIndex = 2;
            _marginCombo.SelectedIndexChanged += PreviewSettingsChanged;
            panel.Controls.Add(_marginCombo);
            return panel;
        }

        private Control BuildWatermarkSection()
        {
            Panel panel = SectionPanel(400, 182);
            panel.Controls.Add(SectionTitle("水印", 12));
            panel.Controls.Add(FieldLabel("水印设置", 59));
            _watermarkCombo = MakeCombo(new[] { "无水印", "自定义", "默认水印" }, 136, 53, 250);
            _watermarkCombo.SelectedIndex = 0;
            _watermarkCombo.SelectedIndexChanged += WatermarkModeChanged;
            panel.Controls.Add(_watermarkCombo);
            _watermarkSummary = new Label { Left = 20, Top = 101, Width = 366, Height = 42, ForeColor = UiTheme.Muted, Font = UiTheme.Font(8.7f, FontStyle.Regular) };
            panel.Controls.Add(_watermarkSummary);
            _editWatermarkButton = UiTheme.Button("编辑自定义水印", 142, 34);
            _editWatermarkButton.Left = 244;
            _editWatermarkButton.Top = 137;
            _editWatermarkButton.Click += delegate { EditCustomWatermark(false); };
            panel.Controls.Add(_editWatermarkButton);
            return panel;
        }

        private Control BuildOutputSection()
        {
            Panel panel = SectionPanel(400, 500);
            panel.Controls.Add(SectionTitle("输出", 12));
            panel.Controls.Add(FieldLabel("输出质量", 58));
            _qualityCombo = MakeCombo(new[]
            {
                "推荐/快速 · 智能处理",
                "标准（220 DPI）",
                "精细打印（300 DPI）",
                "无损（高级）"
            }, 136, 52, 250);
            _qualityCombo.SelectedIndex = 0;
            _qualityCombo.SelectedIndexChanged += delegate { UpdateQualityHelper(); };
            panel.Controls.Add(_qualityCombo);
            _qualityHelper = new Label { Left = 136, Top = 86, Width = 250, Height = 36, ForeColor = UiTheme.Muted, Font = UiTheme.Font(8.2f, FontStyle.Regular) };
            panel.Controls.Add(_qualityHelper);

            panel.Controls.Add(FieldLabel("输出到", 132));
            _targetFileButton = UiTheme.Button("文件", 120, 38);
            _targetFileButton.Left = 136;
            _targetFileButton.Top = 120;
            _targetFolderButton = UiTheme.Button("文件夹", 120, 38);
            _targetFolderButton.Left = 266;
            _targetFolderButton.Top = 120;
            _targetFileButton.Click += delegate { SetTargetMode(OutputTargetMode.File); };
            _targetFolderButton.Click += delegate { SetTargetMode(OutputTargetMode.Folder); };
            panel.Controls.Add(_targetFileButton);
            panel.Controls.Add(_targetFolderButton);
            _outputPathBox = new TextBox { Left = 20, Top = 170, Width = 292, Height = 29, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.Font(9f, FontStyle.Regular) };
            Button browse = UiTheme.Button("浏览", 70, 32);
            browse.Left = 316;
            browse.Top = 167;
            browse.Click += delegate { BrowseOutput(); };
            panel.Controls.Add(_outputPathBox);
            panel.Controls.Add(browse);

            panel.Controls.Add(FieldLabel("导出方式", 226));
            _mergeButton = UiTheme.Button("合并为一个 PDF", 122, 38);
            _mergeButton.Left = 136;
            _mergeButton.Top = 214;
            _separateButton = UiTheme.Button("一图一个 PDF", 122, 38);
            _separateButton.Left = 264;
            _separateButton.Top = 214;
            _mergeButton.Click += delegate { SetExportMode(ExportMode.Merge); };
            _separateButton.Click += delegate { SetExportMode(ExportMode.Separate); };
            panel.Controls.Add(_mergeButton);
            panel.Controls.Add(_separateButton);

            _mergeNameLabel = FieldLabel("合并文件名", 281);
            _mergeNameBox = new TextBox { Left = 136, Top = 275, Width = 250, Height = 28, Text = DefaultMergeName(), BorderStyle = BorderStyle.FixedSingle };
            _batchNameLabel = FieldLabel("批量命名前缀", 281);
            _batchNameBox = new TextBox { Left = 136, Top = 275, Width = 160, Height = 28, Text = "图片", BorderStyle = BorderStyle.FixedSingle };
            _applyBatchButton = UiTheme.Button("应用", 84, 32);
            _applyBatchButton.Left = 302;
            _applyBatchButton.Top = 272;
            _applyBatchButton.Click += ApplyBatchNamesClicked;
            panel.Controls.Add(_mergeNameLabel);
            panel.Controls.Add(_mergeNameBox);
            panel.Controls.Add(_batchNameLabel);
            panel.Controls.Add(_batchNameBox);
            panel.Controls.Add(_applyBatchButton);

            Label outputNote = new Label
            {
                Left = 20,
                Top = 326,
                Width = 366,
                Height = 84,
                Text = "自动生成的文件不会覆盖已有文件；同名时会追加“(2)”“(3)”。\r\n\r\n一图一个 PDF 可在每张卡片中分别修改文件名。",
                ForeColor = UiTheme.Muted,
                Font = UiTheme.Font(8.5f, FontStyle.Regular)
            };
            panel.Controls.Add(outputNote);
            return panel;
        }

        private Control BuildFooter()
        {
            Panel footer = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, Margin = new Padding(0, 10, 0, 0), BorderStyle = BorderStyle.FixedSingle };
            Label privacy = new Label
            {
                Left = 20,
                Top = 21,
                Width = 520,
                Height = 28,
                Text = "▣  本地处理 · 不上传文件 · 免费开源 · 由 ZenthZhang 开发",
                ForeColor = UiTheme.Muted,
                Font = UiTheme.Font(9f, FontStyle.Regular)
            };
            _statusLabel = new Label { Top = 21, Width = 300, Height = 28, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.Muted };
            _cancelButton = UiTheme.Button("取消", 90, 44);
            _cancelButton.Top = 10;
            _cancelButton.Visible = false;
            _cancelButton.Click += delegate { if (_exportCancellation != null) _exportCancellation.Cancel(); };
            _exportButton = UiTheme.Button("导出 PDF", 330, 46);
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

        private static Panel SectionPanel(int width, int height)
        {
            return new Panel { Width = width, Height = height, BackColor = UiTheme.Surface, Margin = new Padding(0, 0, 0, 8) };
        }

        private static Label SectionTitle(string text, int top)
        {
            return new Label { Left = 20, Top = top, Width = 300, Height = 32, Text = text, ForeColor = UiTheme.Text, Font = UiTheme.Font(12f, FontStyle.Bold) };
        }

        private static Label FieldLabel(string text, int top)
        {
            return new Label { Left = 20, Top = top, Width = 104, Height = 30, Text = text, ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft };
        }

        private static ComboBox MakeCombo(string[] values, int left, int top, int width)
        {
            ComboBox combo = new ComboBox { Left = left, Top = top, Width = width, Height = 30, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            combo.Items.AddRange(values);
            return combo;
        }

        private void AddSortItem(ContextMenuStrip menu, string text, SortMode mode)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += delegate { SortItems(mode); };
            menu.Items.Add(item);
        }

        private void SetOrientation(PageOrientation orientation)
        {
            if (_orientation == orientation) return;
            _orientation = orientation;
            UpdatePageButtons();
            if (!_buildingUi) RebuildCardsAndQueuePreviews(false);
        }

        private void UpdatePageButtons()
        {
            if (_portraitButton == null) return;
            UiTheme.StyleSegment(_portraitButton, _orientation == PageOrientation.Portrait);
            UiTheme.StyleSegment(_landscapeButton, _orientation == PageOrientation.Landscape);
        }

        private void PreviewSettingsChanged(object sender, EventArgs e)
        {
            if (!_buildingUi) RebuildCardsAndQueuePreviews(false);
        }

        private void WatermarkModeChanged(object sender, EventArgs e)
        {
            if (_buildingUi) return;
            WatermarkMode requested = _watermarkCombo.SelectedIndex == 1 ? WatermarkMode.Custom : (_watermarkCombo.SelectedIndex == 2 ? WatermarkMode.Default : WatermarkMode.None);
            if (requested == WatermarkMode.Custom && String.IsNullOrWhiteSpace(_customWatermark.Text))
            {
                if (!EditCustomWatermark(true))
                {
                    _buildingUi = true;
                    _watermarkCombo.SelectedIndex = _watermarkMode == WatermarkMode.Default ? 2 : 0;
                    _buildingUi = false;
                    return;
                }
            }
            _watermarkMode = requested;
            UpdateWatermarkUi();
            RebuildCardsAndQueuePreviews(false);
        }

        private bool EditCustomWatermark(bool selecting)
        {
            using (WatermarkDialog dialog = new WatermarkDialog(_customWatermark, Icon))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                _customWatermark = dialog.Result;
            }
            _watermarkMode = WatermarkMode.Custom;
            _buildingUi = true;
            _watermarkCombo.SelectedIndex = 1;
            _buildingUi = false;
            UpdateWatermarkUi();
            if (!selecting) RebuildCardsAndQueuePreviews(false);
            return true;
        }

        private void UpdateWatermarkUi()
        {
            if (_watermarkSummary == null) return;
            if (_watermarkMode == WatermarkMode.Default)
                _watermarkSummary.Text = "默认水印：仅供参考 · 18% · 45° · 全页平铺";
            else if (_watermarkMode == WatermarkMode.Custom)
                _watermarkSummary.Text = String.IsNullOrWhiteSpace(_customWatermark.Text) ? "尚未设置自定义文字" : "自定义：" + _customWatermark.Text + " · " + _customWatermark.OpacityPercent.ToString() + "% · " + _customWatermark.AngleDegrees.ToString() + "°";
            else
                _watermarkSummary.Text = "不添加任何覆盖内容";
            _editWatermarkButton.Visible = _watermarkMode == WatermarkMode.Custom;
        }

        private WatermarkOptions GetWatermarkOptions()
        {
            if (_watermarkMode == WatermarkMode.Default) return WatermarkOptions.DefaultPreset();
            if (_watermarkMode == WatermarkMode.Custom) return _customWatermark.Clone();
            return WatermarkOptions.None();
        }

        private void UpdateQualityHelper()
        {
            if (_qualityHelper == null) return;
            switch (_qualityCombo.SelectedIndex)
            {
                case 1: _qualityHelper.Text = "全部图片按需处理为 220 DPI · JPEG 86"; break;
                case 2: _qualityHelper.Text = "清晰打印 · 300 DPI · JPEG 92"; break;
                case 3: _qualityHelper.Text = "JPEG 原图直嵌；PNG/BMP 原始分辨率无损"; break;
                default: _qualityHelper.Text = "JPEG原图直嵌；PNG/BMP 150 DPI"; break;
            }
        }

        private void SetTargetMode(OutputTargetMode mode)
        {
            if (_exportMode == ExportMode.Separate && mode == OutputTargetMode.File) return;
            string current = _outputPathBox.Text.Trim();
            if (mode == OutputTargetMode.Folder && _targetMode == OutputTargetMode.File)
            {
                string directory = SafeDirectoryName(current);
                if (!String.IsNullOrWhiteSpace(directory)) _outputPathBox.Text = directory;
            }
            else if (mode == OutputTargetMode.File && _targetMode == OutputTargetMode.Folder)
            {
                string directory = String.IsNullOrWhiteSpace(current) ? _settings.LastOutputDirectory : current;
                _outputPathBox.Text = Path.Combine(directory, EnsurePdfExtension(GetMergeBaseName()));
            }
            _targetMode = mode;
            UpdateOutputUi();
        }

        private void SetExportMode(ExportMode mode)
        {
            _exportMode = mode;
            if (mode == ExportMode.Separate && _targetMode != OutputTargetMode.Folder)
                SetTargetMode(OutputTargetMode.Folder);
            UpdateOutputUi();
        }

        private void UpdateOutputUi()
        {
            if (_targetFileButton == null) return;
            _targetFileButton.Enabled = _exportMode == ExportMode.Merge;
            UiTheme.StyleSegment(_targetFileButton, _targetMode == OutputTargetMode.File);
            UiTheme.StyleSegment(_targetFolderButton, _targetMode == OutputTargetMode.Folder);
            UiTheme.StyleSegment(_mergeButton, _exportMode == ExportMode.Merge);
            UiTheme.StyleSegment(_separateButton, _exportMode == ExportMode.Separate);
            bool separate = _exportMode == ExportMode.Separate;
            _mergeNameLabel.Visible = _mergeNameBox.Visible = !separate;
            _batchNameLabel.Visible = _batchNameBox.Visible = _applyBatchButton.Visible = separate;
            if (String.IsNullOrWhiteSpace(_outputPathBox.Text))
            {
                string directory = String.IsNullOrWhiteSpace(_settings.LastOutputDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : _settings.LastOutputDirectory;
                _outputPathBox.Text = _targetMode == OutputTargetMode.File ? Path.Combine(directory, EnsurePdfExtension(GetMergeBaseName())) : directory;
            }
        }

        private void BrowseOutput()
        {
            if (_exportMode == ExportMode.Merge && _targetMode == OutputTargetMode.File)
            {
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Title = "选择 PDF 文件";
                    dialog.Filter = "PDF 文件|*.pdf";
                    dialog.FileName = EnsurePdfExtension(GetMergeBaseName());
                    string currentDirectory = SafeDirectoryName(_outputPathBox.Text.Trim());
                    if (!String.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory)) dialog.InitialDirectory = currentDirectory;
                    if (dialog.ShowDialog(this) == DialogResult.OK) _outputPathBox.Text = dialog.FileName;
                }
            }
            else
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = _exportMode == ExportMode.Separate ? "选择一图一个 PDF 的输出文件夹" : "选择合并 PDF 的输出文件夹";
                    string current = _outputPathBox.Text.Trim();
                    if (Directory.Exists(current)) dialog.SelectedPath = current;
                    if (dialog.ShowDialog(this) == DialogResult.OK) _outputPathBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void ApplyBatchNamesClicked(object sender, EventArgs e)
        {
            string prefix = PdfExporter.SanitizeFileName(_batchNameBox.Text.Trim());
            int digits = Math.Max(2, _items.Count.ToString().Length);
            for (int index = 0; index < _items.Count; index++)
                _items[index].OutputName = prefix + "_" + (index + 1).ToString("D" + digits.ToString());
            RebuildCardControls();
            _statusLabel.Text = "已批量生成 " + _items.Count.ToString() + " 个文件名";
        }

        private void ChooseFiles()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "添加图片文件";
                dialog.Multiselect = true;
                dialog.Filter = "支持的图片|*.jpg;*.jpeg;*.png;*.bmp|JPEG|*.jpg;*.jpeg|PNG|*.png|BMP|*.bmp";
                if (dialog.ShowDialog(this) == DialogResult.OK) AddInputs(dialog.FileNames);
            }
        }

        private void HandleStartupInputs()
        {
            if (_startupArgs.Length == 0) return;
            List<string> pdfFiles = _startupArgs.Where(delegate (string path)
            {
                return File.Exists(path) && PdfToImageExporter.IsSupportedPath(path);
            }).ToList();
            List<string> imageInputs = _startupArgs.Where(delegate (string path)
            {
                return !pdfFiles.Contains(path, StringComparer.OrdinalIgnoreCase);
            }).ToList();
            if (imageInputs.Count > 0) AddInputs(imageInputs);
            if (pdfFiles.Count > 0) OpenPdfConverter(pdfFiles);
        }

        private void OpenPdfConverter(IEnumerable<string> initialPaths)
        {
            if (_exportCancellation != null) return;
            PreparePdfConverter();
            _pdfConverter.AddExternalInputs(initialPaths);
            SuspendLayout();
            _pdfConverter.Show();
            _pdfConverter.BringToFront();
            _imageToPdfView.Hide();
            Text = "PDF转图片";
            ResumeLayout(true);
        }

        private void PreparePdfConverter()
        {
            if (_pdfConverter != null && !_pdfConverter.IsDisposed) return;
            _pdfConverter = new PdfToImageForm(new string[0], Icon, true)
            {
                TopLevel = false,
                FormBorderStyle = FormBorderStyle.None,
                Dock = DockStyle.Fill,
                ShowInTaskbar = false,
                Visible = false
            };
            _pdfConverter.ReturnToImagesRequested += ReturnToImageConverter;
            Controls.Add(_pdfConverter);
            _pdfConverter.CreateControl();
            _pdfConverter.PerformLayout();
            _pdfConverter.Show();
            _pdfConverter.SendToBack();
            _imageToPdfView.BringToFront();
        }

        private void ReturnToImageConverter(object sender, EventArgs e)
        {
            SuspendLayout();
            _pdfConverter.Hide();
            _imageToPdfView.Show();
            _imageToPdfView.BringToFront();
            Text = "图片转PDF";
            ResumeLayout(true);
            AppSettings latest = AppSettingsStore.Load();
            _settings.LastOutputDirectory = latest.LastOutputDirectory;
            if (_targetMode == OutputTargetMode.Folder && _outputPathBox != null)
                _outputPathBox.Text = latest.LastOutputDirectory;
        }

        private void HandleDroppedPaths(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            bool allPdfFiles = paths.All(delegate (string path)
            {
                return File.Exists(path) && PdfToImageExporter.IsSupportedPath(path);
            });
            if (allPdfFiles) OpenPdfConverter(paths);
            else AddInputs(paths);
        }

        private void ChooseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择图片文件夹（只读取当前层，不读取子文件夹）";
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
                        string[] files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
                        Array.Sort(files, new PathNaturalComparer());
                        foreach (string file in files)
                        {
                            if (ImageTools.IsSupportedPath(file)) candidates.Add(file);
                            else rejected.Add(Path.GetFileName(file) + "（不支持的格式）");
                        }
                    }
                    catch (Exception error) { rejected.Add(Path.GetFileName(path) + "（无法读取文件夹：" + error.Message + "）"); }
                }
                else if (File.Exists(path))
                {
                    if (ImageTools.IsSupportedPath(path)) candidates.Add(path);
                    else rejected.Add(Path.GetFileName(path) + "（不支持的格式）");
                }
                else
                    rejected.Add(Path.GetFileName(path) + "（文件不存在）");
            }

            int added = 0;
            foreach (string candidate in candidates)
            {
                string full;
                try { full = Path.GetFullPath(candidate); }
                catch { continue; }
                if (_paths.Contains(full)) continue;
                _items.Add(new ImageItem(full));
                _paths.Add(full);
                added++;
            }
            if (added > 0) RebuildCardsAndQueuePreviews(true);
            ShowRejectedSummary(rejected, "以下项目未加入：");
        }

        private void ShowRejectedSummary(IList<string> rejected, string heading)
        {
            if (rejected == null || rejected.Count == 0) return;
            StringBuilder message = new StringBuilder();
            message.AppendLine(heading);
            int limit = Math.Min(16, rejected.Count);
            for (int index = 0; index < limit; index++) message.AppendLine("• " + rejected[index]);
            if (rejected.Count > limit) message.AppendLine("……另有 " + (rejected.Count - limit).ToString() + " 个项目");
            MessageBox.Show(this, message.ToString(), "图片导入提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ClearItems()
        {
            CancelPreviewQueue();
            foreach (ImageItem item in _items) item.DisposePreview();
            _items.Clear();
            _paths.Clear();
            RebuildCardControls();
            UpdateCount();
        }

        private void RebuildCardsAndQueuePreviews(bool reportCorrupt)
        {
            RebuildCardControls();
            QueuePreviews(reportCorrupt);
        }

        private void RebuildCardControls()
        {
            if (_cards == null) return;
            _cards.SuspendLayout();
            try
            {
                foreach (ModernImageCard card in _cardMap.Values) card.ReleasePreviewReference();
                Control[] old = _cards.Controls.Cast<Control>().ToArray();
                _cards.Controls.Clear();
                foreach (Control control in old) control.Dispose();
                _cardMap.Clear();
                foreach (ImageItem item in _items)
                {
                    int cardWidth;
                    int previewHeight;
                    GetCardMetrics(_items.Count, out cardWidth, out previewHeight);
                    ModernImageCard card = new ModernImageCard(this, item, cardWidth, previewHeight);
                    _cardMap[item] = card;
                    EnableDropRecursive(card);
                    _cards.Controls.Add(card);
                }
            }
            finally
            {
                _cards.ResumeLayout(true);
                UpdateCount();
            }
        }

        private void QueuePreviews(bool reportCorrupt)
        {
            CancelPreviewQueue();
            int generation = ++_previewGeneration;
            _previewCancellation = new CancellationTokenSource();
            CancellationToken token = _previewCancellation.Token;
            foreach (ImageItem item in _items)
            {
                ModernImageCard card;
                if (_cardMap.TryGetValue(item, out card)) card.ReleasePreviewReference();
                item.DisposePreview();
                item.PreviewError = null;
                if (card != null) card.SetPreview(null, null);
            }
            if (_items.Count == 0) return;

            List<ImageItem> order = GetPreviewOrder();
            PaperSizeKind paperSize = GetPaperSize();
            PageOrientation orientation = _orientation;
            bool autoRotate = _autoRotateCheck.Checked;
            int margin = GetMarginMm();
            WatermarkOptions watermark = GetWatermarkOptions();
            int longSide = _items.Count <= 12 ? 820 : (_items.Count <= 60 ? 560 : 380);
            int pageWidth;
            int pageHeight;
            GetPagePixels(paperSize, orientation, longSide, out pageWidth, out pageHeight);
            int workerCount = Environment.ProcessorCount >= 8 ? 4 : (Environment.ProcessorCount >= 4 ? 2 : 1);
            int next = -1;
            List<Tuple<ImageItem, string>> failures = new List<Tuple<ImageItem, string>>();
            Task[] workers = new Task[workerCount];
            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                workers[workerIndex] = Task.Run(delegate
                {
                    while (!token.IsCancellationRequested)
                    {
                        int index = Interlocked.Increment(ref next);
                        if (index >= order.Count) break;
                        ImageItem item = order[index];
                        Bitmap preview = null;
                        string error = null;
                        try
                        {
                            preview = ImageTools.RenderPagePreview(item, paperSize, orientation, autoRotate, margin, pageWidth, pageHeight);
                            WatermarkRenderer.DrawPreview(preview, watermark);
                        }
                        catch (Exception failure)
                        {
                            error = failure.Message;
                            lock (failures) failures.Add(Tuple.Create(item, error));
                        }
                        Bitmap completed = preview;
                        string completedError = error;
                        try
                        {
                            BeginInvoke((Action)delegate
                            {
                                if (IsDisposed || generation != _previewGeneration || !_items.Contains(item))
                                {
                                    if (completed != null) completed.Dispose();
                                    return;
                                }
                                item.DisposePreview();
                                item.Preview = completed;
                                item.PreviewError = completedError;
                                ModernImageCard card;
                                if (_cardMap.TryGetValue(item, out card)) card.SetPreview(completed, completedError);
                            });
                        }
                        catch
                        {
                            if (completed != null) completed.Dispose();
                        }
                    }
                });
            }

            Task.WhenAll(workers).ContinueWith(delegate
            {
                if (token.IsCancellationRequested || IsDisposed) return;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (generation != _previewGeneration) return;
                        _statusLabel.Text = failures.Count == 0 ? "缩略图已就绪" : "有 " + failures.Count.ToString() + " 张图片无法读取";
                        if (reportCorrupt && failures.Count > 0)
                        {
                            List<string> messages = new List<string>();
                            foreach (Tuple<ImageItem, string> failure in failures)
                            {
                                if (_items.Remove(failure.Item1))
                                {
                                    _paths.Remove(failure.Item1.Path);
                                    failure.Item1.DisposePreview();
                                    messages.Add(failure.Item1.FileName + "（损坏或无法读取）");
                                }
                            }
                            ShowRejectedSummary(messages, "以下损坏图片已跳过：");
                            RebuildCardsAndQueuePreviews(false);
                        }
                    });
                }
                catch { }
            }, TaskScheduler.Default);
        }

        private static void GetCardMetrics(int count, out int cardWidth, out int previewHeight)
        {
            if (count <= 3)
            {
                cardWidth = 420;
                previewHeight = 540;
            }
            else if (count <= 12)
            {
                cardWidth = 360;
                previewHeight = 460;
            }
            else if (count <= 60)
            {
                cardWidth = 310;
                previewHeight = 380;
            }
            else
            {
                cardWidth = 270;
                previewHeight = 310;
            }
        }

        private List<ImageItem> GetPreviewOrder()
        {
            List<ImageItem> visible = new List<ImageItem>();
            List<ImageItem> remaining = new List<ImageItem>();
            Rectangle viewport = _cards.ClientRectangle;
            foreach (ImageItem item in _items)
            {
                ModernImageCard card;
                if (_cardMap.TryGetValue(item, out card) && card.Bounds.IntersectsWith(viewport)) visible.Add(item);
                else remaining.Add(item);
            }
            visible.AddRange(remaining);
            return visible;
        }

        private void CancelPreviewQueue()
        {
            _previewGeneration++;
            if (_previewCancellation == null) return;
            try { _previewCancellation.Cancel(); } catch { }
            _previewCancellation.Dispose();
            _previewCancellation = null;
        }

        private void SortItems(SortMode mode)
        {
            NaturalComparer natural = new NaturalComparer();
            _items.Sort(delegate (ImageItem left, ImageItem right)
            {
                int result = CompareBySortMode(left, right, mode, natural);
                if (result != 0) return result;
                return left.AddedOrder.CompareTo(right.AddedOrder);
            });
            RebuildCardControls();
        }

        private static int CompareBySortMode(ImageItem left, ImageItem right, SortMode mode, NaturalComparer natural)
        {
            if (mode == SortMode.NameAscending) return natural.Compare(left, right);
            if (mode == SortMode.NameDescending) return natural.Compare(right, left);
            if (mode == SortMode.AddedAscending) return left.AddedOrder.CompareTo(right.AddedOrder);
            if (mode == SortMode.AddedDescending) return right.AddedOrder.CompareTo(left.AddedOrder);
            FileInfo a = TryGetFileInfo(left.Path);
            FileInfo b = TryGetFileInfo(right.Path);
            if (a == null && b != null) return 1;
            if (a != null && b == null) return -1;
            if (a == null) return 0;
            if (mode == SortMode.SizeAscending) return a.Length.CompareTo(b.Length);
            if (mode == SortMode.SizeDescending) return b.Length.CompareTo(a.Length);
            if (mode == SortMode.ModifiedAscending) return a.LastWriteTime.CompareTo(b.LastWriteTime);
            if (mode == SortMode.ModifiedDescending) return b.LastWriteTime.CompareTo(a.LastWriteTime);
            return 0;
        }

        private static FileInfo TryGetFileInfo(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path) : null; }
            catch { return null; }
        }

        private void ExportClicked(object sender, EventArgs e)
        {
            if (_exportCancellation != null) return;
            if (_items.Count == 0)
            {
                MessageBox.Show(this, "请先添加图片。", "无法导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            WatermarkOptions watermark = GetWatermarkOptions();
            if (watermark.Mode == WatermarkMode.Custom && String.IsNullOrWhiteSpace(watermark.Text))
            {
                MessageBox.Show(this, "自定义水印文字不能为空。", "无法导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            List<string> unavailable = _items.Where(delegate (ImageItem item) { return !File.Exists(item.Path) || !String.IsNullOrWhiteSpace(item.PreviewError); }).Select(delegate (ImageItem item) { return item.FileName; }).ToList();
            if (unavailable.Count > 0)
            {
                MessageBox.Show(this, "以下图片不可用，请重新添加：\r\n\r\n" + String.Join("\r\n", unavailable.ToArray()), "无法导出", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string target;
            string folder;
            bool explicitOverwrite;
            try
            {
                if (!ResolveOutput(out target, out folder, out explicitOverwrite)) return;
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "输出路径无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ExportOptions options = new ExportOptions
            {
                PaperSize = GetPaperSize(),
                Orientation = _orientation,
                AutoRotate = _autoRotateCheck.Checked,
                MarginMm = GetMarginMm(),
                Quality = (QualityPreset)Math.Max(0, _qualityCombo.SelectedIndex),
                Mode = _exportMode,
                BaseName = GetMergeBaseName(),
                Watermark = watermark,
                TargetMode = _targetMode
            };
            List<ImageSnapshot> snapshots = _items.Select(delegate (ImageItem item)
            {
                return new ImageSnapshot { Path = item.Path, ManualRotation = item.ManualRotation, OutputName = item.OutputName };
            }).ToList();

            _exportCancellation = new CancellationTokenSource();
            CancellationToken token = _exportCancellation.Token;
            SetExportState(true, "正在准备导出…");
            Task.Run(delegate
            {
                try
                {
                    Action<int> progress = delegate (int value)
                    {
                        try { BeginInvoke((Action)delegate { _statusLabel.Text = "正在导出 " + value.ToString() + "%"; }); }
                        catch { }
                    };
                    if (options.Mode == ExportMode.Merge)
                        PdfExporter.ExportMerged(target, snapshots, options, progress, token);
                    else
                        PdfExporter.ExportSeparate(folder, snapshots, options, progress, token);
                    if (IsDisposed) return;
                    BeginInvoke((Action)delegate
                    {
                        SaveSuccessfulOutputSettings(options.Mode == ExportMode.Merge ? Path.GetDirectoryName(target) : folder);
                        _statusLabel.Text = "导出完成";
                        MessageBox.Show(this, "PDF 已成功导出到：\r\n" + (options.Mode == ExportMode.Merge ? target : folder), "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                }
                catch (OperationCanceledException)
                {
                    if (!IsDisposed) try { BeginInvoke((Action)delegate { _statusLabel.Text = "已取消导出"; }); } catch { }
                }
                catch (Exception error)
                {
                    if (!IsDisposed) try { BeginInvoke((Action)delegate { _statusLabel.Text = "导出失败"; MessageBox.Show(this, error.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }); } catch { }
                }
                finally
                {
                    if (!IsDisposed) try { BeginInvoke((Action)delegate { SetExportState(false, _statusLabel.Text); }); } catch { }
                }
            });
        }

        private bool ResolveOutput(out string target, out string folder, out bool explicitOverwrite)
        {
            target = null;
            folder = null;
            explicitOverwrite = false;
            string raw = _outputPathBox.Text.Trim();
            if (String.IsNullOrWhiteSpace(raw))
            {
                BrowseOutput();
                raw = _outputPathBox.Text.Trim();
                if (String.IsNullOrWhiteSpace(raw)) return false;
            }
            if (_exportMode == ExportMode.Separate || _targetMode == OutputTargetMode.Folder)
            {
                folder = Path.GetFullPath(raw);
                Directory.CreateDirectory(folder);
                if (_exportMode == ExportMode.Merge)
                    target = PdfExporter.GetUniquePath(folder, EnsurePdfExtension(PdfExporter.SanitizeFileName(GetMergeBaseName())));
            }
            else
            {
                target = Path.GetFullPath(EnsurePdfExtension(raw));
                string directory = Path.GetDirectoryName(target);
                if (String.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("请选择有效的 PDF 保存位置。");
                Directory.CreateDirectory(directory);
                if (File.Exists(target))
                {
                    if (MessageBox.Show(this, "文件已经存在，是否替换？\r\n\r\n" + target, "确认替换", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return false;
                    explicitOverwrite = true;
                }
            }
            return true;
        }

        private void SaveSuccessfulOutputSettings(string directory)
        {
            try
            {
                _settings.LastOutputDirectory = String.IsNullOrWhiteSpace(directory) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : directory;
                _settings.LastTargetMode = _targetMode;
                AppSettingsStore.Save(_settings);
            }
            catch { }
        }

        private void SetExportState(bool exporting, string status)
        {
            _exportButton.Enabled = !exporting;
            _cancelButton.Visible = exporting;
            _statusLabel.Text = status;
            UseWaitCursor = exporting;
            if (!exporting)
            {
                UseWaitCursor = false;
                if (_exportCancellation != null)
                {
                    _exportCancellation.Dispose();
                    _exportCancellation = null;
                }
            }
        }

        private PaperSizeKind GetPaperSize()
        {
            return (PaperSizeKind)Math.Max(0, Math.Min(PaperSizes.DisplayNames.Length - 1, _paperCombo.SelectedIndex));
        }

        private int GetMarginMm()
        {
            return _marginCombo.SelectedIndex == 0 ? 0 : (_marginCombo.SelectedIndex == 1 ? 5 : 10);
        }

        private string GetMergeBaseName()
        {
            string value = _mergeNameBox == null ? String.Empty : _mergeNameBox.Text.Trim();
            if (value.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 4);
            return String.IsNullOrWhiteSpace(value) ? DefaultMergeName() : value;
        }

        private static string DefaultMergeName()
        {
            return "图片合并_" + DateTime.Now.ToString("yyyyMMdd_HHmm");
        }

        private static string EnsurePdfExtension(string value)
        {
            return value.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? value : value + ".pdf";
        }

        private static string SafeDirectoryName(string value)
        {
            try { return Path.GetDirectoryName(value); }
            catch { return null; }
        }

        private static void GetPagePixels(PaperSizeKind paper, PageOrientation orientation, int longSide, out int width, out int height)
        {
            float paperWidth = PaperSizes.GetWidthMm(paper);
            float paperHeight = PaperSizes.GetHeightMm(paper);
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

        private void UpdateCount()
        {
            if (_countLabel != null) _countLabel.Text = "共 " + _items.Count.ToString() + " 页";
            if (_statusLabel != null && _exportCancellation == null)
                _statusLabel.Text = _items.Count == 0 ? "拖入图片开始转换" : "已准备 " + _items.Count.ToString() + " 张图片";
        }

        private void EnableDropRecursive(Control control)
        {
            if (control == null) return;
            control.AllowDrop = true;
            if (control != this && control != _cards)
            {
                control.DragEnter += HandleDragEnter;
                control.DragOver += HandleDragOver;
                control.DragDrop += HandleDragDrop;
            }
            foreach (Control child in control.Controls) EnableDropRecursive(child);
        }

        private void HandleDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else if (e.Data.GetDataPresent(typeof(ImageItem))) e.Effect = DragDropEffects.Move;
            else e.Effect = DragDropEffects.None;
        }

        private void HandleDragOver(object sender, DragEventArgs e)
        {
            HandleDragEnter(sender, e);
        }

        private void HandleDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) HandleDroppedPaths(paths);
            else if (e.Data.GetDataPresent(typeof(ImageItem))) CardsDragDrop(_cards, e);
        }

        private void CardsDragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null)
            {
                HandleDroppedPaths(paths);
                return;
            }
            ImageItem item = e.Data.GetData(typeof(ImageItem)) as ImageItem;
            if (item == null) return;
            Point location = _cards.PointToClient(new Point(e.X, e.Y));
            int targetIndex = _items.Count;
            for (int index = 0; index < _cards.Controls.Count; index++)
            {
                if (_cards.Controls[index].Bounds.Contains(location)) { targetIndex = index; break; }
            }
            int sourceIndex = _items.IndexOf(item);
            if (sourceIndex < 0) return;
            _items.RemoveAt(sourceIndex);
            if (sourceIndex < targetIndex) targetIndex--;
            targetIndex = Math.Max(0, Math.Min(targetIndex, _items.Count));
            _items.Insert(targetIndex, item);
            RebuildCardControls();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_exportCancellation != null) _exportCancellation.Cancel();
            if (_pdfConverter != null && !_pdfConverter.IsDisposed)
            {
                _pdfConverter.ReturnToImagesRequested -= ReturnToImageConverter;
                _pdfConverter.Close();
            }
            CancelPreviewQueue();
            foreach (ModernImageCard card in _cardMap.Values) card.ReleasePreviewReference();
            foreach (ImageItem item in _items) item.DisposePreview();
            base.OnFormClosing(e);
        }
    }
}
