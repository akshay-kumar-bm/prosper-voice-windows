#nullable disable
// Prosper inside PowerToys Command Palette: what used to be the tray app. Command Palette runs this extension as an
// out-of-process COM server on MTA threads; Prosper's own UI (the recording pill, the answer card, the Allow dialog)
// and the clipboard need an STA thread with a message loop, so the engine owns one and everything is marshalled to it.
//
// The flow is Prosper's: start recording (from a palette command, the Dock, the pill or Ctrl+Alt+Space/A), stop,
// transcribe, then dictation / spoken edit of the selection / Assistant, and the text lands in the app the user was in.
// What's new is finding that app: when a command runs from the palette or the Dock, those are in front, so the
// engine remembers the last window that wasn't Command Palette and returns to it before reading the selection.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Prosper
{
    enum EngineMode { Idle, Recording, Working }

    /// <summary>Thread-safe entry points for the palette. Every call is queued onto Prosper's UI thread.</summary>
    static class Engine
    {
        static readonly object Gate = new object();
        static Thread thread;
        static App app;

        /// <summary>Raised (on Prosper's UI thread) whenever the mode, step or a result changes.</summary>
        public static event Action Changed;

        public static EngineMode Mode { get; internal set; }
        public static bool Assistant { get; internal set; }
        public static bool ScreenAdded { get; internal set; }
        public static string Step { get; internal set; }
        public static string LastConversationId { get; internal set; }
        public static string LastMessage { get; internal set; }
        internal static readonly Stopwatch Since = new Stopwatch();
        public static TimeSpan Elapsed { get { return Since.Elapsed; } }

        internal static void Raise() { var h = Changed; if (h != null) try { h(); } catch { } }

        /// <summary>Starts Prosper's UI thread once; later calls return straight away.</summary>
        public static void Start()
        {
            lock (Gate)
            {
                if (thread != null) return;
                var ready = new ManualResetEventSlim(false);
                thread = new Thread(() =>
                {
                    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var g = Graphics.FromHwnd(IntPtr.Zero)) Floating.Dpi = g.DpiX / 96f;
                    Application.ThreadException += (s, e) => Log.Write("error: " + e.Exception.GetType().Name);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Log.Write("start (Command Palette extension)");
                    app = new App();
                    ready.Set();
                    Application.Run(app);
                }) { IsBackground = true, Name = "Prosper UI" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                ready.Wait();
            }
        }

        public static void Post(Action a) { Start(); app.Ui.BeginInvoke(a); }

        public static T Run<T>(Func<T> f) { Start(); return (T)app.Ui.Invoke(f); }

        public static void Run(Action a) { Start(); app.Ui.Invoke(a); }

        // ---- what the palette commands do ----

        /// <summary>Starts recording (dictation or Assistant), or finishes the recording in progress.</summary>
        public static void Toggle(bool assistant) { Post(() => app.Toggle(assistant)); }

        public static void Finish() { Post(() => { if (Mode == EngineMode.Recording) app.Stop(); }); }

        public static void Cancel() { Post(() => app.CancelRecording()); }

        public static void AddScreen() { Post(() => app.AddScreen()); }

        /// <summary>Pastes [text] into the app the user was in before Command Palette.</summary>
        public static void InsertIntoApp(string text) { Post(async () => await app.InsertIntoLastApp(text)); }

        /// <summary>Leaves [text] on the clipboard (never synced to the cloud).</summary>
        public static void Copy(string text) { Run(() => Clip.Copy(text)); }

        /// <summary>A short note in the Prosper pill.</summary>
        public static void Say(string text, int ms = 3000, bool error = false) { Post(() => app.Say(text, ms, error)); }

        /// <summary>Asks on screen before an MCP tool runs (blocks the calling worker thread).</summary>
        public static Approval Confirm(string tool, string args) { Start(); return app.Confirm(tool, args); }

        /// <summary>The name of the app the user was in before Command Palette ("Outlook", "Slack").</summary>
        public static string LastAppName { get { Start(); return app.LastAppName; } }

        public static void ApplySettings() { Post(() => app.ApplySettings()); }

        /// <summary>Shows a step in the pill for work started from the palette (when nothing is recording).</summary>
        public static void ShowWorking(string step) { Post(() => app.ShowWorking(step)); }

        public static void HideWorking() { Post(() => app.HideWorking()); }

        public static void Shutdown()
        {
            lock (Gate)
            {
                if (app == null) return;
                try { app.Ui.Invoke((Action)(() => app.Quit())); } catch { }
            }
        }
    }

    /// <summary>Receives the global shortcuts (RegisterHotKey: no keyboard hook, nothing else is read).</summary>
    class Hotkeys : NativeWindow, IDisposable
    {
        public event Action<int> Pressed;
        readonly List<int> ids = new List<int>();
        public Hotkeys() { CreateHandle(new CreateParams()); }
        public bool Add(int id, Keys key)
        {
            bool ok = Native.RegisterHotKey(Handle, id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)key);
            if (ok) ids.Add(id);
            return ok;
        }
        public void Clear() { foreach (var id in ids) Native.UnregisterHotKey(Handle, id); ids.Clear(); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
        public void Dispose() { Clear(); DestroyHandle(); }
    }

    /// <summary>
    /// Remembers the last window in front that isn't Command Palette (or Prosper), through the system's foreground
    /// event (an out-of-context WinEvent: no hook DLL, no keystrokes read).
    /// </summary>
    class LastApp : IDisposable
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

        const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
        readonly WinEventProc proc; // kept alive: Windows calls it
        readonly IntPtr hook;
        readonly Dictionary<uint, bool> paletteProcess = new Dictionary<uint, bool>();
        readonly uint self = (uint)Process.GetCurrentProcess().Id;

        IntPtr window;
        public IntPtr Window { get { return window; } private set { if (value != window) { window = value; Name = Target.AppName(value); } } }

        /// <summary>The remembered app's name, kept up to date as the foreground changes (cheap to read anywhere).</summary>
        public volatile string Name;

        public LastApp()
        {
            proc = (h, ev, hwnd, o, c, t, time) => { if (o == 0 && IsExternal(hwnd)) Window = GetAncestor(hwnd, 2); };
            hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            var now = Native.GetForegroundWindow();
            if (IsExternal(now)) Window = now;
        }

        /// <summary>A real app window: not Command Palette, its Dock, PowerToys or Prosper itself.</summary>
        public bool IsExternal(IntPtr w)
        {
            if (w == IntPtr.Zero || !IsWindow(w)) return false;
            uint pid; Native.GetWindowThreadProcessId(w, out pid);
            if (pid == 0 || pid == self) return false;
            bool palette;
            if (!paletteProcess.TryGetValue(pid, out palette))
            {
                try
                {
                    string n = Process.GetProcessById((int)pid).ProcessName;
                    palette = n.StartsWith("Microsoft.CmdPal", StringComparison.OrdinalIgnoreCase) || n.StartsWith("PowerToys", StringComparison.OrdinalIgnoreCase);
                }
                catch { palette = false; }
                if (paletteProcess.Count > 200) paletteProcess.Clear();
                paletteProcess[pid] = palette;
            }
            return !palette;
        }

        /// <summary>
        /// Waits for Command Palette to step aside (it hides itself after a command runs) and, if Windows doesn't hand
        /// the focus back by itself, brings the remembered app to the front. Returns the window now in front.
        /// </summary>
        public async Task<IntPtr> Return()
        {
            for (int i = 0; i < 12; i++)
            {
                var fg = Native.GetForegroundWindow();
                if (IsExternal(fg)) return fg;
                await Task.Delay(40);
            }
            var w = Window;
            if (w != IntPtr.Zero && IsWindow(w) && IsWindowVisible(w)) await Activate(w);
            return Native.GetForegroundWindow();
        }

        public async Task Activate(IntPtr w)
        {
            if (Native.GetForegroundWindow() == w) return;
            if (IsIconic(w)) ShowWindow(w, 9); // SW_RESTORE
            // A background process may only take the foreground right after input: a lone Alt tap provides it.
            keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
            Native.SetForegroundWindow(w);
            await Task.Delay(150);
        }

        public void Dispose() { if (hook != IntPtr.Zero) UnhookWinEvent(hook); }
    }

    class App : ApplicationContext
    {
        const int HkDictate = 1, HkAssist = 2, HkScreen = 3;
        public readonly Control Ui = new Control();
        readonly Bar bar = new Bar();
        readonly AnswerCard card = new AnswerCard();
        readonly Hotkeys keys = new Hotkeys();
        readonly LastApp lastApp;
        readonly Recorder recorder = new Recorder("prosperrec");
        readonly System.Windows.Forms.Timer autoStop = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 }; // 10 minutes: under OpenAI's 25 MB

        EngineMode mode;
        bool assist;
        Target target;
        byte[] screen;

        Settings S { get { return Settings.Current; } }

        EngineMode Mode
        {
            get { return mode; }
            set
            {
                mode = value; Engine.Mode = value;
                if (value == EngineMode.Recording) Engine.Since.Restart();
                if (value == EngineMode.Idle) { Engine.Since.Reset(); Engine.Step = null; Engine.ScreenAdded = false; }
                Engine.Raise();
            }
        }

        public App()
        {
            Ui.CreateControl();
            lastApp = new LastApp();

            bar.Tap += () => Toggle(false);
            bar.DoubleTap += () => Toggle(true);
            bar.Hold += () => { };
            bar.ShowMenu += () => Menu().Show(Cursor.Position);
            bar.Finish += () => { if (mode == EngineMode.Recording) Stop(); };
            bar.Cancel += CancelRecording;
            bar.ToggleScreen += AddScreen;
            bar.LevelSource = () => recorder.Level;
            bar.PlaceSaved();
            bar.KeepVisible = S.ShowBubble;

            card.Insert += async () => await Insert(card.Answer);
            autoStop.Tick += (s, e) => { if (mode == EngineMode.Recording) Stop(); };

            keys.Pressed += id =>
            {
                if (id == HkDictate) Toggle(false);
                else if (id == HkAssist) Toggle(true);
                else if (id == HkScreen) { if (mode == EngineMode.Recording && assist) AddScreen(); }
            };
            RegisterShortcuts();
        }

        void RegisterShortcuts()
        {
            keys.Clear();
            if (!S.GlobalShortcuts) return;
            var taken = new List<string>();
            if (!keys.Add(HkDictate, Keys.Space)) taken.Add("Ctrl+Alt+Space");
            if (!keys.Add(HkAssist, Keys.A)) taken.Add("Ctrl+Alt+A");
            if (!keys.Add(HkScreen, Keys.S)) taken.Add("Ctrl+Alt+S");
            if (taken.Count > 0) Log.Write("shortcuts taken by another app: " + string.Join(", ", taken));
        }

        public void ApplySettings()
        {
            bar.KeepVisible = S.ShowBubble;
            RegisterShortcuts();
        }

        ContextMenuStrip Menu()
        {
            var m = Style.NewMenu();
            m.Items.Add("Dictate  (Ctrl+Alt+Space)", null, (s, e) => Toggle(false));
            m.Items.Add("Assistant  (Ctrl+Alt+A)", null, (s, e) => Toggle(true));
            m.Items.Add("-");
            m.Items.Add(S.ShowBubble ? "Hide the bar when idle" : "Keep the bar on screen", null, (s, e) => { S.ShowBubble = !S.ShowBubble; Settings.Save(); ApplySettings(); });
            m.Items.Add("Move the bar back to the bottom", null, (s, e) => { S.BarX = -1; S.BarY = -1; Settings.Save(); bar.PlaceSaved(); });
            m.Items.Add("-");
            var more = m.Items.Add("History and settings: open Command Palette, type \"Prosper\"");
            more.Enabled = false;
            return m;
        }

        public void Say(string message, int ms, bool error = false)
        {
            Engine.LastMessage = message; Engine.Raise();
            bar.Say(message, ms, error);
        }

        public string LastAppName { get { return lastApp.Name; } }

        // ---- recording ----

        public async void Toggle(bool assistant)
        {
            if (mode == EngineMode.Working) return;
            if (mode == EngineMode.Recording) { Stop(); return; }
            if (!S.Configured) { Say("Add your OpenAI API key: Command Palette → Prosper settings", 6000, true); return; }
            card.Hide();
            assist = assistant; Engine.Assistant = assistant; screen = null; Mode = EngineMode.Working; // until the target is read
            // From the palette or the Dock, Command Palette is in front: go back to the app the user was typing in.
            await lastApp.Return();
            target = await Target.Capture();
            string why = recorder.Start();
            if (why != null)
            {
                Mode = EngineMode.Idle;
                Say("Microphone unavailable: " + why + ". Check Settings → Privacy & security → Microphone.", 6000, true);
                return;
            }
            Mode = EngineMode.Recording;
            bar.State = assist ? BarState.Assisting : BarState.Dictating;
            autoStop.Start();
            Log.Write("recording " + (assist ? "assistant" : target.Selected != null ? "edit" : "dictation"));
        }

        public void CancelRecording()
        {
            if (mode != EngineMode.Recording) return;
            autoStop.Stop();
            recorder.Cancel();
            Done();
            Say("Cancelled", 1200);
        }

        /// <summary>The screen behind the bar, taken when the user asks for it.</summary>
        public async void AddScreen()
        {
            if (mode != EngineMode.Recording || !assist) return;
            if (bar.ScreenAdded) { screen = null; bar.ScreenAdded = false; Engine.ScreenAdded = false; Engine.Raise(); return; }
            // The bar is kept out of captures where Windows allows it; elsewhere it steps aside for the moment.
            bool hide = !bar.ExcludedFromCapture;
            if (hide) { bar.Hide(); await Task.Delay(150); }
            // Command Palette may still be closing if this came from the palette.
            if (!lastApp.IsExternal(Native.GetForegroundWindow())) await Task.Delay(250);
            try { screen = await Task.Run(() => Shot.Take(target.Window)); }
            catch (Exception e) { Log.Write("screenshot failed: " + e.GetType().Name); screen = null; }
            if (hide) bar.Show();
            if (mode == EngineMode.Recording) { bar.ScreenAdded = screen != null; Engine.ScreenAdded = screen != null; Engine.Raise(); }
        }

        public async void Stop()
        {
            autoStop.Stop();
            int peak;
            string file = recorder.Stop(out peak);
            Mode = EngineMode.Working;
            bar.State = BarState.Working;
            var t = target; bool a = assist; byte[] image = assist ? screen : null;
            string kind = a ? "assistant" : t.Selected != null ? "rewrite" : "dictation";
            if (file == null || peak < 50)
            {
                Done();
                Say(file == null ? "Nothing was recorded" : "The microphone heard silence", 4000, true);
                return;
            }
            SetStep("Transcribing");
            string heard = null; AssistResult result = null; string error = null;
            Action<string> step = m => Ui.BeginInvoke((Action)(() => SetStep(m)));
            try
            {
                result = await Task.Run(() =>
                {
                    string said = Ai.Transcribe(file);
                    if (said.Length == 0) throw new Exception("Didn't catch that. Try again.");
                    heard = said;
                    if (a)
                    {
                        step(image != null ? "Thinking with your screen" : "Thinking");
                        bool useMcp = S.McpOn && S.McpServer.Length > 0;
                        string token = useMcp ? Mcp.AccessToken() : null;
                        var ctx = new AssistContext
                        {
                            App = t.Editable ? t.App : (t.App ?? "the computer") + " (no text field is focused: your answer is shown to the user and copied)",
                            Selected = t.Selected, AboutMe = S.AboutMe,
                        };
                        return Ai.RunAssistant(said, ctx, useMcp, token, null, step, Confirm, image);
                    }
                    if (t.Selected != null) { step("Rewriting"); return new AssistResult { Text = Ai.VoiceEdit(said, t.Selected) }; }
                    if (S.Cleanup) { step("Cleaning up"); return new AssistResult { Text = Ai.CleanDictation(said) }; }
                    return new AssistResult { Text = said };
                });
            }
            catch (Exception e) { while (e is AggregateException && e.InnerException != null) e = e.InnerException; error = e.Message; }
            finally { try { File.Delete(file); } catch { } }

            if (heard != null || error == null)
            {
                var c = History.Add(null, kind, t.App, heard ?? "", result, error, image);
                Engine.LastConversationId = c.Id;
            }
            if (error != null) { Done(); Say(error, 6000, true); return; }

            bool type = !result.IsStatus && (t.Editable || t.Selected != null);
            if (a) Clip.Copy(result.Text); // every Assistant answer is ready to paste again
            if (type)
            {
                await Insert(result.Text);
                Done();
                Say(a ? "Typed · copied" : "Pasted", 1800);
            }
            else if (a)
            {
                Done();
                card.Show(result.Text, result.IsStatus, bar.PillOnScreen);
            }
            else
            {
                Clip.Copy(result.Text);
                Done();
                Say("Copied · no text field, paste it anywhere", 3000);
            }
        }

        void SetStep(string m) { bar.Step(m); Engine.Step = m; Engine.Raise(); }

        public void ShowWorking(string step)
        {
            if (mode != EngineMode.Idle) return;
            bar.State = BarState.Working; bar.Step(step);
        }

        public void HideWorking() { if (mode == EngineMode.Idle) bar.State = BarState.Rest; }

        void Done() { Mode = EngineMode.Idle; bar.State = BarState.Rest; }

        /// <summary>Pastes into the window that was in front when the recording started.</summary>
        async Task Insert(string text)
        {
            if (target == null) return;
            if (Native.GetForegroundWindow() != target.Window) await lastApp.Activate(target.Window);
            await Clip.Paste(text);
        }

        /// <summary>Pastes into the app the user was in before Command Palette (typed requests, history).</summary>
        public async Task InsertIntoLastApp(string text)
        {
            var w = await lastApp.Return();
            if (!lastApp.IsExternal(w)) { Clip.Copy(text); Say("Copied · no app to paste into", 3000); return; }
            await Clip.Paste(text);
            Say("Pasted", 1500);
        }

        /// <summary>From any worker thread: asks on screen before an MCP tool runs.</summary>
        public Approval Confirm(string tool, string args)
        {
            var answer = Approval.Deny;
            Ui.Invoke((Action)(() =>
            {
                using (var f = new ApprovalForm(tool, Ai.Describe(tool, args), args)) { f.ShowDialog(); answer = f.Result; }
            }));
            return answer;
        }

        public void Quit()
        {
            recorder.Cancel();
            keys.Dispose();
            lastApp.Dispose();
            bar.Hide(); card.Hide();
            ExitThread();
        }
    }
}
