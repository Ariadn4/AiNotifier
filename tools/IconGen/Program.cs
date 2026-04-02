using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

// Generate ICO with multiple sizes - must run on STA thread for WPF
var outputArg = args.Length > 0 ? args[0] : @"..\..\src\AiNotifier\Resources\app.ico";

var thread = new Thread(() =>
{
    var outputPath = System.IO.Path.GetFullPath(outputArg);
    Console.WriteLine($"Generating icon to: {outputPath}");

    var png256 = RenderIcon(256);
    var png64 = RenderIcon(64);
    var png48 = RenderIcon(48);
    var png32 = RenderIcon(32);
    var png16 = RenderIcon(16);

    WriteIco(outputPath, [png16, png32, png48, png64, png256]);
    Console.WriteLine("Icon generated successfully!");
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

static byte[] RenderIcon(int size)
{
    var canvas = new Canvas { Width = 256, Height = 256, Background = Brushes.Transparent };

    // No background - transparent

    // Body
    var body = new Rectangle
    {
        Width = 80, Height = 36, RadiusX = 18, RadiusY = 18,
        Fill = Brushes.White, Opacity = 0.18
    };
    Canvas.SetLeft(body, 88);
    Canvas.SetTop(body, 154);
    canvas.Children.Add(body);

    // Body dots
    AddCircle(canvas, 112, 172, 4, Color.FromRgb(0x02, 0x84, 0xc7), 0.45);
    AddCircle(canvas, 128, 172, 4, Color.FromRgb(0x38, 0xbd, 0xf8), 0.45);
    AddCircle(canvas, 144, 172, 4, Color.FromRgb(0x02, 0x84, 0xc7), 0.45);

    // Antenna stem
    var antLine = new Line
    {
        X1 = 128, Y1 = 62, X2 = 128, Y2 = 38,
        Stroke = Brushes.White, StrokeThickness = 5,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        Opacity = 0.75
    };
    canvas.Children.Add(antLine);

    // Antenna tip
    AddCircle(canvas, 128, 34, 8, Color.FromRgb(0x7d, 0xd3, 0xfc), 1.0);
    AddCircle(canvas, 128, 34, 4, Colors.White, 0.5);

    // Head
    var head = new Rectangle
    {
        Width = 120, Height = 90, RadiusX = 32, RadiusY = 32,
        Fill = Brushes.White, Opacity = 0.92
    };
    Canvas.SetLeft(head, 68);
    Canvas.SetTop(head, 62);
    canvas.Children.Add(head);

    // Ears
    AddRoundedRect(canvas, 48, 86, 22, 38, 11, Colors.White, 0.6);
    AddRoundedRect(canvas, 186, 86, 22, 38, 11, Colors.White, 0.6);

    // Eyes
    AddEllipseXY(canvas, 105, 105, 14, 15, Color.FromRgb(0x02, 0x84, 0xc7), 1.0);
    AddCircle(canvas, 101, 100, 5, Colors.White, 0.55);
    AddEllipseXY(canvas, 151, 105, 14, 15, Color.FromRgb(0x02, 0x84, 0xc7), 1.0);
    AddCircle(canvas, 147, 100, 5, Colors.White, 0.55);

    // Blush
    AddEllipseXY(canvas, 88, 122, 10, 5, Color.FromRgb(0xfd, 0xa4, 0xaf), 0.28);
    AddEllipseXY(canvas, 168, 122, 10, 5, Color.FromRgb(0xfd, 0xa4, 0xaf), 0.28);

    // Smile
    var smile = new System.Windows.Shapes.Path
    {
        Data = Geometry.Parse("M108,128 Q128,144 148,128"),
        Stroke = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xc7)),
        StrokeThickness = 5,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        Fill = Brushes.Transparent
    };
    canvas.Children.Add(smile);

    // No text - removed "AI PING"

    // Render at 256x256
    canvas.Measure(new Size(256, 256));
    canvas.Arrange(new Rect(0, 0, 256, 256));
    canvas.UpdateLayout();

    var rtb = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
    rtb.Render(canvas);

    // Scale to target size
    BitmapSource finalBmp = rtb;
    if (size != 256)
    {
        var scaled = new TransformedBitmap(rtb, new ScaleTransform((double)size / 256, (double)size / 256));
        finalBmp = new WriteableBitmap(scaled);
    }

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(finalBmp));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}

static void AddCircle(Canvas canvas, double cx, double cy, double r, Color color, double opacity)
{
    var e = new Ellipse { Width = r * 2, Height = r * 2, Fill = new SolidColorBrush(color), Opacity = opacity };
    Canvas.SetLeft(e, cx - r);
    Canvas.SetTop(e, cy - r);
    canvas.Children.Add(e);
}

static void AddEllipseXY(Canvas canvas, double cx, double cy, double rx, double ry, Color color, double opacity)
{
    var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = new SolidColorBrush(color), Opacity = opacity };
    Canvas.SetLeft(e, cx - rx);
    Canvas.SetTop(e, cy - ry);
    canvas.Children.Add(e);
}

static void AddRoundedRect(Canvas canvas, double x, double y, double w, double h, double r, Color color, double opacity)
{
    var rect = new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = new SolidColorBrush(color), Opacity = opacity };
    Canvas.SetLeft(rect, x);
    Canvas.SetTop(rect, y);
    canvas.Children.Add(rect);
}

static void WriteIco(string path, byte[][] pngImages)
{
    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var bw = new BinaryWriter(fs);

    int count = pngImages.Length;

    // ICO header
    bw.Write((short)0);       // reserved
    bw.Write((short)1);       // type = ICO
    bw.Write((short)count);   // image count

    int dataOffset = 6 + count * 16;

    // Decode each PNG to get dimensions
    var sizes = new int[count];
    for (int i = 0; i < count; i++)
    {
        using var ms = new MemoryStream(pngImages[i]);
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.None, BitmapCacheOption.Default);
        sizes[i] = decoder.Frames[0].PixelWidth;
    }

    // Write directory entries
    int currentOffset = dataOffset;
    for (int i = 0; i < count; i++)
    {
        byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
        bw.Write(dim);           // width
        bw.Write(dim);           // height
        bw.Write((byte)0);      // color palette
        bw.Write((byte)0);      // reserved
        bw.Write((short)1);     // color planes
        bw.Write((short)32);    // bits per pixel
        bw.Write(pngImages[i].Length);
        bw.Write(currentOffset);
        currentOffset += pngImages[i].Length;
    }

    // Write image data
    for (int i = 0; i < count; i++)
        bw.Write(pngImages[i]);
}
