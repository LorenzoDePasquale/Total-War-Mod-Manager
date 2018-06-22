using System.Windows;

namespace Total_War_Mod_Manager
{
    /// <summary>
    /// Logica di interazione per WindowNewProfile.xaml
    /// </summary>
    public partial class WindowNewProfile : Window
    {

        public WindowNewProfile(string[] currentProfiles)
        {
            InitializeComponent();
            ComboBoxCopyProfile.ItemsSource = currentProfiles;
            ComboBoxCopyProfile.SelectedIndex = 0;
        }

        public bool? ShowDialog(out string name, out string copyFrom)
        {
            bool? dr = ShowDialog();
            if (dr == true)
            {
                name = textBoxProfile.Text;
                copyFrom = ComboBoxCopyProfile.SelectedItem.ToString();
            }
            else
            {
                name = null;
                copyFrom = null;
            }
            return dr;
        }

        private void buttonCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void buttonCreatProfile_Click(object sender, RoutedEventArgs e)
        {
            if (textBoxProfile.Text != "" && textBoxProfile.Text != "New profile...")
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
