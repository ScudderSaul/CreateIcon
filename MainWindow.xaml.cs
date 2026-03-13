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

        private void SaveSampleSvg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Title = "Save Sample SVG",
                    Filter = "SVG Images (*.svg)|*.svg",
                    DefaultExt = ".svg",
                    FileName = "sample-dragon.svg",
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (saveDialog.ShowDialog(this) != true)
                    return;

                File.WriteAllText(saveDialog.FileName, GetSampleDragonSvg());
                MessageBox.Show(this, "Sample SVG file created:\n" + saveDialog.FileName, "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Unable to save sample SVG: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HelpClose_Click(object sender, RoutedEventArgs e)
        {
            HelpPopup.IsOpen = false;
        }

        private static string GetSampleDragonSvg() =>
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
              <defs>
                <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0%" stop-color="#1f1634"/>
                  <stop offset="100%" stop-color="#3b1020"/>
                </linearGradient>
                <linearGradient id="body" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0%" stop-color="#ff8661"/>
                  <stop offset="45%" stop-color="#d63838"/>
                  <stop offset="100%" stop-color="#7f1020"/>
                </linearGradient>
                <linearGradient id="wing" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0%" stop-color="#fff3a8"/>
                  <stop offset="45%" stop-color="#f5c84b"/>
                  <stop offset="100%" stop-color="#bb7a12"/>
                </linearGradient>
                <radialGradient id="flame" cx="50%" cy="50%" r="50%">
                  <stop offset="0%" stop-color="#fff9d4"/>
                  <stop offset="35%" stop-color="#ffd34d"/>
                  <stop offset="70%" stop-color="#ff7a1f"/>
                  <stop offset="100%" stop-color="#c91b00"/>
                </radialGradient>
                <filter id="glow" x="-30%" y="-30%" width="160%" height="160%">
                  <feGaussianBlur stdDeviation="6" result="blur"/>
                  <feMerge>
                    <feMergeNode in="blur"/>
                    <feMergeNode in="SourceGraphic"/>
                  </feMerge>
                </filter>
              </defs>

              <rect width="512" height="512" rx="72" fill="url(#bg)"/>

              <ellipse cx="270" cy="435" rx="165" ry="34" fill="#00000055"/>

              <g transform="translate(18 8)">
                <path d="M198 270 C142 212, 122 132, 152 84 C206 98, 255 140, 284 218 C259 232, 228 249, 198 270 Z"
                      fill="url(#wing)" stroke="#fff2b0" stroke-width="7" opacity="0.96"/>
                <path d="M251 248 C196 179, 187 94, 220 48 C287 83, 338 155, 352 241 C320 239, 286 242, 251 248 Z"
                      fill="url(#wing)" stroke="#fff2b0" stroke-width="7" opacity="0.98"/>
                <path d="M214 271 C169 225, 156 158, 174 110" fill="none" stroke="#c98d21" stroke-width="5" opacity="0.8"/>
                <path d="M269 244 C237 194, 239 128, 255 80" fill="none" stroke="#c98d21" stroke-width="5" opacity="0.8"/>

                <path d="M131 373 C91 340, 86 289, 122 258 C155 230, 215 231, 255 252 C296 273, 331 319, 349 345 C382 393, 383 428, 356 445 C325 464, 269 453, 230 427 C207 412, 189 391, 169 385 C151 379, 141 380, 131 373 Z"
                      fill="url(#body)" stroke="#5a0914" stroke-width="8"/>

                <path d="M121 261 C102 230, 109 192, 145 172 C186 149, 242 157, 281 182 C321 207, 344 250, 334 285 C302 251, 246 227, 194 226 C161 225, 136 237, 121 261 Z"
                      fill="#a3182b" opacity="0.82"/>

                <path d="M323 249 C360 224, 404 220, 438 236 C469 252, 480 287, 462 314 C444 341, 401 352, 362 343 C331 337, 306 322, 291 304 C305 290, 315 271, 323 249 Z"
                      fill="url(#body)" stroke="#5a0914" stroke-width="8"/>

                <ellipse cx="369" cy="284" rx="65" ry="58" fill="#cf3042" opacity="0.7"/>
                <path d="M338 245 L359 213 L376 245" fill="#ffd34d" stroke="#8b4d00" stroke-width="5"/>
                <path d="M366 236 L388 200 L406 239" fill="#ffd34d" stroke="#8b4d00" stroke-width="5"/>
                <path d="M393 241 L415 213 L430 247" fill="#ffd34d" stroke="#8b4d00" stroke-width="5"/>

                <ellipse cx="392" cy="279" rx="15" ry="18" fill="#fffaf0"/>
                <ellipse cx="396" cy="282" rx="7" ry="9" fill="#191919"/>
                <circle cx="399" cy="279" r="2.4" fill="#ffffff"/>

                <path d="M440 295 C460 292, 480 304, 490 322 C469 321, 454 320, 440 317 Z" fill="#5a0914"/>
                <path d="M487 318 C520 300, 546 297, 573 303 C546 316, 527 327, 501 343 C484 352, 459 356, 437 347 C457 342, 474 332, 487 318 Z"
                      transform="translate(-8 0)"
                      fill="url(#flame)" filter="url(#glow)"/>
                <path d="M495 330 C523 324, 546 332, 568 349 C540 351, 517 359, 490 373 C472 382, 448 384, 425 374 C451 367, 473 353, 495 330 Z"
                      transform="translate(-10 6)"
                      fill="#ffb11b" opacity="0.85" filter="url(#glow)"/>

                <path d="M140 387 C111 395, 95 419, 85 448" fill="none" stroke="#5a0914" stroke-width="14" stroke-linecap="round"/>
                <path d="M181 409 C156 425, 149 449, 150 475" fill="none" stroke="#5a0914" stroke-width="14" stroke-linecap="round"/>
                <path d="M286 427 C268 446, 265 464, 270 482" fill="none" stroke="#5a0914" stroke-width="14" stroke-linecap="round"/>

                <path d="M108 351 C77 335, 55 311, 49 286 C84 293, 115 309, 138 330" fill="none" stroke="#7f1020" stroke-width="18" stroke-linecap="round"/>
                <path d="M58 286 C78 279, 96 282, 112 291" fill="none" stroke="#ffd34d" stroke-width="7" stroke-linecap="round"/>
              </g>
            </svg>
            """;
    }
}
