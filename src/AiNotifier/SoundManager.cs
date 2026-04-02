using System.IO;
using System.Windows;
using System.Windows.Media;

namespace AiNotifier;

public class SoundManager : IDisposable
{
    public record SoundInfo(string Id, string DisplayName);

    public static readonly SoundInfo[] BuiltInSounds =
    [
        new("alert-1", "提示音 1"),
        new("alert-2", "提示音 2"),
        new("alert-3", "提示音 3"),
        new("alert-4", "提示音 4"),
    ];

    // All sounds to extract: (id, extension)
    private static readonly (string Id, string Ext)[] AllExtractSounds =
    [
        ("alert-1", ".wav"), ("alert-2", ".wav"), ("alert-3", ".wav"), ("alert-4", ".wav"),
        ("bubble-pop", ".mp3"),
    ];

    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "AiNotifier_Sounds");

    private MediaPlayer _player = new();
    private MediaPlayer _bubblePlayer = new();
    private bool _bubblePendingPlay;
    private string? _bubblePath;
    private bool _looping;
    private bool _pendingPlay;
    private double _volume = 0.6;
    private string _currentSoundId = "alert-1";
    private string? _customSoundPath;

    public bool IsPlaying { get; private set; }
    public bool HasCompletedFirstPlay { get; private set; }
    public event Action? PlaybackStarted;
    public event Action? FirstPlayCompleted;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            _player.Volume = _volume;
        }
    }

    public string CurrentSoundId => _currentSoundId;
    public string? CustomSoundPath => _customSoundPath;

    public SoundManager()
    {
        _player.Volume = _volume;
        _player.MediaOpened += (_, _) =>
        {
            if (_pendingPlay)
            {
                _pendingPlay = false;
                _player.Play();
                PlaybackStarted?.Invoke();
            }
        };
        _player.MediaEnded += (_, _) =>
        {
            if (!HasCompletedFirstPlay)
            {
                HasCompletedFirstPlay = true;
                FirstPlayCompleted?.Invoke();
            }
            if (_looping && IsPlaying)
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
            }
        };

        _bubblePlayer.MediaOpened += (_, _) =>
        {
            if (_bubblePendingPlay)
            {
                _bubblePendingPlay = false;
                _bubblePlayer.Play();
            }
        };

        // Extract built-in sounds to temp dir on startup
        ExtractBuiltInSounds();
    }

    private void ExtractBuiltInSounds()
    {
        try
        {
            Directory.CreateDirectory(TempDir);
            foreach (var (soundId, ext) in AllExtractSounds)
            {
                var tempPath = Path.Combine(TempDir, $"{soundId}{ext}");
                if (File.Exists(tempPath)) continue;

                var uri = new Uri($"pack://application:,,,/Resources/{soundId}{ext}", UriKind.Absolute);
                var stream = Application.GetResourceStream(uri)?.Stream;
                if (stream != null)
                {
                    using var fs = File.Create(tempPath);
                    stream.CopyTo(fs);
                    stream.Dispose();
                }
            }
        }
        catch { /* ignore extraction errors */ }
    }

    public void SetSound(string soundId, string? customPath = null)
    {
        _currentSoundId = soundId;
        _customSoundPath = soundId == "custom" ? customPath : null;
    }

    private string? GetSoundFilePath()
    {
        if (_currentSoundId == "custom" && _customSoundPath != null)
        {
            return File.Exists(_customSoundPath) ? _customSoundPath : null;
        }

        var path = Path.Combine(TempDir, $"{_currentSoundId}.wav");
        return File.Exists(path) ? path : null;
    }

    private void OpenAndPlay(string path, bool looping)
    {
        try
        {
            _looping = looping;
            _pendingPlay = true;
            IsPlaying = true;
            HasCompletedFirstPlay = false;
            // Always Close before Open to guarantee MediaOpened fires
            // (MediaPlayer.Open with the same URI won't re-fire MediaOpened)
            _player.Close();
            _player.Open(new Uri(path, UriKind.Absolute));
            _player.Volume = _volume;
        }
        catch
        {
            _pendingPlay = false;
            IsPlaying = false;
        }
    }

    /// <summary>
    /// Preview a specific sound by ID (without changing the selected sound).
    /// </summary>
    public void Preview(string soundId, string? customPath = null)
    {
        string? path;
        if (soundId == "custom" && customPath != null)
            path = File.Exists(customPath) ? customPath : null;
        else
            path = Path.Combine(TempDir, $"{soundId}.wav");

        if (path == null || !File.Exists(path)) return;

        OpenAndPlay(path, false);
    }

    public void PlayLooping()
    {
        var path = GetSoundFilePath();
        if (path == null) return;
        OpenAndPlay(path, true);
    }

    public void PlayOnce()
    {
        var path = GetSoundFilePath();
        if (path == null) return;
        OpenAndPlay(path, false);
    }

    /// <summary>
    /// Play the bubble sound effect once using a dedicated player.
    /// </summary>
    public void PlayBubble(double volume)
    {
        var path = Path.Combine(TempDir, "bubble-pop.mp3");
        if (!File.Exists(path)) return;

        _bubblePlayer.Volume = volume;

        // If same file already loaded, just seek to start and play
        if (_bubblePath == path)
        {
            _bubblePlayer.Stop();
            _bubblePlayer.Position = TimeSpan.Zero;
            _bubblePlayer.Play();
        }
        else
        {
            // First time or path changed — open async
            _bubblePath = path;
            _bubblePendingPlay = true;
            _bubblePlayer.Open(new Uri(path, UriKind.Absolute));
        }
    }

    public void Stop()
    {
        _looping = false;
        _player.Stop();
        IsPlaying = false;
    }

    public void Dispose()
    {
        Stop();
        _player.Close();
        _bubblePlayer.Close();
    }
}
