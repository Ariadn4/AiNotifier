using System.Windows;

namespace AiNotifier;

public partial class VolumeWindow : Window
{
    private static LocalizationService L => LocalizationService.Instance;
    private readonly SoundManager _sound;
    private readonly double _originalVolume;

    public double ResultVolume { get; private set; } = 0.6;
    public bool ResultGradualVolume { get; private set; }

    public VolumeWindow(AppSettings settings, SoundManager sound)
    {
        _sound = sound;
        _originalVolume = sound.Volume;

        InitializeComponent();

        Title = L.Get("Menu_Volume");
        VolumeLabel2.Text = L.Get("Sound_Volume");
        GradualVolumeCheck.Content = L.Get("Sound_GradualVolume");
        CancelBtn.Content = L.Get("Dialog_Cancel");
        OKBtn.Content = L.Get("Dialog_OK");

        VolumeSlider.Value = settings.Volume;
        VolumeLabel.Text = $"{(int)(settings.Volume * 100)}%";
        GradualVolumeCheck.IsChecked = settings.GradualVolume;

        VolumeSlider.ValueChanged += (_, args) =>
        {
            VolumeLabel.Text = $"{(int)(args.NewValue * 100)}%";
            _sound.Volume = args.NewValue;
        };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        ResultVolume = VolumeSlider.Value;
        ResultGradualVolume = GradualVolumeCheck.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _sound.Volume = _originalVolume;
        DialogResult = false;
    }
}
