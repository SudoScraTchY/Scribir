using System.Windows.Input;
using Scriber.Core.Domain;

namespace Scriber.App.ViewModels;

public sealed class SegmentItemViewModel : BaseViewModel
{
    private string _text;
    private string _editText;
    private bool _isPlaying;
    private bool _isEditing;

    public string SpeakerId { get; }
    private string _speakerName;
    public string SpeakerName
    {
        get => _speakerName;
        set => SetProperty(ref _speakerName, value);
    }
    public string SpeakerColorHex { get; }
    public TimeSpan Start { get; }
    public TimeSpan End { get; }
    public string TimeString => $"{Start:mm\\:ss} - {End:mm\\:ss}";
    public IReadOnlyList<Word> Words { get; }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string EditText
    {
        get => _editText;
        set => SetProperty(ref _editText, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public ICommand ToggleEditCommand { get; }

    public SegmentItemViewModel(Segment segment, string speakerName, string colorHex)
    {
        SpeakerId = segment.SpeakerId;
        _speakerName = speakerName;
        SpeakerColorHex = colorHex;
        Start = segment.Start;
        End = segment.End;
        Words = segment.Words;

        _text = string.Join(" ", segment.Words.Select(w => w.Text)).Trim();
        _editText = _text;

        ToggleEditCommand = new Command(() =>
        {
            if (IsEditing)
            {
                // Cancel
                EditText = Text;
                IsEditing = false;
            }
            else
            {
                EditText = Text;
                IsEditing = true;
            }
        });
    }

    public void CommitEdit()
    {
        Text = EditText.Trim();
        IsEditing = false;
    }
}
