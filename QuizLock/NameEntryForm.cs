namespace QuizLock
{
    /// <summary>
    /// Shown right after "Start Lockdown" is clicked. The quiz taker must
    /// type their name before the lockdown actually engages.
    /// </summary>
    internal sealed class NameEntryForm : Form
    {
        private readonly TextBox _txtName = new() { Width = 260 };
        public string EnteredName => _txtName.Text.Trim();

        public NameEntryForm()
        {
            Text = "Who's taking this quiz?";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(320, 140);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var lbl = new Label { Text = "Enter your full name to begin:", AutoSize = true, Location = new Point(20, 20) };
            _txtName.Location = new Point(20, 45);

            var lblError = new Label
            {
                Name = "lblError",
                Text = "",
                AutoSize = true,
                ForeColor = Color.Firebrick,
                Location = new Point(20, 72),
                Font = new Font(Font.FontFamily, 8f)
            };

            var btnOk = new Button { Text = "Begin Lockdown", DialogResult = DialogResult.None, Location = new Point(20, 95), Width = 260 };
            btnOk.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_txtName.Text))
                {
                    lblError.Text = "Please enter your name to continue.";
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };

            AcceptButton = btnOk;

            Controls.AddRange(new Control[] { lbl, _txtName, lblError, btnOk });
        }
    }
}
