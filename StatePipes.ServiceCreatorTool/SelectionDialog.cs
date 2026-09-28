namespace StatePipes.ServiceCreatorTool
{
    internal static class SelectionDialog
    {
        public static string? ShowListSelection(List<string> options, string title)
        {
            Size size = new(700, 200);
            Form inputBox = new()
            {
                Size = size,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                ClientSize = size,
                Text = title,
                TopMost = true
            };
            ListBox listBox = new();
            foreach (var option in options) listBox.Items.Add(option);
            listBox.SelectedIndex = 0;
            listBox.Size = new Size(size.Width - 80 - 80 - 80, size.Height - 30);
            inputBox.Controls.Add(listBox);
            Button okButton = new()
            {
                DialogResult = DialogResult.OK,
                Name = "okButton",
                Size = new Size(75, 23),
                Text = "&OK",
                Location = new Point(size.Width - 80 - 80, size.Height - 30)
            };
            inputBox.Controls.Add(okButton);
            Button cancelButton = new()
            {
                DialogResult = DialogResult.Cancel,
                Name = "cancelButton",
                Size = new Size(75, 23),
                Text = "&Cancel",
                Location = new Point(size.Width - 80, size.Height - 30)
            };
            inputBox.Controls.Add(cancelButton);
            inputBox.AcceptButton = okButton;
            inputBox.CancelButton = cancelButton;
            if (inputBox.ShowDialog() == DialogResult.OK && listBox.SelectedItem != null)
            {
                return listBox.SelectedItem.ToString();
            }
            Console.WriteLine("Action canceled.");
            return null;
        }
        public static DialogResult ShowInputDialog(ref string input, string question)
        {
            Size size = new(800, 70);
            Form inputBox = new()
            {
                FormBorderStyle = FormBorderStyle.FixedDialog,
                ClientSize = size,
                Text = question,
                TopMost = true
            };
            TextBox textBox = new()
            {
                Size = new Size(size.Width - 10, 23),
                Location = new Point(5, 5),
                Text = input
            };
            inputBox.Controls.Add(textBox);
            Button okButton = new()
            {
                DialogResult = DialogResult.OK,
                Name = "okButton",
                Size = new Size(75, 23),
                Text = "&OK",
                Location = new Point(size.Width - 80 - 80, 39)
            };
            inputBox.Controls.Add(okButton);
            Button cancelButton = new()
            {
                DialogResult = DialogResult.Cancel,
                Name = "cancelButton",
                Size = new Size(75, 23),
                Text = "&Cancel",
                Location = new Point(size.Width - 80, 39)
            };
            inputBox.Controls.Add(cancelButton);
            inputBox.AcceptButton = okButton;
            inputBox.CancelButton = cancelButton;
            DialogResult result = inputBox.ShowDialog();
            input = textBox.Text;
            return result;
        }
        public static bool GetUserInput(ref string userEnteredString, string prompt)
        {
            if (ShowInputDialog(ref userEnteredString, prompt) == DialogResult.OK)
            {
                if (!string.IsNullOrEmpty(userEnteredString)) return true;
            }
            Console.WriteLine($"Bad User Input for {prompt}");
            return false;
        }

        public static bool GetUserConfirmation(string prompt)
        {
            Size size = new(500, 130);
            Form confirmBox = new()
            {
                FormBorderStyle = FormBorderStyle.FixedDialog,
                ClientSize = size,
                Text = "Confirm",
                TopMost = true
            };
            // The prompt goes in the body rather than the title bar so a long question wraps instead of truncating.
            Label promptLabel = new()
            {
                Size = new Size(size.Width - 10, size.Height - 45),
                Location = new Point(5, 5),
                Text = prompt
            };
            confirmBox.Controls.Add(promptLabel);
            Button yesButton = new()
            {
                DialogResult = DialogResult.Yes,
                Name = "yesButton",
                Size = new Size(75, 23),
                Text = "&Yes",
                Location = new Point(size.Width - 80 - 80, size.Height - 30)
            };
            confirmBox.Controls.Add(yesButton);
            Button noButton = new()
            {
                DialogResult = DialogResult.No,
                Name = "noButton",
                Size = new Size(75, 23),
                Text = "&No",
                Location = new Point(size.Width - 80, size.Height - 30)
            };
            confirmBox.Controls.Add(noButton);
            confirmBox.AcceptButton = yesButton;
            // Esc and the window's close button both land here, so anything short of an explicit Yes is a No.
            confirmBox.CancelButton = noButton;
            if (confirmBox.ShowDialog() == DialogResult.Yes) return true;
            Console.WriteLine($"User declined: {prompt}");
            return false;
        }

        public static bool SelectFile(out string fileName, string prompt, string fileFilter = "All files (*.*)|*.*", string? initialDirectory = null)
        {
            OpenFileDialog dialog = new(){ Title = prompt, Filter = fileFilter, Multiselect = false};
            if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
                dialog.RestoreDirectory = true;
            }
            if (DialogResult.OK == dialog.ShowDialog())
            {
                Console.WriteLine($"file: {dialog.FileName}");
                fileName = dialog.FileName;
                return true;
            }
            Console.WriteLine($"Action ({prompt}) canceled.");
            fileName = string.Empty;
            return false;
        }
    }
}
