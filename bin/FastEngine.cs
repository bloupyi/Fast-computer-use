using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Automation;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

namespace FastComputerUse {
    public class FastEngine {
        // Win32 API Imports
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        static extern uint TimeEndPeriod(uint uMilliseconds);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        const uint WM_CLOSE = 0x0010;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT {
            public uint type;
            public MOUSEKEYBDHARDWAREINPUT mkhi;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct MOUSEKEYBDHARDWAREINPUT {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        const uint INPUT_MOUSE = 0;
        const uint INPUT_KEYBOARD = 1;

        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        const uint MOUSEEVENTF_WHEEL = 0x0800;
        const uint MOUSEEVENTF_HWHEEL = 0x1000;

        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_UNICODE = 0x0004;
        const uint KEYEVENTF_SCANCODE = 0x0008;

        const int SW_RESTORE = 9;
        const int SW_MINIMIZE = 6;
        const int SW_MAXIMIZE = 3;

        static JavaScriptSerializer serializer = new JavaScriptSerializer();

        [STAThread]
        public static void Main(string[] args) {
            try {
                SetProcessDPIAware();
            } catch {}

            // Windows' default timer granularity is ~15.6 ms, so every Sleep(5) in an
            // input sequence really costs 15.6 ms. Typing 11 characters would burn
            // 170 ms of pure waiting. Asking for 1 ms resolution removes that tax.
            try {
                TimeBeginPeriod(1);
                AppDomain.CurrentDomain.ProcessExit += delegate { try { TimeEndPeriod(1); } catch {} };
            } catch {}

            serializer.MaxJsonLength = int.MaxValue;

            // Without this the daemon writes its JSON in the console codepage, so any
            // non-ASCII window title comes back mangled and can no longer be used as a
            // lookup key. Every non-English Windows hits this.
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch {}
            try { Console.InputEncoding = new UTF8Encoding(false); } catch {}

            if (args.Length > 0 && args[0] == "--daemon") {
                RunDaemon();
                return;
            }

            if (args.Length > 0) {
                string cmd = args[0];
                string payload = args.Length > 1 ? args[1] : "{}";
                Dictionary<string, object> request = new Dictionary<string, object>();
                try {
                    var deserialized = serializer.DeserializeObject(payload) as Dictionary<string, object>;
                    if (deserialized != null) request = deserialized;
                } catch {}
                request["cmd"] = cmd;
                var res = DispatchCommand(request);
                Console.WriteLine(serializer.Serialize(res));
                return;
            }

            Console.WriteLine("FastEngine v1.1.0. Usage: FastEngine.exe --daemon OR FastEngine.exe <cmd> '<json_payload>'");
        }

        static void RunDaemon() {
            Console.WriteLine("{\"ready\":true,\"version\":\"1.1.0\"}");
            Console.Out.Flush();

            string line;
            while ((line = Console.ReadLine()) != null) {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Trim() == "exit" || line.Trim() == "quit") break;

                Dictionary<string, object> request = null;
                try {
                    var obj = serializer.DeserializeObject(line) as Dictionary<string, object>;
                    if (obj != null) request = obj;
                } catch (Exception ex) {
                    var err = new Dictionary<string, object>();
                    err["status"] = "error";
                    err["message"] = "Invalid JSON: " + ex.Message;
                    Console.WriteLine(serializer.Serialize(err));
                    Console.Out.Flush();
                    continue;
                }

                if (request == null) continue;

                Stopwatch sw = Stopwatch.StartNew();
                var response = DispatchCommand(request);
                sw.Stop();
                response["elapsedMs"] = sw.ElapsedMilliseconds;

                Console.WriteLine(serializer.Serialize(response));
                Console.Out.Flush();
            }
        }

        static Dictionary<string, object> DispatchCommand(Dictionary<string, object> req) {
            var res = new Dictionary<string, object>();
            string cmd = "";
            if (req.ContainsKey("cmd") && req["cmd"] != null) {
                cmd = req["cmd"].ToString().ToLower();
            } else if (req.ContainsKey("type") && req["type"] != null) {
                cmd = req["type"].ToString().ToLower();
            }

            try {
                switch (cmd) {
                    case "ping":
                        res["status"] = "ok";
                        res["message"] = "pong";
                        break;

                    case "info":
                    case "screen_info":
                        return GetScreenInfo();

                    case "screenshot":
                    case "screen_capture":
                        if (ToBool(req, "panorama", false) || ToStr(req, "target", "").ToLower() == "panorama") {
                            return CapturePanorama(req);
                        }
                        return CaptureScreenshot(req);

                    case "panorama":
                    case "screen_panorama":
                        return CapturePanorama(req);

                    case "mouse":
                    case "mouse_action":
                        string mouseAct = req.ContainsKey("action") && req["action"] != null ? req["action"].ToString().ToLower() : "click";
                        if (mouseAct == "move" || mouseAct == "mouse_move") return MouseMove(req);
                        if (mouseAct == "drag" || mouseAct == "mouse_drag") return MouseDrag(req);
                        if (mouseAct == "scroll" || mouseAct == "mouse_scroll") return MouseScroll(req);
                        return MouseClick(req);

                    case "mouse_click":
                    case "click":
                        return MouseClick(req);

                    case "mouse_move":
                    case "move":
                        return MouseMove(req);

                    case "mouse_drag":
                    case "drag":
                        return MouseDrag(req);

                    case "mouse_scroll":
                    case "scroll":
                        return MouseScroll(req);

                    case "cursor_pos":
                    case "get_cursor":
                        POINT pt;
                        GetCursorPos(out pt);
                        res["status"] = "ok";
                        res["x"] = pt.X;
                        res["y"] = pt.Y;
                        break;

                    case "keyboard":
                    case "keyboard_action":
                        string kbAct = req.ContainsKey("action") && req["action"] != null ? req["action"].ToString().ToLower() : "type";
                        if (kbAct == "type_text" || kbAct == "type") {
                            req["mode"] = "type";
                            return KeyboardType(req);
                        }
                        if (kbAct == "paste_text" || kbAct == "paste") {
                            req["mode"] = "paste";
                            return KeyboardType(req);
                        }
                        if (kbAct == "hotkey") return KeyboardHotkey(req);
                        return KeyboardKey(req);

                    case "type":
                    case "keyboard_type":
                        return KeyboardType(req);

                    case "hotkey":
                    case "keyboard_hotkey":
                        return KeyboardHotkey(req);

                    case "key":
                    case "keyboard_key":
                        return KeyboardKey(req);

                    case "batch":
                    case "batch_actions":
                        return ExecuteBatch(req);

                    case "wait_for":
                    case "wait_until":
                        return WaitForCondition(req);

                    case "launch":
                    case "start_app":
                    case "app_launch":
                        return LaunchApp(req);

                    case "read_text":
                    case "get_text":
                        return ReadText(req);

                    case "wait":
                    case "sleep":
                        PreciseSleep(ToInt(req, "ms", 100));
                        res["status"] = "ok";
                        res["action"] = "wait";
                        res["ms"] = ToInt(req, "ms", 100);
                        break;

                    case "window_list":
                    case "windows":
                        return ListWindows();

                    case "window_focus":
                    case "focus":
                        return FocusWindow(req);

                    case "window_state":
                        // tools.mjs sends the target state as "state" (minimize/maximize/restore),
                        // not "action" — map it so FocusWindow's action switch picks it up.
                        if (req.ContainsKey("state") && req["state"] != null) req["action"] = req["state"];
                        return FocusWindow(req);

                    case "window_close":
                        req["action"] = "close";
                        return FocusWindow(req);

                    case "window_pos":
                        return SetWindowPosCommand(req);

                    case "ui_inspect":
                    case "inspect":
                        return InspectUI(req);

                    case "ui_click":
                        return ClickUIElement(req);

                    default:
                        res["status"] = "error";
                        res["message"] = "Unknown command: " + cmd;
                        break;
                }
            } catch (Exception ex) {
                res["status"] = "error";
                res["message"] = ex.Message;
                res["stack"] = ex.StackTrace;
            }

            return res;
        }

        static Dictionary<string, object> GetScreenInfo() {
            var res = new Dictionary<string, object>();
            res["status"] = "ok";

            POINT pt;
            GetCursorPos(out pt);
            res["cursor"] = new Dictionary<string, object> { { "x", pt.X }, { "y", pt.Y } };

            var monitors = new List<Dictionary<string, object>>();
            for (int i = 0; i < Screen.AllScreens.Length; i++) {
                var s = Screen.AllScreens[i];
                var m = new Dictionary<string, object>();
                m["index"] = i;
                m["primary"] = s.Primary;
                m["device"] = s.DeviceName;
                m["bounds"] = new Dictionary<string, object> {
                    { "x", s.Bounds.X }, { "y", s.Bounds.Y },
                    { "width", s.Bounds.Width }, { "height", s.Bounds.Height }
                };
                monitors.Add(m);
            }
            res["monitors"] = monitors;

            var vs = SystemInformation.VirtualScreen;
            res["virtualScreen"] = new Dictionary<string, object> {
                { "x", vs.X }, { "y", vs.Y },
                { "width", vs.Width }, { "height", vs.Height }
            };

            IntPtr fgHwnd = GetForegroundWindow();
            if (fgHwnd != IntPtr.Zero) {
                StringBuilder sb = new StringBuilder(512);
                GetWindowText(fgHwnd, sb, 512);
                RECT rect;
                GetWindowRect(fgHwnd, out rect);
                uint pid;
                GetWindowThreadProcessId(fgHwnd, out pid);
                string procName = "";
                try {
                    procName = Process.GetProcessById((int)pid).ProcessName;
                } catch {}

                res["activeWindow"] = new Dictionary<string, object> {
                    { "hwnd", fgHwnd.ToInt64() },
                    { "title", sb.ToString() },
                    { "processName", procName },
                    { "pid", pid },
                    { "rect", new Dictionary<string, object> {
                        { "x", rect.Left }, { "y", rect.Top },
                        { "width", rect.Right - rect.Left }, { "height", rect.Bottom - rect.Top }
                    }}
                };
            }

            return res;
        }

        static Dictionary<string, object> CaptureScreenshot(Dictionary<string, object> req) {
            var res = new Dictionary<string, object>();
            string target = req.ContainsKey("target") && req["target"] != null ? req["target"].ToString().ToLower() : "fullscreen";
            // Accept both the MCP layer field name (screenIndex) and the legacy name (monitorIndex)
            int monitorIndex = -1;
            if (req.ContainsKey("screenIndex")) monitorIndex = Convert.ToInt32(req["screenIndex"]);
            else if (req.ContainsKey("monitorIndex")) monitorIndex = Convert.ToInt32(req["monitorIndex"]);
            bool targetActiveWindow = req.ContainsKey("targetActiveWindow") && Convert.ToBoolean(req["targetActiveWindow"]);
            int maxDim = req.ContainsKey("maxDimension") ? Convert.ToInt32(req["maxDimension"]) : 0;
            string format = req.ContainsKey("format") && req["format"] != null ? req["format"].ToString().ToLower() : "jpeg";
            int quality = req.ContainsKey("quality") ? Convert.ToInt32(req["quality"]) : 85;
            string savePath = req.ContainsKey("savePath") && req["savePath"] != null ? req["savePath"].ToString() : null;
            bool returnBase64 = !req.ContainsKey("returnBase64") || Convert.ToBoolean(req["returnBase64"]);

            Rectangle srcRect;

            if (targetActiveWindow || target == "active_window" || target == "window") {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) fg = Process.GetCurrentProcess().MainWindowHandle;
                RECT r;
                GetWindowRect(fg, out r);
                srcRect = new Rectangle(r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top));
            } else if (req.ContainsKey("roi")) {
                // ROI takes priority over the default screenIndex (which the MCP layer always sends, defaulting to 0)
                var roi = req["roi"] as Dictionary<string, object>;
                if (roi != null && roi.ContainsKey("x") && roi.ContainsKey("y") && roi.ContainsKey("width") && roi.ContainsKey("height")) {
                    srcRect = new Rectangle(
                        Convert.ToInt32(roi["x"]), Convert.ToInt32(roi["y"]),
                        Convert.ToInt32(roi["width"]), Convert.ToInt32(roi["height"]));
                } else {
                    srcRect = Screen.PrimaryScreen.Bounds;
                }
            } else if (monitorIndex >= 0 && monitorIndex < Screen.AllScreens.Length) {
                srcRect = Screen.AllScreens[monitorIndex].Bounds;
            } else if (target == "all" || target == "all_monitors" || target == "virtual_screen") {
                srcRect = SystemInformation.VirtualScreen;
            } else if (monitorIndex == -1 && (req.ContainsKey("screenIndex") || req.ContainsKey("monitorIndex"))) {
                // Explicit screen_index: -1 = combined virtual desktop canvas across all screens
                srcRect = SystemInformation.VirtualScreen;
            } else {
                srcRect = Screen.PrimaryScreen.Bounds;
            }

