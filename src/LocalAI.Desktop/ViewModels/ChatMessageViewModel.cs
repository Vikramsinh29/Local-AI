using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LocalAI.Desktop.ViewModels;

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _content;

    public ChatMessageViewModel(
        bool isUser,
        string content)
    {
        IsUser = isUser;
        _content = content;
        Timestamp = DateTime.Now.ToString("h:mm tt");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsUser { get; }

    public string Author => IsUser
        ? "You"
        : "Local AI";

    public string Timestamp { get; }

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value)
            {
                return;
            }

            _content = value;

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Content)));
        }
    }
}
