namespace QuizLock
{
    /// <summary>
    /// Tiny always-on-top password prompt shown when the user presses
    /// the unlock hotkey (Ctrl+Alt+Shift+U) while QuizLock is locked.
    /// </summary>
    internal sealed class UnlockPromptForm : Form
    {
        private readonly TextBox _txtPassword = new() { UseSystemPasswordChar = true, Width = 260 };
        public string EnteredPassword => _txtPassword.Text;

        public UnlockPromptForm()
        {
            Text = "Enter unlock password";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(320, 130);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var lbl = new Label { Text = "Password:", AutoSize = true, Location = new Point(20, 20) };
            _txtPassword.Location = new Point(20, 45);

            var btnOk = new Button { Text = "Unlock", DialogResult = DialogResult.OK, Location = new Point(20, 80) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(110, 80) };

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lbl, _txtPassword, btnOk, btnCancel });
        }
    }
}
