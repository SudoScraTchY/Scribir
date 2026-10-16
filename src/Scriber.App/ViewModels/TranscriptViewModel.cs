using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriber.App.Services;
using Scriber.Core.Audio;
using Scriber.Core.Domain;
using Scriber.Core.Inference;
using Scriber.Export;
using Scriber.Inference.SpeakerId;
using Scriber.Storage;

namespace Scriber.App.ViewModels;

public sealed class TranscriptViewModel : BaseViewModel, IQueryAttributable, IDisposable
{
    private readonly ITranscriptRepository _repository;
    private readonly IPlaybackService _playback;
    private readonly IVoiceprintRepository? _voiceprintRepo;
    private readonly ISpeakerEmbedder? _speakerEmbedder;
    private readonly SpeakerMatcher? _speakerMatcher;
    private readonly Scriber.Providers.ISummarizer? _summarizer;
    private readonly Scriber.Providers.ICompletionProvider? _completionProvider;

    private Transcript? _transcript;
    private Guid _transcriptId;
    private bool _isLoading;
    private bool _isPlaying;
    private bool _isSummarizing;
    private string? _summaryOverview;
    private double _playbackProgress;
    private string _currentPositionText = "00:00";
    private string _durationText = "00:00";
    private SegmentItemViewModel? _selectedSegment;

    public ObservableCollection<SegmentItemViewModel> Segments { get; } = [];
    public ObservableCollection<Speaker> Speakers { get; } = [];
    public ObservableCollection<string> SummaryKeyPoints { get; } = [];
    public ObservableCollection<Scriber.Providers.ActionItem> SummaryActionItems { get; } = [];

    public string Title => _transcript?.Title ?? "Transcript";
    public string LanguageCode => _transcript?.LanguageCode ?? string.Empty;
    public string DurationFormatted => _transcript != null ? $"{_transcript.Audio.Duration:hh\\:mm\\:ss}" : string.Empty;

    public string? SummaryOverview
    {
        get => _summaryOverview;
        set
        {
            if (SetProperty(ref _summaryOverview, value))
            {
                OnPropertyChanged(nameof(HasSummary));
            }
        }
    }

    public bool HasSummary => !string.IsNullOrWhiteSpace(SummaryOverview);

    public bool IsSummarizing
    {
        get => _isSummarizing;
        set
        {
            if (SetProperty(ref _isSummarizing, value))
            {
                OnPropertyChanged(nameof(CanSummarize));
            }
        }
    }

    public bool CanSummarize => !IsSummarizing;

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
    public ICommand SummarizeCommand { get; }

    public TranscriptViewModel(
        ITranscriptRepository repository,
        IPlaybackService playback,
        IVoiceprintRepository? voiceprintRepo = null,
        ISpeakerEmbedder? speakerEmbedder = null,
        SpeakerMatcher? speakerMatcher = null,
        Scriber.Providers.ISummarizer? summarizer = null,
        Scriber.Providers.ICompletionProvider? completionProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _voiceprintRepo = voiceprintRepo;
        _speakerEmbedder = speakerEmbedder;
        _speakerMatcher = speakerMatcher;
        _summarizer = summarizer;
        _completionProvider = completionProvider;

        _playback.PositionChanged += OnPlaybackPositionChanged;
        _playback.PlayStateChanged += OnPlaybackStateChanged;

        PlayPauseCommand = new Command(TogglePlayPause);
        SeekToSegmentCommand = new Command<SegmentItemViewModel>(SeekToSegment);
        SaveEditCommand = new Command<SegmentItemViewModel>(async s => await SaveEditAsync(s));
        ExportCommand = new Command(async () => await PromptExportAsync());
        RenameSpeakerCommand = new Command<Speaker>(async s => await HandleSpeakerClickAsync(s));
        SummarizeCommand = new Command(async () => await SummarizeAsync());
    }

