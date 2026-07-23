using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QuizLock
{
    public partial class MainForm : Form
    {
        // ---- Setup screen controls ----
        private readonly Panel _setupPanel = new();
        private readonly TextBox _txtUrl = new();
        private readonly TextBox _txtPassword = new();
        private readonly CheckBox _chkStrictMode = new();
        private readonly NumericUpDown _numTimeLimit = new();
        private readonly TextBox _txtResultsPath = new();
        private readonly Button _btnBrowseResults = new();
        private readonly Button _btnStart = new();
        private readonly Label _lblStatus = new();

        // ---- Lock screen controls ----
        private readonly Panel _lockPanel = new();
        private readonly Label _lblNameBanner = new();
        private WebView2? _webView;

        private KeyboardHook? _hook;
        private System.Windows.Forms.Timer? _timeLimitTimer;

        private string _unlockPassword = string.Empty;
        private string _quizHost = string.Empty;
        private string _quizTakerName = string.Empty;
        private string _quizUrlString = string.Empty;
        private string _lastDetectedScore = string.Empty;
        private bool _strictMode;
        private bool _isLocked;

        // Domains treated as "AI assistants" and always blocked while locked,
        // regardless of strict mode.
        private static readonly string[] AiBlocklist =
        {
            "chat.openai.com", "chatgpt.com", "openai.com",
            "claude.ai", "anthropic.com",
            "gemini.google.com", "bard.google.com",
            "copilot.microsoft.com", "bing.com/chat",
            "perplexity.ai", "poe.com", "character.ai",
            "you.com", "phind.com", "meta.ai", "grok.com", "x.ai",
            "huggingface.co/chat", "pi.ai"
        };

        // Common SSO/login hosts allowed through even in strict mode, since many
        // quiz platforms (Canvas, Google Forms, etc.) redirect through these.
        private static readonly string[] SsoAllowlist =
        {
            "accounts.google.com", "login.microsoftonline.com",
            "login.live.com", "okta.com", "auth0.com"
        };

        public MainForm()
        {
            Text = "QuizLock";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 460);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            BuildSetupPanel();
            BuildLockPanel();

            Controls.Add(_setupPanel);
            Controls.Add(_lockPanel);
            _lockPanel.Visible = false;

            FormClosing += MainForm_FormClosing;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => FailSafeRestore();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => FailSafeRestore();
        }

        // ---------------------------------------------------------------
        // Setup screen
        // ---------------------------------------------------------------
        private void BuildSetupPanel()
        {
            _setupPanel.Dock = DockStyle.Fill;
            _setupPanel.Padding = new Padding(20);

            var lblUrl = new Label { Text = "Quiz link:", AutoSize = true, Location = new Point(20, 20) };
            _txtUrl.Location = new Point(20, 45);
            _txtUrl.Width = 430;
            _txtUrl.PlaceholderText = "https://forms.example.com/your-quiz";

            var lblPassword = new Label { Text = "Unlock password (used to end lockdown early):", AutoSize = true, Location = new Point(20, 85) };
            _txtPassword.Location = new Point(20, 110);
            _txtPassword.Width = 430;
            _txtPassword.UseSystemPasswordChar = true;

            _chkStrictMode.Text = "Strict mode: only allow the quiz's own site (recommended - uncheck to allow other sites too)";
            _chkStrictMode.Location = new Point(20, 150);
            _chkStrictMode.Width = 430;
            _chkStrictMode.Height = 40;
            _chkStrictMode.Checked = true;

            var lblTime = new Label { Text = "Auto-unlock after (minutes, 0 = no limit):", AutoSize = true, Location = new Point(20, 200) };
            _numTimeLimit.Location = new Point(20, 225);
            _numTimeLimit.Minimum = 0;
            _numTimeLimit.Maximum = 600;
            _numTimeLimit.Value = 0;
            _numTimeLimit.Width = 100;

            var lblResultsPath = new Label { Text = "Results Excel file (name + score logged here on unlock):", AutoSize = true, Location = new Point(20, 260) };
            _txtResultsPath.Location = new Point(20, 285);
            _txtResultsPath.Width = 340;
            _txtResultsPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "QuizLock-Results.xlsx");

            _btnBrowseResults.Text = "Browse...";
            _btnBrowseResults.Location = new Point(368, 283);
            _btnBrowseResults.Width = 82;
            _btnBrowseResults.Click += BtnBrowseResults_Click;

            _btnStart.Text = "Start Lockdown";
            _btnStart.Location = new Point(20, 325);
            _btnStart.Width = 200;
            _btnStart.Height = 35;
            _btnStart.Click += BtnStart_Click;

            _lblStatus.AutoSize = true;
            _lblStatus.ForeColor = Color.DarkRed;
            _lblStatus.Location = new Point(20, 372);
            _lblStatus.MaximumSize = new Size(430, 0);
            _lblStatus.Text = "Unlock hotkey once locked: Ctrl+Alt+Shift+U";
            _lblStatus.ForeColor = Color.DimGray;

            _setupPanel.Controls.AddRange(new Control[]
            {
                lblUrl, _txtUrl, lblPassword, _txtPassword, _chkStrictMode,
                lblTime, _numTimeLimit, lblResultsPath, _txtResultsPath, _btnBrowseResults,
                _btnStart, _lblStatus
            });
        }

        private void BtnBrowseResults_Click(object? sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                FileName = Path.GetFileName(_txtResultsPath.Text),
                InitialDirectory = Path.GetDirectoryName(_txtResultsPath.Text),
                OverwritePrompt = false // we append to existing files, not overwrite
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtResultsPath.Text = dlg.FileName;
            }
        }

        private void BtnStart_Click(object? sender, EventArgs e)
        {
            var urlText = _txtUrl.Text.Trim();
            if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(this, "Please enter a valid http(s) quiz URL.", "QuizLock",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_txtPassword.Text))
            {
                MessageBox.Show(this, "Please set an unlock password. You need this to end the lockdown.",
                    "QuizLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _unlockPassword = _txtPassword.Text;
            _quizHost = uri.Host;
            _quizUrlString = uri.ToString();
            _strictMode = _chkStrictMode.Checked;
            _lastDetectedScore = string.Empty;

            // Require the quiz taker's name before lockdown actually begins.
            using var nameDlg = new NameEntryForm { TopMost = true };
            if (nameDlg.ShowDialog(this) != DialogResult.OK)
            {
                return; // cancelled - stay on setup screen
            }
            _quizTakerName = nameDlg.EnteredName;

            _ = StartLockAsync(uri);
        }

        // ---------------------------------------------------------------
        // Lock screen / WebView2
        // ---------------------------------------------------------------
        private void BuildLockPanel()
        {
            _lockPanel.Dock = DockStyle.Fill;
            _lockPanel.BackColor = Color.Black;

            _lblNameBanner.Dock = DockStyle.Top;
            _lblNameBanner.Height = 20;
            _lblNameBanner.BackColor = Color.FromArgb(30, 30, 30);
            _lblNameBanner.ForeColor = Color.Silver;
            _lblNameBanner.Font = new Font(_lblNameBanner.Font.FontFamily, 8f);
            _lblNameBanner.TextAlign = ContentAlignment.MiddleLeft;
            _lblNameBanner.Padding = new Padding(6, 0, 0, 0);
            _lockPanel.Controls.Add(_lblNameBanner);
        }

        private async Task StartLockAsync(Uri quizUri)
        {
            try
            {
                _webView = new WebView2 { Dock = DockStyle.Fill };
                _lockPanel.Controls.Add(_webView);

                await _webView.EnsureCoreWebView2Async(null);

                var settings = _webView.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = false; // no right-click menu
                settings.AreDevToolsEnabled = false;             // no F12 devtools
                settings.AreBrowserAcceleratorKeysEnabled = false; // no Ctrl+T/N/W, F12, etc. inside the browser
                settings.IsZoomControlEnabled = false;
                settings.IsStatusBarEnabled = false;

                _webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                _webView.CoreWebView2.NewWindowRequested += (_, args) =>
                {
                    // Open any "open in new tab/window" attempt in the same view instead,
                    // so it still passes through our navigation filter.
                    args.Handled = true;
                    _webView!.CoreWebView2.Navigate(args.Uri);
                };
                _webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                _webView.CoreWebView2.Navigate(quizUri.ToString());
            }
            catch (Exception ex)
            {
                // Clean up the partially-created browser control so a retry
                // (after e.g. installing the WebView2 Runtime) doesn't leak
                // an orphaned control into _lockPanel.
                if (_webView is not null)
                {
                    _lockPanel.Controls.Remove(_webView);
                    _webView.Dispose();
                    _webView = null;
                }

                MessageBox.Show(this,
                    "Could not start the embedded browser. Make sure the WebView2 Runtime is installed.\n\n" + ex.Message,
                    "QuizLock", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Switch to fullscreen lockdown UI
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            _setupPanel.Visible = false;
            _lockPanel.Visible = true;
            _lblNameBanner.Text = $"Quiz taker: {_quizTakerName}";

            SetTaskbarVisible(false);

            _hook = new KeyboardHook();
            _hook.Install();

            NativeMethods.RegisterHotKey(Handle, NativeMethods.UNLOCK_HOTKEY_ID,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT,
                (uint)Keys.U);

            var minutes = (int)_numTimeLimit.Value;
            if (minutes > 0)
            {
                _timeLimitTimer = new System.Windows.Forms.Timer { Interval = minutes * 60 * 1000 };
                _timeLimitTimer.Tick += (_, _) => Unlock(auto: true);
                _timeLimitTimer.Start();
            }

            _isLocked = true;
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var host = SafeGetHost(e.Uri);
            if (host is null) return;

            // Always block known AI assistant sites.
            if (AiBlocklist.Any(blocked => host.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
                                            host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase)))
            {
                e.Cancel = true;
                return;
            }

            // Strict mode: only the quiz's own domain (plus common SSO hosts) is allowed.
            if (_strictMode)
            {
                bool isQuizDomain = host.Equals(_quizHost, StringComparison.OrdinalIgnoreCase) ||
                                    host.EndsWith("." + _quizHost, StringComparison.OrdinalIgnoreCase) ||
                                    RootDomain(host).Equals(RootDomain(_quizHost), StringComparison.OrdinalIgnoreCase);
                bool isSso = SsoAllowlist.Any(sso => host.Equals(sso, StringComparison.OrdinalIgnoreCase) ||
                                                      host.EndsWith("." + sso, StringComparison.OrdinalIgnoreCase));
                if (!isQuizDomain && !isSso)
                {
                    e.Cancel = true;
                }
            }
        }

        // Best-effort: after every page load in the locked browser, scan the
        // visible text for common "score" phrasing. This is fragile by
        // nature - it depends entirely on how the quiz site words its
        // results - which is why the result is always shown to the proctor
        // for confirmation/edit before being saved, never saved silently.
        private const string ScoreScrapeJs = @"
            (function () {
                try {
                    var text = document.body.innerText || '';
                    var patterns = [
                        /you\s+scored\s*[:\-]?\s*[0-9]+(\.[0-9]+)?\s*(%|\/\s*[0-9]+|out of\s*[0-9]+)?/i,
                        /your\s+score\s*[:\-]?\s*[0-9]+(\.[0-9]+)?\s*(%|\/\s*[0-9]+|out of\s*[0-9]+)?/i,
                        /score\s*[:\-]\s*[0-9]+(\.[0-9]+)?\s*(%|\/\s*[0-9]+|out of\s*[0-9]+)?/i,
                        /[0-9]+\s*out of\s*[0-9]+/i,
                        /[0-9]+\s*\/\s*[0-9]+/i,
                        /[0-9]+(\.[0-9]+)?\s*%/i
                    ];
                    for (var i = 0; i < patterns.length; i++) {
                        var m = text.match(patterns[i]);
                        if (m) return m[0].trim();
                    }
                    return null;
                } catch (e) { return null; }
            })();";

        private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess || _webView is null) return;
            try
            {
                var json = await _webView.CoreWebView2.ExecuteScriptAsync(ScoreScrapeJs);
                var detected = JsonSerializer.Deserialize<string?>(json);
                if (!string.IsNullOrWhiteSpace(detected))
                {
                    _lastDetectedScore = detected!;
                }
            }
            catch
            {
                // Scraping is best-effort only - never let it break navigation.
            }
        }


        // Registrable-domain-ish comparison: "take.quiz-maker.com" and
        // "www.quiz-maker.com" should both count as the same site. Simple
        // last-two-labels check - good enough for typical .com/.io/etc
        // domains; not exhaustive for multi-part TLDs like .co.uk.
        private static string RootDomain(string host)
        {
            var parts = host.Split('.');
            return parts.Length <= 2 ? host : string.Join(".", parts[^2..]);
        }

        private static string? SafeGetHost(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
        }

        // ---------------------------------------------------------------
        // Unlock flow
        // ---------------------------------------------------------------
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == NativeMethods.UNLOCK_HOTKEY_ID)
            {
                PromptUnlock();
                return;
            }
            base.WndProc(ref m);
        }

        private void PromptUnlock()
        {
            using var dlg = new UnlockPromptForm();
            dlg.TopMost = true;
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (dlg.EnteredPassword == _unlockPassword)
                {
                    Unlock(auto: false);
                }
                else
                {
                    MessageBox.Show(this, "Incorrect password.", "QuizLock",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void Unlock(bool auto)
        {
            if (!_isLocked) return;
            _isLocked = false;

            // Give the proctor a chance to confirm/correct the auto-detected
            // score and log this attempt before tearing the session down.
            using (var scoreDlg = new ScoreConfirmForm(_lastDetectedScore) { TopMost = true })
            {
                if (scoreDlg.ShowDialog(this) == DialogResult.OK && !scoreDlg.Skipped)
                {
                    TrySaveResultToExcel(_quizTakerName, scoreDlg.EnteredScore, _quizUrlString, _txtResultsPath.Text);
                }
            }

            _timeLimitTimer?.Stop();
            _timeLimitTimer?.Dispose();
            _timeLimitTimer = null;

            NativeMethods.UnregisterHotKey(Handle, NativeMethods.UNLOCK_HOTKEY_ID);

            _hook?.Uninstall();
            _hook?.Dispose();
            _hook = null;

            SetTaskbarVisible(true);

            if (_webView is not null)
            {
                _lockPanel.Controls.Remove(_webView);
                _webView.Dispose();
                _webView = null;
            }

            FormBorderStyle = FormBorderStyle.FixedDialog;
            WindowState = FormWindowState.Normal;
            TopMost = false;
            ClientSize = new Size(480, 460);
            StartPosition = FormStartPosition.CenterScreen;

            _lockPanel.Visible = false;
            _setupPanel.Visible = true;
            _lblNameBanner.Text = string.Empty;
            _quizTakerName = string.Empty;
            _lastDetectedScore = string.Empty;

            if (auto)
            {
                MessageBox.Show(this, "Time limit reached - lockdown ended automatically.", "QuizLock",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SetTaskbarVisible(bool visible)
        {
            var taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (taskbarHandle != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(taskbarHandle, visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
            }
        }

        private void TrySaveResultToExcel(string name, string score, string quizUrl, string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    MessageBox.Show(this, "No results file path was set - nothing was logged.", "QuizLock",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var workbook = File.Exists(filePath) ? new XLWorkbook(filePath) : new XLWorkbook();
                var sheet = workbook.Worksheets.FirstOrDefault() ?? workbook.Worksheets.Add("Results");

                if (!sheet.CellsUsed().Any())
                {
                    sheet.Cell(1, 1).Value = "Name";
                    sheet.Cell(1, 2).Value = "Score";
                    sheet.Cell(1, 3).Value = "Quiz Link";
                    sheet.Cell(1, 4).Value = "Date/Time";
                    sheet.Row(1).Style.Font.Bold = true;
                }

                var nextRow = (sheet.LastRowUsed()?.RowNumber() ?? 1) + 1;
                sheet.Cell(nextRow, 1).Value = name;
                sheet.Cell(nextRow, 2).Value = score;
                sheet.Cell(nextRow, 3).Value = quizUrl;
                sheet.Cell(nextRow, 4).Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                sheet.Columns().AdjustToContents();
                workbook.SaveAs(filePath);
            }
            catch (Exception ex)
            {
                // Common cause: the file is currently open in Excel and locked.
                MessageBox.Show(this,
                    "Couldn't save to the results file (it may be open in Excel - close it and try again).\n\n" + ex.Message,
                    "QuizLock", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------------------------------------------------------
        // Safety nets: whatever happens, never leave the machine locked down.
        // ---------------------------------------------------------------
        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            FailSafeRestore();
        }

        private void FailSafeRestore()
        {
            try { _hook?.Uninstall(); } catch { /* best effort */ }
            try { NativeMethods.UnregisterHotKey(Handle, NativeMethods.UNLOCK_HOTKEY_ID); } catch { /* best effort */ }
            try { SetTaskbarVisible(true); } catch { /* best effort */ }
        }
    }
}
