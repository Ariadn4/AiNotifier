using System.IO;

var outDir = args.Length > 0 ? args[0] : @"..\..\src\AiNotifier\Resources";
outDir = Path.GetFullPath(outDir);
Directory.CreateDirectory(outDir);

// 1. 柔和铃声 (Gentle Chime) - two soft ascending notes
GenerateChime(Path.Combine(outDir, "gentle-chime.wav"));

// 2. 气泡提示 (Bubble Pop) - quick bright ascending tones
GenerateBubble(Path.Combine(outDir, "bubble.wav"));

// 3. 清脆叮咚 (Crystal Ding) - single clean bell tone
GenerateDing(Path.Combine(outDir, "crystal-ding.wav"));

Console.WriteLine("All sounds generated!");

static void GenerateChime(string path)
{
    int sampleRate = 44100;
    double duration = 1.2;
    int samples = (int)(sampleRate * duration);
    var data = new short[samples];

    // Note 1: C5 (523 Hz) at t=0
    // Note 2: E5 (659 Hz) at t=0.25s
    for (int i = 0; i < samples; i++)
    {
        double t = (double)i / sampleRate;
        double sample = 0;

        // Note 1: C5 with soft attack and long decay
        if (t < 1.0)
        {
            double env1 = Math.Min(t / 0.01, 1.0) * Math.Exp(-t * 3.5);
            sample += Math.Sin(2 * Math.PI * 523.25 * t) * env1 * 0.35;
            // Add soft harmonic
            sample += Math.Sin(2 * Math.PI * 523.25 * 2 * t) * env1 * 0.1;
        }

        // Note 2: E5 delayed 0.3s
        if (t > 0.3 && t < 1.2)
        {
            double t2 = t - 0.3;
            double env2 = Math.Min(t2 / 0.01, 1.0) * Math.Exp(-t2 * 3.0);
            sample += Math.Sin(2 * Math.PI * 659.25 * t) * env2 * 0.35;
            sample += Math.Sin(2 * Math.PI * 659.25 * 2 * t) * env2 * 0.08;
        }

        data[i] = (short)(sample * 20000);
    }

    WriteWav(path, sampleRate, data);
    Console.WriteLine($"Generated: {path}");
}

static void GenerateBubble(string path)
{
    int sampleRate = 44100;
    double duration = 0.8;
    int samples = (int)(sampleRate * duration);
    var data = new short[samples];

    // Two "bloop" bubbles: quick upward frequency sweeps
    double[] starts = [0.0, 0.25];
    double[] baseFreqs = [300, 420];

    for (int i = 0; i < samples; i++)
    {
        double t = (double)i / sampleRate;
        double sample = 0;

        for (int n = 0; n < 2; n++)
        {
            if (t >= starts[n] && t < starts[n] + 0.35)
            {
                double tn = t - starts[n];
                // Envelope: quick pop then fade
                double env = Math.Min(tn / 0.003, 1.0) * Math.Exp(-tn * 8.0);
                // Frequency sweeps upward quickly (bubble rising)
                double freq = baseFreqs[n] + 600 * (1 - Math.Exp(-tn * 20));
                // Phase integration for smooth sweep
                double phase = 2 * Math.PI * (baseFreqs[n] * tn + 600 * (tn + Math.Exp(-tn * 20) / 20 - 1.0 / 20));
                sample += Math.Sin(phase) * env * 0.4;
                // Subtle second harmonic for roundness
                sample += Math.Sin(phase * 1.5) * env * 0.1;
            }
        }

        data[i] = (short)(sample * 18000);
    }

    WriteWav(path, sampleRate, data);
    Console.WriteLine($"Generated: {path}");
}

static void GenerateDing(string path)
{
    int sampleRate = 44100;
    double duration = 1.5;
    int samples = (int)(sampleRate * duration);
    var data = new short[samples];

    // Single clean bell: A5 (880 Hz) with harmonics for metallic quality
    for (int i = 0; i < samples; i++)
    {
        double t = (double)i / sampleRate;
        double env = Math.Min(t / 0.003, 1.0) * Math.Exp(-t * 2.5);

        double sample = 0;
        sample += Math.Sin(2 * Math.PI * 880 * t) * 0.4;           // fundamental
        sample += Math.Sin(2 * Math.PI * 880 * 2.0 * t) * 0.15;   // 2nd harmonic
        sample += Math.Sin(2 * Math.PI * 880 * 3.0 * t) * 0.08;   // 3rd harmonic
        sample += Math.Sin(2 * Math.PI * 880 * 5.2 * t) * 0.04;   // inharmonic (bell-like)

        data[i] = (short)(sample * env * 20000);
    }

    WriteWav(path, sampleRate, data);
    Console.WriteLine($"Generated: {path}");
}

static void WriteWav(string path, int sampleRate, short[] data)
{
    using var fs = new FileStream(path, FileMode.Create);
    using var bw = new BinaryWriter(fs);

    int byteRate = sampleRate * 2; // 16-bit mono
    int dataSize = data.Length * 2;

    // RIFF header
    bw.Write("RIFF"u8);
    bw.Write(36 + dataSize);
    bw.Write("WAVE"u8);

    // fmt chunk
    bw.Write("fmt "u8);
    bw.Write(16);           // chunk size
    bw.Write((short)1);     // PCM
    bw.Write((short)1);     // mono
    bw.Write(sampleRate);
    bw.Write(byteRate);
    bw.Write((short)2);     // block align
    bw.Write((short)16);    // bits per sample

    // data chunk
    bw.Write("data"u8);
    bw.Write(dataSize);
    foreach (var s in data)
        bw.Write(s);
}
