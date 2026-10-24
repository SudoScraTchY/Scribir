using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriber.Storage;

namespace Scriber.App.ViewModels;

public sealed class LibraryViewModel : BaseViewModel
{
    private readonly ITranscriptRepository _repository;

    private string _searchQuery = string.Empty;
    private bool _isSearching;
    private bool _isLoading;

    public ObservableCollection<TranscriptSummary> Transcripts { get; } = [];
    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    IsSearching = false;
                    SearchResults.Clear();
                }
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        set => SetProperty(ref _isSearching, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public ICommand LoadCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand OpenTranscriptCommand { get; }
    public ICommand DeleteTranscriptCommand { get; }

    public LibraryViewModel(ITranscriptRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        LoadCommand = new Command(async () => await LoadTranscriptsAsync());
        SearchCommand = new Command(async () => await PerformSearchAsync());
        OpenTranscriptCommand = new Command<Guid>(async id => await OpenTranscriptAsync(id));
        DeleteTranscriptCommand = new Command<Guid>(async id => await DeleteTranscriptAsync(id));
    }

    public async Task LoadTranscriptsAsync()
    {
        IsLoading = true;
        try
        {
            var items = await _repository.ListAsync();
            Transcripts.Clear();
            foreach (var item in items)
            {
                Transcripts.Add(item);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task PerformSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            IsSearching = false;
            SearchResults.Clear();
            return;
        }

        IsLoading = true;
        IsSearching = true;
        try
        {
            var results = await _repository.SearchAsync(SearchQuery);
            SearchResults.Clear();
            foreach (var r in results)
            {
                SearchResults.Add(r);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static async Task OpenTranscriptAsync(Guid id)
    {
        await Shell.Current.GoToAsync($"transcript?id={id}");
    }

    private async Task DeleteTranscriptAsync(Guid id)
    {
        bool confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Transcript",
            "Are you sure you want to delete this transcript? This action cannot be undone.",
            "Delete",
            "Cancel");

        if (confirm)
        {
            await _repository.DeleteAsync(id);
            await LoadTranscriptsAsync();
        }
    }
}
