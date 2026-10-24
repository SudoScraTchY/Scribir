using Scriber.Core.Domain;

namespace Scriber.App.ViewModels;

/// <summary>
/// Presentation wrapper for a single recognized word with confidence-aware review support.
/// Implements PH1-UI-007.
/// </summary>
public sealed class WordItemViewModel : BaseViewModel
{
    private bool _isLowConfidence;

    public Word Word { get; }

    public string Text => Word.Text;
    public TimeSpan Start => Word.Start;
    public TimeSpan End => Word.End;
    public double Confidence => Word.Confidence;

    public bool IsLowConfidence
    {
        get => _isLowConfidence;
        private set
        {
            if (SetProperty(ref _isLowConfidence, value))
            {
                OnPropertyChanged(nameof(TextDecoration));
                OnPropertyChanged(nameof(ConfidenceTooltip));
            }
        }
    }

    public string ConfidencePercentText => $"{(int)(Confidence * 100)}%";

    public string ConfidenceTooltip => $"{(int)(Confidence * 100)}% confidence";

    public TextDecorations TextDecoration => IsLowConfidence ? TextDecorations.Underline : TextDecorations.None;

    public WordItemViewModel(Word word, double threshold = 0.80)
    {
        Word = word ?? throw new ArgumentNullException(nameof(word));
        UpdateThreshold(threshold);
    }

    public void UpdateThreshold(double threshold)
    {
        IsLowConfidence = Confidence < threshold;
    }
}
