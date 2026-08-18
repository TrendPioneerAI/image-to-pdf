using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LocalImageToPdf
{
    /// <summary>
    /// Applies Windows DPI scaling and keeps the top-level converter window inside
    /// the active monitor. It deliberately does not rearrange child controls, so
    /// the v1.2.0 interface structure remains unchanged at every scale factor.
    /// </summary>
    internal abstract class DisplayAwareMainForm : Form
    {
        private const int DesignDpi = 96;
        private const int WmDpiChanged = 0x02E0;
        private bool _displayEventAttached;
        private bool _fitPending;
        private int _testDpi;

        protected DisplayAwareMainForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(DesignDpi, DesignDpi);
        }

        protected abstract Size MinimumLogicalWindowSize { get; }

        protected int CurrentDpi
        {
            get
            {
                if (_testDpi >= DesignDpi) return _testDpi;
                if (IsHandleCreated)
                {
                    try
                    {
                        uint value = GetDpiForWindow(Handle);
                        if (value >= DesignDpi) return (int)value;
                    }
                    catch (EntryPointNotFoundException) { }
                    catch (DllNotFoundException) { }
                }
                try
                {
                    using (Graphics graphics = CreateGraphics())
                        return Math.Max(DesignDpi, (int)Math.Round(graphics.DpiX));
                }
                catch
                {
                    return DesignDpi;
                }
            }
        }

        protected int ScaleLogical(int value)
        {
            return Math.Max(0, (int)Math.Round(value * CurrentDpi / (float)DesignDpi));
        }

        internal void SetDpiForLayoutTesting(int dpi)
        {
            _testDpi = dpi >= DesignDpi ? dpi : 0;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_displayEventAttached) return;
            try
            {
                SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
                _displayEventAttached = true;
            }
            catch { }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_displayEventAttached)
            {
                try { SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged; }
                catch { }
                _displayEventAttached = false;
            }
            base.OnHandleDestroyed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitMainWindowToWorkingArea(true);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WmDpiChanged) QueueWindowFit();
        }

        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            QueueWindowFit();
        }

        private void QueueWindowFit()
        {
            if (_fitPending || IsDisposed || Disposing || !IsHandleCreated || !TopLevel) return;
            _fitPending = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    _fitPending = false;
                    if (!IsDisposed && !Disposing) FitMainWindowToWorkingArea(false);
                });
            }
            catch
            {
                _fitPending = false;
            }
        }

        private void FitMainWindowToWorkingArea(bool centerIfReduced)
        {
            if (!TopLevel || !IsHandleCreated || WindowState != FormWindowState.Normal) return;

            Rectangle workArea = Screen.FromHandle(Handle).WorkingArea;
            int margin = Math.Max(8, ScaleLogical(12));
            int maximumWidth = Math.Max(640, workArea.Width - margin * 2);
            int maximumHeight = Math.Max(440, workArea.Height - margin * 2);
            Size logicalMinimum = MinimumLogicalWindowSize;
            MinimumSize = new Size(
                Math.Min(ScaleLogical(logicalMinimum.Width), maximumWidth),
                Math.Min(ScaleLogical(logicalMinimum.Height), maximumHeight));

            int width = Math.Min(Math.Max(Width, MinimumSize.Width), maximumWidth);
            int height = Math.Min(Math.Max(Height, MinimumSize.Height), maximumHeight);
            bool reduced = width != Width || height != Height;
            bool outside = Right <= workArea.Left || Left >= workArea.Right ||
                           Bottom <= workArea.Top || Top >= workArea.Bottom;
            int left = Left;
            int top = Top;

            if (outside || (centerIfReduced && reduced))
            {
                left = workArea.Left + (workArea.Width - width) / 2;
                top = workArea.Top + (workArea.Height - height) / 2;
            }
            else
            {
                left = Math.Max(workArea.Left + margin, Math.Min(left, workArea.Right - margin - width));
                top = Math.Max(workArea.Top + margin, Math.Min(top, workArea.Bottom - margin - height));
            }

            Bounds = new Rectangle(left, top, width, height);
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);
    }
}
