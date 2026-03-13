using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SkiaSharp;
using Svg.Skia;

namespace CreateIcon
{
    public partial class MainWindow : Window
    {
        private static readonly int[] IconSizes = [16, 32, 48, 64, 128, 256];

        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnLoadAndCreate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openDialog = new OpenFileDialog
                {
                    Title = "Select PNG Image",
                    Filter = "PNG Images (*.png)|*.png",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (openDialog.ShowDialog(this) != true)
                    return;

                var bitmap = LoadBitmapFromFile(openDialog.FileName);
                ShowPreview(bitmap);
                ExportBitmapToIconFile(bitmap, GetTargetIconPath(openDialog.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Operation failed: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoadSvgAndCreate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openDialog = new OpenFileDialog
                {
                    Title = "Select SVG Image",
                    Filter = "SVG Images (*.svg)|*.svg",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (openDialog.ShowDialog(this) != true)
                    return;

                var preview = RenderSvgToBitmap(openDialog.FileName, 256);
                ShowPreview(preview);
                ExportSvgToIconFile(openDialog.FileName, GetTargetIconPath(openDialog.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Operation failed: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static BitmapImage LoadBitmapFromFile(string fileName)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(fileName);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private void ShowPreview(BitmapSource bitmap)
        {
            IconCanvas.Children.Clear();

            var img = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform
            };

            IconCanvas.Width = Math.Max(bitmap.PixelWidth, 1);
            IconCanvas.Height = Math.Max(bitmap.PixelHeight, 1);

            Canvas.SetLeft(img, 0);
            Canvas.SetTop(img, 0);
            IconCanvas.Children.Add(img);
            IconCanvas.UpdateLayout();
        }

        private static string GetTargetIconPath(string sourceFileName)
        {
            string sourceDir = Path.GetDirectoryName(sourceFileName) ?? AppContext.BaseDirectory;
            string iconDir = Path.Combine(sourceDir, "icon");
            Directory.CreateDirectory(iconDir);
            string iconFileName = Path.GetFileNameWithoutExtension(sourceFileName) + ".ico";
            return Path.Combine(iconDir, iconFileName);
        }

        private void ExportBitmapToIconFile(BitmapSource original, string filename)
        {
            if (original.PixelWidth <= 0 || original.PixelHeight <= 0)
            {
                MessageBox.Show(this, "No image loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var imageBlobs = new List<byte[]>(IconSizes.Length);
            foreach (var size in IconSizes)
                imageBlobs.Add(EncodeToPngBytes(RenderBitmapAtSize(original, size)));

            WriteIconFile(filename, imageBlobs);
            MessageBox.Show(this, "Icon file created:\n" + filename, "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportSvgToIconFile(string svgPath, string filename)
        {
            var imageBlobs = new List<byte[]>(IconSizes.Length);
            foreach (var size in IconSizes)
                imageBlobs.Add(EncodeToPngBytes(RenderSvgToBitmap(svgPath, size)));

            WriteIconFile(filename, imageBlobs);
            MessageBox.Show(this, "Icon file created:\n" + filename, "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static RenderTargetBitmap RenderBitmapAtSize(BitmapSource original, int size)
        {
            double scale = Math.Min((double)size / original.PixelWidth, (double)size / original.PixelHeight);
            double drawW = original.PixelWidth * scale;
            double drawH = original.PixelHeight * scale;
            double offsetX = (size - drawW) / 2.0;
            double offsetY = (size - drawH) / 2.0;

            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
                dc.DrawImage(original, new Rect(offsetX, offsetY, drawW, drawH));
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private static BitmapSource RenderSvgToBitmap(string svgPath, int size)
        {
            using var stream = File.OpenRead(svgPath);
            using var svg = new SKSvg();
            var picture = svg.Load(stream) ?? throw new InvalidOperationException("Unable to load SVG file.");
            var bounds = picture.CullRect;

            if (bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException("SVG file has invalid dimensions.");

            using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            float scale = Math.Min(size / bounds.Width, size / bounds.Height);
            float offsetX = (size - bounds.Width * scale) / 2f;
            float offsetY = (size - bounds.Height * scale) / 2f;

            canvas.Translate(offsetX - bounds.Left * scale, offsetY - bounds.Top * scale);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return LoadBitmapFromBytes(data.ToArray());
        }

        private static BitmapImage LoadBitmapFromBytes(byte[] data)
        {
            using var ms = new MemoryStream(data);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private static void WriteIconFile(string filename, List<byte[]> imageBlobs)
        {
            using var fs = new FileStream(filename, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            bw.Write((short)0);
            bw.Write((short)1);
            bw.Write((short)IconSizes.Length);

            int offset = 6 + IconSizes.Length * 16;
            for (int i = 0; i < IconSizes.Length; i++)
            {
                int s = IconSizes[i];
                byte widthByte = (byte)(s >= 256 ? 0 : s);
                byte heightByte = (byte)(s >= 256 ? 0 : s);
                var blob = imageBlobs[i];

                bw.Write(widthByte);
                bw.Write(heightByte);
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write(blob.Length);
                bw.Write(offset);
                offset += blob.Length;
            }

            foreach (var blob in imageBlobs)
                bw.Write(blob);
        }

        private static byte[] EncodeToPngBytes(BitmapSource src)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            HelpPopup.IsOpen = true;
        }

        private void HelpClose_Click(object sender, RoutedEventArgs e)
        {
            HelpPopup.IsOpen = false;
        }
    }
}
