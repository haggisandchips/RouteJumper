using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace RouteJumper.Services.Companion
{
    /// <summary>Renders a companion site URL (RouteView's QR popup, SPEC §13) as a WPF-displayable QR code image.</summary>
    public static class QrCodeImageFactory
    {
        public static BitmapImage Generate(string url)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(data).GetGraphic(10);

            var image = new BitmapImage();
            using (var stream = new MemoryStream(png))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }

            image.Freeze(); // safe to hand to a bound property from a background continuation
            return image;
        }
    }
}
