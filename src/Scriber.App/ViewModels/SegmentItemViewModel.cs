using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriber.Core.Domain;

namespace Scriber.App.ViewModels;

/// <summary>
/// Presentation wrapper for a single transcript segment with confidence-aware review support.
/// Implements PH1-UI-003 and PH1-UI-007.
/// </summary>
public sealed class SegmentItemViewModel : BaseViewModel
{
    private string _text;
    private string _editText;
    private bool _isPlaying;
    private bool _isEditing;
    private double _confidenceThreshold;

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

    public ObservableCollection<WordItemViewModel> WordItems { get; } = [];

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
        set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(ShowRichWordsView));
                OnPropertyChanged(nameof(ShowStandardTextView));
            }
        }
    }

    public bool HasLowConfidenceWords => WordItems.Any(w => w.IsLowConfidence);

    public int LowConfidenceCount => WordItems.Count(w => w.IsLowConfidence);

    public string LowConfidenceBadgeText => HasLowConfidenceWords
        ? $"{LowConfidenceCount} word{(LowConfidenceCount == 1 ? "" : "s")} to review"
        : string.Empty;

    public bool ShowRichWordsView => !IsEditing && HasLowConfidenceWords && WordItems.Count > 0;

    public bool ShowStandardTextView => !IsEditing && !ShowRichWordsView;

    public ICommand ToggleEditCommand { get; }
    public ICommand StartEditWordCommand { get; }

    public SegmentItemViewModel(Segment segment, string speakerName, string colorHex, double confidenceThreshold = 0.80)
    {
        SpeakerId = segment.SpeakerId;
        _speakerName = speakerName;
        SpeakerColorHex = colorHex;
        Start = segment.Start;
        End = segment.End;
        Words = segment.Words;
        _confidenceThreshold = confidenceThreshold;

        _text = string.Join(" ", segment.Words.Select(w => w.Text)).Trim();
        if (string.IsNullOrWhiteSpace(_text) && !string.IsNullOrWhiteSpace(segment.Text))
        {
            _text = segment.Text;
        }
        _editText = _text;

        foreach (var word in segment.Words)
        {
            WordItems.Add(new WordItemViewModel(word, _confidenceThreshold));
        }

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

        StartEditWordCommand = new Command<WordItemViewModel>(word =>
        {
            EditText = Text;
            IsEditing = true;
        });
    }

    public void UpdateConfidenceThreshold(double threshold)
    {
        _confidenceThreshold = threshold;
        foreach (var word in WordItems)
        {
            word.UpdateThreshold(threshold);
        }
        OnPropertyChanged(nameof(HasLowConfidenceWords));
        OnPropertyChanged(nameof(LowConfidenceCount));
        OnPropertyChanged(nameof(LowConfidenceBadgeText));
        OnPropertyChanged(nameof(ShowRichWordsView));
        OnPropertyChanged(nameof(ShowStandardTextView));
    }

    public void CommitEdit()
    {
        Text = EditText.Trim();
        IsEditing = false;
    }
}
