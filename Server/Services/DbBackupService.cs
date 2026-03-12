using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Remotely.Server.Options;

namespace Remotely.Server.Services;

public class DbBackupService : BackgroundService
{
    private const int MaxBackupsToKeep = 7;

    private readonly IConfiguration _configuration;
    private readonly IOptions<ApplicationOptions> _appOptions;
    private readonly ILogger<DbBackupService> _logger;

    public static string BackupsDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData", "db-backups");

    public DbBackupService(
        IConfiguration configuration,
        IOptions<ApplicationOptions> appOptions,
        ILogger<DbBackupService> logger)
    {
        _configuration = configuration;
        _appOptions = appOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.Equals(_appOptions.Value.DbProvider, "SQLite", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Database backup service only supports SQLite. Skipping for provider: {Provider}.",
                _appOptions.Value.DbProvider);
            return;
        }

        await Task.Yield();

        await PerformBackupAsync();

        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _ = await timer.WaitForNextTickAsync(stoppingToken);
                await PerformBackupAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Application is shutting down. Stopping database backup service.");
            }
        }
    }

    internal async Task PerformBackupAsync()
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("SQLite");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _logger.LogWarning("SQLite connection string is not configured. Skipping database backup.");
                return;
            }

            Directory.CreateDirectory(BackupsDirectory);

            var backupFileName = $"Remotely-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.db";
            var backupPath = Path.Combine(BackupsDirectory, backupFileName);

            await CreateBackupAsync(connectionString, backupPath);

            _logger.LogInformation("Database backup created: {BackupPath}", backupPath);

            RotateOldBackups(BackupsDirectory, MaxBackupsToKeep);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating database backup.");
        }
    }

    internal static async Task CreateBackupAsync(string sourceConnectionString, string backupPath)
    {
        using var source = new SqliteConnection(sourceConnectionString);
        using var destination = new SqliteConnection($"Data Source={backupPath}");

        await source.OpenAsync();
        await destination.OpenAsync();

        source.BackupDatabase(destination);
    }

    internal static void RotateOldBackups(string backupDirectory, int maxBackupsToKeep)
    {
        var filesToDelete = Directory
            .GetFiles(backupDirectory, "*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(maxBackupsToKeep)
            .ToList();

        foreach (var file in filesToDelete)
        {
            file.Delete();
        }
    }
}
