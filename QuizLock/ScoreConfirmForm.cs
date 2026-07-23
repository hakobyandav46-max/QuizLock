namespace QuizLock
{
    /// <summary>
    /// Shown when the session unlocks. Pre-fills whatever score text was
    /// auto-scraped from the quiz page (best-effort, may be blank or wrong),
    /// and lets the proctor confirm or correct it before it's logged.
    /// </summary>
    internal sealed class ScoreConfirmForm : Form
    {
        private readonly TextBox _txtScore = new() { Width = 260 };
        public string EnteredScore => _txtScore.Text.Trim();
        public bool Skipped { get; private set; }

        public ScoreConfirmForm(string detectedScore)
        {
            Text = "Confirm result";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(340, 175);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var lbl = new Label
            {
                Text = string.IsNullOrWhiteSpace(detectedScore)
                    ? "Couldn't auto-detect a score. Enter it manually:"
                    : "Auto-detected from the quiz page - edit if wrong:",
                AutoSize = false,
                Width = 300,
                Height = 32,
                Location = new Point(20, 20)
            };
            _txtScore.Location = new Point(20, 58);
            _txtScore.Text = detectedScore;

            var btnSave = new Button { Text = "Save to Excel", Location = new Point(20, 95), Width = 260, Height = 32 };
            btnSave.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

            var btnSkip = new Button { Text = "Skip - don't log this attempt", Location = new Point(20, 133), Width = 260 };
            btnSkip.Click += (_, _) => { Skipped = true; DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = btnSave;

            Controls.AddRange(new Control[] { lbl, _txtScore, btnSave, btnSkip });
        }
    }
}
