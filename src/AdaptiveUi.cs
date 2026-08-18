using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LocalImageToPdf
{
    /// <summary>
    /// Common high-DPI and display-change handling for every application window.
    /// Coordinates in the existing UI are authored at 96 DPI. WinForms scales them
    /// once, while responsive layout code uses logical (96-DPI) measurements.
    /// </summary>
    internal abstract class AdaptiveForm : Form
    {
        private const int DesignDpi = 96;
        private const int WmDpiChanged = 0x02E0;
        private bool _displayEventsAttached;
        private bool _applyingAdaptiveLayout;
        private bool _adaptiveLayoutPending;
        private int _layoutDpiOverride;

        protected AdaptiveForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(DesignDpi, DesignDpi);
        }

        protected virtual Size MinimumLogicalSize
        {
            get { return new Size(520, 380); }
        }

        protected int CurrentDpi
        {
            get
            {
                if (_layoutDpiOverride >= DesignDpi) return _layoutDpiOverride;
                if (IsHandleCreated)
                {
                    try
                    {
                        uint dpi = GetDpiForWindow(Handle);
                        if (dpi > 0) return (int)dpi;
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

        protected int LogicalClientWidth
        {
            get { return ToLogical(ClientSize.Width); }
        }

        protected int LogicalClientHeight
        {
            get { return ToLogical(ClientSize.Height); }
        }

        protected int ScaleLogical(int value)
        {
            return Math.Max(0, (int)Math.Round(value * CurrentDpi / (float)DesignDpi));
        }

        protected int ToLogical(int value)
        {
            return Math.Max(0, (int)Math.Round(value * DesignDpi / (float)Math.Max(DesignDpi, CurrentDpi)));
        }

        internal void SetLayoutDpiForTesting(int dpi)
        {
            _layoutDpiOverride = dpi >= DesignDpi ? dpi : 0;
        }

        protected virtual void ApplyAdaptiveLayout()
        {
        }

        protected void RefreshAdaptiveLayout()
        {
            if (_applyingAdaptiveLayout || IsDisposed || Disposing) return;
            _applyingAdaptiveLayout = true;
            try
            {
                SuspendLayout();
                ApplyAdaptiveLayout();
            }
            finally
            {
                ResumeLayout(true);
                _applyingAdaptiveLayout = false;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!_displayEventsAttached)
            {
                try
                {
                    SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
                    SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
                    _displayEventsAttached = true;
                }
                catch { }
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_displayEventsAttached)
            {
                try
                {
                    SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
                    SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
                }
                catch { }
                _displayEventsAttached = false;
            }
            base.OnHandleDestroyed(e);
        }

        protected override void OnLoad(EventArgs e)
        {
            FitToWorkingArea(true);
            RefreshAdaptiveLayout();
            base.OnLoad(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState != FormWindowState.Minimized) RefreshAdaptiveLayout();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WmDpiChanged) QueueDisplayRefresh();
        }

        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            QueueDisplayRefresh();
        }

        private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Window ||
                e.Category == UserPreferenceCategory.General ||
                e.Category == UserPreferenceCategory.Accessibility)
                QueueDisplayRefresh();
        }

        private void QueueDisplayRefresh()
        {
            if (_adaptiveLayoutPending || IsDisposed || Disposing || !IsHandleCreated) return;
            _adaptiveLayoutPending = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    _adaptiveLayoutPending = false;
                    if (IsDisposed || Disposing) return;
                    FitToWorkingArea(false);
                    RefreshAdaptiveLayout();
                });
            }
            catch
            {
                _adaptiveLayoutPending = false;
            }
        }

        private void FitToWorkingArea(bool centerIfOversized)
        {
            if (!TopLevel || !IsHandleCreated || WindowState != FormWindowState.Normal) return;

            Rectangle workArea = Screen.FromControl(this).WorkingArea;
            int margin = Math.Max(8, ScaleLogical(12));
            int maximumWidth = Math.Max(320, workArea.Width - margin * 2);
            int maximumHeight = Math.Max(260, workArea.Height - margin * 2);

            Size logicalMinimum = MinimumLogicalSize;
            MinimumSize = new Size(
                Math.Min(ScaleLogical(logicalMinimum.Width), maximumWidth),
                Math.Min(ScaleLogical(logicalMinimum.Height), maximumHeight));

            int width = Math.Min(Math.Max(Width, MinimumSize.Width), maximumWidth);
            int height = Math.Min(Math.Max(Height, MinimumSize.Height), maximumHeight);
            int left = Left;
            int top = Top;
            bool outside = Right <= workArea.Left || Left >= workArea.Right ||
                           Bottom <= workArea.Top || Top >= workArea.Bottom;
            bool oversized = Width > maximumWidth || Height > maximumHeight;

            if (outside || (centerIfOversized && oversized))
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
