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
            _message = "æ­£åœ¨ç”Ÿæˆé¢„è§ˆâ€¦";
        }

        public void SetPreview(Bitmap preview, string error)
        {
            _preview = preview;
            _error = !String.IsNullOrWhiteSpace(error);
            _message = _error ? "æ— æ³•è¯»å–å›¾ç‰‡" : (preview == null ? "æ­£åœ¨ç”Ÿæˆé¢„è§ˆâ€¦" : String.Empty);
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
                Text = "PDF æ–‡ä»¶å",
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
                AccessibleName = "PDF è¾“å‡ºæ–‡ä»¶å"
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
            Button left = UiTheme.Button("â†¶  å·¦æ—‹è½¬", 92, 38);
            Button right = UiTheme.Button("â†·  å³æ—‹è½¬", 92, 38);
            Button remove = UiTheme.Button("åˆ é™¤", 92, 38);
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
            _fileName.Text = String.IsNullOrWhiteSpace(error) ? _item.FileName : _item.FileName + "ï¼ˆæ— æ³•è¯»å–ï¼‰";
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
            Text = "è‡ªå®šä¹‰æ–‡å­—æ°´å°";
            Icon = icon;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(480, 330);
            BackColor = UiTheme.Surface;
            Font = UiTheme.Font(9.5f, FontStyle.Regular);

            Label title = new Label { Left = 28, Top = 24, Width = 420, Height = 30, Text = "è‡ªå®šä¹‰æ–‡å­—æ°´å°", Font = UiTheme.Font(14f, FontStyle.Bold), ForeColor = UiTheme.Text };
            Label helper = new Label { Left = 28, Top = 55, Width = 420, Height = 38, Text = "æ°´å°ä¼šæ˜¾ç¤ºåœ¨ç¼©ç•¥å›¾ã€å¤§å›¾é¢„è§ˆå’Œæœ€ç»ˆ PDF ä¸­ã€‚", ForeColor = UiTheme.Muted };
            AddFieldLabel("æ–‡å­—ï¼ˆ1ï½64 ä¸ªå­—ç¬¦ï¼‰", 28, 96);
            _text = new TextBox { Left = 180, Top = 92, Width = 260, MaxLength = 64, Text = current == null ? String.Empty : current.Text };
            AddFieldLabel("é€æ˜åº¦", 28, 139);
            _opacity = new NumericUpDown { Left = 180, Top = 135, Width = 110, Minimum = 5, Maximum = 60, Increment = 1, Value = current == null ? 18 : Math.Max(5, Math.Min(60, current.OpacityPercent)) };
            Label percent = new Label { Left = 298, Top = 139, AutoSize = true, Text = "%", ForeColor = UiTheme.Muted };
            AddFieldLabel("å€¾æ–œè§’åº¦", 28, 182);
            _angle = MakeCombo(180, 178, 130, new object[] { "-45Â°", "0Â°", "45Â°" });
            int angle = current == null ? 45 : current.AngleDegrees;
            _angle.SelectedIndex = angle < 0 ? 0 : (angle == 0 ? 1 : 2);
            AddFieldLabel("å¸ƒå±€", 28, 225);
            _layout = MakeCombo(180, 221, 180, new object[] { "å±…ä¸­", "å…¨é¡µå¹³é“º", "å³ä¸‹è§’" });
            WatermarkLayout layout = current == null ? WatermarkLayout.Tile : current.Layout;
            _layout.SelectedIndex = layout == WatermarkLayout.Center ? 0 : (layout == WatermarkLayout.Tile ? 1 : 2);

            Button cancel = UiTheme.Button("å–æ¶ˆ", 94, 38);
            cancel.Left = 244;
            cancel.Top = 274;
            cancel.DialogResult = DialogResult.Cancel;
            Button confirm = UiTheme.Button("ä¿å­˜", 94, 38);
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
                MessageBox.Show(this, "è¯·è¾“å…¥ 1ï½64 ä¸ªå­—ç¬¦çš„æ°´å°æ–‡å­—ã€‚", "æ°´å°æ–‡å­—ä¸ºç©º", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        private void AddFieldLabel(string text, int left, int top)×­|êÚ$z{-®éÜj×Ğ ¢&—fFRÆ—7CÄ–ÖvT—FVÓâvWE&Wf–Wt÷&FW"‚¢°¢Æ—7CÄ–ÖvT—FVÓâf—6–&ÆRÒæWrÆ—7CÄ–ÖvT—FVÓâ‚“°¢Æ—7CÄ–ÖvT—FVÓâ&VÖ–æ–ærÒæWrÆ—7CÄ–ÖvT—FVÓâ‚“°¢&V7FævÆRf–Ww÷'BÒö6&G2ä6Æ–VçE&V7FævÆS°¢f÷&V6‚„–ÖvT—FVÒ—FVÒ–âö—FV×2¢°¢ÖöFW&ä–ÖvT6&B6&C°¢–b…ö6&DÖåG'”vWEfÇVR†—FVÒÂ÷WB6&B’bb6&Bä&÷VæG2ä–çFW'6V7G5v—F‚‡f–Ww÷'B’’f—6–&ÆRäFB†—FVÒ“°¢VÇ6R&VÖ–æ–æräFB†—FVÒ“°¢Ğ¢f—6–&ÆRäFE&ævR‡&VÖ–æ–ær“°¢&WGW&âf—6–&ÆS°¢Ğ ¢&—fFRfö–B6æ6VÅ&Wf–WuVWVR‚¢°¢÷&Wf–WtvVæW&F–öâ²³°¢–b…÷&Wf–Wt6æ6VÆÆF–öâÓÒçVÆÂ’&WGW&ã°¢G'’²÷&Wf–Wt6æ6VÆÆF–öâä6æ6VÂ‚“²Ò6F6‚²Ğ¢÷&Wf–Wt6æ6VÆÆF–öâäF—7÷6R‚“°¢÷&Wf–Wt6æ6VÆÆF–öâÒçVÆÃ°¢Ğ ¢&—fFRfö–B6÷'D—FV×2…6÷'DÖöFRÖöFR¢°¢æGW&Ä6ö×&W"æGW&ÂÒæWræGW&Ä6ö×&W"‚“°¢ö—FV×2å6÷'B†FVÆVvFR„–ÖvT—FVÒÆVgBÂ–ÖvT—FVÒ&–v‡B¢°¢–çB&W7VÇBÒ6ö×&T'•6÷'DÖöFR†ÆVgBÂ&–v‡BÂÖöFRÂæGW&Â“°¢–b‡&W7VÇBÒ’&WGW&â&W7VÇC°¢&WGW&âÆVgBäFFVD÷&FW"ä6ö×&UFò‡&–v‡BäFFVD÷&FW"“°¢Ò“°¢&V'V–ÆD6&D6öçG&öÇ2‚“°¢Ğ ¢&—fFR7FF–2–çB6ö×&T'•6÷'DÖöFR„–ÖvT—FVÒÆVgBÂ–ÖvT—FVÒ&–v‡BÂ6÷'DÖöFRÖöFRÂæGW&Ä6ö×&W"æGW&Â¢°¢–b†ÖöFRÓÒ6÷'DÖöFRäæÖT66VæF–ær’&WGW&âæGW&Âä6ö×&R†ÆVgBÂ&–v‡B“°¢–b†ÖöFRÓÒ6÷'DÖöFRäæÖTFW66VæF–ær’&WGW&âæGW&Âä6ö×&R‡&–v‡BÂÆVgB“°¢–b†ÖöFRÓÒ6÷'DÖöFRäFFVD66VæF–ær’&WGW&âÆVgBäFFVD÷&FW"ä6ö×&UFò‡&–v‡BäFFVD÷&FW"“°¢–b†ÖöFRÓÒ6÷'DÖöFRäFFVDFW66VæF–ær’&WGW&â&–v‡BäFFVD÷&FW"ä6ö×&UFò†ÆVgBäFFVD÷&FW"“°¢f–ÆT–æfòÒG'”vWDf–ÆT–æfò†ÆVgBåF‚“°¢f–ÆT–æfò"ÒG'”vWDf–ÆT–æfò‡&–v‡BåF‚“°¢–b†ÓÒçVÆÂbb"ÒçVÆÂ’&WGW&â°¢–b†ÒçVÆÂbb"ÓÒçVÆÂ’&WGW&âÓ°¢–b†ÓÒçVÆÂ’&WGW&â°¢–b†ÖöFRÓÒ6÷'DÖöFRå6—¦T66VæF–ær’&WGW&âäÆVæwF‚ä6ö×&UFò†"äÆVæwF‚“°¢–b†ÖöFRÓÒ6÷'DÖöFRå6—¦TFW66VæF–ær’&WGW&â"äÆVæwF‚ä6ö×&UFò†äÆVæwF‚“°¢–b†ÖöFRÓÒ6÷'DÖöFRäÖöF–f–VD66VæF–ær’&WGW&âäÆ7Ew&—FUF–ÖRä6ö×&UFò†"äÆ7Ew&—FUF–ÖR“°¢–b†ÖöFRÓÒ6÷'DÖöFRäÖöF–f–VDFW66VæF–ær’&WGW&â"äÆ7Ew&—FUF–ÖRä6ö×&UFò†äÆ7Ew&—FUF–ÖR“°¢&WGW&â°¢Ğ ¢&—fFR7FF–2f–ÆT–æfòG'”vWDf–ÆT–æfò‡7G&–ærF‚¢°¢G'’²&WGW&âf–ÆRäW†—7G2‡F‚’òæWrf–ÆT–æfò‡F‚’¢çVÆÃ²Ğ¢6F6‚²&WGW&âçVÆÃ²Ğ¢Ğ ¢&—fFRfö–BW‡÷'D6Æ–6¶VB†ö&¦V7B6VæFW"ÂWfVçD&w2R¢°¢–b…öW‡÷'D6æ6VÆÆF–öâÒçVÆÂ’&WGW&ã°¢–b…ö—FV×2ä6÷VçBÓÒ¢°¢ÖW76vT&÷‚å6†÷r‡F†—2Â.Šû~XXk{¾XªY»îx˜~8""Â.izk9^ZûÎX{¢"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°¢&WGW&ã°¢Ğ¢vFW&Ö&´÷F–öç2vFW&Ö&²ÒvWEvFW&Ö&´÷F–öç2‚“°¢–b‡vFW&Ö&²äÖöFRÓÒvFW&Ö&´ÖöFRä7W7FöÒbb7G&–ærä—4çVÆÄ÷%v†—FU76R‡vFW&Ö&²åFW‡B’¢°¢ÖW76vT&÷‚å6†÷r‡F†—2Â.ˆz®Zé®K˜kNXÛih~ZÙ~KˆŞˆ;ŞK‹®z›®8""Â.izk9^ZûÎX{¢"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°¢&WGW&ã°¢Ğ¢Æ—7CÇ7G&–æsâVæf–Æ&ÆRÒö—FV×2åv†W&R†FVÆVvFR„–ÖvT—FVÒ—FVÒ’²&WGW&âf–ÆRäW†—7G2†—FVÒåF‚’ÇÂ7G&–ærä—4çVÆÄ÷%v†—FU76R†—FVÒå&Wf–WtW'&÷"“²Ò’å6VÆV7B†FVÆVvFR„–ÖvT—FVÒ—FVÒ’²&WGW&â—FVÒäf–ÆTæÖS²Ò’åFôÆ—7B‚“°¢–b‡Væf–Æ&ÆRä6÷VçBâ¢°¢ÖW76vT&÷‚å6†÷r‡F†—2Â.Kº^Kˆ¾Y»îx˜~KˆŞXúşyJûÈÎŠû~˜xŞikk{¾XªûÉ¥Ç%ÆåÇ%Æâ"²7G&–ærä¦ö–â‚%Ç%Æâ"ÂVæf–Æ&ÆRåFô'&’‚’’Â.izk9^ZûÎX{¢"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâåv&æ–ær“°¢&WGW&ã°¢Ğ ¢7G&–ærF&vWC°¢7G&–ærföÆFW#°¢&ööÂW‡Æ–6—D÷fW'w&—FS°¢G'¢°¢–b‚&W6öÇfT÷WGWB†÷WBF&vWBÂ÷WBföÆFW"Â÷WBW‡Æ–6—D÷fW'w&—FR’’&WGW&ã°¢Ğ¢6F6‚„W†6WF–öâW'&÷"¢°¢ÖW76vT&÷‚å6†÷r‡F†—2ÂW'&÷"äÖW76vRÂ.‹é>X{®‹zş[èNiziX‚"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâåv&æ–ær“°¢&WGW&ã°¢Ğ ¢W‡÷'D÷F–öç2÷F–öç2ÒæWrW‡÷'D÷F–öç0¢°¢W%6—¦RÒvWEW%6—¦R‚’À¢÷&–VçFF–öâÒö÷&–VçFF–öâÀ¢WFõ&÷FFRÒöWFõ&÷FFT6†V6²ä6†V6¶VBÀ¢Ö&v–äÖÒÒvWDÖ&v–äÖÒ‚’À¢VÆ—G’Ò…VÆ—G•&W6WB”ÖF‚äÖ‚ƒÂ÷VÆ—G”6öÖ&òå6VÆV7FVD–æFW‚’À¢ÖöFRÒöW‡÷'DÖöFRÀ¢&6TæÖRÒvWDÖW&vT&6TæÖR‚’À¢vFW&Ö&²ÒvFW&Ö&²À¢F&vWDÖöFRÒ÷F&vWDÖöFP¢Ó°¢Æ—7CÄ–ÖvU6æ6†÷Câ6æ6†÷G2Òö—FV×2å6VÆV7B†FVÆVvFR„–ÖvT—FVÒ—FVÒ¢°¢&WGW&âæWr–ÖvU6æ6†÷B²F‚Ò—FVÒåF‚ÂÖçVÅ&÷FF–öâÒ—FVÒäÖçVÅ&÷FF–öâÂ÷WGWDæÖRÒ—FVÒä÷WGWDæÖRÓ°¢Ò’åFôÆ—7B‚“° ¢öW‡÷'D6æ6VÆÆF–öâÒæWr6æ6VÆÆF–öåFö¶Vå6÷W&6R‚“°¢6æ6VÆÆF–öåFö¶VâFö¶VâÒöW‡÷'D6æ6VÆÆF–öâåFö¶Vã°¢6WDW‡÷'E7FFR‡G'VRÂ.jÚ>YÊXxnZH~ZûÎX{®(
b"“°¢F6²å'Vâ†FVÆVvFP¢°¢G'¢°¢7F–öãÆ–çCâ&öw&W72ÒFVÆVvFR†–çBfÇVR¢°¢G'’²&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFR²÷7FGW4Æ&VÂåFW‡BÒ.jÚ>YÊZûÎX{¢"²fÇVRåFõ7G&–ær‚’²"R#²Ò“²Ğ¢6F6‚²Ğ¢Ó°¢–b†÷F–öç2äÖöFRÓÒW‡÷'DÖöFRäÖW&vR¢FdW‡÷'FW"äW‡÷'DÖW&vVB‡F&vWBÂ6æ6†÷G2Â÷F–öç2Â&öw&W72ÂFö¶Vâ“°¢VÇ6P¢FdW‡÷'FW"äW‡÷'E6W&FR†föÆFW"Â6æ6†÷G2Â÷F–öç2Â&öw&W72ÂFö¶Vâ“°¢–b„—4F—7÷6VB’&WGW&ã°¢&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFP¢°¢6fU7V66W76gVÄ÷WGWE6WGF–æw2†÷F–öç2äÖöFRÓÒW‡÷'DÖöFRäÖW&vRòF‚ävWDF—&V7F÷'”æÖR‡F&vWB’¢föÆFW"“°¢÷7FGW4Æ&VÂåFW‡BÒ.ZûÎX{®ZèÎh‰#°¢ÖW76vT&÷‚å6†÷r‡F†—2Â%Db[{.h‰X©şZûÎX{®X‹ûÉ¥Ç%Æâ"²†÷F–öç2äÖöFRÓÒW‡÷'DÖöFRäÖW&vRòF&vWB¢föÆFW"’Â.ZûÎX{®ZèÎh‰"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâä–æf÷&ÖF–öâ“°¢Ò“°¢Ğ¢6F6‚„÷W&F–öä6æ6VÆVDW†6WF–öâ¢°¢–b‚—4F—7÷6VB’G'’²&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFR²÷7FGW4Æ&VÂåFW‡BÒ.[{.XùnkhZûÎX{¢#²Ò“²Ò6F6‚²Ğ¢Ğ¢6F6‚„W†6WF–öâW'&÷"¢°¢–b‚—4F—7÷6VB’G'’²&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFR²÷7FGW4Æ&VÂåFW‡BÒ.ZûÎX{®ZK‹JR#²ÖW76vT&÷‚å6†÷r‡F†—2ÂW'&÷"äÖW76vRÂ.ZûÎX{®ZK‹JR"ÂÖW76vT&÷„'WGFöç2äô²ÂÖW76vT&÷„–6öâäW'&÷"“²Ò“²Ò6F6‚²Ğ¢Ğ¢f–æÆÇ¢°¢–b‚—4F—7÷6VB’G'’²&Vv–ä–çfö¶R‚„7F–öâ–FVÆVvFR²6WDW‡÷'E7FFR†fÇ6RÂ÷7FGW4Æ&VÂåFW‡B“²Ò“²Ò6F6‚²Ğ¢Ğ¢Ò“°¢Ğ ¢&—fFR&ööÂ&W6öÇfT÷WGWB†÷WB7G&–ærF&vWBÂ÷WB7G&–ærföÆFW"Â÷WB&ööÂW‡Æ–6—D÷fW'w&—FR¢°¢F&vWBÒçVÆÃ°¢föÆFW"ÒçVÆÃ°¢W‡Æ–6—D÷fW'w&—FRÒfÇ6S°¢7G&–ær&rÒö÷WGWEF„&÷‚åFW‡BåG&–Ò‚“°¢–b…7G&–ærä—4çVÆÄ÷%v†—FU76R‡&r’¢°¢'&÷w6T÷WGWB‚“°¢&rÒö÷WGWEF„&÷‚åFW‡BåG&–Ò‚“°¢–b…7G&–ærä—4çVÆÄ÷%v†—FU76R‡&r’’&WGW&âfÇ6S°¢Ğ¢–b…öW‡÷'DÖöFRÓÒW‡÷'DÖöFRå6W&FRÇÂ÷F&vWDÖöFRÓÒ÷WGWEF&vWDÖöFRäföÆFW"¢°¢föÆFW"ÒF‚ävWDgVÆÅF‚‡&r“°¢F—&V7F÷'’ä7&VFTF—&V7F÷'’†föÆFW"“°¢–b…öW‡÷'DÖöFRÓÒW‡÷'DÖöFRäÖW&vR¢F&vWBÒFdW‡÷'FW"ävWEVæ—VUF‚†föÆFW"ÂVç7W&UFdW‡FVç6–öâ…FdW‡÷'FW"å6æ—F—¦Tf–ÆTæÖR„vWDÖW&vT&6TæÖR‚’’’“°¢Ğ¢VÇ6P¢°¢F&vWBÒF‚ävWDgVÆÅF‚„Vç7W&UFdW‡FVç6–öâ‡&r’“°¢7G&–ærF—&V7F÷'’ÒF‚ävWDF—&V7F÷'”æÖR‡F&vWB“°¢–b…7G&–ærä—4çVÆÄ÷%v†—FU76R†F—&V7F÷'’’’F‡&÷ræWr–çfÆ–D÷W&F–öäW†6WF–öâ‚.Šû~˜hºiÈiXy¨BDbKùŞZÙKØŞ{Úî8""“°¢F—&V7F÷'’ä7&VFTF—&V7F÷'’†F—&V7F÷'’“°¢–b„f–ÆRäW†—7G2‡F&vWB’¢°¢–b„ÖW76vT&÷‚å6†÷r‡F†—2Â.ih~K»n[{.{¸şZÙYÊûÈÎiŠşY
ni»şhÚ.ûÉõÇ%ÆåÇ%Æâ"²F&vWBÂ.zîŠêNi»şhÚ""ÂÖW76vT&÷„'WGFöç2å–W4æòÂÖW76vT&÷„–6öâåVW7F–öâ’ÒF–Æöu&W7VÇBå–W2¢&WGW&âfÇ6S°¢W‡Æ–6—D÷fW'w&—FRÒG'VS°¢Ğ¢Ğ¢&WGW&âG'VS°¢Ğ ¢&—fFRfö–B6fU7V66W76gVÄ÷WGWE6WGF–æw2‡7G&–ærF—&V7F÷'’¢°¢G'¢°¢÷6WGF–æw2äÆ7D÷WGWDF—&V7F÷'’Ò7G&–ærä—4çVÆÄ÷%v†—FU76R†F—&V7F÷'’’òVçf—&öæÖVçBävWDföÆFW%F‚„Vçf—&öæÖVçBå7V6–ÄföÆFW"ä×”Fö7VÖVçG2’¢F—&V7F÷'“°¢÷6WGF–æw2äÆ7EF&vWDÖöFRÒ÷F&vWDÖöFS°¢6WGF–æw57F÷&Rå6fR…÷6WGF–æw2“°¢Ğ¢6F6‚²Ğ¢Ğ ¢&—fFRfö–B6WDW‡÷'E7FFR†&ööÂW‡÷'F–ærÂ7G&–ær7FGW2¢°¢öW‡÷'D'WGFöâäVæ&ÆVBÒW‡÷'F–æs°¢ö6æ6VÄ'WGFöâåf—6–&ÆRÒW‡÷'F–æs°¢÷7FGW4Æ&VÂåFW‡BÒ7FGW3°¢W6Uv—D7W'6÷"ÒW‡÷'F–æs°¢–b‚W‡÷'F–ær¢°¢W6Uv—D7W'6÷"ÒfÇ6S°¢–b…öW‡÷'D6æ6VÆÆF–öâÒçVÆÂ¢°¢öW‡÷'D6æ6VÆÆF–öâäF—7÷6R‚“°¢öW‡÷'D6æ6VÆÆF–öâÒçVÆÃ°¢Ğ¢Ğ¢Ğ ¢&—fFRW%6—¦T¶–æBvWEW%6—¦R‚¢°¢&WGW&â…W%6—¦T¶–æB”ÖF‚äÖ‚ƒÂÖF‚äÖ–â…W%6—¦W2äF—7Æ”æÖW2äÆVæwF‚ÒÂ÷W$6öÖ&òå6VÆV7FVD–æFW‚’“°¢Ğ ¢&—fFR–çBvWDÖ&v–äÖÒ‚¢°¢&WGW&âöÖ&v–ä6öÖ&òå6VÆV7FVD–æFW‚ÓÒò¢…öÖ&v–ä6öÖ&òå6VÆV7FVD–æFW‚ÓÒòR¢“°¢Ğ ¢&—fFR7G&–ærvWDÖW&vT&6TæÖR‚¢°¢7G&–ærfÇVRÒöÖW&vTæÖT&÷‚ÓÒçVÆÂò7G&–æräV×G’¢öÖW&vTæÖT&÷‚åFW‡BåG&–Ò‚“°¢–b‡fÇVRäVæG5v—F‚‚"çFb"Â7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R’’fÇVRÒfÇVRå7V'7G&–ærƒÂfÇVRäÆVæwF‚ÒB“°¢&WGW&â7G&–ærä—4çVÆÄ÷%v†—FU76R‡fÇVR’òFVfVÇDÖW&vTæÖR‚’¢fÇVS°¢Ğ ¢&—fFR7FF–27G&–ærFVfVÇDÖW&vTæÖR‚¢°¢&WGW&â.Y»îx˜~Y[›eò"²FFUF–ÖRäæ÷råFõ7G&–ær‚'———”ÔÖFEô„†ÖÒ"“°¢Ğ ¢&—fFR7FF–27G&–ærVç7W&UFdW‡FVç6–öâ‡7G&–ærfÇVR¢°¢&WGW&âfÇVRäVæG5v—F‚‚"çFb"Â7G&–æt6ö×&—6öâä÷&F–æÄ–væ÷&T66R’òfÇVR¢fÇVR²"çFb#°¢Ğ ¢&—fFR7FF–27G&–ær6fTF—&V7F÷'”æÖR‡7G&–ærfÇVR¢°¢G'’²&WGW&âF‚ävWDF—&V7F÷'”æÖR‡fÇVR“²Ğ¢6F6‚²&WGW&âçVÆÃ²Ğ¢Ğ ¢&—fFR7FF–2fö–BvWEvU—†VÇ2…W%6—¦T¶–æBW"ÂvT÷&–VçFF–öâ÷&–VçFF–öâÂ–çBÆöæu6–FRÂ÷WB–çBv–GF‚Â÷WB–çB†V–v‡B¢°¢fÆöBW%v–GF‚ÒW%6—¦W2ävWEv–GF„ÖÒ‡W"“°¢fÆöBW$†V–v‡BÒW%6—¦W2ävWD†V–v‡DÖÒ‡W"“°¢–b†÷&–VçFF–öâÓÒvT÷&–VçFF–öâäÆæG66R¢°¢fÆöB7vÒW%v–GFƒ°¢W%v–GF‚ÒW$†V–v‡C°¢W$†V–v‡BÒ7v°¢Ğ¢–b‡W%v–GF‚ãÒW$†V–v‡B¢°¢v–GF‚ÒÆöæu6–FS°¢†V–v‡BÒÖF‚äÖ‚ƒÂ†–çB”ÖF‚å&÷VæB†Æöæu6–FR¢W$†V–v‡BòW%v–GF‚’“°¢Ğ¢VÇ6P¢°¢†V–v‡BÒÆöæu6–FS°¢v–GF‚ÒÖF‚äÖ‚ƒÂ†–çB”ÖF‚å&÷VæB†Æöæu6–FR¢W%v–GF‚òW$†V–v‡B’“°¢Ğ¢Ğ ¢&—fFRfö–BWFFT6÷VçB‚¢°¢–b…ö6÷VçDÆ&VÂÒçVÆÂ’ö6÷VçDÆ&VÂåFW‡BÒ.X["²ö—FV×2ä6÷VçBåFõ7G&–ær‚’²"šR#°¢–b…÷7FGW4Æ&VÂÒçVÆÂbböW‡÷'D6æ6VÆÆF–öâÓÒçVÆÂ¢÷7FGW4Æ&VÂåFW‡BÒö—FV×2ä6÷VçBÓÒò.h¹nXZ^Y»îx˜~[ÈZx¾‹ÚÎhÚ""¢.[{.XxnZHr"²ö—FV×2ä6÷VçBåFõ7G&–ær‚’²"[ÊY»îx˜r#°¢Ğ ¢&—fFRfö–BVæ&ÆTG&÷&V7W'6—fR„6öçG&öÂ6öçG&öÂ¢°¢–b†6öçG&öÂÓÒçVÆÂ’&WGW&ã°¢6öçG&öÂäÆÆ÷tG&÷ÒG'VS°¢–b†6öçG&öÂÒF†—2bb6öçG&öÂÒö6&G2¢°¢6öçG&öÂäG&tVçFW"³Ò†æFÆTG&tVçFW#°¢6öçG&öÂäG&t÷fW"³Ò†æFÆTG&t÷fW#°¢6öçG&öÂäG&tG&÷³Ò†æFÆTG&tG&÷°¢Ğ¢f÷&V6‚„6öçG&öÂ6†–ÆB–â6öçG&öÂä6öçG&öÇ2’Væ&ÆTG&÷&V7W'6—fR†6†–ÆB“°¢Ğ ¢&—fFRfö–B†æFÆTG&tVçFW"†ö&¦V7B6VæFW"ÂG&tWfVçD&w2R¢°¢–b†RäFFävWDFF&W6VçB„FFf÷&ÖG2äf–ÆTG&÷’’RäVffV7BÒG&tG&÷VffV7G2ä6÷“°¢VÇ6R–b†RäFFävWDFF&W6VçB‡G—Vöb„–ÖvT—FVÒ’’’RäVffV7BÒG&tG&÷VffV7G2äÖ÷fS°¢VÇ6RRäVffV7BÒG&tG&÷VffV7G2äæöæS°¢Ğ ¢&—fFRfö–B†æFÆTG&t÷fW"†ö&¦V7B6VæFW"ÂG&tWfVçD&w2R¢°¢†æFÆTG&tVçFW"‡6VæFW"ÂR“°¢Ğ ¢&—fFRfö–B†æFÆTG&tG&÷†ö&¦V7B6VæFW"ÂG&tWfVçD&w2R¢°¢7G&–æuµÒF‡2ÒRäFFävWDFF„FFf÷&ÖG2äf–ÆTG&÷’27G&–æuµÓ°¢–b‡F‡2ÒçVÆÂ’FD–çWG2‡F‡2“°¢VÇ6R–b†RäFFävWDFF&W6VçB‡G—Vöb„–ÖvT—FVÒ’’’6&G4G&tG&÷…ö6&G2ÂR“°¢Ğ ¢&—fFRfö–B6&G4G&tG&÷†ö&¦V7B6VæFW"ÂG&tWfVçD&w2R¢°¢7G&–æuµÒF‡2ÒRäFFävWDFF„FFf÷&ÖG2äf–ÆTG&÷’27G&–æuµÓ°¢–b‡F‡2ÒçVÆÂ¢°¢FD–çWG2‡F‡2“°¢&WGW&ã°¢Ğ¢–ÖvT—FVÒ—FVÒÒRäFFävWDFF‡G—Vöb„–ÖvT—FVÒ’’2–ÖvT—FVÓ°¢–b†—FVÒÓÒçVÆÂ’&WGW&ã°¢ö–çBÆö6F–öâÒö6&G2åö–çEFô6Æ–VçB†æWrö–çB†Rå‚ÂRå’’“°¢–çBF&vWD–æFW‚Òö—FV×2ä6÷VçC°¢f÷"†–çB–æFW‚Ò²–æFW‚Âö6&G2ä6öçG&öÇ2ä6÷VçC²–æFW‚²²¢°¢–b…ö6&G2ä6öçG&öÇ5¶–æFW…Òä&÷VæG2ä6öçF–ç2†Æö6F–öâ’’²F&vWD–æFW‚Ò–æFWƒ²'&V³²Ğ¢Ğ¢–çB6÷W&6T–æFW‚Òö—FV×2ä–æFW„öb†—FVÒ“°¢–b‡6÷W&6T–æFW‚Â’&WGW&ã°¢ö—FV×2å&VÖ÷fTB‡6÷W&6T–æFW‚“°¢–b‡6÷W&6T–æFW‚ÂF&vWD–æFW‚’F&vWD–æFW‚ÒÓ°¢F&vWD–æFW‚ÒÖF‚äÖ‚ƒÂÖF‚äÖ–â‡F&vWD–æFW‚Âö—FV×2ä6÷VçB’“°¢ö—FV×2ä–ç6W'B‡F&vWD–æFW‚Â—FVÒ“°¢&V'V–ÆD6&D6öçG&öÇ2‚“°¢Ğ ¢&÷FV7FVB÷fW'&–FRfö–Böäf÷&Ô6Æ÷6–ær„f÷&Ô6Æ÷6–ætWfVçD&w2R¢°¢–b…öW‡÷'D6æ6VÆÆF–öâÒçVÆÂ’öW‡÷'D6æ6VÆÆF–öâä6æ6VÂ‚“°¢6æ6VÅ&Wf–WuVWVR‚“°¢f÷&V6‚„ÖöFW&ä–ÖvT6&B6&B–âö6&DÖåfÇVW2’6&Bå&VÆV6U&Wf–Wu&VfW&Væ6R‚“°¢f÷&V6‚„–ÖvT—FVÒ—FVÒ–âö—FV×2’—FVÒäF—7÷6U&Wf–Wr‚“°¢&6Räöäf÷&Ô6Æ÷6–ær†R“°¢Ğ¢Ğ§Ğ 