using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Il2CppDumper.Forms
{
    /// <summary>
    /// Interaction logic for InputOffsetWindow.xaml
    /// </summary>
    public partial class InputOffsetForm : Window
    {
        public string ReturnedOffset { get; set; }

        public InputOffsetForm()
        {
            InitializeComponent();
        }

        private void okBtn_Click(object sender, RoutedEventArgs e)
        {
            ReturnedOffset = dumpAdrTxtBox.Text;
            DialogResult = true;
            Close();
        }

        private static readonly Regex HexadecimalRegex = new Regex("^[0-9A-Fa-f]+$");

        private void HexTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Validate the input character
            e.Handled = !IsHexadecimal(e.Text);
        }

        private void HexTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string));
                if (!IsHexadecimal(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private static bool IsHexadecimal(string input)
        {
            // Check if the input string is a valid hexadecimal
            return HexadecimalRegex.IsMatch(input);
        }
    }
}
