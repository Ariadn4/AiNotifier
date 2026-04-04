using System.Windows;

namespace AiNotifier;

public partial class MessageEditorWindow : Window
{
    private static LocalizationService L => LocalizationService.Instance;
    private readonly string[] _defaultMessages;
    public List<string>? ResultMessages { get; private set; }

    public MessageEditorWindow(List<string> messages, string[] defaultMessages)
    {
        _defaultMessages = defaultMessages;
        InitializeComponent();

        // Apply localized text
        Title = L.Get("MessageEditor_Title");
        InstructionLabel.Text = L.Get("MessageEditor_Instruction");
        ResetBtn.Content = L.Get("Dialog_Reset");
        CancelBtn.Content = L.Get("Dialog_Cancel");
        OKBtn.Content = L.Get("Dialog_OK");

        MessagesBox.Text = string.Join(Environment.NewLine, messages);
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        var lines = MessagesBox.Text
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        ResultMessages = lines.Count > 0 ? lines : null;
        DialogResult = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        MessagesBox.Text = string.Join(Environment.NewLine, _defaultMessages);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
