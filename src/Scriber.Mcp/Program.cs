namespace Scriber.Mcp;

using Scriber.Storage;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? dbPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--db" || args[i] == "-d") && i + 1 < args.Length)
            {
                dbPath = args[i + 1];
                i++;
            }
        }

        if (string.IsNullOrEmpty(dbPath))
        {
            // Default paths: check local directory first, then AppData
            string localDb = Path.Combine(Directory.GetCurrentDirectory(), "scriber.db");
            if (File.Exists(localDb))
            {
                dbPath = localDb;
            }
            else
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                dbPath = Path.Combine(appData, "Scribir", "scriber.db");
            }
        }

        Console.Error.WriteLine($"[Scriber.Mcp] Using database: {dbPath}");

        ITranscriptRepository repository = new SqliteTranscriptRepository(dbPath);
        var server = new McpServer(repository);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await server.RunAsync(cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Scriber.Mcp] Fatal error: {ex}");
            return 1;
        }
    }
}
