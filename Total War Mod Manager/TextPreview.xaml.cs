using System.Text;
using System.Windows;

namespace Total_War_Mod_Manager
{
    public partial class TextPreview : Window
    {
        public static string[] SUPPORTED_FORMATS = { ".xml", ".environment", ".wsmodel", ".material", ".variantmeshdefinition", ".lua" };

        public TextPreview(PackedFile packedFile)
        {
            InitializeComponent();
            if (packedFile.Name.EndsWith(".xml") || packedFile.Name.EndsWith(".environment") || packedFile.Name.EndsWith(".wsmodel") || packedFile.Name.EndsWith(".material") || packedFile.Name.EndsWith(".variantmeshdefinition"))
            {
                TextBox1.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(".xml");
                TextBox1.ShowLineNumbers = true;
            }
            else if (packedFile.Name.EndsWith(".lua"))
            {
                TextBox1.SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(".c");
                TextBox1.ShowLineNumbers = true;
            }
            TextBox1.Text = Encoding.Default.GetString(packedFile.Data);

            Title = "Preview: " + packedFile.FullPath;
        }
    }
}