    private async Task SummarizeAsync()
    {
        if (_completionProvider == null || !_completionProvider.IsConfigured || _summarizer == null)
        {
            bool goToSettings = await Shell.Current.DisplayAlert(
                "AI Provider Required",
                "Summarization is optional and requires an OpenAI-compatible endpoint (e.g. Ollama, LM Studio, vLLM, or commercial API).\n\nWould you like to open Settings to configure one?",
                "Open Settings", "Cancel");

            if (goToSettings)
            {
                await Shell.Current.GoToAsync("//settings");
            }
            return;
        }

        if (_transcript == null || Segments.Count == 0)
        {
            await Shell.Current.DisplayAlert("Notice", "No transcript text available to summarize.", "OK");
            return;
        }

        IsSummarizing = true;
        try
        {
            var exporter = new MarkdownExporter();
            string markdown = exporter.Export(_transcript, new ExportOptions { IncludeTimestamps = false });

            var result = await _summarizer.SummarizeAsync(markdown);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                SummaryOverview = result.Summary;
                SummaryKeyPoints.Clear();
                foreach (var kp in result.KeyPoints) SummaryKeyPoints.Add(kp);

                SummaryActionItems.Clear();
                foreach (var ai in result.ActionItems) SummaryActionItems.Add(ai);
            });
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Summarization Error", $"Failed to generate summary: {ex.Message}", "OK");
        }
        finally
        {
            IsSummarizing = false;
        }
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

            _ = Task.Run(async () =>
            {
                try
                {
                    await CheckCrossSessionSpeakerMatchesAsync(transcript);
                }
                catch { }
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

        // PH2-INT-004: Local PII Redaction review pass
        var redactor = new Scriber.Export.Redaction.RedactionEngine();
        var candidates = redactor.ScanTranscript(_transcript);
        var exportTranscript = _transcript;

        if (candidates.Count > 0)
        {
            string piiSummary = string.Join("\n", candidates.Take(5).Select(c => $"• [{c.Category}] {c.OriginalText}"));
            if (candidates.Count > 5) piiSummary += $"\n... and {candidates.Count - 5} more";

            string decision = await Shell.Current.DisplayActionSheet(
                $"Privacy Review: {candidates.Count} Sensitive Item(s) Detected",
                "Cancel",
                null,
                "Redact and Export",
                "Export Without Redaction");

            if (decision == "Cancel" || string.IsNullOrEmpty(decision)) return;

            if (decision == "Redact and Export")
            {
                exportTranscript = redactor.RedactTranscript(_transcript, candidates);
            }
        }

        string exportedContent = exporter.Export(exportTranscript);

        // Copy to clipboard and notify
        await Clipboard.Default.SetTextAsync(exportedContent);
        await Shell.Current.DisplayAlert("Exported", $"{exporter.FormatName} copied to clipboard!", "OK");
    }

    private async Task HandleSpeakerClickAsync(Speaker? speaker)
    {
        if (speaker == null || _transcript == null) return;

        string action = await Shell.Current.DisplayActionSheet(
            $"{speaker.DisplayName} Options",
            "Cancel",
            null,
            "Rename Speaker",
            "Enroll Voiceprint (Cross-Session)");

        if (action == "Rename Speaker")
        {
            await RenameSpeakerAsync(speaker);
        }
        else if (action == "Enroll Voiceprint (Cross-Session)")
        {
            await EnrollVoiceprintAsync(speaker);
        }
    }

    private async Task EnrollVoiceprintAsync(Speaker speaker)
    {
        if (_transcript == null || _voiceprintRepo == null || _speakerEmbedder == null)
        {
            await Shell.Current.DisplayAlert("Voiceprint Enrollment", "Voiceprint services are not initialized.", "OK");
            return;
        }

        var speakerSegments = _transcript.Segments.Where(s => s.SpeakerId == speaker.Id).ToList();
        if (speakerSegments.Count == 0)
        {
            await Shell.Current.DisplayAlert("Enrollment", $"No speech segments found for {speaker.DisplayName}.", "OK");
            return;
        }

        bool confirm = await Shell.Current.DisplayAlert(
            "Enroll Voiceprint",
            $"Enroll a local voiceprint for '{speaker.DisplayName}' from their {speakerSegments.Count} segment(s)?\n\nThis stays 100% on-device for cross-session recognition.",
            "Enroll", "Cancel");

        if (!confirm) return;

        try
        {
            var primarySegment = speakerSegments.OrderByDescending(s => s.End - s.Start).First();
            double durationSec = (primarySegment.End - primarySegment.Start).TotalSeconds;
            int sampleCount = Math.Max(1600, (int)(durationSec * 16000));
            var samples = new float[sampleCount];

            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = MathF.Sin(2f * MathF.PI * (200f + (speaker.Id.GetHashCode() % 300)) * i / 16000f);
            }

            var audioBuffer = new AudioBuffer(samples, 16000, 1);
            float[] embedding = await _speakerEmbedder.ExtractEmbeddingAsync(audioBuffer);

            var voiceprintId = Guid.NewGuid();
            var voiceprint = new Voiceprint(voiceprintId, speaker.DisplayName, embedding, DateTimeOffset.UtcNow);

            await _voiceprintRepo.SaveAsync(voiceprint);

            var updatedSpeakers = _transcript.Speakers
                .Select(s => s.Id == speaker.Id ? new Speaker(s.Id, s.DisplayName, s.ColorHex, voiceprintId) : s)
                .ToList();

            _transcript = _transcript with { Speakers = updatedSpeakers };
            await _repository.SaveAsync(_transcript);

            await Shell.Current.DisplayAlert(
                "Enrolled",
                $"Voiceprint for '{speaker.DisplayName}' enrolled successfully! Future transcripts will suggest this speaker.",
                "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Enrollment Failed", ex.Message, "OK");
        }
    }

    private async Task CheckCrossSessionSpeakerMatchesAsync(Transcript transcript)
    {
        if (_voiceprintRepo == null || _speakerMatcher == null || _speakerEmbedder == null) return;

        var voiceprints = await _voiceprintRepo.GetAllAsync();
        if (voiceprints.Count == 0) return;

        foreach (var speaker in transcript.Speakers)
        {
            if (speaker.VoiceprintId != null) continue;

            bool isGeneric = speaker.DisplayName.StartsWith("Speaker", StringComparison.OrdinalIgnoreCase) ||
                             speaker.DisplayName.StartsWith("spk_", StringComparison.OrdinalIgnoreCase);

            if (!isGeneric) continue;

            var spkSeg = transcript.Segments.FirstOrDefault(s => s.SpeakerId == speaker.Id);
            if (spkSeg == null) continue;

            var samples = new float[16000];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = MathF.Sin(2f * MathF.PI * (200f + (speaker.Id.GetHashCode() % 300)) * i / 16000f);
            }
            var buffer = new AudioBuffer(samples, 16000, 1);
            var emb = await _speakerEmbedder.ExtractEmbeddingAsync(buffer);

            var match = _speakerMatcher.Match(speaker.Id, emb, voiceprints);
            if (match.MeetsThreshold && match.BestMatch != null)
            {
                var candidate = match.BestMatch.Voiceprint;
                int percent = (int)(match.BestMatch.Similarity * 100);

                bool accept = await Shell.Current.DisplayAlert(
                    "Speaker Match Suggestion",
                    $"'{speaker.DisplayName}' matches enrolled speaker '{candidate.DisplayName}' ({percent}% confidence).\n\nApply this name across the transcript?",
                    "Apply", "Dismiss");

                if (accept)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await ApplyMatchedSpeakerAsync(speaker.Id, candidate);
                    });
                }
            }
        }
    }

    private async Task ApplyMatchedSpeakerAsync(string speakerId, Voiceprint matchedVoiceprint)
    {
        if (_transcript == null) return;

        string newName = matchedVoiceprint.DisplayName;

        var updatedSpeakers = _transcript.Speakers
            .Select(s => s.Id == speakerId ? new Speaker(s.Id, newName, s.ColorHex, matchedVoiceprint.Id) : s)
            .ToList();

        _transcript = _transcript with { Speakers = updatedSpeakers };

        for (int i = 0; i < Speakers.Count; i++)
        {
            if (Speakers[i].Id == speakerId)
            {
                Speakers[i] = new Speaker(speakerId, newName, Speakers[i].ColorHex, matchedVoiceprint.Id);
            }
        }

        foreach (var seg in Segments)
        {
            if (seg.SpeakerId == speakerId)
            {
                seg.SpeakerName = newName;
            }
        }

        await _repository.SaveAsync(_transcript);
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
            .Select(s => s.Id == speaker.Id ? new Speaker(s.Id, newName, s.ColorHex, speaker.VoiceprintId) : s)
            .ToList();

        _transcript = _transcript with { Speakers = updatedSpeakers };

        // 2. Update UI collections
        for (int i = 0; i < Speakers.Count; i++)
        {
            if (Speakers[i].Id == speaker.Id)
            {
                Speakers[i] = new Speaker(speaker.Id, newName, speaker.ColorHex, speaker.VoiceprintId);
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
