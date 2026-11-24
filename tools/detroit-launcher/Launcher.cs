// DBH FSR/DLSS Launcher
// Configures the Detroit: Become Human FSR/DLSS RenoDX mod, installs it, patches the
// Epic exe's 70% resolution-scale minimum and launches the game.
// Written for the C# compiler built into Windows (C# 5), so no newer syntax is used.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DBH FSR/DLSS Launcher")]
[assembly: AssemblyVersion("1.2.0.0")]

namespace DbhLauncher
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch (Exception) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // Minimal INI reader/writer that only touches the one key it is asked to change.
    static class Ini
    {
        public static string Get(string path, string section, string key, string fallback)
        {
            if (!File.Exists(path)) return fallback;
            string header = "[" + section + "]";
            bool inside = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("["))
                {
                    inside = string.Equals(line, header, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inside) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(eq + 1).Trim();
            }
            return fallback;
        }

        public static void Set(string path, string section, string key, string value)
        {
            List<string> lines = File.Exists(path)
                ? new List<string>(File.ReadAllLines(path))
                : new List<string>();
            string header = "[" + section + "]";
            bool inside = false;
            int sectionStart = -1;
            int lastInSection = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("["))
                {
                    inside = string.Equals(line, header, StringComparison.OrdinalIgnoreCase);
                    if (inside) { sectionStart = i; lastInSection = i; }
                    continue;
                }
                if (!inside) continue;
                if (line.Length > 0) lastInSection = i;
                int eq = line.IndexOf('=');
                if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    if (line.Substring(eq + 1).Trim() == value) return;
                    lines[i] = key + "=" + value;
                    File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
                    return;
                }
            }
            if (sectionStart >= 0)
            {
                lines.Insert(lastInSection + 1, key + "=" + value);
            }
            else
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add(header);
                lines.Add(key + "=" + value);
            }
            File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
        }
    }

    enum ExeState { Missing, Unsupported, Original, Patched }

    class MainForm : Form
    {
        // ---- The Epic exe patch (the same 3-byte change as "Patch Detroit exe.cmd") ----
        const long ExpectedExeSize = 57782272L;
        const long OpcodeOffset = 0x15C676L;
        const long DisplacementOffset = 0x15C67AL;
        static readonly byte[] Opcode = new byte[] { 0xF3, 0x0F, 0x10, 0x05 };
        static readonly byte[] OriginalDisplacement = new byte[] { 0xCE, 0x0D, 0xD4, 0x01 };  // 0.7
        static readonly byte[] LoweredDisplacement = new byte[] { 0x02, 0x7C, 0x77, 0x01 };    // 0.1

        static readonly string[] ModFiles = new string[] {
            "renodx-detroitbecomehuman.addon64",
            "renodx-detroitbecomehuman-effects.addon64",
            "amd_fidelityfx_loader_vk.dll",
            "amd_fidelityfx_upscaler_vk.dll"
        };

        const string Section = "renodx-preset1";

        // One line of the settings list. Rows can be hidden when they do not apply.
        class Row
        {
            public Panel Panel;
            public bool Advanced;
            public Func<bool> Applies;
        }

        readonly string appDir;
        readonly string configPath;
        string epicAppName = "";
        bool loading = true;
        bool hasNvidia = true;
        bool showAdvanced = false;
        bool largeText = false;
        bool modDisabled = false;
        bool updatingMod = false;
        float uiScale = 1f;
        string lastLogKey = "";
        readonly List<Row> rows = new List<Row>();

        Label lblBanner;
        GroupBox grpGame, grpSettings, grpActions;
        TextBox txtFolder;
        Label lblExe, lblMod, lblReShade, lblDlss, lblSession, lblNote;
        ComboBox cmbUpscaler, cmbAa, cmbScale, cmbFsrVersion, cmbDlssPreset;
        NumericUpDown numSharp;
        CheckBox chkUncap, chkModEnabled, chkEpic, chkAutoPatch, chkLarge;
        Button btnInstall, btnPatch, btnRestore, btnLaunch, btnReset, btnAdvanced, btnUninstall, btnDiag;
        ToolTip tips = new ToolTip();

        public MainForm()
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            configPath = Path.Combine(appDir, "launcher.ini");
            largeText = Ini.Get(configPath, "Launcher", "LargeText", "0") == "1";
            float dpi = 96f;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
            }
            catch (Exception) { }
            float textScale = largeText ? 1.3f : 1f;
            uiScale = (dpi / 96f) * textScale;
            Text = "DBH FSR/DLSS Launcher";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f * textScale);
            hasNvidia = HasNvidiaGpu();
            BuildUi();
            Activated += delegate { RefreshStatus(); };
            Shown += delegate { Initialise(); };
        }

        int S(int value)
        {
            return (int)Math.Round(value * uiScale);
        }

        // ------------------------------------------------------------------ UI
        Label AddLabel(Control parent, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text; l.Left = S(x); l.Top = S(y); l.Width = S(w); l.Height = S(20);
            l.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(l);
            return l;
        }

        Panel AddRow(string label, bool advanced, Func<bool> applies)
        {
            Panel panel = new Panel();
            panel.Left = S(12); panel.Width = S(596); panel.Height = S(28);
            Label l = new Label();
            l.Text = label; l.Left = 0; l.Top = 0; l.Width = S(196); l.Height = S(26);
            l.TextAlign = ContentAlignment.MiddleLeft;
            panel.Controls.Add(l);
            grpSettings.Controls.Add(panel);
            Row row = new Row();
            row.Panel = panel; row.Advanced = advanced; row.Applies = applies;
            rows.Add(row);
            return panel;
        }

        ComboBox AddComboRow(string label, string[] items, string tip, bool advanced, Func<bool> applies)
        {
            Panel panel = AddRow(label, advanced, applies);
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Left = S(208); c.Top = 0; c.Width = S(300);
            c.Items.AddRange(items);
            c.SelectedIndexChanged += delegate { OnSettingChanged(); };
            panel.Controls.Add(c);
            tips.SetToolTip(c, tip);
            return c;
        }

        CheckBox AddCheckRow(string text, string tip, bool advanced, Func<bool> applies)
        {
            Panel panel = AddRow("", advanced, applies);
            CheckBox box = new CheckBox();
            box.Text = text;
            box.Left = S(208); box.Top = S(2); box.Width = S(388); box.Height = S(24);
            box.CheckedChanged += delegate { OnSettingChanged(); };
            panel.Controls.Add(box);
            tips.SetToolTip(box, tip);
            return box;
        }

        Button AddButtonRow(string text, bool advanced, Func<bool> applies)
        {
            Panel panel = AddRow("", advanced, applies);
            Button b = new Button();
            b.Text = text;
            b.Left = S(208); b.Top = 0; b.Width = S(300); b.Height = S(28);
            panel.Controls.Add(b);
            return b;
        }

        bool EffectiveDlss()
        {
            if (!hasNvidia) return false;
            int choice = cmbUpscaler == null ? 0 : cmbUpscaler.SelectedIndex;
            if (choice == 2) return true;
            if (choice == 1) return false;
            return File.Exists(Path.Combine(txtFolder.Text, "nvngx_dlss.dll"));
        }

        void BuildUi()
        {
            lblBanner = new Label();
            lblBanner.Left = S(10); lblBanner.Top = S(8); lblBanner.Width = S(620); lblBanner.Height = S(46);
            lblBanner.TextAlign = ContentAlignment.MiddleLeft;
            lblBanner.Padding = new Padding(S(10), 0, S(6), 0);
            lblBanner.Font = new Font("Segoe UI", 10.5f * (largeText ? 1.3f : 1f), FontStyle.Bold);
            lblBanner.ForeColor = Color.White;
            lblBanner.BackColor = Color.SteelBlue;
            Controls.Add(lblBanner);

            grpGame = new GroupBox();
            grpGame.Text = "Game"; grpGame.Left = S(10); grpGame.Top = S(62); grpGame.Width = S(620); grpGame.Height = S(160);
            Controls.Add(grpGame);

            txtFolder = new TextBox();
            txtFolder.Left = S(12); txtFolder.Top = S(22); txtFolder.Width = S(500); txtFolder.ReadOnly = true;
            grpGame.Controls.Add(txtFolder);
            Button browse = new Button();
            browse.Text = "Browse..."; browse.Left = S(520); browse.Top = S(20); browse.Width = S(88); browse.Height = S(26);
            browse.Click += delegate { BrowseForGame(); };
            grpGame.Controls.Add(browse);

            lblExe = AddLabel(grpGame, "", 12, 52, 596);
            lblMod = AddLabel(grpGame, "", 12, 74, 596);
            lblReShade = AddLabel(grpGame, "", 12, 96, 596);
            lblDlss = AddLabel(grpGame, "", 12, 118, 596);
            lblSession = AddLabel(grpGame, "", 12, 138, 596);

            grpSettings = new GroupBox();
            grpSettings.Text = "Mod settings (saved to the game's ReShade.ini)";
            grpSettings.Left = S(10); grpSettings.Top = S(230); grpSettings.Width = S(620);
            Controls.Add(grpSettings);

            cmbUpscaler = AddComboRow("Upscaler", new string[] { "Auto (DLSS on NVIDIA, otherwise FSR)", "FSR", "DLSS" },
                "Auto uses DLSS when an NVIDIA driver and nvngx_dlss.dll are present, otherwise FSR. Needs a game restart.",
                false, null);
            if (!hasNvidia)
            {
                cmbUpscaler.Enabled = false;
                tips.SetToolTip(cmbUpscaler, "No NVIDIA GPU was found, so DLSS is not available and FSR is used.");
            }
            cmbScale = AddComboRow("Quality preset",
                new string[] { "Game setting", "Native AA (100%)", "Quality (67%)", "Balanced (59%)", "Performance (50%)", "Ultra Performance (33%)" },
                "The game's render scale: Quality, Balanced, Performance or Ultra Performance (1.5x, 1.7x, 2x, 3x upscaling), written into the game's GraphicOptions.JSON at launch. Everything below Native AA is under the game's own 70% minimum and needs the exe patch.",
                false, null);
            chkUncap = AddCheckRow("Uncap FPS (sets the game's frame limit to Off at launch)",
                "Removes the game's frame-rate limit each time the game starts.", false, null);

            btnAdvanced = AddButtonRow("Show advanced settings", false, null);
            btnAdvanced.Click += delegate { ToggleAdvanced(); };

            cmbAa = AddComboRow("Temporal anti-aliasing", new string[] { "Native TAA (the game's own)", "DLSS / FSR" },
                "DLSS/FSR replaces the game's own TAA. At 100% render scale it is anti-aliasing only; below that it upscales.",
                true, null);

            Panel sharpRow = AddRow("Sharpening (0-100)", true, null);
            numSharp = new NumericUpDown();
            numSharp.Minimum = 0; numSharp.Maximum = 100; numSharp.Left = S(208); numSharp.Top = 0; numSharp.Width = S(80);
            numSharp.ValueChanged += delegate { OnSettingChanged(); };
            sharpRow.Controls.Add(numSharp);
            tips.SetToolTip(numSharp, "Sharpening applied to the DLSS/FSR output. 0 is off.");

            cmbFsrVersion = AddComboRow("FSR version", new string[] { "Auto (FSR 3.x)", "FSR 4 (AMD driver)" },
                "Auto uses the newest FSR 3.x. FSR 4 is only offered when the AMD graphics driver provides it (some Radeon cards); otherwise the newest 3.x is used. FSR 4 can be much slower on cards without dedicated hardware for it. Takes effect the next time FSR starts.",
                true, delegate { return !EffectiveDlss(); });
            cmbDlssPreset = AddComboRow("DLSS preset",
                new string[] { "Default", "A", "B", "C", "D", "E", "F", "J", "K", "L", "M" },
                "NVIDIA's DLSS render preset, applied to every quality level. Default lets the driver choose; an unsupported preset falls back to the default. Takes effect the next time DLSS starts.",
                true, delegate { return EffectiveDlss(); });

            btnReset = AddButtonRow("Reset to defaults", true, null);
            btnReset.Click += delegate { ResetDefaults(); };
            tips.SetToolTip(btnReset, "Puts every mod setting above back to its default value.");

            lblNote = AddLabel(grpSettings, "", 12, 0, 596);
            lblNote.ForeColor = Color.DarkRed;

            grpActions = new GroupBox();
            grpActions.Text = "Actions"; grpActions.Left = S(10); grpActions.Width = S(620); grpActions.Height = S(162);
            Controls.Add(grpActions);

            btnInstall = MakeButton(grpActions, "Install / update mod files", 12, 22, 192);
            btnInstall.Click += delegate { InstallMod(); };
            btnPatch = MakeButton(grpActions, "Patch exe (allow <70%)", 213, 22, 192);
            btnPatch.Click += delegate { PatchExe(); };
            btnRestore = MakeButton(grpActions, "Restore original exe", 414, 22, 192);
            btnRestore.Click += delegate { RestoreExe(); };

            Button open = MakeButton(grpActions, "Open game folder", 12, 56, 192);
            open.Click += delegate { OpenPath(txtFolder.Text); };
            Button log = MakeButton(grpActions, "Open ReShade.log", 213, 56, 192);
            log.Click += delegate { OpenPath(Path.Combine(txtFolder.Text, "ReShade.log")); };
            btnDiag = MakeButton(grpActions, "Copy diagnostics", 414, 56, 192);
            btnDiag.Click += delegate { CopyDiagnostics(); };
            tips.SetToolTip(btnDiag, "Copies the versions, your settings and the key lines of ReShade.log to the clipboard, ready to paste into a bug report.");

            btnUninstall = MakeButton(grpActions, "Uninstall mod", 12, 90, 192);
            btnUninstall.Click += delegate { UninstallMod(); };
            tips.SetToolTip(btnUninstall, "Removes the mod files from the game folder and restores the original exe. ReShade itself is not touched.");

            chkLarge = new CheckBox();
            chkLarge.Text = "Large text (for TVs)";
            chkLarge.Left = S(213); chkLarge.Top = S(92); chkLarge.Width = S(190); chkLarge.Height = S(24);
            chkLarge.Checked = largeText;
            chkLarge.CheckedChanged += delegate { OnLargeTextChanged(); };
            grpActions.Controls.Add(chkLarge);

            chkModEnabled = new CheckBox();
            chkModEnabled.Text = "Mod enabled";
            chkModEnabled.Left = S(414); chkModEnabled.Top = S(92); chkModEnabled.Width = S(190); chkModEnabled.Height = S(24);
            chkModEnabled.Checked = true;
            chkModEnabled.CheckedChanged += delegate { OnModEnabledChanged(); };
            tips.SetToolTip(chkModEnabled, "Untick to play the game without the mod (no upscaling, original look). The mod files are kept and tick it again to turn the mod back on.");
            grpActions.Controls.Add(chkModEnabled);

            chkAutoPatch = new CheckBox();
            chkAutoPatch.Text = "Patch the exe automatically (install and launch)";
            chkAutoPatch.Left = S(14); chkAutoPatch.Top = S(126); chkAutoPatch.Width = S(340); chkAutoPatch.Height = S(24); chkAutoPatch.Checked = true;
            chkAutoPatch.CheckedChanged += delegate { SaveLauncherConfig(); RefreshStatus(); };
            grpActions.Controls.Add(chkAutoPatch);

            chkEpic = new CheckBox();
            chkEpic.Text = "Start through the Epic launcher";
            chkEpic.Left = S(364); chkEpic.Top = S(126); chkEpic.Width = S(244); chkEpic.Height = S(24); chkEpic.Checked = true;
            chkEpic.CheckedChanged += delegate { SaveLauncherConfig(); };
            grpActions.Controls.Add(chkEpic);

            btnLaunch = new Button();
            btnLaunch.Text = "Launch game";
            btnLaunch.Left = S(10); btnLaunch.Width = S(620); btnLaunch.Height = S(62);
            btnLaunch.Font = new Font("Segoe UI", 13f * (largeText ? 1.3f : 1f), FontStyle.Bold);
            btnLaunch.Click += delegate { LaunchGame(); };
            Controls.Add(btnLaunch);

            Relayout();
        }

        Button MakeButton(Control parent, string text, int x, int y, int w)
        {
            Button b = new Button();
            b.Text = text; b.Left = S(x); b.Top = S(y); b.Width = S(w); b.Height = S(28);
            parent.Controls.Add(b);
            return b;
        }

        // Stacks the visible settings rows and sizes the groups and the window.
        void Relayout()
        {
            int y = S(24);
            foreach (Row row in rows)
            {
                bool show = (!row.Advanced || showAdvanced) && (row.Applies == null || row.Applies());
                row.Panel.Visible = show;
                if (!show) continue;
                row.Panel.Top = y;
                y += row.Panel.Height + S(4);
            }
            lblNote.Top = y;
            lblNote.Visible = lblNote.Text.Length > 0;
            y += S(24);
            grpSettings.Height = y + S(4);
            grpActions.Top = grpSettings.Top + grpSettings.Height + S(8);
            btnLaunch.Top = grpActions.Top + grpActions.Height + S(8);
            ClientSize = new Size(S(640), btnLaunch.Top + btnLaunch.Height + S(10));
            btnAdvanced.Text = showAdvanced ? "Hide advanced settings" : "Show advanced settings";
        }

        // Turning the mod off renames the two add-ons so ReShade no longer loads them.
        void OnModEnabledChanged()
        {
            if (updatingMod || loading) return;
            if (!IsGameFolder(txtFolder.Text)) return;
            bool enable = chkModEnabled.Checked;
            if (!EnsureGameClosed())
            {
                updatingMod = true; chkModEnabled.Checked = !enable; updatingMod = false;
                return;
            }
            try
            {
                // Without the mod the game must not keep a low render scale from an upscaling preset.
                if (!enable)
                {
                    string json = Path.Combine(txtFolder.Text, "GraphicOptions.JSON");
                    if (File.Exists(json))
                    {
                        string text = File.ReadAllText(json);
                        string fixedText = Regex.Replace(text, "(\"RESOLUTION_SCALING\"\\s*:\\s*)[-+0-9.eE]+", "${1}1.0");
                        if (fixedText != text) File.WriteAllText(json, fixedText, new UTF8Encoding(false));
                    }
                }
                for (int i = 0; i < 2; i++)
                {
                    string path = Path.Combine(txtFolder.Text, ModFiles[i]);
                    string off = path + ".off";
                    if (enable && File.Exists(off))
                    {
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(off, path);
                    }
                    else if (!enable && File.Exists(path))
                    {
                        if (File.Exists(off)) File.Delete(off);
                        File.Move(path, off);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                ReportAccessDenied();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not change the mod state:\n" + ex.Message, Text);
            }
            RefreshStatus();
        }

        void ToggleAdvanced()
        {
            showAdvanced = !showAdvanced;
            Relayout();
        }

        void OnLargeTextChanged()
        {
            if (loading) return;
            try
            {
                Ini.Set(configPath, "Launcher", "LargeText", chkLarge.Checked ? "1" : "0");
            }
            catch (Exception) { }
            Application.Restart();
        }

        // ------------------------------------------------------- startup / config
        void Initialise()
        {
            string folder = Ini.Get(configPath, "Launcher", "GameFolder", "");
            string app = Ini.Get(configPath, "Launcher", "EpicApp", "");
            if (!IsGameFolder(folder))
            {
                FindGame(out folder, out app);
            }
            epicAppName = app;
            chkEpic.Checked = Ini.Get(configPath, "Launcher", "UseEpic", "1") != "0";
            chkAutoPatch.Checked = Ini.Get(configPath, "Launcher", "AutoPatch", "1") != "0";
            SetGameFolder(folder);
            loading = false;
        }

        static bool IsGameFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "DetroitBecomeHuman.exe"));
        }

        void FindGame(out string folder, out string app)
        {
            folder = ""; app = "";
            string manifests = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Epic\EpicGamesLauncher\Data\Manifests");
            try
            {
                if (Directory.Exists(manifests))
                {
                    foreach (string file in Directory.GetFiles(manifests, "*.item"))
                    {
                        string text = File.ReadAllText(file);
                        if (text.IndexOf("DetroitBecomeHuman.exe", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        Match loc = Regex.Match(text, "\"InstallLocation\"\\s*:\\s*\"([^\"]+)\"");
                        Match name = Regex.Match(text, "\"AppName\"\\s*:\\s*\"([^\"]+)\"");
                        if (!loc.Success) continue;
                        string candidate = loc.Groups[1].Value.Replace("\\\\", "\\");
                        if (IsGameFolder(candidate))
                        {
                            folder = candidate;
                            if (name.Success) app = name.Groups[1].Value;
                            return;
                        }
                    }
                }
            }
            catch (Exception) { }
            string[] guesses = new string[] {
                @"C:\SSD Games\DetroitBecomeHuman",
                @"C:\Program Files\Epic Games\DetroitBecomeHuman",
                @"C:\Program Files (x86)\Epic Games\DetroitBecomeHuman",
                @"D:\Epic Games\DetroitBecomeHuman",
                @"D:\Games\DetroitBecomeHuman",
                @"E:\Games\DetroitBecomeHuman"
            };
            foreach (string g in guesses)
            {
                if (IsGameFolder(g)) { folder = g; return; }
            }
        }

        void BrowseForGame()
        {
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "Choose the folder that contains DetroitBecomeHuman.exe";
            if (IsGameFolder(txtFolder.Text)) dlg.SelectedPath = txtFolder.Text;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (!IsGameFolder(dlg.SelectedPath))
            {
                MessageBox.Show(this, "DetroitBecomeHuman.exe was not found in that folder.", Text);
                return;
            }
            SetGameFolder(dlg.SelectedPath);
        }

        void SetGameFolder(string folder)
        {
            txtFolder.Text = folder ?? "";
            SaveLauncherConfig();
            LoadSettings();
            RefreshStatus();
        }

        void SaveLauncherConfig()
        {
            if (loading && txtFolder.Text.Length == 0) return;
            try
            {
                Ini.Set(configPath, "Launcher", "GameFolder", txtFolder.Text);
                Ini.Set(configPath, "Launcher", "EpicApp", epicAppName);
                Ini.Set(configPath, "Launcher", "UseEpic", chkEpic.Checked ? "1" : "0");
                Ini.Set(configPath, "Launcher", "AutoPatch", chkAutoPatch.Checked ? "1" : "0");
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ settings
        string ReShadeIni { get { return Path.Combine(txtFolder.Text, "ReShade.ini"); } }

        static int ToInt(string text, int fallback)
        {
            float value;
            if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
                return (int)Math.Round(value);
            return fallback;
        }

        static void SelectIndex(ComboBox box, int index)
        {
            if (index < 0 || index >= box.Items.Count) index = 0;
            box.SelectedIndex = index;
        }

        void LoadSettings()
        {
            bool was = loading;
            loading = true;
            string ini = ReShadeIni;
            SelectIndex(cmbUpscaler, ToInt(Ini.Get(ini, Section, "UpscalerBackend", "0"), 0));
            if (!hasNvidia) SelectIndex(cmbUpscaler, 1);
            SelectIndex(cmbAa, ToInt(Ini.Get(ini, Section, "DLSSMode", "1"), 1));
            SelectIndex(cmbScale, ToInt(Ini.Get(ini, Section, "ResolutionScalePreset", "0"), 0));
            SelectIndex(cmbFsrVersion, ToInt(Ini.Get(ini, Section, "FSRVersion", "0"), 0));
            SelectIndex(cmbDlssPreset, ToInt(Ini.Get(ini, Section, "DLSSPreset", "0"), 0));
            chkUncap.Checked = ToInt(Ini.Get(ini, Section, "UncapFps", "0"), 0) != 0;
            int sharp = ToInt(Ini.Get(ini, Section, "DLAASharpening", "0"), 0);
            numSharp.Value = Math.Max(0, Math.Min(100, sharp));
            loading = was;
        }

        void OnSettingChanged()
        {
            if (loading) return;
            if (!IsGameFolder(txtFolder.Text)) return;
            try
            {
                string ini = ReShadeIni;
                Ini.Set(ini, Section, "UpscalerBackend", cmbUpscaler.SelectedIndex.ToString());
                Ini.Set(ini, Section, "DLSSMode", cmbAa.SelectedIndex.ToString());
                Ini.Set(ini, Section, "ResolutionScalePreset", cmbScale.SelectedIndex.ToString());
                Ini.Set(ini, Section, "FSRVersion", cmbFsrVersion.SelectedIndex.ToString());
                Ini.Set(ini, Section, "DLSSPreset", cmbDlssPreset.SelectedIndex.ToString());
                // Fixed values that match how Detroit renders (no longer user options).
                Ini.Set(ini, Section, "FSRJitterMode", "3");
                Ini.Set(ini, Section, "FSRJitterCancel", "0");
                Ini.Set(ini, Section, "DLSSJitterMode", "3");
                Ini.Set(ini, Section, "DLSSMVJittered", "0");
                Ini.Set(ini, Section, "UncapFps", chkUncap.Checked ? "1" : "0");
                Ini.Set(ini, Section, "DLAASharpening", ((int)numSharp.Value).ToString());
            }
            catch (UnauthorizedAccessException)
            {
                ReportAccessDenied();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the settings:\n" + ex.Message, Text);
            }
            RefreshStatus();
        }

        // -------------------------------------------------------------- status
        static bool GameRunning()
        {
            return Process.GetProcessesByName("DetroitBecomeHuman").Length > 0;
        }

        ExeState GetExeState()
        {
            string exe = Path.Combine(txtFolder.Text, "DetroitBecomeHuman.exe");
            if (!File.Exists(exe)) return ExeState.Missing;
            try
            {
                FileInfo info = new FileInfo(exe);
                if (info.Length != ExpectedExeSize) return ExeState.Unsupported;
                byte[] code = new byte[8];
                using (FileStream fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(OpcodeOffset, SeekOrigin.Begin);
                    fs.Read(code, 0, 8);
                }
                for (int i = 0; i < 4; i++)
                    if (code[i] != Opcode[i]) return ExeState.Unsupported;
                if (Same(code, 4, LoweredDisplacement)) return ExeState.Patched;
                if (Same(code, 4, OriginalDisplacement)) return ExeState.Original;
                return ExeState.Unsupported;
            }
            catch (Exception)
            {
                return ExeState.Unsupported;
            }
        }

        static bool Same(byte[] data, int offset, byte[] expected)
        {
            for (int i = 0; i < expected.Length; i++)
                if (data[offset + i] != expected[i]) return false;
            return true;
        }

        void RefreshStatus()
        {
            string folder = txtFolder.Text;
            bool ok = IsGameFolder(folder);
            btnInstall.Enabled = btnPatch.Enabled = btnRestore.Enabled = btnLaunch.Enabled = ok;
            btnUninstall.Enabled = btnDiag.Enabled = ok;
            if (!ok)
            {
                lblExe.Text = "Game not found. Click Browse and choose the folder with DetroitBecomeHuman.exe.";
                lblMod.Text = lblReShade.Text = lblDlss.Text = lblSession.Text = "";
                SetBanner("Game not found. Click Browse and choose the folder with DetroitBecomeHuman.exe.", Color.Firebrick);
                Relayout();
                return;
            }

            ExeState state = GetExeState();
            switch (state)
            {
                case ExeState.Original:
                    lblExe.Text = "Game exe: original (resolution scale limited to 70%)"; break;
                case ExeState.Patched:
                    lblExe.Text = "Game exe: patched (resolution scale can go below 70%)"; break;
                case ExeState.Unsupported:
                    lblExe.Text = "Game exe: not the Epic build this patch supports (patch unavailable)"; break;
                default:
                    lblExe.Text = "Game exe: missing"; break;
            }
            btnPatch.Enabled = state == ExeState.Original;
            btnRestore.Enabled = File.Exists(Path.Combine(folder, "DetroitBecomeHuman_orig.exe")) && state == ExeState.Patched;

            int missing = 0, outdated = 0, disabled = 0;
            string modDir = Path.Combine(appDir, "mod");
            bool haveSource = Directory.Exists(modDir);
            foreach (string name in ModFiles)
            {
                string dst = Path.Combine(folder, name);
                if (!File.Exists(dst))
                {
                    if (File.Exists(dst + ".off")) disabled++; else missing++;
                    continue;
                }
                string src = Path.Combine(modDir, name);
                if (haveSource && File.Exists(src))
                {
                    FileInfo a = new FileInfo(src), b = new FileInfo(dst);
                    if (a.Length != b.Length || a.LastWriteTimeUtc != b.LastWriteTimeUtc) outdated++;
                }
            }
            modDisabled = disabled > 0;
            updatingMod = true;
            chkModEnabled.Checked = !modDisabled;
            updatingMod = false;
            if (modDisabled && missing == 0)
                lblMod.Text = "Mod files: installed but turned off (the game will run without the mod)";
            else if (missing > 0)
                lblMod.Text = "Mod files: " + missing + " of " + ModFiles.Length + " missing (click Install / update mod files)";
            else if (outdated > 0)
                lblMod.Text = "Mod files: installed, update available";
            else
                lblMod.Text = "Mod files: installed and up to date";
            btnInstall.Enabled = haveSource;

            bool haveReShade = Directory.Exists(Path.Combine(folder, "reshade-shaders"))
                || File.Exists(Path.Combine(folder, "ReShade.ini"))
                || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"ReShade\ReShade64.dll"));
            lblReShade.Text = haveReShade
                ? "ReShade: found"
                : "ReShade: not found - install ReShade 6.8+ with add-on support and choose Vulkan";
            if (!hasNvidia)
                lblDlss.Text = "GPU: no NVIDIA card found, so FSR is used (DLSS is not available)";
            else
                lblDlss.Text = File.Exists(Path.Combine(folder, "nvngx_dlss.dll"))
                    ? "nvngx_dlss.dll: found (DLSS available on NVIDIA GPUs)"
                    : "nvngx_dlss.dll: not in the game folder (needed only for DLSS; FSR works without it)";
            UpdateSessionLine(folder);

            string note = "";
            if (GameRunning()) note = "The game is running. Close it before changing settings or files.";
            else if (cmbScale.SelectedIndex >= 2 && state != ExeState.Patched)
                note = "Resolution scales below 70% need the exe patch (use the button or leave auto-patch on).";
            lblNote.Text = note;

            UpdateBanner(state, missing, outdated, haveReShade, haveSource);
            Relayout();
        }

        void SetBanner(string text, Color color)
        {
            lblBanner.Text = text;
            lblBanner.BackColor = color;
        }

        // The one line that says whether you can press Launch.
        void UpdateBanner(ExeState state, int missing, int outdated, bool haveReShade, bool haveSource)
        {
            bool lowScale = cmbScale.SelectedIndex >= 2;
            if (GameRunning())
                SetBanner("The game is running. Close it before changing settings or files.", Color.SteelBlue);
            else if (modDisabled)
                SetBanner("The mod is turned off: the game will start without upscaling. Tick \"Mod enabled\" to turn it back on.", Color.SteelBlue);
            else if (missing > 0)
                SetBanner(haveSource
                    ? "Not ready: the mod files are not installed. Click \"Install / update mod files\"."
                    : "Not ready: the mod files are not installed and the launcher's mod folder is missing.", Color.Firebrick);
            else if (!haveReShade)
                SetBanner("Not ready: ReShade was not found in the game folder. Install ReShade 6.8+ with add-on support (Vulkan).", Color.Firebrick);
            else if (lowScale && state == ExeState.Unsupported)
                SetBanner("Almost ready: this preset needs the Epic exe patch, which is unavailable for this exe. The game will use 70%.", Color.DarkOrange);
            else if (outdated > 0)
                SetBanner("Ready, but an updated mod build is available. Click \"Install / update mod files\".", Color.DarkOrange);
            else if (lowScale && state == ExeState.Original)
                SetBanner(chkAutoPatch.Checked
                    ? "Ready: the exe will be patched automatically when you launch."
                    : "Almost ready: this preset needs the exe patch. Click \"Patch exe\" or turn on automatic patching.",
                    chkAutoPatch.Checked ? Color.SeaGreen : Color.DarkOrange);
            else
                SetBanner("Ready to play.", Color.SeaGreen);
        }

        // ----------------------------------------------- GPU, log, elevation
        static List<string> GetGpuNames()
        {
            List<string> names = new List<string>();
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (key == null) return names;
                    foreach (string name in key.GetSubKeyNames())
                    {
                        using (RegistryKey sub = key.OpenSubKey(name))
                        {
                            if (sub == null) continue;
                            string desc = sub.GetValue("DriverDesc") as string;
                            if (desc != null && !names.Contains(desc)) names.Add(desc);
                        }
                    }
                }
            }
            catch (Exception) { }
            return names;
        }

        static bool HasNvidiaGpu()
        {
            List<string> names = GetGpuNames();
            if (names.Count == 0) return true;
            foreach (string name in names)
                if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        void ReportAccessDenied()
        {
            if (IsAdmin())
            {
                MessageBox.Show(this,
                    "Windows still blocked writing to the game folder, even as administrator. Check that the folder or the file is not read-only.",
                    Text);
                return;
            }
            DialogResult answer = MessageBox.Show(this,
                "Windows blocked writing to the game folder.\n\nRestart the launcher as administrator now?",
                Text, MessageBoxButtons.YesNo);
            if (answer != DialogResult.Yes) return;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath);
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
                Application.Exit();
            }
            catch (Exception)
            {
                // The user declined the Windows prompt: stay open as before.
            }
        }

        void UpdateSessionLine(string folder)
        {
            string path = Path.Combine(folder, "ReShade.log");
            if (!File.Exists(path))
            {
                lblSession.Text = "Last session: no ReShade.log yet (start the game once)";
                lastLogKey = "";
                return;
            }
            try
            {
                FileInfo info = new FileInfo(path);
                string key = path + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;
                if (key == lastLogKey) return;
                string text;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (StreamReader reader = new StreamReader(fs))
                    text = reader.ReadToEnd();
                MatchCollection found = Regex.Matches(text,
                    @"created upscaler (.+?) render=(\d+)x(\d+) output=(\d+)x(\d+)");
                if (found.Count == 0)
                {
                    lblSession.Text = "Last session: no FSR upscaler recorded (DLSS, native TAA, or the mod did not load)";
                }
                else
                {
                    Match m = found[found.Count - 1];
                    double ratio = double.Parse(m.Groups[4].Value) / Math.Max(1.0, double.Parse(m.Groups[2].Value));
                    lblSession.Text = "Last session: " + m.Groups[1].Value + " " +
                        m.Groups[2].Value + "x" + m.Groups[3].Value + " -> " +
                        m.Groups[4].Value + "x" + m.Groups[5].Value + " (" +
                        ratio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x upscale)";
                }
                lastLogKey = key;
            }
            catch (Exception)
            {
                lblSession.Text = "Last session: ReShade.log is in use (close the game to read it)";
                lastLogKey = "";
            }
        }

        void ResetDefaults()
        {
            if (!IsGameFolder(txtFolder.Text)) return;
            if (MessageBox.Show(this, "Put every mod setting back to its default?", Text,
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            loading = true;
            SelectIndex(cmbUpscaler, hasNvidia ? 0 : 1);
            SelectIndex(cmbAa, 1);
            SelectIndex(cmbScale, 0);
            SelectIndex(cmbFsrVersion, 0);
            SelectIndex(cmbDlssPreset, 0);
            chkUncap.Checked = false;
            numSharp.Value = 0;
            loading = false;
            OnSettingChanged();
        }

        // ------------------------------------------------------------- actions
        bool EnsureGameClosed()
        {
            if (!GameRunning()) return true;
            MessageBox.Show(this, "Close the game first.", Text);
            return false;
        }

        void InstallMod()
        {
            if (!EnsureGameClosed()) return;
            string modDir = Path.Combine(appDir, "mod");
            try
            {
                foreach (string name in ModFiles)
                {
                    string src = Path.Combine(modDir, name);
                    if (!File.Exists(src))
                        throw new FileNotFoundException("Missing from the launcher's mod folder: " + name);
                    File.Copy(src, Path.Combine(txtFolder.Text, name), true);
                    string off = Path.Combine(txtFolder.Text, name + ".off");
                    if (File.Exists(off)) File.Delete(off);
                }
                string extra = "";
                if (GetExeState() == ExeState.Original && chkAutoPatch.Checked)
                {
                    extra = PatchExe()
                        ? "\n\nThe game exe was patched so the lower resolution presets work."
                        : "\n\nThe exe could not be patched; presets below Native AA will use 70%.";
                }
                MessageBox.Show(this,
                    "Mod files installed." + extra + "\n\nIf this is the first time, start the game once, quit, and start it again.",
                    Text);
            }
            catch (UnauthorizedAccessException)
            {
                ReportAccessDenied();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Install failed:\n" + ex.Message, Text);
            }
            RefreshStatus();
        }

        bool PatchExe()
        {
            if (!EnsureGameClosed()) return false;
            string exe = Path.Combine(txtFolder.Text, "DetroitBecomeHuman.exe");
            string backup = Path.Combine(txtFolder.Text, "DetroitBecomeHuman_orig.exe");
            ExeState state = GetExeState();
            if (state == ExeState.Patched) return true;
            if (state != ExeState.Original)
            {
                MessageBox.Show(this, "This exe is not the Epic build the patch supports, so nothing was changed.", Text);
                return false;
            }
            try
            {
                if (!File.Exists(backup)) File.Copy(exe, backup);
                using (FileStream fs = new FileStream(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    fs.Seek(DisplacementOffset, SeekOrigin.Begin);
                    fs.Write(LoweredDisplacement, 0, LoweredDisplacement.Length);
                }
            }
            catch (UnauthorizedAccessException)
            {
                ReportAccessDenied();
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Patch failed:\n" + ex.Message, Text);
                return false;
            }
            RefreshStatus();
            return GetExeState() == ExeState.Patched;
        }

        void RestoreExe()
        {
            if (!EnsureGameClosed()) return;
            string exe = Path.Combine(txtFolder.Text, "DetroitBecomeHuman.exe");
            string backup = Path.Combine(txtFolder.Text, "DetroitBecomeHuman_orig.exe");
            if (!File.Exists(backup))
            {
                MessageBox.Show(this, "No backup (DetroitBecomeHuman_orig.exe) was found.", Text);
                return;
            }
            try
            {
                File.Copy(backup, exe, true);
            }
            catch (UnauthorizedAccessException)
            {
                ReportAccessDenied();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Restore failed:\n" + ex.Message, Text);
            }
            RefreshStatus();
        }

        void UninstallMod()
        {
            if (!EnsureGameClosed()) return;
            string folder = txtFolder.Text;
            string exe = Path.Combine(folder, "DetroitBecomeHuman.exe");
            string backup = Path.Combine(folder, "DetroitBecomeHuman_orig.exe");
            if (MessageBox.Show(this,
                "Remove the mod files from the game folder and restore the original exe?\n\nReShade itself and your ReShade.ini are not touched.",
                Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            List<string> removed = new List<string>();
            try
            {
                foreach (string name in ModFiles)
                {
                    string path = Path.Combine(folder, name);
                    if (File.Exists(path + ".off")) File.Delete(path + ".off");
                    if (!File.Exists(path)) continue;
                    File.Delete(path);
                    removed.Add(name);
                }
                bool restored = false;
                if (File.Exists(backup))
                {
                    File.Copy(backup, exe, true);
                    restored = true;
                }
                string text = removed.Count + " mod file(s) removed." +
                    (restored ? "\nThe original exe was restored." : "\nNo exe backup was found, so the exe was left as it is.");
                MessageBox.Show(this, text, Text);
            }
            catch (UnauthorizedAccessException)
            {
                ReportAccessDenied();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Uninstall failed:\n" + ex.Message, Text);
            }
            lastLogKey = "";
            RefreshStatus();
        }

        // Collects what is needed for a bug report and puts it on the clipboard.
        void CopyDiagnostics()
        {
            string folder = txtFolder.Text;
            StringBuilder sb = new StringBuilder();
            try
            {
                sb.AppendLine("DBH FSR/DLSS Launcher " + Assembly.GetExecutingAssembly().GetName().Version);
                sb.AppendLine("Windows: " + Environment.OSVersion.VersionString);
                sb.AppendLine("GPU: " + string.Join(", ", GetGpuNames().ToArray()));
                sb.AppendLine("Game folder: " + folder);
                string exe = Path.Combine(folder, "DetroitBecomeHuman.exe");
                long size = File.Exists(exe) ? new FileInfo(exe).Length : -1L;
                sb.AppendLine("Exe: " + size + " bytes, patch state " + GetExeState());
                foreach (string name in ModFiles)
                {
                    string path = Path.Combine(folder, name);
                    sb.AppendLine("Mod file " + name + ": " +
                        (File.Exists(path) ? new FileInfo(path).Length + " bytes" : "missing"));
                }
                sb.AppendLine("reshade-shaders folder: " + (Directory.Exists(Path.Combine(folder, "reshade-shaders")) ? "yes" : "no"));
                sb.AppendLine("nvngx_dlss.dll: " + (File.Exists(Path.Combine(folder, "nvngx_dlss.dll")) ? "yes" : "no"));
                sb.AppendLine();
                sb.AppendLine("[" + Section + "]");
                string[] keys = new string[] { "UpscalerBackend", "DLSSMode", "ResolutionScalePreset", "DLAASharpening",
                    "UncapFps", "FSRJitterMode", "FSRJitterCancel", "FSRVersion", "DLSSPreset", "DLSSJitterMode", "DLSSMVJittered", "AspectRatioMode", "DepthOfFieldMode" };
                foreach (string key in keys)
                    sb.AppendLine(key + "=" + Ini.Get(ReShadeIni, Section, key, "(not set)"));
                sb.AppendLine();

                string logPath = Path.Combine(folder, "ReShade.log");
                if (!File.Exists(logPath))
                {
                    sb.AppendLine("ReShade.log: not found");
                }
                else
                {
                    string text;
                    using (FileStream fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(fs))
                        text = reader.ReadToEnd();
                    string[] patterns = new string[] { "Initializing crosire", "Detroit upscaler", "Detroit ultrawide",
                        "created upscaler", "available upscaler versions", "pinning FSR", "Detroit FSR: GPU",
                        "Detroit resolution scale", "render preset", "Detroit DOF:" };
                    List<string> picked = new List<string>();
                    int rejected = 0;
                    foreach (string line in text.Split('\n'))
                    {
                        if (line.IndexOf("frame rejected safely", StringComparison.Ordinal) >= 0) rejected++;
                        foreach (string pattern in patterns)
                        {
                            if (line.IndexOf(pattern, StringComparison.Ordinal) < 0) continue;
                            string trimmed = line.TrimEnd('\r');
                            if (trimmed.Length > 260) trimmed = trimmed.Substring(0, 260);
                            picked.Add(trimmed);
                            break;
                        }
                    }
                    sb.AppendLine("ReShade.log (key lines, " + rejected + " rejected frame(s)):");
                    int start = Math.Max(0, picked.Count - 40);
                    for (int i = start; i < picked.Count; i++) sb.AppendLine(picked[i]);
                }
                Clipboard.SetText(sb.ToString());
                MessageBox.Show(this, "The diagnostics were copied to the clipboard. Paste them into your bug report.", Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not collect the diagnostics:\n" + ex.Message, Text);
            }
        }

        void LaunchGame()
        {
            if (!IsGameFolder(txtFolder.Text)) return;
            if (GameRunning())
            {
                MessageBox.Show(this, "The game is already running.", Text);
                return;
            }
            OnSettingChanged();

            if (GetExeState() == ExeState.Original && (cmbScale.SelectedIndex >= 2 || chkAutoPatch.Checked))
            {
                bool doPatch = chkAutoPatch.Checked;
                if (!doPatch)
                {
                    doPatch = MessageBox.Show(this,
                        "The selected resolution scale is below the game's 70% minimum and the exe is not patched, so the game would use 70%.\n\nPatch the exe now?",
                        Text, MessageBoxButtons.YesNo) == DialogResult.Yes;
                }
                if (doPatch && !PatchExe()) return;
            }

            try
            {
                if (chkEpic.Checked && epicAppName.Length > 0)
                {
                    ProcessStartInfo epic = new ProcessStartInfo(
                        "com.epicgames.launcher://apps/" + epicAppName + "?action=launch&silent=true");
                    epic.UseShellExecute = true;
                    Process.Start(epic);
                }
                else
                {
                    ProcessStartInfo direct = new ProcessStartInfo(Path.Combine(txtFolder.Text, "DetroitBecomeHuman.exe"));
                    direct.WorkingDirectory = txtFolder.Text;
                    direct.UseShellExecute = true;
                    Process.Start(direct);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not start the game:\n" + ex.Message, Text);
            }
        }

        void OpenPath(string path)
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path)) Process.Start(path);
                else MessageBox.Show(this, "Not found: " + path, Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text);
            }
        }
    }
}
