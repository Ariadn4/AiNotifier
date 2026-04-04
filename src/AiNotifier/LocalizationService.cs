using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace AiNotifier;

public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private readonly ResourceManager _resourceManager =
        new("AiNotifier.Resources.Strings", typeof(LocalizationService).Assembly);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        return _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    public string Get(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    public void SwitchLanguage(string cultureCode)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(cultureCode);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public bool IsChinese => CultureInfo.CurrentUICulture.Name.StartsWith("zh");
}
