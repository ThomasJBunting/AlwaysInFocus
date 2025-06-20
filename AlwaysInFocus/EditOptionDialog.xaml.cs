using System.Windows;

namespace AlwaysInFocus
{
    public partial class EditOptionDialog : Window
    {
        public string DisplayText { get; set; }
        public string Id { get; set; }

        public EditOptionDialog(string displayText, string id)
        {
            InitializeComponent();
            DisplayTextBox.Text = displayText;
            IdTextBox.Text = id;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DisplayText = DisplayTextBox.Text;
            Id = IdTextBox.Text;
            DialogResult = true;

        }
    }
} 