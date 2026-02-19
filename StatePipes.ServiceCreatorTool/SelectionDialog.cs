namespace StatePipes.ServiceCreatorTool
{
    internal static class SelectionDialog
    {
        public static string? ShowDllSelection(string serviceBinDirectory)
        {
            OpenFileDialog dialog = new()
            {
                Title = "Select Class Library To Create Proxy To",
                Filter = "dll files (*.dll)|*.dll|All files (*.*)|*.*",
                Multiselect = false
            };
            if (Directory.Exists(serviceBinDirectory))
            {
                dialog.InitialDirectory = serviceBinDirectory;
                dialog.RestoreDirectory = true;
            }
            if (DialogResult.OK == dialog.ShowDialog())
            {
                Console.WriteLine($"dll file: {dialog.FileName}");
                return dialog.FileName;
            }
            else
            {
                Console.WriteLine("Action canceled.");
                return null;
            }
        }
        public static string? ShowListSelection(List<string> options, string title)
        {
            Size size = new(700, 200);
            Form inputBox = new()
            {
                Size = size,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                ClientSize = size,
                Text = title
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
                Text = question
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
    }
}
