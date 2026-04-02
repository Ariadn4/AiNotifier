using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string outputPath = args.Length > 0 ? args[0] : "app.ico";

        // Render ONE high-res image, then resize for all icon sizes
        // This guarantees identical proportions at every size
        var masterPng = RenderRobotIcon(256);

        // Save preview
        string previewPath = Path.ChangeExtension(outputPath, ".preview.png");
        File.WriteAllBytes(previewPath, masterPng);
        Console.WriteLine($"Preview saved to: {Path.GetFullPath(previewPath)}");

        // Decode master image
        var masterBitmap = LoadPng(masterPng);

        // Generate all sizes by downscaling the single master image
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngFrames = new List<(byte[] data, int size)>();

        foreach (int size in sizes)
        {
            if (size == 256)
            {
                pngFrames.Add((masterPng, 256));
            }
            else
            {
                var resized = ResizePng(masterBitmap, size);
                pngFrames.Add((resized, size));
            }
        }

        // Write ICO file
        WriteIco(outputPath, pngFrames);
        Console.WriteLine($"Icon saved to: {Path.GetFullPath(outputPath)}");
    }

    static byte[] RenderRobotIcon(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            double s = size; // scale factor
            double cx = s / 2, cy = s / 2;
            double radius = s / 2;

            // Blue gradient background circle
            var gradientBrush = new RadialGradientBrush();
            gradientBrush.GradientOrigin = new Point(0.38, 0.30);
            gradientBrush.Center = new Point(0.38, 0.30);
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x7d, 0xd3, 0xfc), 0));
            gradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x02, 0x84, 0xc7), 1));
            dc.DrawEllipse(gradientBrush, null, new Point(cx, cy), radius, radius);

            // Icon needs the robot larger than the floating ball for readability at small sizes.
            // Robot canvas is 100x80. We want it to fill ~85% of the icon circle.
            // Use the wider dimension (100) to determine scale: 100 * canvasScale = 0.85 * s
            double canvasScale = 1.0 * s / 100.0;
            double offsetX = (s - 100 * canvasScale) / 2.0;
            double offsetY = (s - 80 * canvasScale) / 2.0 - (1.0 / 96.0 * s);

            // Helper to transform canvas coords to icon coords
            Point P(double canvasX, double canvasY) =>
                new Point(offsetX + canvasX * canvasScale, offsetY + canvasY * canvasScale);
            double S(double canvasVal) => canvasVal * canvasScale;

            var whiteBrush = Brushes.White;
            var white80 = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)); // 0.8 opacity
            var white95 = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)); // 0.95 opacity
            var white70 = new SolidColorBrush(Color.FromArgb(179, 255, 255, 255)); // 0.7 opacity
            var blueBrush = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xc7));
            var lightBlueBrush = new SolidColorBrush(Color.FromArgb(191, 0xba, 0xe6, 0xfd)); // antenna tip
            var blushBrush = new SolidColorBrush(Color.FromArgb(89, 0xfd, 0xa4, 0xaf)); // 0.35 opacity
            var eyeHighlight = new SolidColorBrush(Color.FromArgb(166, 255, 255, 255)); // 0.65 opacity
            var dotBrush1 = new SolidColorBrush(Color.FromArgb(128, 0x02, 0x84, 0xc7));
            var dotBrush2 = new SolidColorBrush(Color.FromArgb(128, 0x38, 0xbd, 0xf8));

            // Antenna stem
            var antennaPen = new Pen(whiteBrush, S(3)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(antennaPen, P(50, 18), P(50, 6));

            // Antenna tip
            dc.DrawEllipse(lightBlueBrush, null, P(50, 4), S(4), S(4));

            // Head (rounded rectangle)
            var headRect = new Rect(P(24, 18), new Size(S(52), S(42)));
            dc.DrawRoundedRectangle(white95, null, headRect, S(14), S(14));

            // Left ear
            var leftEarRect = new Rect(P(14, 30), new Size(S(10), S(16)));
            dc.DrawRoundedRectangle(white80, null, leftEarRect, S(5), S(5));

            // Right ear
            var rightEarRect = new Rect(P(76, 30), new Size(S(10), S(16)));
            dc.DrawRoundedRectangle(white80, null, rightEarRect, S(5), S(5));

            // Body
            var bodyRect = new Rect(P(36, 62), new Size(S(28), S(14)));
            dc.DrawRoundedRectangle(white70, null, bodyRect, S(7), S(7));

            // Body dots
            dc.DrawEllipse(dotBrush1, null, P(44, 69), S(2), S(2));
            dc.DrawEllipse(dotBrush2, null, P(50, 69), S(2), S(2));
            dc.DrawEllipse(dotBrush1, null, P(56, 69), S(2), S(2));

            // === ON State Face ===

            // Left eye
            dc.DrawEllipse(blueBrush, null, P(38, 38), S(6.5), S(7));
            dc.DrawEllipse(eyeHighlight, null, P(36, 35.5), S(2.2), S(2.2));

            // Right eye
            dc.DrawEllipse(blueBrush, null, P(62, 38), S(6.5), S(7));
            dc.DrawEllipse(eyeHighlight, null, P(60, 35.5), S(2.2), S(2.2));

            // Blush
            dc.DrawEllipse(blushBrush, null, P(28, 48), S(6), S(3));
            dc.DrawEllipse(blushBrush, null, P(72, 48), S(6), S(3));

            // Smile
            var smilePen = new Pen(blueBrush, S(3)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var smileGeometry = new StreamGeometry();
            using (var ctx = smileGeometry.Open())
            {
                ctx.BeginFigure(P(38, 50), false, false);
                ctx.QuadraticBezierTo(P(50, 60), P(62, 50), true, true);
            }
            smileGeometry.Freeze();
            dc.DrawGeometry(null, smilePen, smileGeometry);
        }

        // Render to bitmap
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        // Encode to PNG
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    static BitmapSource LoadPng(byte[] pngData)
    {
        using var ms = new MemoryStream(pngData);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    static byte[] ResizePng(BitmapSource source, int size)
    {
        double scaleX = (double)size / source.PixelWidth;
        double scaleY = (double)size / source.PixelHeight;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));

        // Re-render with high quality
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(source, new Rect(0, 0, size, size));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    static void WriteIco(string path, List<(byte[] data, int size)> frames)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);

        // ICO header
        bw.Write((short)0);          // Reserved
        bw.Write((short)1);          // Type: ICO
        bw.Write((short)frames.Count); // Number of images

        // Calculate offset for image data (after header + directory entries)
        int dataOffset = 6 + frames.Count * 16;

        // Write directory entries
        foreach (var (data, size) in frames)
        {
            bw.Write((byte)(size >= 256 ? 0 : size)); // Width
            bw.Write((byte)(size >= 256 ? 0 : size)); // Height
            bw.Write((byte)0);     // Color palette
            bw.Write((byte)0);     // Reserved
            bw.Write((short)1);    // Color planes
            bw.Write((short)32);   // Bits per pixel
            bw.Write(data.Length); // Size of image data
            bw.Write(dataOffset);  // Offset to image data
            dataOffset += data.Length;
        }

        // Write image data (PNG format)
        foreach (var (data, _) in frames)
        {
            bw.Write(data);
        }
    }
}
