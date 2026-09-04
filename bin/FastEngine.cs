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

            serializer.MaxJsonLength = int.MaxValue;

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

            Console.WriteLine("FastEngine v1.0.0. Usage: FastEngine.exe --daemon OR FastEngine.exe <cmd> '<json_payload>'");
        }

        static void RunDaemon() {
            Console.WriteLine("{\"ready\":true,\"version\":\"1.0.0\"}");
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
                        return CaptureScreenshot(req);

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

        static ImageCodecInfo GetEncoder(ImageFormat format) {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs) {
                if (codec.FormatID == format.Guid) return codec;
            }
            return codecs[0];
        }

        static Dictionary<string, object> MouseMove(Dictionary<string, object> req) {
            int x = Convert.ToInt32(req["x"]);
            int y = Convert.ToInt32(req["y"]);
            bool smooth = req.ContainsKey("smooth") && Convert.ToBoolean(req["smooth"]);

            if (smooth) {
                POINT start;
                GetCursorPos(out start);
                int steps = 10;
                for (int i = 1; i <= steps; i++) {
                    int cx = start.X + (x - start.X) * i / steps;
                    int cy = start.Y + (y - start.Y) * i / steps;
                    SetCursorPos(cx, cy);
                    Thread.Sleep(5);
                }
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
                Thread.Sleep(5);
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
                Thread.Sleep(10);
                mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
                if (i < clicks - 1) Thread.Sleep(50);
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
            Thread.Sleep(20);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);

            int steps = Math.Max(5, duration / 10);
            for (int i = 1; i <= steps; i++) {
                int cx = x1 + (x2 - x1) * i / steps;
                int cy = y1 + (y2 - y1) * i / steps;
                SetCursorPos(cx, cy);
                Thread.Sleep(duration / steps);
            }

            SetCursorPos(x2, y2);
            Thread.Sleep(20);
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
                Thread.Sleep(5);
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

                Thread.Sleep(10);
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
                    if (delayMs > 0) Thread.Sleep(delayMs);
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
                Thread.Sleep(5);
            }

            Thread.Sleep(10);

            for (int i = vks.Count - 1; i >= 0; i--) {
                SendKey(vks[i], false);
                Thread.Sleep(5);
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
                Thread.Sleep(10);
                SendKey(vk, false);
            }

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["key"] = key;
            res["action"] = action;
            return res;
        }

        static Dictionary<string, object> ExecuteBatch(Dictionary<string, object> req) {
            var results = new List<Dictionary<string, object>>();

            if (req.ContainsKey("actions") && req["actions"] is IEnumerable) {
                IEnumerable list = req["actions"] as IEnumerable;
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

                    string type = act.ContainsKey("type") && act["type"] != null ? act["type"].ToString().ToLower() : "";
                    // Batch items may name the action via "cmd" instead of "type" — fall back to it
                    // so cmd:"wait" is recognized the same way type:"wait" already is.
                    if (string.IsNullOrEmpty(type) && act.ContainsKey("cmd") && act["cmd"] != null) {
                        type = act["cmd"].ToString().ToLower();
                    }
                    if (!act.ContainsKey("cmd")) act["cmd"] = type;

                    if (type == "wait" || type == "sleep" || type == "delay") {
                        int ms = act.ContainsKey("ms") ? Convert.ToInt32(act["ms"]) : 100;
                        Thread.Sleep(ms);
                        var waitRes = new Dictionary<string, object> { { "status", "ok" }, { "action", "wait" }, { "ms", ms } };
                        results.Add(waitRes);
                    } else {
                        var actRes = DispatchCommand(act);
                        results.Add(actRes);
                    }
                }
            }

            var res = new Dictionary<string, object>();
            res["status"] = "ok";
            res["count"] = results.Count;
            res["results"] = results;
            return res;
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
            string target = req.ContainsKey("target") && req["target"] != null ? req["target"].ToString() : "";
            string action = req.ContainsKey("action") && req["action"] != null ? req["action"].ToString().ToLower() : "focus";

            IntPtr targetHwnd = IntPtr.Zero;
            string foundTitle = "";

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

            var res = new Dictionary<string, object>();
            if (targetHwnd == IntPtr.Zero) {
                res["status"] = "error";
                res["message"] = "Window not found matching: " + target;
                return res;
            }

            if (action == "minimize") {
                ShowWindowAsync(targetHwnd, SW_MINIMIZE);
            } else if (action == "maximize") {
                ShowWindowAsync(targetHwnd, SW_MAXIMIZE);
            } else if (action == "restore") {
                ShowWindowAsync(targetHwnd, SW_RESTORE);
            } else if (action == "close") {
                SendMessage(targetHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            } else {
                if (IsIconic(targetHwnd)) ShowWindowAsync(targetHwnd, SW_RESTORE);
                SetForegroundWindow(targetHwnd);
            }

            res["status"] = "ok";
            res["hwnd"] = targetHwnd.ToInt64();
            res["title"] = foundTitle;
            res["action"] = action;
            return res;
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
            string name = req.ContainsKey("name") && req["name"] != null ? req["name"].ToString() : "";
            string type = req.ContainsKey("type") && req["type"] != null ? req["type"].ToString() : "";

            var inspectReq = new Dictionary<string, object> { { "target", "active_window" } };
            var inspectRes = InspectUI(inspectReq);
            var elements = inspectRes["elements"] as List<Dictionary<string, object>>;

            Dictionary<string, object> targetElem = null;
            if (elements != null) {
                foreach (var el in elements) {
                    string elName = el.ContainsKey("name") && el["name"] != null ? el["name"].ToString() : "";
                    string elType = el.ContainsKey("type") && el["type"] != null ? el["type"].ToString() : "";

                    if (!string.IsNullOrEmpty(name) && elName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) {
                        if (string.IsNullOrEmpty(type) || elType.Equals(type, StringComparison.OrdinalIgnoreCase)) {
                            targetElem = el;
                            break;
                        }
                    }
                }
            }

            var res = new Dictionary<string, object>();
            if (targetElem == null) {
                res["status"] = "error";
                res["message"] = "Element not found matching name: " + name;
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
