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
        private readonly CheckBox _chkCaptureScore = new();
        private readonly TextBox _txtScoreSelector = new();
        private readonly TextBox _txtOutputPath = new();
        private readonly Button _btnBrowseOutput = new();
        private readonly Button _btnStart = new();
        private readonly Label _lblStatus = new();

        // ---- Lock screen controls ----
        private readonly Panel _lockPanel = new();
        private readonly Label _lblNameBanner = new();
        private readonly Label _lblToast = new();
        private System.Windows.Forms.Timer? _toastTimer;
        private WebView2? _webView;

        private sealed class QuizScoreMessage
        {
            public string? Score { get; set; }
            public string? Url { get; set; }
        }

        private KeyboardHook? _hook;
        private System.Windows.Forms.Timer? _timeLimitTimer;

        private string _unlockPassword = string.Empty;
        private string _quizHost = string.Empty;
        private string _quizTakerName = string.Empty;
        private bool _strictMode;
        private bool _isLocked;

        private bool _captureScore;
        private string _scoreSelector = string.Empty;
        private string _outputPath = string.Empty;
        private bool _scoreCapturedThisSession;
        private string? _lastSaveError;

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
            ClientSize = new Size(480, 500);
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

            _chkCaptureScore.Text = "Save name + score from the quiz results page to an Excel file";
            _chkCaptureScore.Location = new Point(20, 260);
            _chkCaptureScore.Width = 430;
            _chkCaptureScore.Height = 20;
            _chkCaptureScore.CheckedChanged += (_, _) =>
            {
                bool on = _chkCaptureScore.Checked;
                _txtScoreSelector.Enabled = on;
                _txtOutputPath.Enabled = on;
                _btnBrowseOutput.Enabled = on;
            };

            var lblScoreSel = new Label { Text = "CSS selector for the score element on the results page:", AutoSize = true, Location = new Point(20, 288) };
            _txtScoreSelector.Location = new Point(20, 310);
            _txtScoreSelector.Width = 430;
            _txtScoreSelector.Text = "#quiz-score, .result-score";
            _txtScoreSelector.Enabled = false;

            var lblOutputPath = new Label { Text = "Save results to:", AutoSize = true, Location = new Point(20, 340) };
            _txtOutputPath.Location = new Point(20, 362);
            _txtOutputPath.Width = 340;
            _txtOutputPath.Enabled = false;
            _txtOutputPath.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QuizLock Results.xlsx");

            _btnBrowseOutput.Text = "Browse...";
            _btnBrowseOutput.Location = new Point(365, 360);
            _btnBrowseOutput.Width = 85;
            _btnBrowseOutput.Height = 26;
            _btnBrowseOutput.Enabled = false;
            _btnBrowseOutput.Click += BtnBrowseOutput_Click;

            _btnStart.Text = "Start Lockdown";
            _btnStart.Location = new Point(20, 405);
            _btnStart.Width = 200;
            _btnStart.Height = 35;
            _btnStart.Click += BtnStart_Click;

            _lblStatus.AutoSize = true;
            _lblStatus.ForeColor = Color.DarkRed;
            _lblStatus.Location = new Point(20, 450);
            _lblStatus.MaximumSize = new Size(430, 0);
            _lblStatus.Text = "Unlock hotkey once locked: Ctrl+Alt+Shift+U";
            _lblStatus.ForeColor = Color.DimGray;

            _setupPanel.Controls.AddRange(new Control[]
            {
                lblUrl, _txtUrl, lblPassword, _txtPassword, _chkStrictMode,
                lblTime, _numTimeLimit,
                _chkCaptureScore, lblScoreSel, _txtScoreSelector,
                lblOutputPath, _txtOutputPath, _btnBrowseOutput,
                _btnStart, _lblStatus
            });
        }

        private void BtnBrowseOutput_Click(object? sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = Path.GetFileName(_txtOutputPath.Text),
                InitialDirectory = Path.GetDirectoryName(_txtOutputPath.Text),
                OverwritePrompt = false
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtOutputPath.Text = dlg.FileName;
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
            _strictMode = _chkStrictMode.Checked;

            _captureScore = _chkCaptureScore.Checked;
            _scoreSelector = _txtScoreSelector.Text.Trim();
            _outputPath = _txtOutputPath.Text.Trim();
            _scoreCapturedThisSession = false;

            if (_captureScore && (string.IsNullOrWhiteSpace(_scoreSelector) || string.IsNullOrWhiteSpace(_outputPath)))
            {
                MessageBox.Show(this,
                    "Fill in the score selector and an output file to save results, or uncheck that option.",
                    "QuizLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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

            _lblToast.AutoSize = false;
            _lblToast.Size = new Size(380, 32);
            _lblToast.BackColor = Color.FromArgb(220, 30, 120, 30);
            _lblToast.ForeColor = Color.White;
            _lblToast.TextAlign = ContentAlignment.MiddleCenter;
            _lblToast.Font = new Font(_lblToast.Font, FontStyle.Bold);
            _lblToast.Visible = false;
            _lockPanel.Controls.Add(_lblToast);
            _lblToast.BringToFront();
        }

        private void ShowToast(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ShowToast(text)));
                return;
            }

            _lblToast.Text = text;
            _lblToast.Location = new Point((_lockPanel.ClientSize.Width - _lblToast.Width) / 2, 30);
            _lblToast.Visible = true;
            _lblToast.BringToFront();

            _toastTimer?.Stop();
            _toastTimer?.Dispose();
            _toastTimer = new System.Windows.Forms.Timer { Interval = 4000 };
            _toastTimer.Tick += (_, _) =>
            {
                _lblToast.Visible = false;
                _toastTimer?.Stop();
            };
            _toastTimer.Start();
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

                if (_captureScore)
                {
                    settings.IsWebMessageEnabled = true;
                    _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                    await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                        BuildScoreCaptureScript(_quizHost, _scoreSelector));
                }

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
        // Score capture: watch the quiz page for the score element and
        // post it back to the host app once it appears.
        // ---------------------------------------------------------------
        private static string BuildScoreCaptureScript(string quizHost, string scoreSelector)
        {
            // Values are JSON-encoded so quotes/special characters in the
            // selector or host can't break out of the injected script.
            string hostJson = JsonSerializer.Serialize(quizHost);
            string scoreSelJson = JsonSerializer.Serialize(scoreSelector);

            return $$"""
            (function () {
                if (window.__quizlockInstalled) return;
                window.__quizlockInstalled = true;

                var QUIZ_HOST = {{hostJson}};
                var SCORE_SEL = {{scoreSelJson}};
                var captured = false;

                function onQuizHost() {
                    return location.hostname === QUIZ_HOST ||
                           location.hostname.endsWith("." + QUIZ_HOST);
                }

                function tryCapture() {
                    if (captured || !onQuizHost()) return;
                    try {
                        var scoreEl = document.querySelector(SCORE_SEL);
                        var score = scoreEl && scoreEl.textContent ? scoreEl.textContent.trim() : "";
                        if (score) {
                            captured = true;
                            window.chrome.webview.postMessage(JSON.stringify({
                                score: score, url: location.href
                            }));
                        }
                    } catch (err) { /* selector errors just mean "not found yet" */ }
                }

                function start() {
                    tryCapture();
                    if (document.body) {
                        new MutationObserver(tryCapture).observe(document.body, {
                            childList: true, subtree: true, characterData: true
                        });
                    }
                }

                if (document.readyState === "loading") {
                    document.addEventListener("DOMContentLoaded", start);
                } else {
                    start();
                }
                // Belt-and-suspenders poll, in case the score renders in a way
                // the mutation observer doesn't catch (e.g. an iframe swap).
                setInterval(tryCapture, 1500);
            })();
            """;
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (_scoreCapturedThisSession) return;

            string? json;
            try
            {
                json = e.TryGetWebMessageAsString();
            }
            catch (InvalidCastException)
            {
                return; // not a string message; not ours
            }

            if (string.IsNullOrWhiteSpace(json)) return;

            QuizScoreMessage? result;
            try
            {
                result = JsonSerializer.Deserialize<QuizScoreMessage>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                return;
            }

            if (result is null || string.IsNullOrWhiteSpace(result.Score))
            {
                return;
            }

            _scoreCapturedThisSession = true;
            SaveResultToExcel(_quizTakerName, result.Score.Trim());
        }

        private void SaveResultToExcel(string name, string score)
        {
            try
            {
                var directory = Path.GetDirectoryName(_outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var workbook = File.Exists(_outputPath) ? new XLWorkbook(_outputPath) : new XLWorkbook();
                var ws = workbook.Worksheets.Contains("Results")
                    ? workbook.Worksheet("Results")
                    : workbook.Worksheets.Add("Results");

                if (ws.Cell(1, 1).IsEmpty())
                {
                    ws.Cell(1, 1).Value = "Name";
                    ws.Cell(1, 2).Value = "Score";
                    ws.Cell(1, 3).Value = "Date/Time";
                    ws.Row(1).Style.Font.Bold = true;
                }

                int nextRow = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
                ws.Cell(nextRow, 1).Value = name;
                ws.Cell(nextRow, 2).Value = score;
                ws.Cell(nextRow, 3).Value = DateTime.Now;
                ws.Columns().AdjustToContents();

                workbook.SaveAs(_outputPath);

                ShowToast($"Saved: {name} — {score}");
            }
            catch (Exception ex)
            {
                ShowToast("Couldn't save result — see details on unlock.");
                _lastSaveError = ex.Message;
            }
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
            ClientSize = new Size(480, 500);
            StartPosition = FormStartPosition.CenterScreen;

            _lockPanel.Visible = false;
            _setupPanel.Visible = true;
            _lblNameBanner.Text = string.Empty;
            _quizTakerName = string.Empty;

            if (auto)
            {
                MessageBox.Show(this, "Time limit reached - lockdown ended automatically.", "QuizLock",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            if (_lastSaveError is not null)
            {
                MessageBox.Show(this,
                    $"The quiz result couldn't be saved to the Excel file:\n{_lastSaveError}\n\n" +
                    "Common cause: the file is open in Excel. Close it and try again next time.",
                    "QuizLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _lastSaveError = null;
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