            using (Bitmap rawBmp = new Bitmap(srcRect.Width, srcRect.Height, PixelFormat.Format32bppArgb)) {
                using (Graphics g = Graphics.FromImage(rawBmp)) {
                    g.CopyFromScreen(srcRect.Location, Point.Empty, srcRect.Size, CopyPixelOperation.SourceCopy);
                }

                Bitmap finalBmp = rawBmp;
                bool needsDispose = false;

                if (maxDim > 0 && (srcRect.Width > maxDim || srcRect.Height > maxDim)) {
                    double scale = Math.Min((double)maxDim / srcRect.Width, (double)maxDim / srcRect.Height);
                    int newW = Math.Max(1, (int)(srcRect.Width * scale));
                    int newH = Math.Max(1, (int)(srcRect.Height * scale));
                    finalBmp = new Bitmap(newW, newH);
                    using (Graphics g = Graphics.FromImage(finalBmp)) {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(rawBmp, 0, 0, newW, newH);
                    }
                    needsDispose = true;
                }

                if (string.IsNullOrEmpty(savePath)) {
                    string ext = format == "png" ? ".png" : ".jpg";
                    savePath = Path.Combine(Path.GetTempPath(), "fast_cu_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
                }

                ImageCodecInfo encoder = GetEncoder(format == "png" ? ImageFormat.Png : ImageFormat.Jpeg);
                EncoderParameters encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);

                finalBmp.Save(savePath, encoder, encoderParams);

                res["status"] = "ok";
                res["path"] = savePath;
                res["width"] = finalBmp.Width;
                res["height"] = finalBmp.Height;
                res["sourceRect"] = new Dictionary<string, object> {
                    { "x", srcRect.X }, { "y", srcRect.Y }, { "width", srcRect.Width }, { "height", srcRect.Height }
                };

                if (returnBase64) {
                    byte[] bytes = File.ReadAllBytes(savePath);
                    res["base64"] = Convert.ToBase64String(bytes);
                    res["mimeType"] = format == "png" ? "image/png" : "image/jpeg";
                }

                if (needsDispose) finalBmp.Dispose();
            }

            return res;
        }

