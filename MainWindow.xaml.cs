using System;
using System.Collections.Generic;
using SDrawing = System.Drawing;
using SDI = System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;


namespace CreateIcon
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Removed automatic export on startup; user now triggers via button.
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

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(openDialog.FileName);
                bmp.EndInit();

                IconCanvas.Children.Clear();

                var img = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Uniform
                };
                // Let the image keep its natural pixel size; size the canvas explicitly.
                if (double.IsNaN(IconCanvas.Width) || IconCanvas.Width <= 0)
                    IconCanvas.Width = bmp.PixelWidth;
                if (double.IsNaN(IconCanvas.Height) || IconCanvas.Height <= 0)
                    IconCanvas.Height = bmp.PixelHeight;

                Canvas.SetLeft(img, 0);
                Canvas.SetTop(img, 0);
                IconCanvas.Children.Add(img);
                IconCanvas.UpdateLayout(); // Ensure ActualWidth/Height are valid

                // Auto-save .ico into 'icon' sub-directory beside source .png
                string pngDir = System.IO.Path.GetDirectoryName(openDialog.FileName);
                string iconDir = System.IO.Path.Combine(pngDir, "icon");
                Directory.CreateDirectory(iconDir);
                string iconFileName = System.IO.Path.GetFileNameWithoutExtension(openDialog.FileName) + ".ico";
                string targetIconPath = System.IO.Path.Combine(iconDir, iconFileName);

                ExportToIconFile(IconCanvas, targetIconPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Operation failed: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportToIconFile(Canvas canvas, string filename)
        {
            // Instead of rendering the canvas (which was producing blank/grey),
            // extract the original BitmapSource and scale directly.
            if (canvas.Children.Count == 0 || !(canvas.Children[0] is Image baseImg) || !(baseImg.Source is BitmapSource original))
            {
                MessageBox.Show(this, "No image loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            original.Freeze();

            int origW = original.PixelWidth;
            int origH = original.PixelHeight;

            var sizes = new[] { 16, 32, 48, 64, 128, 256 };
            var imageBlobs = new List<byte[]>(sizes.Length);

            foreach (var size in sizes)
            {
                var dv = new DrawingVisual();
                // Ensure high quality bitmap scaling
                RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
                    double scale = Math.Min((double)size / origW, (double)size / origH);
                    double drawW = origW * scale;
                    double drawH = origH * scale;
                    double offsetX = (size - drawW) / 2.0;
                    double offsetY = (size - drawH) / 2.0;
                    dc.DrawImage(original, new Rect(offsetX, offsetY, drawW, drawH));
                }

                var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();

                imageBlobs.Add(BuildIconImageData(ToGdiBitmap(rtb)));
            }

            using (var fs = new FileStream(filename, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);
                bw.Write((short)1);
                bw.Write((short)sizes.Length);

                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
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

            MessageBox.Show(this, "Icon file created:\n" + filename, "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Convert WPF RenderTargetBitmap to System.Drawing.Bitmap (32bpp ARGB)
        // Convert WPF RenderTargetBitmap (Pbgra32 premultiplied) to non‑premultiplied System.Drawing.Bitmap (32bpp ARGB)
        private static System.Drawing.Bitmap ToGdiBitmap(BitmapSource src)
        {
            int width = src.PixelWidth;
            int height = src.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            src.CopyPixels(pixels, stride, 0);

            // Un-premultiply if needed (Pbgra32 -> straight alpha)
            if (src.Format == PixelFormats.Pbgra32)
            {
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte a = pixels[i + 3];
                    if (a > 0 && a < 255)
                    {
                        // channels in order: B,G,R,A
                        pixels[i + 0] = (byte)Math.Min(255, (pixels[i + 0] * 255 + (a / 2)) / a);
                        pixels[i + 1] = (byte)Math.Min(255, (pixels[i + 1] * 255 + (a / 2)) / a);
                        pixels[i + 2] = (byte)Math.Min(255, (pixels[i + 2] * 255 + (a / 2)) / a);
                    }
                }
            }

            var bmp = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, width, height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
            try
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        private static byte[] BuildIconImageData(System.Drawing.Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            const int headerSize = 40;
            int xorStride = width * 4; // logical row size we will write
            int andStrideBytes = ((width + 31) / 32) * 4;
            int andMaskSize = andStrideBytes * height;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                // BITMAPINFOHEADER
                bw.Write(headerSize);
                bw.Write(width);
                bw.Write(height * 2);   // XOR + AND
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write(0);
                bw.Write(xorStride * height);
                bw.Write(0); bw.Write(0);
                bw.Write(0); bw.Write(0);

                var bmpData = bmp.LockBits(
                    new System.Drawing.Rectangle(0, 0, width, height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    int srcStride = bmpData.Stride;
                    int total = srcStride * height;
                    byte[] pixelData = new byte[total];
                    Marshal.Copy(bmpData.Scan0, pixelData, 0, total);

                    // Write rows bottom-up (only the meaningful width*4 bytes per row)
                    for (int y = height - 1; y >= 0; y--)
                    {
                        bw.Write(pixelData, y * srcStride, xorStride);
                    }
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }

                // AND mask (opaque)
                byte[] andRow = new byte[andStrideBytes];
                for (int y = 0; y < height; y++)
                    bw.Write(andRow);

                return ms.ToArray();
            }
        }
    }
}
    
