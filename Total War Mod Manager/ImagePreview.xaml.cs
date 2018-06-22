using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Imaging.DDSReader;

namespace Total_War_Mod_Manager
{
    public partial class ImagePreview : Window
    {
        public ImagePreview(PackedFile packedFile)
        {
            InitializeComponent();
            if (packedFile.Name.EndsWith(".dds"))
            {
                var image = DDS.LoadImage(packedFile.Data);
                Image1.Source = Convert(image);
            }
            else if (packedFile.Name.EndsWith(".png"))
            {
                Image1.Source = Convert(packedFile.Data);
            }

            Title = "Preview: " + packedFile.FullPath;
        }

        private BitmapImage Convert(Bitmap src)
        {
            MemoryStream ms = new MemoryStream();
            src.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            image.StreamSource = ms;
            image.EndInit();
            return image;
        }

        private BitmapImage Convert(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0) return null;
            var image = new BitmapImage();
            using (var mem = new MemoryStream(imageData))
            {
                mem.Position = 0;
                image.BeginInit();
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = null;
                image.StreamSource = mem;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }
    }
}
