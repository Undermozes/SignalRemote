using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Remotely.Server.Tests;

[TestClass]
public class DbBackupServiceTests
{
    private string _testBackupDir = null!;

    [TestInitialize]
    public void TestInit()
    {
        _testBackupDir = Path.Combine(Path.GetTempPath(), $"remotely-backup-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBackupDir);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(_testBackupDir))
        {
            Directory.Delete(_testBackupDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task CreateBackupAsync_CreatesBackupFile()
    {
        var sourcePath = Path.Combine(_testBackupDir, "source.db");
        var backupPath = Path.Combine(_testBackupDir, "backup.db");

        // Create a source SQLite database with a table and row
        using (var conn = new SqliteConnection($"Data Source={sourcePath}"))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Test (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Test VALUES (1, 'hello');";
            await cmd.ExecuteNonQueryAsync();
        }

        await DbBackupService.CreateBackupAsync($"Data Source={sourcePath}", backupPath);

        Assert.IsTrue(File.Exists(backupPath), "Backup file should be created.");

        // Verify backup contains the data
        using var backupConn = new SqliteConnection($"Data Source={backupPath}");
        await backupConn.OpenAsync();
        using var selectCmd = backupConn.CreateCommand();
        selectCmd.CommandText = "SELECT Name FROM Test WHERE Id = 1;";
        var result = (string?)await selectCmd.ExecuteScalarAsync();
        Assert.AreEqual("hello", result, "Backup file should contain the original data.");
    }

    [TestMethod]
    public void RotateOldBackups_RemovesExcessBackups()
    {
        // Create 10 fake backup files
        for (var i = 0; i < 10; i++)
        {
            var file = Path.Combine(_testBackupDir, $"Remotely-{i:D2}.db");
            File.WriteAllText(file, "placeholder");
        }

        DbBackupService.RotateOldBackups(_testBackupDir, maxBackupsToKeep: 7);

        var remaining = Directory.GetFiles(_testBackupDir, "*.db");
        Assert.AreEqual(7, remaining.Length, "Only 7 backups should remain after rotation.");
    }

    [TestMethod]
    public void RotateOldBackups_KeepsAllWhenUnderLimit()
    {
        // Create 3 fake backup files (under the limit of 7)
        for (var i = 0; i < 3; i++)
        {
            var file = Path.Combine(_testBackupDir, $"Remotely-{i:D2}.db");
            File.WriteAllText(file, "placeholder");
        }

        DbBackupService.RotateOldBackups(_testBackupDir, maxBackupsToKeep: 7);

        var remaining = Directory.GetFiles(_testBackupDir, "*.db");
        Assert.AreEqual(3, remaining.Length, "All backups should remain when under the limit.");
    }

    [TestMethod]
    public void RotateOldBackups_PreservesNewestBackups()
    {
        // Create files with distinct last-write times by setting them explicitly
        for (var i = 0; i < 5; i++)
        {
            var file = Path.Combine(_testBackupDir, $"Remotely-backup-{i:D2}.db");
            File.WriteAllText(file, "placeholder");
            // Older files get an earlier LastWriteTimeUtc
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-i));
        }

        DbBackupService.RotateOldBackups(_testBackupDir, maxBackupsToKeep: 3);

        var remaining = Directory.GetFiles(_testBackupDir, "*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        Assert.AreEqual(3, remaining.Count, "Only 3 backups should remain.");
    }
}
