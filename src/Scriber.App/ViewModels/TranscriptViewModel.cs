using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriber.App.Services;
using Scriber.Core.Domain;
using Scriber.Export;
using Scriber.Storage;

namespace Scriber.App.ViewModels;

public sealed class TranscriptViewModel : BaseViewModel, IQueryAttributable, IDisposable
{
    private readonly ITranscriptRepository _repository;
    private readonly IPlaybackService _playback;

    private Transcript? _transcript;
    private Guid _transcriptId;
    private bool _isLoading;
    private bool _isPlaying;
    private double _playbackProgress;
    private string _currentPositionText = "00:00";
    private string _durationText = "00:00";
    private SegmentItemViewModel? _selectedSegment;

    public ObservableCollection<SegmentItemViewModel> Segments { get; } = [];
    public ObservableCollection<Speaker> Speakers { get; } = [];

    public string Title => _transcript?.Title ?? "Transcript";
    public string LanguageCode => _transcript?.LanguageCode ?? string.Empty;
    public string DurationFormatted => _transcript != null ? $"{_transcript.Audio.Duration:hh\\:mm\\:ss}" : string.Empty;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetProperty(ref _isPlaying, value);
    }

    public double PlaybackProgress
    {
        get => _playbackProgress;
        set
        {
            if (SetProperty(ref _playbackProgress, value))
            {
                // Scrubber dragged
            }
        }
    }

    public string CurrentPositionText
    {
        get => _currentPositionText;
        set => SetProperty(ref _currentPositionText, value);
    }

    public string DurationText
    {
        get => _durationText;
        set => SetProperty(ref _durationText, value);
    }

    public SegmentItemViewModel? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            if (SetProperty(ref _selectedSegment, value) && value != null)
            {
                SeekToSegment(value);
            }
        }
    }

    public ICommand PlayPauseCommand { get; }
    public ICommand SeekToSegmentCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand RenameSpeakerCommand { get; }

    public TranscriptViewModel(ITranscriptRepository repository, IPlaybackService playback)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));

        _playback.PositionChanged += OnPlaybackPositionChanged;
        _playback.PlayStateChanged += OnPlaybackStateChanged;

        PlayPauseCommand = new Command(TogglePlayPause);
        SeekToSegmentCommand = new Command<SegmentItemViewModel>(SeekToSegment);
        SaveEditCommand = new Command<SegmentItemViewModel>(async s => await SaveEditAsync(s));
        ExportCommand = new Command(async () => await PromptExportAsync());
        RenameSpeakerCommand = new Command<Speaker>(async s => await RenameSpeakerAsync(s));
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var idObj) && idObj != null)
        {
            if (idObj is Guid g)
            {
                _transcriptId = g;
            }
            else if (Guid.TryParse(idObj.ToString(), out var parsed))
            {
                _transcriptId = parsed;
            }

            Task.Run(LoadTranscriptAsync);
        }
    }

    public async Task LoadTranscriptAsync()
    {
        if (_transcriptId == Guid.Empty) return;

        IsLoading = true;
        try
        {
            var transcript = await _repository.GetByIdAsync(_transcriptId);
            if (transcript == null) return;

            _transcript = transcript;
            _playback.Load(transcript.Audio.LocalPath, transcript.Audio.Duration);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(LanguageCode));
                OnPropertyChanged(nameof(DurationFormatted));
                DurationText = $"{transcript.Audio.Duration:mm\\:ss}";

                Speakers.Clear();
                foreach (var spk in transcript.Speakers)
                {
                    Speakers.Add(spk);
                }

                var speakerMap = transcript.Speakers.ToDictionary(s => s.Id, s => s);

                Segments.Clear();
                foreach (var seg in transcript.Segments)
                {
                    speakerMap.TryGetValue(seg.SpeakerId, out var spk);
                    string name = spk?.DisplayName ?? seg.SpeakerId;
                    string color = spk?.ColorHex ?? "#2563EB";
                    Segments.Add(new SegmentItemViewModel(seg, name, color));
                }
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void TogglePlayPause()
    {
        if (_playback.IsPlaying)
        {
            _playback.Pause();
        }
        else
        {
            _playback.Play();
        }
    }

    private void SeekToSegment(SegmentItemViewModel? segment)
    {
        if (segment == null) return;

        _playback.Seek(segment.Start);
        _playback.Play();
    }

    public void SeekToFraction(double fraction)
    {
        if (_transcript == null) return;
        var targetTime = TimeSpan.FromSeconds(_transcript.Audio.Duration.TotalSeconds * fraction);
        _playback.Seek(targetTime);
    }

    private async Task SaveEditAsync(SegmentItemViewModel? item)
    {
        if (item == null || _transcript == null) return;

        item.CommitEdit();

        // Reconstruct updated Segment with new words
        var updatedSegments = new List<Segment>(_transcript.Segments.Count);
        for (int i = 0; i < Segments.Count; i++)
        {
            var segItem = Segments[i];
            var origSeg = _transcript.Segments[i];

            if (segItem == item)
            {
                var newWords = segItem.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var words = new List<Word>(newWords.Length);
                double perWord = (segItem.End - segItem.Start).TotalSeconds / Math.Max(1, newWords.Length);

                for (int w = 0; w < newWords.Length; w++)
                {
                    var wStart = segItem.Start + TimeSpan.FromSeconds(w * perWord);
                    var wEnd = segItem.Start + TimeSpan.FromSeconds((w + 1) * perWord);
                    if (wEnd > segItem.End) wEnd = segItem.End;
                    if (wEnd <= wStart) wEnd = wStart + TimeSpan.FromMilliseconds(50);
                    words.Add(new Word(newWords[w], wStart, wEnd, 0.95));
                }

                updatedSegments.Add(new Segment(segItem.SpeakerId, segItem.Start, segItem.End, words));
            }
            else
            {
                updatedSegments.Add(origSeg);
            }
        }

        _transcript = _transcript with { Segments = updatedSegments };
        await _repository.SaveAsync(_transcript);
    }

    private async Task PromptExportAsync()
    {
        if (_transcript == null) return;

        string action = await Shell.Current.DisplayActionSheet(
            "Export Transcript",
            "Cancel",
            null,
            "Plain Text (.txt)",
            "SubRip Subtitles (.srt)",
            "WebVTT Subtitles (.vtt)",
            "JSON Format (.json)",
            "Markdown Notes (.md)");

        if (action == "Cancel" || string.IsNullOrEmpty(action)) return;

        ITranscriptExporter exporter = action switch
        {
            "Plain Text (.txt)" => new TxtExporter(),
            "SubRip Subtitles (.srt)" => new SrtExporter(),
            "WebVTT Subtitles (.vtt)" => new VttExporter(),
            "JSON Format (.json)" => new JsonExporter(),
            "Markdown Notes (.md)" => new MarkdownExporter(),
            _ => new TxtExporter()
        };

        string exportedContent = exporter.Export(_transcript);

        // Copy to clipboard and notify
        await Clipboard.Default.SetTextAsync(exportedContent);
        await Shell.Current.DisplayAlert("Exported", $"{exporter.FormatName} copied to clipboard!", "OK");
    }

    private async Task RenameSpeakerAsync(Speaker? speaker)
    {
        if (speaker == null || _transcript == null) return;

        string currentName = speaker.DisplayName;
        string result = await Shell.Current.DisplayPromptAsync(
            "Rename Speaker",
            $"Enter new name for {currentName}:",
            initialValue: currentName);

        if (string.IsNullOrWhiteSpace(result) || result == currentName) return;

        string newName = result.Trim();

        // 1. Update domain transcript speakers
        var updatedSpeakers = _transcript.Speakers
            .Select(s => s.Id == speaker.Id ? new Speaker(s.Id, newName, s.ColorHex) : s)
            .ToList();

        _transcript = _transcript with { Speakers = updatedSpeakers };

        // 2. Update UI collections
        for (int i = 0; i < Speakers.Count; i++)
        {
            if (Speakers[i].Id == speaker.Id)
            {
                Speakers[i] = new Speaker(speaker.Id, newName, speaker.ColorHex);
            }
        }

        foreach (var seg in Segments)
        {
            if (seg.SpeakerId == speaker.Id)
            {
                seg.SpeakerName = newName;
            }
        }

        // 3. Persist updated transcript
        await _repository.SaveAsync(_transcript);
    }

    private void OnPlaybackPositionChanged(object? sender, TimeSpan pos)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentPositionText = $"{pos:mm\\:ss}";
            if (_transcript != null && _transcript.Audio.Duration.TotalSeconds > 0)
            {
                _playbackProgress = Math.Clamp(pos.TotalSeconds / _transcript.Audio.Duration.TotalSeconds, 0.0, 1.0);
                OnPropertyChanged(nameof(PlaybackProgress));
            }

            // Highlight currently playing segment
            foreach (var seg in Segments)
            {
                seg.IsPlaying = pos >= seg.Start && pos < seg.End;
            }
        });
    }

    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsPlaying = isPlaying;
        });
    }

    public void Dispose()
    {
        _playback.PositionChanged -= OnPlaybackPositionChanged;
        _playback.PlayStateChanged -= OnPlaybackStateChanged;
        _playback.Stop();
    }
}
