#if WINDOWS

using Microsoft.Xna.Framework;
using SDL2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Olympus.NativeImpls {
	public unsafe partial class NativeWin32 : NativeImpl {
		private class Win10BackgroundForm : Form {

            NativeWin32 Native;
            public IntPtr FriendlyHandle;
            public uint ThreadID;
            bool Acrylic;

            RECT RectPrev;

            new bool Shown = true;

            protected override CreateParams CreateParams {
                get {
                    CreateParams args = base.CreateParams;
                    args.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                    return args;
                }
            }

            public Win10BackgroundForm(NativeWin32 native, string title, bool acrylic) {
                Native = native;
                FriendlyHandle = Handle;
                ThreadID = GetCurrentThreadId();

                base.Text = title;

                ShowInTaskbar = false;
                ControlBox = false;
                BackColor = System.Drawing.Color.FromArgb(255, 0, 0, 0);

                Acrylic = acrylic;
                if (acrylic) {
                    MARGINS margins = new() {
                        Left = 0,
                        Right = 0,
                        Top = 1,
                        Bottom = 0,
                    };
                    DwmExtendFrameIntoClientArea(Handle, ref margins);

                    int policy = 1;
                    DwmSetWindowAttribute(Handle, DwmWindowAttribute.DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));

                } else {
                    // Might need further tweaks.
                    FormBorderStyle = FormBorderStyle.None;

                    SetWindowLongPtr(Handle, /* GWL_EXSTYLE */ -20, (IntPtr) ((long) GetWindowLongPtr(Handle, -20) | /* WS_EX_LAYERED */ 0x80000));
                    SetLayeredWindowAttributes(Handle, 0x00000000, 0xFF, /* LWA_ALPHA */ 0x2);

                    MARGINS margins = new() {
                        Left = 0,
                        Right = 0,
                        Top = 1,
                        Bottom = 0,
                    };
                    DwmExtendFrameIntoClientArea(Handle, ref margins);

                    DWM_BLURBEHIND blurBehind = new() {
                        dwFlags = /* DWM_BB_ENABLE | DWM_BB_BLUREGION */ 0x1 | 0x2,
                        fEnable = true,
                        hRgnBlur = InvisibleRegion,
                        fTransitionOnMaximized = false
                    };
                    DwmEnableBlurBehindWindow(Handle, ref blurBehind);

                    int policy = 0;
                    DwmSetWindowAttribute(Handle, DwmWindowAttribute.DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));

                    SendMessage(Handle, (int) WindowsMessage.WM_SETREDRAW, (IntPtr) 0, NULL);
                }
            }

            public void Fix(bool force, int left, int top, int right, int bottom) {
                RECT rectWindow = Native.LastWindowRect;
                RECT rectClient = Native.LastClientRect;
                RECT rect = new() {
                    Left = rectWindow.Left + rectClient.Left + Native.OffsetLeft + left,
                    Top = rectWindow.Top + rectClient.Top + top,
                    Right = rectWindow.Left + rectClient.Right + Native.OffsetLeft + right,
                    Bottom = rectWindow.Top + rectClient.Bottom + bottom,
                };
                if (!force &&
                    rect.Left == RectPrev.Left && rect.Top == RectPrev.Top &&
                    rect.Right == RectPrev.Right && rect.Bottom == RectPrev.Bottom &&
                    GetWindow(Native.HWnd, /* GW_HWNDNEXT */ 2) == Handle)
                    return;

                if (rectWindow.Left == -32000 && rectWindow.Top == -32000 &&
                    rectWindow.Right == -32000 && rectWindow.Bottom == -32000) {
                    if (Shown) {
                        Shown = false;
                        Hide();
                    }
                } else if (!Shown) {
                    Shown = true;
                    Show();
                }

                RectPrev = rect;
                SetWindowPos(
                    Handle, Native.HWnd,
                    rect.Left, rect.Top,
                    rect.Right - rect.Left, rect.Bottom - rect.Top,
                    /* SWP_NOACTIVATE | SWP_DEFERERASE */ 0x0010 | 0x2000
                );

                if (!Acrylic) {
                    // FIXME: Delete the old region? According to MSDN, Windows does that for us.
                    IntPtr rgnResize = CreateRectRgn(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top);
                    IntPtr rgnClient = CreateRectRgn(8, 8, rect.Right - rect.Left - 8, rect.Bottom - rect.Top - 8);
                    CombineRgn(rgnResize, rgnResize, rgnClient, CombineRgnStyles.RGN_XOR);
                    SetWindowRgn(Handle, rgnResize, false);
                    DeleteObject(rgnClient);

                    PostMessage(
                        Native.HWnd, (int) WindowsMessage.WM_USER_ADE_MOVE_AFTER,
                        Handle,
                        (IntPtr) (/* SWP_NOSIZE | SWP_NOMOVE | SWP_NOREDRAW | SWP_NOACTIVATE | SWP_DEFERERASE | SWP_NOSENDCHANGING */ 0x0001 | 0x0002 | 0x0008 | 0x0010 | 0x2000 | 0x0400)
                    );
                }
            }

            protected override void WndProc(ref Message m) {
                // AppLogger.Log.Debug($"{Handle}, {(WindowsMessage) m.Msg}: {m.WParam}, {m.LParam}");

                switch ((WindowsMessage) m.Msg) {
                    case WindowsMessage.WM_NCCALCSIZE:
                        if (Acrylic) {
                            // On Windows 10, the background blur extends into the non-client area.
                            // We thus MUST return the entire proposed area as the client area.
                            if (m.WParam == ONE) {
                                m.Result = NULL;
                                return;
                            }
                        }
                        break;

                    case WindowsMessage.WM_NCHITTEST:
                        if (Acrylic) {
                            m.Result = (IntPtr) HitTestValues.HTNOWHERE;
                            return;
                        }

                        base.WndProc(ref m);
                        switch ((HitTestValues) m.Result) {
                            case HitTestValues.HTCAPTION:
                            case HitTestValues.HTSYSMENU:
                            case HitTestValues.HTGROWBOX:
                            case HitTestValues.HTMENU:
                                m.Result = (IntPtr) HitTestValues.HTNOWHERE;
                                break;

                            case HitTestValues.HTCLIENT:
                                POINT pointReal = new() {
                                    X = (short) (((ulong) m.LParam >> 0) & 0xFFFF),
                                    Y = (short) (((ulong) m.LParam >> 16) & 0xFFFF),
                                };
                                POINT point = pointReal;
                                const int border = 8;
                                if (GetClientRect(Handle, out RECT rect) && ScreenToClient(Handle, ref point)) {
                                    if (point.Y < border * 2) {
                                        if (point.X < border * 2) {
                                            m.Result = (IntPtr) HitTestValues.HTTOPLEFT;
                                            break;
                                        }
                                        if (point.X >= rect.Right - border * 2) {
                                            m.Result = (IntPtr) HitTestValues.HTTOPRIGHT;
                                            break;
                                        }
                                        if (point.Y < border) {
                                            m.Result = (IntPtr) HitTestValues.HTTOP;
                                            break;
                                        }
                                    }
                                    if (point.Y >= rect.Bottom - border * 2) {
                                        if (point.X < border * 2) {
                                            m.Result = (IntPtr) HitTestValues.HTBOTTOMLEFT;
                                            break;
                                        }
                                        if (point.X >= rect.Right - border * 2) {
                                            m.Result = (IntPtr) HitTestValues.HTBOTTOMRIGHT;
                                            break;
                                        }
                                        if (point.Y <= rect.Bottom - border) {
                                            m.Result = (IntPtr) HitTestValues.HTBOTTOM;
                                            break;
                                        }
                                    }
                                    if (point.X < border) {
                                        m.Result = (IntPtr) HitTestValues.HTLEFT;
                                        break;
                                    }
                                    if (point.X >= rect.Right - border) {
                                        m.Result = (IntPtr) HitTestValues.HTRIGHT;
                                        break;
                                    }
                                }
                                break;
                        }
                        return;

                    case WindowsMessage.WM_NCPAINT:
                        NCPaint(Handle, m.WParam, System.Drawing.Color.Transparent);
                        return;

                    case WindowsMessage.WM_SIZING:
                        // TODO.
                        break;

                    case WindowsMessage.WM_ACTIVATE:
                        // TODO: This likes to fire quite often.
                        base.WndProc(ref m);
                        PostMessage(
                            Native.HWnd, (int) WindowsMessage.WM_USER_ADE_MOVE_AFTER,
                            Handle,
                            (IntPtr) (/* SWP_NOSIZE | SWP_NOMOVE | SWP_NOREDRAW | SWP_NOACTIVATE | SWP_DEFERERASE | SWP_NOSENDCHANGING */ 0x0001 | 0x0002 | 0x0008 | 0x0010 | 0x2000 | 0x0400)
                        );
                        return;

                    case WindowsMessage.WM_USER_ADE_CALL_FIX:
                        Fix(
                            m.WParam != NULL,
                            unchecked((sbyte) (byte) (((ulong) m.LParam) >> 0)),
                            unchecked((sbyte) (byte) (((ulong) m.LParam) >> 8)),
                            unchecked((sbyte) (byte) (((ulong) m.LParam) >> 16)),
                            unchecked((sbyte) (byte) (((ulong) m.LParam) >> 24))
                        );
                        m.Result = NULL;
                        return;
                }

                base.WndProc(ref m);
            }

            public void Invoke(Action<Win10BackgroundForm> cb)
                => base.Invoke(new Action(() => cb(this)));

            public IAsyncResult InvokeBG(Action<Win10BackgroundForm> cb)
                => base.BeginInvoke(new Action(() => cb(this)));

        }
	}
}

#endif