        // Unlike a raw virtual-desktop grab, the downscale budget applies PER MONITOR,
        // so nothing is squashed into an illegible strip. Each panel carries the map
        // back to real desktop coordinates.
        static Dictionary<string, object> CapturePanorama(Dictionary<string, object> req) {
            var res = new Dictionary<string, object>();
            res["action"] = "panorama";

            int maxDim = ToInt(req, "maxDimension", ToInt(req, "max_dimension", 1280));
            string format = ToStr(req, "format", "jpeg").ToLower();
            int quality = ToInt(req, "quality", 80);
            string savePath = ToStr(req, "savePath", ToStr(req, "save_path", ""));
            bool returnBase64 = !req.ContainsKey("returnBase64") || ToBool(req, "returnBase64", true);
            bool labels = ToBool(req, "labels", true);
            int gap = ToInt(req, "gap", 8);

            // Left-to-right, top-to-bottom: the order a person sees their desks in.
            var screens = new List<Screen>(Screen.AllScreens);
            screens.Sort(delegate(Screen a, Screen b) {
                if (a.Bounds.X != b.Bounds.X) return a.Bounds.X.CompareTo(b.Bounds.X);
                return a.Bounds.Y.CompareTo(b.Bounds.Y);
            });

            var only = new List<int>();
            if (req.ContainsKey("screens") && req["screens"] is IEnumerable && !(req["screens"] is string)) {
                foreach (var v in (IEnumerable)req["screens"]) {
                    try { only.Add(Convert.ToInt32(v)); } catch {}
                }
            }

            var panels = new List<Dictionary<string, object>>();
            int totalW = 0, maxH = 0;

            for (int i = 0; i < screens.Count; i++) {
                Rectangle b = screens[i].Bounds;
                int originalIndex = Array.IndexOf(Screen.AllScreens, screens[i]);
                if (only.Count > 0 && !only.Contains(originalIndex)) continue;

                double scale = 1.0;
                if (maxDim > 0 && (b.Width > maxDim || b.Height > maxDim)) {
                    scale = Math.Min((double)maxDim / b.Width, (double)maxDim / b.Height);
                }
                int dw = Math.Max(1, (int)(b.Width * scale));
                int dh = Math.Max(1, (int)(b.Height * scale));

                var panel = new Dictionary<string, object>();
                panel["index"] = originalIndex;
                panel["primary"] = screens[i].Primary;
                panel["bounds"] = new Dictionary<string, object> {
                    { "x", b.X }, { "y", b.Y }, { "width", b.Width }, { "height", b.Height }
                };
                panel["scale"] = Math.Round(scale, 4);
                panel["_w"] = dw;
                panel["_h"] = dh;
                panel["_src"] = b;
                panels.Add(panel);

                totalW += dw + (panels.Count > 1 ? gap : 0);
                if (dh > maxH) maxH = dh;
            }

            if (panels.Count == 0) {
                res["status"] = "error";
                res["message"] = "no monitor matched the requested screens";
                return res;
            }

            using (Bitmap canvas = new Bitmap(Math.Max(1, totalW), Math.Max(1, maxH), PixelFormat.Format24bppRgb)) {
                using (Graphics g = Graphics.FromImage(canvas)) {
                    g.Clear(Color.FromArgb(24, 24, 24));
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                    int cursorX = 0;
                    foreach (var panel in panels) {
                        Rectangle src = (Rectangle)panel["_src"];
                        int dw = (int)panel["_w"];
                        int dh = (int)panel["_h"];
                        int dy = (maxH - dh) / 2;

                        using (Bitmap raw = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb)) {
                            using (Graphics rg = Graphics.FromImage(raw)) {
                                rg.CopyFromScreen(src.Location, Point.Empty, src.Size, CopyPixelOperation.SourceCopy);
                            }
                            g.DrawImage(raw, cursorX, dy, dw, dh);
                        }

                        if (labels) {
                            string tag = "screen " + panel["index"] + ((bool)panel["primary"] ? " (primary)" : "");
                            using (Font f = new Font("Segoe UI", 11, FontStyle.Bold))
                            using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                            using (SolidBrush fgb = new SolidBrush(Color.White)) {
                                SizeF sz = g.MeasureString(tag, f);
                                g.FillRectangle(bg, cursorX + 4, dy + 4, sz.Width + 8, sz.Height + 4);
                                g.DrawString(tag, f, fgb, cursorX + 8, dy + 6);
                            }
                        }

                        panel["dest"] = new Dictionary<string, object> {
                            { "x", cursorX }, { "y", dy }, { "width", dw }, { "height", dh }
                        };
                        panel.Remove("_w");
                        panel.Remove("_h");
                        panel.Remove("_src");

                        cursorX += dw + gap;
                    }
                }

                if (string.IsNullOrEmpty(savePath)) {
                    string ext = format == "png" ? ".png" : ".jpg";
                    savePath = Path.Combine(Path.GetTempPath(), "fast_cu_pano_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
                }

                ImageCodecInfo encoder = GetEncoder(format == "png" ? ImageFormat.Png : ImageFormat.Jpeg);
                EncoderParameters encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                canvas.Save(savePath, encoder, encoderParams);

                res["status"] = "ok";
                res["path"] = savePath;
                res["width"] = canvas.Width;
                res["height"] = canvas.Height;
                res["screens"] = panels;
                res["virtualScreen"] = new Dictionary<string, object> {
                    { "x", SystemInformation.VirtualScreen.X }, { "y", SystemInformation.VirtualScreen.Y },
                    { "width", SystemInformation.VirtualScreen.Width }, { "height", SystemInformation.VirtualScreen.Height }
                };
                res["mapping"] = "desktop_x = screens[i].bounds.x + (pano_x - screens[i].dest.x) / screens[i].scale; same for y";

                if (returnBase64) {
                    byte[] bytes = File.ReadAllBytes(savePath);
                    res["base64"] = Convert.ToBase64String(bytes);
                    res["mimeType"] = format == "png" ? "image/png" : "image/jpeg";
                }
            }

            return res;
        }

