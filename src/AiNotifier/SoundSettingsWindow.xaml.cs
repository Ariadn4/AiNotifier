using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace AiNotifier;

public partial class SoundSettingsWindow : Window
{
    private readonly SoundManager _sound;

    // Results
    public string ResultStopSoundId { get; private set; } = "alert-1";
    public string? ResultStopCustomPath { get; private set; }
    public string ResultNotificationSoundId { get; private set; } = "alert-3";
    public string? ResultNotificationCustomPath { get; private set; }
    public double ResultVolume { get; private set; } = 0.6;
    public bool ResultGradualVolume { get; private set; }

    // Current selections
    private string _stopSoundId;
    private string? _stopCustomPath;
    private string _notifSoundId;
    private string? _notifCustomPath;

    public SoundSettingsWindow(AppSettings settings, SoundManager sound)
    {
        _sound = sound;
        _stopSoundId = settings.StopSoundId;
        _stopCustomPath = settings.StopCustomSoundPath;
        _notifSoundId = settings.NotificationSoundId;
        _notifCustomPath = settings.NotificationCustomSoundPath;

        InitializeComponent();

        VolumeSlider.Value = settings.Volume;
        VolumeLabel.Text = $"{(int)(settings.Volume * 100)}%";
        GradualVolumeCheck.IsChecked = settings.GradualVolume;

        VolumeSlider.ValueChanged += (_, args) =>
        {
            VolumeLabel.Text = $"{(int)(args.NewValue * 100)}%";
            _sound.Volume = args.NewValue;
        };

        PopulateCombo(StopSoundCombo, _stopSoundId, _stopCustomPath);
        PopulateCombo(NotifSoundCombo, _notifSoundId, _notifCustomPath);

        StopSoundCombo.SelectionChanged += (_, _) => OnComboChanged(StopSoundCombo, isStop: true);
        NotifSoundCombo.SelectionChanged += (_, _) => OnComboChanged(NotifSoundCombo, isStop: false);
    }

    private void PopulateCombo(ComboBox combo, string currentSoundId, string? customPath)
    {
        combo.Items.Clear();

        foreach (var sound in SoundManager.BuiltInSounds)
        {
            combo.Items.Add(new SoundItem(sound.Id, sound.DisplayName));
        }

        if (customPath != null)
        {
            var customName = "♪ " + (Path.GetFileName(customPath) ?? "自定义");
            combo.Items.Add(new SoundItem("custom", customName, customPath));
        }

        // Select current
        for (int i = 0; i < combo.Items.Count; i++)
        {
            var item = (SoundItem)combo.Items[i];
            if (item.Id == currentSoundId)
            {
                combo.SelectedIndex = i;
                break;
            }
        }

        // Fallback: select first if nothing matched
        if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private void OnComboChanged(ComboBox combo, bool isStop)
    {
        if (combo.SelectedItem is not SoundItem item) return;

        if (isStop)
        {
            _stopSoundId = item.Id;
            _stopCustomPath = item.CustomPath;
        }
        else
        {
            _notifSoundId = item.Id;
            _notifCustomPath = item.CustomPath;
        }
    }

    private void PreviewCurrentSelection(ComboBox combo)
    {
        if (combo.SelectedItem is not SoundItem item) return;
        _sound.Preview(item.Id, item.CustomPath);
    }

    private void StopPreview_Click(object sender, RoutedEventArgs e) => PreviewCurrentSelection(StopSoundCombo);
    private void NotifPreview_Click(object sender, RoutedEventArgs e) => PreviewCurrentSelection(NotifSoundCombo);

    private void ChooseCustomSound(bool isStop)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择提示音效文件",
            Filter = "音频文件|*.wav;*.mp3;*.wma;*.aac|所有文件|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };
        if (dlg.ShowDialog() != true) return;

        var combo = isStop ? StopSoundCombo : NotifSoundCombo;

        if (isStop)
        {
            _stopSoundId = "custom";
            _stopCustomPath = dlg.FileName;
        }
        else
        {
            _notifSoundId = "custom";
            _notifCustomPath = dlg.FileName;
        }

        PopulateCombo(combo,
            isStop ? _stopSoundId : _notifSoundId,
            isStop ? _stopCustomPath : _notifCustomPath);

        _sound.Preview("custom", dlg.FileName);
    }

    private void StopCustom_Click(object sender, RoutedEventArgs e) => ChooseCustomSound(isStop: true);
    private void NotifCustom_Click(object sender, RoutedEventArgs e) => ChooseCustomSound(isStop: false);

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ResultStopSoundId = _stopSoundId;
        ResultStopCustomPath = _stopCustomPath;
        ResultNotificationSoundId = _notifSoundId;
        ResultNotificationCustomPath = _notifCustomPath;
        ResultVolume = VolumeSlider.Value;
        ResultGradualVolume = GradualVolumeCheck.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private record SoundItem(string Id, string DisplayName, string? CustomPath = null)
    {
        public override string ToString() => DisplayName;
    }
}
