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
            
        }

        /// <summary>
        /// Handles the click event for the "Load and Create" button.  Allows the user to select a PNG image, displays
        /// it on a canvas, and generates an ICO file from the image.
        /// </summary>
        /// <remarks>This method opens a file dialog for the user to select a PNG image. The selected
        /// image is loaded  and displayed on a canvas. The canvas is resized to match the dimensions of the image if
        /// necessary.  An ICO file is automatically generated from the image and saved in an "icon" subdirectory 
        /// located beside the source PNG file.</remarks>
        /// <param name="sender">The source of the event, typically the button that was clicked.</param>
        /// <param name="e">The event data associated with the click event.</param>
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
                // Always size the canvas to the image pixel size for an accurate preview
                IconCanvas.Width = bmp.PixelWidth;
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
/// <summary>
/// Exports the content of a <see cref="Canvas"/> to an ICO file with multiple icon sizes.
/// </summary>
/// <remarks>This method generates an ICO file containing multiple icon sizes (16x16, 32x32, 48x48, 64x64,
/// 128x128, and 256x256 pixels).  The method scales the image from the canvas to fit each size while maintaining the
/// aspect ratio, and centers it within the icon dimensions.  If the canvas does not contain a valid image, an error
/// message is displayed, and the method exits without creating a file.</remarks>
/// <param name="canvas">The <see cref="Canvas"/> containing the image to export. The first child of the canvas must be an <see
/// cref="Image"/> with a <see cref="BitmapSource"/> as its source.</param>
/// <param name="filename">The full path of the output ICO file. If the file already exists, it will be overwritten.</param>
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

                // Dispose the GDI+ bitmap after use to avoid leaks
                using (var gdiBmp = ToGdiBitmap(rtb))
                {
                    imageBlobs.Add(BuildIconImageData(gdiBmp));
                }
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


        /// <summary>
        /// Converts a <see cref="BitmapSource"/> to a <see cref="System.Drawing.Bitmap"/>.
        /// </summary>
        /// <remarks>This method creates a new <see cref="System.Drawing.Bitmap"/> with a 32bpp ARGB pixel
        /// format. If the source <see cref="BitmapSource"/> uses the <see cref="PixelFormats.Pbgra32"/> format,  the
        /// method will un-premultiply the alpha channel to ensure correct color representation.</remarks>
        /// <param name="src">The <see cref="BitmapSource"/> to convert. Must not be null.</param>
        /// <returns>A <see cref="System.Drawing.Bitmap"/> representation of the input <see cref="BitmapSource"/>.</returns>
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

        /// <summary>
        /// Builds the image data for an icon from the specified bitmap.
        /// </summary>
        /// <remarks>The returned byte array represents the image data in a format suitable for use in an
        /// icon file.  The method generates a 32-bit color XOR mask from the bitmap's pixel data and an opaque AND
        /// mask. The bitmap is processed bottom-up, as required by the icon format.</remarks>
        /// <param name="bmp">The <see cref="System.Drawing.Bitmap"/> to convert into icon image data. The bitmap must have a pixel format
        /// of <see cref="System.Drawing.Imaging.PixelFormat.Format32bppArgb"/>.</param>
        /// <returns>A byte array containing the icon image data, including both the XOR and AND masks.</returns>
        private static byte[] BuildIconImageData(System.Drawing.Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;

            // Use PNG encoding for 256x256 entries for better compatibility
            if (width == 256 && height == 256)
            {
                using (var msPng = new MemoryStream())
                {
                    bmp.Save(msPng, ImageFormat.Png);
                    return msPng.ToArray();
                }
            }

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
    