        static ImageCodecInfo GetEncoder(ImageFormat format) {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs) {
                if (codec.FormatID == format.Guid) return codec;
            }
            return codecs[0];
        }

        static Dictionary<string, object> MouseMove(Dictionary<string, object> req) {
            int x = ToInt(req, "x", 0);
            int y = ToInt(req, "y", 0);
            bool smooth = ToBool(req, "smooth", false);

            if (smooth) {
                POINT start;
                GetCursorPos(out start);
                // Step count follows the distance travelled, so a short hop is not
                // charged the same fixed cost as a cross-screen sweep.
                double dist = Math.Sqrt(Math.Pow(x - start.X, 2) + Math.Pow(y - start.Y, 2));
                int steps = ToInt(req, "steps", Math.Max(4, Math.Min(24, (int)(dist / 40))));
                int totalMs = ToInt(req, "duration_ms", ToInt(req, "durationMs", 40));
                int stepDelay = Math.Max(0, totalMs / Math.Max(1, steps));
                for (int i = 1; i <= steps; i++) {
                    int cx = start.X + (x - start.X) * i / steps;
                    int cy = start.Y + (y - start.Y) * i / steps;
                    SetCursorPos(cx, cy);
                    if (stepDelay > 0) PreciseSleep(stepDelay);
                }
                SetCursorPos(x, y);
            } else {
                SetCursorPos(x, y);
            }

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["x"] = x;
            res["y"] = y;
            return res;
        }

        static Dictionary<string, object> MouseClick(Dictionary<string, object> req) {
            if (req.ContainsKey("x") && req.ContainsKey("y") && req["x"] != null && req["y"] != null) {
                int x = Convert.ToInt32(req["x"]);
                int y = Convert.ToInt32(req["y"]);
                SetCursorPos(x, y);
                int settle = ToInt(req, "settle_ms", 3);
                if (settle > 0) PreciseSleep(settle);
            }

            string action = req.ContainsKey("action") && req["action"] != null ? req["action"].ToString().ToLower() : "click";
            string btn = req.ContainsKey("button") && req["button"] != null ? req["button"].ToString().ToLower() : "left";

            // Action always wins over the generic button field for the dedicated actions:
            // tools.mjs sends button:"left" by default on every call (never absent), so
            // gating this on ContainsKey("button") made it unreachable in practice.
            if (action == "right_click") btn = "right";
            else if (action == "middle_click") btn = "middle";

            uint downFlag = MOUSEEVENTF_LEFTDOWN;
            uint upFlag = MOUSEEVENTF_LEFTUP;

            if (btn == "right") {
                downFlag = MOUSEEVENTF_RIGHTDOWN;
                upFlag = MOUSEEVENTF_RIGHTUP;
            } else if (btn == "middle") {
                downFlag = MOUSEEVENTF_MIDDLEDOWN;
                upFlag = MOUSEEVENTF_MIDDLEUP;
            }

            int clicks = 1;
            if (action == "double_click" || btn == "double" || btn == "double_click") clicks = 2;
            if (action == "triple_click" || btn == "triple" || btn == "triple_click") clicks = 3;

            for (int i = 0; i < clicks; i++) {
                mouse_event(downFlag, 0, 0, 0, UIntPtr.Zero);
                PreciseSleep(10);
                mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
                if (i < clicks - 1) PreciseSleep(50);
            }

            POINT pt;
            GetCursorPos(out pt);
            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["action"] = action;
            res["button"] = btn;
            res["clicks"] = clicks;
            res["x"] = pt.X;
            res["y"] = pt.Y;
            return res;
        }

        static Dictionary<string, object> MouseDrag(Dictionary<string, object> req) {
            POINT curPt;
            GetCursorPos(out curPt);

            int x1 = curPt.X;
            int y1 = curPt.Y;
            if (req.ContainsKey("startX")) x1 = Convert.ToInt32(req["startX"]);
            else if (req.ContainsKey("fromX")) x1 = Convert.ToInt32(req["fromX"]);
            else if (req.ContainsKey("x") && req["x"] != null) x1 = Convert.ToInt32(req["x"]);

            if (req.ContainsKey("startY")) y1 = Convert.ToInt32(req["startY"]);
            else if (req.ContainsKey("fromY")) y1 = Convert.ToInt32(req["fromY"]);
            else if (req.ContainsKey("y") && req["y"] != null) y1 = Convert.ToInt32(req["y"]);

            int x2 = x1;
            int y2 = y1;
            if (req.ContainsKey("endX")) x2 = Convert.ToInt32(req["endX"]);
            else if (req.ContainsKey("toX")) x2 = Convert.ToInt32(req["toX"]);
            else if (req.ContainsKey("to_x")) x2 = Convert.ToInt32(req["to_x"]);

            if (req.ContainsKey("endY")) y2 = Convert.ToInt32(req["endY"]);
            else if (req.ContainsKey("toY")) y2 = Convert.ToInt32(req["toY"]);
            else if (req.ContainsKey("to_y")) y2 = Convert.ToInt32(req["to_y"]);

            int duration = req.ContainsKey("durationMs") ? Convert.ToInt32(req["durationMs"]) : 100;

            SetCursorPos(x1, y1);
            PreciseSleep(20);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            PreciseSleep(20);

            int steps = Math.Max(5, duration / 10);
            for (int i = 1; i <= steps; i++) {
                int cx = x1 + (x2 - x1) * i / steps;
                int cy = y1 + (y2 - y1) * i / steps;
                SetCursorPos(cx, cy);
                PreciseSleep(duration / steps);
            }

            SetCursorPos(x2, y2);
            PreciseSleep(20);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["from"] = new Dictionary<string, object> { { "x", x1 }, { "y", y1 } };
            res["to"] = new Dictionary<string, object> { { "x", x2 }, { "y", y2 } };
            return res;
        }

        static Dictionary<string, object> MouseScroll(Dictionary<string, object> req) {
            int amount = -120;
            if (req.ContainsKey("amount")) amount = Convert.ToInt32(req["amount"]);
            else if (req.ContainsKey("scrollAmount")) amount = Convert.ToInt32(req["scrollAmount"]);
            else if (req.ContainsKey("scroll_amount")) amount = Convert.ToInt32(req["scroll_amount"]);

            bool horizontal = req.ContainsKey("horizontal") && Convert.ToBoolean(req["horizontal"]);

            if (req.ContainsKey("x") && req.ContainsKey("y") && req["x"] != null && req["y"] != null) {
                SetCursorPos(Convert.ToInt32(req["x"]), Convert.ToInt32(req["y"]));
                PreciseSleep(5);
            }

            uint flag = horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL;
            mouse_event(flag, 0, 0, (uint)amount, UIntPtr.Zero);

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["amount"] = amount;
            return res;
        }

        static Dictionary<string, object> KeyboardType(Dictionary<string, object> req) {
            string text = req.ContainsKey("text") && req["text"] != null ? req["text"].ToString() : "";
            string mode = req.ContainsKey("mode") && req["mode"] != null ? req["mode"].ToString().ToLower() : "type";
            bool paste = mode == "paste" || (req.ContainsKey("paste") && Convert.ToBoolean(req["paste"]));

            if (paste) {
                Thread t = new Thread(() => {
                    try {
                        Clipboard.SetText(text);
                    } catch {}
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();

                PreciseSleep(10);
                SendHotkeySequence(new List<string> { "ctrl", "v" });
            } else {
                int delayMs = req.ContainsKey("delayMs") ? Convert.ToInt32(req["delayMs"]) : 2;
                foreach (char c in text) {
                    if (c == '\r') continue;
                    if (c == '\n') {
                        SendKey(0x0D, true);
                        SendKey(0x0D, false);
                    } else if (c == '\t') {
                        SendKey(0x09, true);
                        SendKey(0x09, false);
                    } else {
                        SendUnicodeChar(c);
                    }
                    if (delayMs > 0) PreciseSleep(delayMs);
                }
            }

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["length"] = text.Length;
            res["mode"] = paste ? "paste" : "type";
            return res;
        }

        static void SendUnicodeChar(char c) {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].mkhi.ki.wVk = 0;
            inputs[0].mkhi.ki.wScan = (ushort)c;
            inputs[0].mkhi.ki.dwFlags = KEYEVENTF_UNICODE;

            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].mkhi.ki.wVk = 0;
            inputs[1].mkhi.ki.wScan = (ushort)c;
            inputs[1].mkhi.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        static void SendKey(ushort vk, bool down) {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].mkhi.ki.wVk = vk;
            inputs[0].mkhi.ki.wScan = (ushort)MapVirtualKey((uint)vk, 0);
            inputs[0].mkhi.ki.dwFlags = down ? 0 : KEYEVENTF_KEYUP;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        static ushort ParseKeyName(string key) {
            key = key.Trim().ToLower();
            switch (key) {
                case "ctrl": case "control": return 0x11;
                case "shift": return 0x10;
                case "alt": return 0x12;
                case "win": case "windows": case "super": case "meta": return 0x5B;
                case "enter": case "return": return 0x0D;
                case "esc": case "escape": return 0x1B;
                case "tab": return 0x09;
                case "backspace": case "bs": return 0x08;
                case "delete": case "del": return 0x2E;
                case "insert": case "ins": return 0x2D;
                case "home": return 0x24;
                case "end": return 0x23;
                case "pageup": case "pgup": return 0x21;
                case "pagedown": case "pgdn": return 0x22;
                case "up": return 0x26;
                case "down": return 0x28;
                case "left": return 0x25;
                case "right": return 0x27;
                case "space": case "spacebar": return 0x20;
                case "capslock": return 0x14;
                case "f1": return 0x70;
                case "f2": return 0x71;
                case "f3": return 0x72;
                case "f4": return 0x73;
                case "f5": return 0x74;
                case "f6": return 0x75;
                case "f7": return 0x76;
                case "f8": return 0x77;
                case "f9": return 0x78;
                case "f10": return 0x79;
                case "f11": return 0x7A;
                case "f12": return 0x7B;
                default:
                    if (key.Length == 1) {
                        char c = key.ToUpper()[0];
                        if (c >= 'A' && c <= 'Z') return (ushort)c;
                        if (c >= '0' && c <= '9') return (ushort)c;
                        short scan = VkKeyScan(c);
                        if (scan != -1) return (ushort)(scan & 0xFF);
                    }
                    return 0;
            }
        }

        static void SendHotkeySequence(List<string> keys) {
            List<ushort> vks = new List<ushort>();
            foreach (string k in keys) {
                ushort vk = ParseKeyName(k);
                if (vk != 0) vks.Add(vk);
            }

            foreach (ushort vk in vks) {
                SendKey(vk, true);
                PreciseSleep(5);
            }

            PreciseSleep(10);

            for (int i = vks.Count - 1; i >= 0; i--) {
                SendKey(vks[i], false);
                PreciseSleep(5);
            }
        }

        static Dictionary<string, object> KeyboardHotkey(Dictionary<string, object> req) {
            string keysStr = "";
            if (req.ContainsKey("hotkey") && req["hotkey"] != null) keysStr = req["hotkey"].ToString();
            else if (req.ContainsKey("keys") && req["keys"] != null) keysStr = req["keys"].ToString();
            else if (req.ContainsKey("text") && req["text"] != null) keysStr = req["text"].ToString();

            string[] parts = keysStr.Split('+', '-');
            List<string> keys = new List<string>();
            foreach (string p in parts) {
                if (!string.IsNullOrWhiteSpace(p)) keys.Add(p.Trim());
            }

            SendHotkeySequence(keys);

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["keys"] = keysStr;
            return res;
        }

        static Dictionary<string, object> KeyboardKey(Dictionary<string, object> req) {
            string key = "enter";
            if (req.ContainsKey("key") && req["key"] != null) key = req["key"].ToString();
            else if (req.ContainsKey("text") && req["text"] != null) key = req["text"].ToString();
            else if (req.ContainsKey("hotkey") && req["hotkey"] != null) key = req["hotkey"].ToString();

            string action = req.ContainsKey("action") && req["action"] != null ? req["action"].ToString().ToLower() : "press";
            ushort vk = ParseKeyName(key);

            if (action == "down") {
                SendKey(vk, true);
            } else if (action == "up") {
                SendKey(vk, false);
            } else {
                SendKey(vk, true);
                PreciseSleep(10);
                SendKey(vk, false);
            }

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["key"] = key;
            res["action"] = action;
            return res;
        }

        // Maps a bare "action" value (mouse/keyboard/window/screen verbs used by the
        // MCP tool schemas) onto the engine command that handles it.
        static string CommandForAction(string action) {
            switch (action) {
                case "click": case "double_click": case "triple_click":
                case "right_click": case "middle_click": case "move":
                case "mouse_down": case "mouse_up": case "drag": case "scroll":
                    return "mouse";

                case "type_text": case "type": case "paste_text": case "paste":
                case "hotkey": case "press_key": case "key": case "key_down": case "key_up":
                    return "keyboard";

                case "list": case "windows": case "window_list":
                    return "window_list";
                case "get_active": case "active_window":
                    return "info";
                case "focus": case "activate":
                    return "window_focus";
                case "minimize": case "maximize": case "restore":
                    return "window_state";
                case "close":
                    return "window_close";
                case "set_pos": case "move_window": case "resize":
                    return "window_pos";

                case "screenshot": case "capture": case "screen_capture":
                    return "screenshot";
                case "inspect": case "ui_inspect":
                    return "ui_inspect";
                case "launch": case "start": case "run": case "open":
                    return "launch";
                case "info": case "system_info": case "screen_info":
                    return "info";
                default:
                    return action;
            }
        }

        // Short waits spin on the stopwatch instead of yielding to the scheduler: below
        // one timer tick, Sleep overshoots by an order of magnitude.
        static void PreciseSleep(int ms) {
            if (ms <= 0) return;
            if (ms >= 16) { Thread.Sleep(ms); return; }
            var sw = Stopwatch.StartNew();
            long ticks = (long)(ms * (Stopwatch.Frequency / 1000.0));
            while (sw.ElapsedTicks < ticks) Thread.SpinWait(40);
        }

        static string ToStr(Dictionary<string, object> req, string key, string def) {
            if (req.ContainsKey(key) && req[key] != null) {
                string v = req[key].ToString();
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return def;
        }

        static int ToInt(Dictionary<string, object> req, string key, int def) {
            if (req.ContainsKey(key) && req[key] != null) {
                try { return Convert.ToInt32(req[key]); } catch {}
            }
            return def;
        }

        static bool ToBool(Dictionary<string, object> req, string key, bool def) {
            if (req.ContainsKey(key) && req[key] != null) {
                object v = req[key];
                if (v is bool) return (bool)v;
                string s = v.ToString().Trim();
                if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Length == 0) return false;
                try { return Convert.ToBoolean(v); } catch {}
            }
            return def;
        }

        static Dictionary<string, object> ExecuteBatch(Dictionary<string, object> req) {
            var results = new List<Dictionary<string, object>>();
            int failed = 0;
            int stoppedAt = -1;
            // A pipeline that keeps typing after a focus or click failed lands input in
            // the wrong window, so a failed step halts the batch by default.
            bool continueOnError = ToBool(req, "continue_on_error", ToBool(req, "continueOnError", false));

            if (req.ContainsKey("actions") && req["actions"] is IEnumerable) {
                IEnumerable list = req["actions"] as IEnumerable;
                int index = 0;
                foreach (var item in list) {
                    Dictionary<string, object> act = null;
                    if (item is Dictionary<string, object>) {
                        act = item as Dictionary<string, object>;
                    } else if (item is IDictionary) {
                        act = new Dictionary<string, object>();
                        var idict = item as IDictionary;
                        foreach (var k in idict.Keys) {
                            act[k.ToString()] = idict[k];
                        }
                    }

                    if (act == null) continue;
                    index++;

                    string type = ToStr(act, "cmd", "").ToLower();
                    if (string.IsNullOrEmpty(type)) type = ToStr(act, "type", "").ToLower();
                    // A batch item may carry only "action" - the field name every MCP tool
                    // schema uses. Infer the engine command from it so the step runs instead
                    // of falling through to "Unknown command".
                    if (string.IsNullOrEmpty(type)) type = CommandForAction(ToStr(act, "action", "").ToLower());
                    act["cmd"] = type;

                    Stopwatch stepSw = Stopwatch.StartNew();
                    Dictionary<string, object> actRes;
                    if (type == "wait" || type == "sleep" || type == "delay") {
                        int ms = ToInt(act, "ms", ToInt(act, "duration_ms", 100));
                        PreciseSleep(ms);
                        actRes = new Dictionary<string, object> { { "status", "ok" }, { "action", "wait" }, { "ms", ms } };
                    } else if (type == "wait_for" || type == "waitfor" || type == "wait_until") {
                        actRes = WaitForCondition(act);
                    } else {
                        actRes = DispatchCommand(act);
                    }

                    if (actRes == null) {
                        actRes = new Dictionary<string, object> { { "status", "error" }, { "message", "no response from step" } };
                    }
                    actRes["step"] = index;
                    if (!actRes.ContainsKey("action") && !string.IsNullOrEmpty(type)) actRes["action"] = type;

                    bool stepOk = ToStr(actRes, "status", "") == "ok";

                    // Per-step verification: confirm the step actually landed instead of
                    // trusting it was merely issued without error. Text-only by design, so
                    // confirming a step never costs an image payload.
                    if (stepOk && act.ContainsKey("verify") && act["verify"] != null) {
                        var vres = VerifyStep(act["verify"]);
                        actRes["verify"] = vres;
                        if (ToStr(vres, "status", "") != "ok") {
                            stepOk = false;
                            actRes["status"] = "error";
                            actRes["message"] = ToStr(vres, "message", "verification failed");
                        }
                    }

                    stepSw.Stop();
                    actRes["elapsedMs"] = stepSw.ElapsedMilliseconds;
                    results.Add(actRes);

                    if (!stepOk) {
                        failed++;
                        if (!continueOnError) { stoppedAt = index; break; }
                    }
                }
            }

            var res = new Dictionary<string, object>();
            res["status"] = failed > 0 ? "partial" : "ok";
            res["count"] = results.Count;
            res["failed"] = failed;
            if (stoppedAt > 0) {
                res["stoppedAtStep"] = stoppedAt;
                res["message"] = "batch halted at step " + stoppedAt + " (pass continue_on_error:true to run past failures)";
            }
            res["results"] = results;
            return res;
        }

        // Confirms a step landed. Accepts a bare UIA filter string, true (report the
        // foreground window), or an object { mode, filter, window, timeout_ms, present }.
        static Dictionary<string, object> VerifyStep(object spec) {
            var req = new Dictionary<string, object>();
            if (spec is IDictionary) {
                var idict = spec as IDictionary;
                foreach (var k in idict.Keys) req[k.ToString()] = idict[k];
            } else if (spec is bool) {
                if (!((bool)spec)) return new Dictionary<string, object> { { "status", "ok" }, { "mode", "skipped" } };
                req["mode"] = "state";
            } else {
                string str = spec.ToString().Trim();
                if (str.Length == 0) return new Dictionary<string, object> { { "status", "ok" }, { "mode", "skipped" } };
                req["filter"] = str;
            }

            string mode = ToStr(req, "mode", req.ContainsKey("window") ? "window" : (req.ContainsKey("filter") ? "element" : "state"));

            if (mode == "state") {
                IntPtr fg = GetForegroundWindow();
                StringBuilder sb = new StringBuilder(512);
                GetWindowText(fg, sb, 512);
                return new Dictionary<string, object> {
                    { "status", "ok" }, { "mode", "state" },
                    { "foreground", sb.ToString() }, { "hwnd", fg.ToInt64() }
                };
            }

            // Text verification reads the value back out of the control, which is the
            // only reliable way to confirm a keystroke or a paste actually landed.
            if (mode == "text" || req.ContainsKey("expect")) {
                int textTimeout = ToInt(req, "timeout_ms", ToInt(req, "timeoutMs", 1500));
                Stopwatch tsw = Stopwatch.StartNew();
                Dictionary<string, object> last;
                while (true) {
                    last = ReadText(req);
                    if (ToStr(last, "status", "") == "ok") { last["mode"] = "text"; return last; }
                    if (tsw.ElapsedMilliseconds >= textTimeout) break;
                    PreciseSleep(60);
                }
                tsw.Stop();
                last["mode"] = "text";
                last["waitedMs"] = tsw.ElapsedMilliseconds;
                return last;
            }

            // Element and window verification reuse the wait_for polling loop, so a check
            // tolerates the few hundred ms an app needs to repaint.
            if (!req.ContainsKey("timeout_ms") && !req.ContainsKey("timeoutMs")) req["timeout_ms"] = 1500;
            var waited = WaitForCondition(req);
            waited["mode"] = mode;
            return waited;
        }

        // Polls until a UIA element (filter) or a window (window) is present or absent.
        // This replaces the fire-action / screenshot / ask-the-model-to-look round trip.
        static Dictionary<string, object> WaitForCondition(Dictionary<string, object> req) {
            string filter = ToStr(req, "filter", ToStr(req, "text", ""));
            string window = ToStr(req, "window", "");
            bool present = ToBool(req, "present", true);
            bool needForeground = ToBool(req, "foreground", false);
            int timeoutMs = ToInt(req, "timeout_ms", ToInt(req, "timeoutMs", 5000));
            int pollMs = Math.Max(20, ToInt(req, "poll_ms", ToInt(req, "pollMs", 80)));
            bool byWindow = !string.IsNullOrEmpty(window) && string.IsNullOrEmpty(filter);

            var res = new Dictionary<string, object>();
            res["action"] = "wait_for";
            res["present"] = present;
            if (byWindow) res["window"] = window; else res["filter"] = filter;

            if (!byWindow && string.IsNullOrEmpty(filter)) {
                res["status"] = "error";
                res["message"] = "wait_for needs a filter (UI element) or a window (title/process)";
                return res;
            }

            Stopwatch sw = Stopwatch.StartNew();
            int count = 0;
            string detail = "";
            while (true) {
                if (byWindow) {
                    string foundTitle;
                    IntPtr hwnd = FindWindowByTarget(window, out foundTitle);
                    bool hit = hwnd != IntPtr.Zero;
                    if (hit && needForeground) hit = (GetForegroundWindow() == hwnd);
                    count = hit ? 1 : 0;
                    if (hit) {
                        detail = foundTitle;
                        res["hwnd"] = hwnd.ToInt64();
                    }
                } else {
                    var inspectReq = new Dictionary<string, object> {
                        { "target", ToStr(req, "scope", "active_window") },
                        { "maxDepth", ToInt(req, "max_depth", 8) },
                        { "filter", filter },
                        { "interactiveOnly", ToBool(req, "interactive_only", false) }
                    };
                    var inspectRes = InspectUI(inspectReq);
                    count = ToInt(inspectRes, "count", 0);
                }

                if (present ? count > 0 : count == 0) {
                    sw.Stop();
                    res["status"] = "ok";
                    res["count"] = count;
                    res["waitedMs"] = sw.ElapsedMilliseconds;
                    if (detail.Length > 0) res["title"] = detail;
                    return res;
                }

                if (sw.ElapsedMilliseconds >= timeoutMs) break;
                PreciseSleep(pollMs);
            }

            sw.Stop();
            res["status"] = "error";
            res["count"] = count;
            res["waitedMs"] = sw.ElapsedMilliseconds;
            res["message"] = present
                ? "not found within " + timeoutMs + "ms: " + (byWindow ? window : filter)
                : "still present after " + timeoutMs + "ms: " + (byWindow ? window : filter);
            return res;
        }

        // Replaces win+r, typing a name, enter, a blind sleep and a screenshot to check.
        // A failure to start is reported as such, not silently matched to some other window.
        static Dictionary<string, object> LaunchApp(Dictionary<string, object> req) {
            string app = ToStr(req, "app", ToStr(req, "path", ToStr(req, "target", "")));
            string arguments = ToStr(req, "args", ToStr(req, "arguments", ""));
            string expect = ToStr(req, "expect_window", ToStr(req, "expectWindow", ""));
            bool reuse = ToBool(req, "reuse_existing", ToBool(req, "reuseExisting", true));
            bool focus = ToBool(req, "focus", true);
            int timeoutMs = ToInt(req, "timeout_ms", ToInt(req, "timeoutMs", 10000));

            var res = new Dictionary<string, object>();
            res["action"] = "launch";
            res["app"] = app;

            if (string.IsNullOrEmpty(app)) {
                res["status"] = "error";
                res["message"] = "launch needs an app: executable name, full path, document or URI";
                return res;
            }

            string baseName = app;
            try {
                string bn = Path.GetFileNameWithoutExtension(app);
                if (!string.IsNullOrEmpty(bn)) baseName = bn;
            } catch {}
            if (string.IsNullOrEmpty(expect)) expect = baseName;
            res["expectWindow"] = expect;

            IntPtr existing = FindLaunchedWindow(expect, baseName, -1);
            bool reused = false;
            int startedPid = -1;

            if (reuse && existing != IntPtr.Zero) {
                reused = true;
            } else {
                try {
                    ProcessStartInfo psi = new ProcessStartInfo(app);
                    if (!string.IsNullOrEmpty(arguments)) psi.Arguments = arguments;
                    psi.UseShellExecute = true;
                    Process started = Process.Start(psi);
                    if (started != null) {
                        try { startedPid = started.Id; } catch {}
                    }
                } catch (Exception ex) {
                    res["status"] = "error";
                    res["message"] = "could not start " + app + ": " + ex.Message;
                    return res;
                }
            }

            // Poll for the app window, but bail out early if Windows puts up a startup
            // error dialog for it - that dialog's title contains the exe name, so a
            // plain title match would otherwise report a broken app as a success.
            Stopwatch sw = Stopwatch.StartNew();
            IntPtr hwnd = IntPtr.Zero;
            while (true) {
                string errTitle;
                IntPtr errDlg = FindStartupErrorDialog(baseName, out errTitle);
                if (errDlg != IntPtr.Zero) {
                    sw.Stop();
                    res["status"] = "error";
                    res["message"] = "the app failed to start: " + errTitle;
                    res["errorDialog"] = errTitle;
                    res["errorText"] = ReadWindowStaticText(errDlg);
                    res["hwnd"] = errDlg.ToInt64();
                    res["waitedMs"] = sw.ElapsedMilliseconds;
                    return res;
                }

                hwnd = FindLaunchedWindow(expect, baseName, startedPid);
                if (hwnd != IntPtr.Zero) break;
                if (sw.ElapsedMilliseconds >= timeoutMs) break;
                PreciseSleep(50);
            }
            sw.Stop();

            if (hwnd == IntPtr.Zero) {
                res["status"] = "error";
                res["message"] = "started but no window matching " + expect + " appeared within " + timeoutMs + "ms";
                res["waitedMs"] = sw.ElapsedMilliseconds;
                return res;
            }

            StringBuilder titleSb = new StringBuilder(512);
            GetWindowText(hwnd, titleSb, 512);

            res["reused"] = reused;
            res["waitedMs"] = sw.ElapsedMilliseconds;
            res["title"] = titleSb.ToString();
            res["hwnd"] = hwnd.ToInt64();

            if (focus) {
                var focusReq = new Dictionary<string, object> { { "target", hwnd.ToInt64().ToString() }, { "action", "focus" } };
                var focusRes = FocusWindow(focusReq);
                res["focused"] = ToStr(focusRes, "status", "") == "ok";
                if (focusRes.ContainsKey("foreground")) res["foreground"] = focusRes["foreground"];
            }

            res["status"] = "ok";
            return res;
        }

        // A window that belongs to the launched app: by PID when the launcher handed us
        // one, otherwise by process name, otherwise by title substring. Startup error
        // dialogs are never treated as the app's window.
        static IntPtr FindLaunchedWindow(string expect, string baseName, int pid) {
            IntPtr byPid = IntPtr.Zero;
            IntPtr byProc = IntPtr.Zero;
            IntPtr byTitle = IntPtr.Zero;

            EnumWindows((hWnd, lParam) => {
                if (!IsWindowVisible(hWnd)) return true;
                StringBuilder sb = new StringBuilder(512);
                GetWindowText(hWnd, sb, 512);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;
                if (IsErrorDialogTitle(title)) return true;

                uint wpid;
                GetWindowThreadProcessId(hWnd, out wpid);
                if (pid > 0 && (int)wpid == pid) { byPid = hWnd; return false; }

                if (byProc == IntPtr.Zero) {
                    string proc = "";
                    try { proc = Process.GetProcessById((int)wpid).ProcessName; } catch {}
                    if (!string.IsNullOrEmpty(proc) &&
                        proc.Equals(baseName, StringComparison.OrdinalIgnoreCase)) {
                        byProc = hWnd;
                    }
                }
                if (byTitle == IntPtr.Zero &&
                    title.IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0) {
                    byTitle = hWnd;
                }
                return true;
            }, IntPtr.Zero);

            if (byPid != IntPtr.Zero) return byPid;
            if (byProc != IntPtr.Zero) return byProc;
            return byTitle;
        }

        static bool IsErrorDialogTitle(string title) {
            return title.IndexOf("System Error", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Application Error", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static IntPtr FindStartupErrorDialog(string baseName, out string foundTitle) {
            IntPtr found = IntPtr.Zero;
            string title = "";
            EnumWindows((hWnd, lParam) => {
                if (!IsWindowVisible(hWnd)) return true;
                StringBuilder sb = new StringBuilder(512);
                GetWindowText(hWnd, sb, 512);
                string t = sb.ToString();
                if (string.IsNullOrWhiteSpace(t)) return true;
                if (IsErrorDialogTitle(t) && t.IndexOf(baseName, StringComparison.OrdinalIgnoreCase) >= 0) {
                    found = hWnd; title = t; return false;
                }
                return true;
            }, IntPtr.Zero);
            foundTitle = title;
            return found;
        }

        // Reads what an app actually contains, straight out of UI Automation. This is
        // how a typed or pasted value gets confirmed: one text answer instead of a
        // screenshot the model has to look at.
        static Dictionary<string, object> ReadText(Dictionary<string, object> req) {
            string filter = ToStr(req, "filter", "");
            string target = ToStr(req, "target", "focused").ToLower();
            int maxChars = ToInt(req, "max_chars", ToInt(req, "maxChars", 20000));

            var res = new Dictionary<string, object>();
            res["action"] = "read_text";

            AutomationElement root = null;
            IntPtr fg = GetForegroundWindow();
            try { if (fg != IntPtr.Zero) root = AutomationElement.FromHandle(fg); } catch {}

            AutomationElement el = null;
            string how = "";

            if (target == "focused" && filter.Length == 0) {
                try { el = AutomationElement.FocusedElement; how = "focused"; } catch {}
            }

            if (el == null && root != null && filter.Length > 0) {
                el = FindElementByFilter(root, filter);
                how = "filter";
            }

            if (el == null && root != null) {
                el = FindFirstTextHolder(root);
                how = el != null ? "first-editable" : "";
            }

            if (el == null) {
                res["status"] = "error";
                res["message"] = filter.Length > 0
                    ? "no element matching: " + filter
                    : "no readable element in the foreground window";
                return res;
            }

            string text = ExtractElementText(el);
            if (text == null) text = "";
            if (text.Length > maxChars) text = text.Substring(0, maxChars);

            res["status"] = "ok";
            res["text"] = text;
            res["length"] = text.Length;
            res["source"] = how;
            try {
                res["element"] = el.Current.Name;
                res["type"] = el.Current.ControlType != null
                    ? el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "") : "";
                res["automationId"] = el.Current.AutomationId ?? "";
            } catch {}

            if (req.ContainsKey("expect") && req["expect"] != null) {
                string expect = req["expect"].ToString();
                bool match = text.IndexOf(expect, StringComparison.Ordinal) >= 0;
                res["expect"] = expect;
                res["matched"] = match;
                if (!match) {
                    res["status"] = "error";
                    res["message"] = "text does not contain: " + expect;
                }
            }

            return res;
        }

        static string ExtractElementText(AutomationElement el) {
            if (el == null) return "";
            try {
                object vp;
                if (el.TryGetCurrentPattern(ValuePattern.Pattern, out vp)) {
                    string v = ((ValuePattern)vp).Current.Value;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            } catch {}
            try {
                object tp;
                if (el.TryGetCurrentPattern(TextPattern.Pattern, out tp)) {
                    string v = ((TextPattern)tp).DocumentRange.GetText(-1);
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            } catch {}
            try {
                string n = el.Current.Name;
                if (!string.IsNullOrEmpty(n)) return n;
            } catch {}
            return "";
        }

        // First descendant that can actually hold text (Edit or Document), preferring
        // one that already has a value.
        static AutomationElement FindFirstTextHolder(AutomationElement root) {
            try {
                var cond = new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
                var all = root.FindAll(TreeScope.Descendants, cond);
                AutomationElement firstAny = null;
                foreach (AutomationElement candidate in all) {
                    if (firstAny == null) firstAny = candidate;
                    string t = ExtractElementText(candidate);
                    if (!string.IsNullOrEmpty(t)) return candidate;
                }
                return firstAny;
            } catch {
                return null;
            }
        }

        // Id, exact name and control type are resolved by UI Automation itself in one
        // call. The manual walk is the last resort and is capped: it costs seconds.
        static AutomationElement FindElementByFilter(AutomationElement root, string filter) {
            if (root == null || string.IsNullOrEmpty(filter)) return null;

            try {
                var byId = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, filter));
                if (byId != null) return byId;
            } catch {}

            try {
                var byName = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, filter));
                if (byName != null) return byName;
            } catch {}

            ControlType ct = ControlTypeByName(filter);
            if (ct != null) {
                try {
                    var byType = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ct));
                    if (byType != null) return byType;
                } catch {}
            }

            try {
                var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                int seen = 0;
                foreach (AutomationElement candidate in all) {
                    if (++seen > 250) break;
                    try {
                        var cur = candidate.Current;
                        string name = cur.Name ?? "";
                        string autoId = cur.AutomationId ?? "";
                        if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            autoId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) {
                            return candidate;
                        }
                    } catch {}
                }
            } catch {}
            return null;
        }

        static ControlType ControlTypeByName(string name) {
            switch (name.ToLower()) {
                case "button": return ControlType.Button;
                case "edit": return ControlType.Edit;
                case "document": return ControlType.Document;
                case "text": return ControlType.Text;
                case "combobox": return ControlType.ComboBox;
                case "list": return ControlType.List;
                case "listitem": return ControlType.ListItem;
                case "tab": return ControlType.Tab;
                case "tabitem": return ControlType.TabItem;
                case "checkbox": return ControlType.CheckBox;
                case "radiobutton": return ControlType.RadioButton;
                case "menuitem": return ControlType.MenuItem;
                case "menu": return ControlType.Menu;
                case "hyperlink": return ControlType.Hyperlink;
                case "window": return ControlType.Window;
                case "pane": return ControlType.Pane;
                case "group": return ControlType.Group;
                case "image": return ControlType.Image;
                case "tree": return ControlType.Tree;
                case "treeitem": return ControlType.TreeItem;
                case "table": return ControlType.Table;
                case "datagrid": return ControlType.DataGrid;
                case "toolbar": return ControlType.ToolBar;
                case "spinner": return ControlType.Spinner;
                case "slider": return ControlType.Slider;
                case "custom": return ControlType.Custom;
                default: return null;
            }
        }

        // Joins the static text of a window, so an error dialog can explain itself
        // without the caller needing a screenshot.
        static string ReadWindowStaticText(IntPtr hwnd) {
            try {
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                if (root == null) return "";
                var parts = new List<string>();
                var texts = root.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                foreach (AutomationElement el in texts) {
                    try {
                        string n = el.Current.Name;
                        if (!string.IsNullOrWhiteSpace(n)) parts.Add(n.Trim());
                    } catch {}
                }
                return string.Join(" ", parts.ToArray());
            } catch {
                return "";
            }
        }

        // Single window lookup shared by focus, wait_for and launch: matches an HWND
        // given as a number, then a window title substring, then a process name.
        static IntPtr FindWindowByTarget(string target, out string foundTitle) {
            foundTitle = "";
            if (string.IsNullOrEmpty(target)) return IntPtr.Zero;

            long parsedHwnd;
            if (long.TryParse(target, out parsedHwnd) && parsedHwnd > 0) {
                IntPtr direct = new IntPtr(parsedHwnd);
                if (IsWindow(direct)) {
                    StringBuilder sbd = new StringBuilder(512);
                    GetWindowText(direct, sbd, 512);
                    foundTitle = sbd.ToString();
                    return direct;
                }
            }

            IntPtr byTitle = IntPtr.Zero;
            string byTitleText = "";
            IntPtr byProc = IntPtr.Zero;
            string byProcText = "";

            EnumWindows((hWnd, lParam) => {
                if (!IsWindowVisible(hWnd)) return true;
                StringBuilder sb = new StringBuilder(512);
                GetWindowText(hWnd, sb, 512);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                if (title.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0) {
                    if (byTitle == IntPtr.Zero) { byTitle = hWnd; byTitleText = title; }
                    return false;
                }

                if (byProc == IntPtr.Zero) {
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    string proc = "";
                    try { proc = Process.GetProcessById((int)pid).ProcessName; } catch {}
                    if (!string.IsNullOrEmpty(proc) && proc.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0) {
                        byProc = hWnd; byProcText = title;
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (byTitle != IntPtr.Zero) { foundTitle = byTitleText; return byTitle; }
            if (byProc != IntPtr.Zero) { foundTitle = byProcText; return byProc; }
            return IntPtr.Zero;
        }

        static Dictionary<string, object> ListWindows() {
            var windows = new List<Dictionary<string, object>>();
            IntPtr fgHwnd = GetForegroundWindow();

            EnumWindows((hWnd, lParam) => {
                if (!IsWindowVisible(hWnd)) return true;

                StringBuilder sb = new StringBuilder(512);
                GetWindowText(hWnd, sb, 512);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                RECT r;
                GetWindowRect(hWnd, out r);
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w <= 10 || h <= 10) return true;

                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                string procName = "";
                try {
                    procName = Process.GetProcessById((int)pid).ProcessName;
                } catch {}

                var item = new Dictionary<string, object>();
                item["hwnd"] = hWnd.ToInt64();
                item["title"] = title;
                item["processName"] = procName;
                item["pid"] = pid;
                item["rect"] = new Dictionary<string, object> {
                    { "x", r.Left }, { "y", r.Top }, { "width", w }, { "height", h }
                };
                item["isMinimized"] = IsIconic(hWnd);
                item["isForeground"] = (hWnd == fgHwnd);
                windows.Add(item);

                return true;
            }, IntPtr.Zero);

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["count"] = windows.Count;
            res["windows"] = windows;
            return res;
        }

        static Dictionary<string, object> FocusWindow(Dictionary<string, object> req) {
            string target = ToStr(req, "target", "");
            string action = ToStr(req, "action", "focus").ToLower();

            string foundTitle;
            IntPtr targetHwnd = FindWindowByTarget(target, out foundTitle);

            var res = new Dictionary<string, object>();
            if (targetHwnd == IntPtr.Zero) {
                res["status"] = "error";
                res["message"] = "Window not found matching: " + target;
                return res;
            }

            res["status"] = "ok";
            res["hwnd"] = targetHwnd.ToInt64();
            res["title"] = foundTitle;
            res["action"] = action;

            if (action == "minimize") {
                ShowWindowAsync(targetHwnd, SW_MINIMIZE);
            } else if (action == "maximize") {
                ShowWindowAsync(targetHwnd, SW_MAXIMIZE);
            } else if (action == "restore") {
                ShowWindowAsync(targetHwnd, SW_RESTORE);
            } else if (action == "close") {
                SendMessage(targetHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            } else {
                bool got = ForceForeground(targetHwnd);
                res["foreground"] = got;
                if (!got) {
                    res["status"] = "error";
                    res["message"] = "could not bring the window to the foreground: " + foundTitle;
                }
            }

            return res;
        }

        // A bare SetForegroundWindow is refused, silently, whenever the caller does not
        // already own the foreground window. Attaching to the foreground thread's input
        // queue lifts that restriction; the result is then read back rather than assumed.
        static bool ForceForeground(IntPtr hWnd) {
            if (hWnd == IntPtr.Zero) return false;
            if (GetForegroundWindow() == hWnd) return true;
            if (IsIconic(hWnd)) ShowWindowAsync(hWnd, SW_RESTORE);

            IntPtr fg = GetForegroundWindow();
            uint fgThread = 0;
            uint pidIgnored;
            if (fg != IntPtr.Zero) fgThread = GetWindowThreadProcessId(fg, out pidIgnored);
            uint selfThread = GetCurrentThreadId();
            bool attached = false;

            try {
                if (fgThread != 0 && fgThread != selfThread) {
                    attached = AttachThreadInput(selfThread, fgThread, true);
                }
                for (int attempt = 0; attempt < 3; attempt++) {
                    BringWindowToTop(hWnd);
                    SetForegroundWindow(hWnd);
                    if (GetForegroundWindow() == hWnd) return true;
                    SwitchToThisWindow(hWnd, true);
                    if (GetForegroundWindow() == hWnd) return true;
                    PreciseSleep(30);
                }
            } finally {
                if (attached) AttachThreadInput(selfThread, fgThread, false);
            }

            return GetForegroundWindow() == hWnd;
        }

        static Dictionary<string, object> SetWindowPosCommand(Dictionary<string, object> req) {
            string target = req.ContainsKey("target") && req["target"] != null ? req["target"].ToString() : "";
            int x = req.ContainsKey("x") ? Convert.ToInt32(req["x"]) : 0;
            int y = req.ContainsKey("y") ? Convert.ToInt32(req["y"]) : 0;
            int w = req.ContainsKey("width") ? Convert.ToInt32(req["width"]) : 800;
            int h = req.ContainsKey("height") ? Convert.ToInt32(req["height"]) : 600;

            IntPtr targetHwnd = IntPtr.Zero;
            string foundTitle = "";

            if (!string.IsNullOrEmpty(target)) {
                long parsedHwnd;
                if (long.TryParse(target, out parsedHwnd) && parsedHwnd > 0) {
                    targetHwnd = new IntPtr(parsedHwnd);
                } else {
                    EnumWindows((hWnd, lParam) => {
                        if (!IsWindowVisible(hWnd)) return true;
                        StringBuilder sb = new StringBuilder(512);
                        GetWindowText(hWnd, sb, 512);
                        string title = sb.ToString();
                        if (string.IsNullOrWhiteSpace(title)) return true;

                        uint pid;
                        GetWindowThreadProcessId(hWnd, out pid);
                        string proc = "";
                        try { proc = Process.GetProcessById((int)pid).ProcessName; } catch {}

                        if (title.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            proc.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0) {
                            targetHwnd = hWnd;
                            foundTitle = title;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                }
            } else {
                targetHwnd = GetForegroundWindow();
            }

            var res = new Dictionary<string, object>();
            if (targetHwnd == IntPtr.Zero) {
                res["status"] = "error";
                res["message"] = "Window not found matching: " + target;
                return res;
            }

            SetWindowPos(targetHwnd, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);

            res["status"] = "ok";
            res["hwnd"] = targetHwnd.ToInt64();
            res["x"] = x;
            res["y"] = y;
            res["width"] = w;
            res["height"] = h;
            return res;
        }

        static Dictionary<string, object> InspectUI(Dictionary<string, object> req) {
            string target = req.ContainsKey("target") && req["target"] != null ? req["target"].ToString().ToLower() : "active_window";
            int maxDepth = req.ContainsKey("maxDepth") ? Convert.ToInt32(req["maxDepth"]) : 8;
            string filter = req.ContainsKey("filter") && req["filter"] != null ? req["filter"].ToString() : "";
            bool interactiveOnly = !req.ContainsKey("interactiveOnly") || req["interactiveOnly"] == null || Convert.ToBoolean(req["interactiveOnly"]);

            AutomationElement root = null;
            if (target == "desktop" || target == "root" || target == "screen") {
                root = AutomationElement.RootElement;
            } else if (target == "cursor") {
                POINT cursorPt;
                GetCursorPos(out cursorPt);
                try { root = AutomationElement.FromPoint(new System.Windows.Point(cursorPt.X, cursorPt.Y)); } catch {}
                if (root == null) root = AutomationElement.RootElement;
            } else {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero) {
                    try { root = AutomationElement.FromHandle(fg); } catch {}
                }
                if (root == null) root = AutomationElement.RootElement;
            }

            var elements = new List<Dictionary<string, object>>();
            TraverseUIA(root, elements, 0, maxDepth, filter, interactiveOnly);

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["count"] = elements.Count;
            res["elements"] = elements;
            return res;
        }

        static void TraverseUIA(AutomationElement elem, List<Dictionary<string, object>> list, int depth, int maxDepth, string filter, bool interactiveOnly) {
            if (elem == null || depth > maxDepth || list.Count > 150) return;

            try {
                var current = elem.Current;
                string name = current.Name;
                string controlType = current.ControlType != null ? current.ControlType.ProgrammaticName.Replace("ControlType.", "") : "";
                string automationId = current.AutomationId ?? "";
                var rect = current.BoundingRectangle;

                bool isInteractiveType = controlType == "Button" || controlType == "Edit" || controlType == "MenuItem" ||
                    controlType == "CheckBox" || controlType == "RadioButton" || controlType == "TabItem" ||
                    controlType == "ComboBox" || controlType == "Hyperlink" || controlType == "ListItem";
                // interactiveOnly=false surfaces every element with a real bounding box (e.g. panes,
                // groups, text) instead of just the click-worthy subset.
                bool isInteresting = interactiveOnly ? (!string.IsNullOrEmpty(name) || isInteractiveType) : true;

                bool matchesFilter = string.IsNullOrEmpty(filter) ||
                    (!string.IsNullOrEmpty(name) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    controlType.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(automationId) && automationId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

                if (isInteresting && matchesFilter && rect.Width > 0 && rect.Height > 0 && !rect.IsEmpty) {
                    var item = new Dictionary<string, object>();
                    item["name"] = name ?? "";
                    item["type"] = controlType;
                    item["rect"] = new Dictionary<string, object> {
                        { "x", (int)rect.X }, { "y", (int)rect.Y },
                        { "width", (int)rect.Width }, { "height", (int)rect.Height }
                    };
                    item["center"] = new Dictionary<string, object> {
                        { "x", (int)(rect.X + rect.Width / 2) },
                        { "y", (int)(rect.Y + rect.Height / 2) }
                    };
                    item["enabled"] = current.IsEnabled;
                    if (!string.IsNullOrEmpty(automationId)) item["automationId"] = automationId;
                    list.Add(item);
                }

                // Filtering only decides inclusion in the result, never pruning — a matching
                // descendant can live under a non-matching container.
                AutomationElementCollection children = elem.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement child in children) {
                    TraverseUIA(child, list, depth + 1, maxDepth, filter, interactiveOnly);
                }
            } catch {}
        }

        static Dictionary<string, object> ClickUIElement(Dictionary<string, object> req) {
            string name = ToStr(req, "name", ToStr(req, "filter", ""));
            // "control_type" rather than "type": inside a batch, "type" is the routing key.
            string type = ToStr(req, "control_type", ToStr(req, "controlType", ""));
            string autoId = ToStr(req, "automation_id", ToStr(req, "automationId", ""));

            var inspectReq = new Dictionary<string, object> {
                { "target", "active_window" },
                { "interactiveOnly", ToBool(req, "interactive_only", false) },
                { "maxDepth", ToInt(req, "max_depth", 8) }
            };
            var inspectRes = InspectUI(inspectReq);
            var elements = inspectRes["elements"] as List<Dictionary<string, object>>;

            Dictionary<string, object> targetElem = null;
            if (elements != null) {
                foreach (var el in elements) {
                    string elName = ToStr(el, "name", "");
                    string elType = ToStr(el, "type", "");
                    string elId = ToStr(el, "automationId", "");

                    bool hit;
                    if (autoId.Length > 0) {
                        hit = elId.Equals(autoId, StringComparison.OrdinalIgnoreCase);
                    } else if (name.Length > 0) {
                        hit = elName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
                    } else {
                        hit = type.Length > 0;
                    }

                    if (hit && (type.Length == 0 || elType.Equals(type, StringComparison.OrdinalIgnoreCase))) {
                        targetElem = el;
                        break;
                    }
                }
            }

            var res = new Dictionary<string, object>();
            if (targetElem == null) {
                res["status"] = "error";
                res["message"] = "Element not found matching "
                    + (autoId.Length > 0 ? "automation_id: " + autoId : "name: " + name)
                    + (type.Length > 0 ? " (control_type: " + type + ")" : "");
                return res;
            }

            var center = targetElem["center"] as Dictionary<string, object>;
            int cx = Convert.ToInt32(center["x"]);
            int cy = Convert.ToInt32(center["y"]);

            var clickReq = new Dictionary<string, object> {
                { "x", cx }, { "y", cy },
                { "button", req.ContainsKey("button") ? req["button"] : "left" }
            };
            MouseClick(clickReq);

            res["status"] = "ok";
            res["clickedElement"] = targetElem;
            res["clickedCoords"] = center;
            return res;
        }
    }
}
