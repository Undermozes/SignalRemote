using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Remotely.Server.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Remotely.Server.Services;

public class GoogleDriveFileInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset? CreatedTime { get; set; }
    public long? Size { get; set; }
}

public interface IGoogleDriveService
{
    bool IsConfigured { get; }
    string GetAuthorizationUrl(string userId, string redirectUri);
    Task ExchangeCodeForTokenAsync(string userId, string code, string redirectUri);
    bool IsConnected(string userId);
    void Disconnect(string userId);
    Task<string> UploadBackupAsync(string userId, string fileName, Stream content);
    Task<List<GoogleDriveFileInfo>> ListBackupsAsync(string userId);
    Task<Stream> DownloadBackupAsync(string userId, string fileId);
}

public class GoogleDriveService : IGoogleDriveService
{
    private const string AppFolderName = "Remotely Backups";
    private const string BackupMimeType = "application/json";
    private static readonly string[] Scopes = { DriveService.Scope.DriveFile };

    private readonly ConcurrentDictionary<string, TokenResponse> _userTokens = new();
    private readonly GoogleDriveOptions _options;
    private readonly ILogger<GoogleDriveService> _logger;

    public GoogleDriveService(
        IOptions<GoogleDriveOptions> options,
        ILogger<GoogleDriveService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ClientId) &&
        !string.IsNullOrWhiteSpace(_options.ClientSecret);

    public string GetAuthorizationUrl(string userId, string redirectUri)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Google Drive is not configured.");
        }

        var flow = CreateFlow();
        var uri = flow.CreateAuthorizationCodeRequest(redirectUri);
        uri.State = userId;
        return uri.Build().AbsoluteUri;
    }

    public async Task ExchangeCodeForTokenAsync(string userId, string code, string redirectUri)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Google Drive is not configured.");
        }

        var flow = CreateFlow();
        var token = await flow.ExchangeCodeForTokenAsync(
            userId,
            code,
            redirectUri,
            CancellationToken.None);

        _userTokens[userId] = token;

        _logger.LogInformation("Google Drive OAuth token obtained for user {UserId}.", userId);
    }

    public bool IsConnected(string userId)
    {
        return _userTokens.ContainsKey(userId);
    }

    public void Disconnect(string userId)
    {
        _userTokens.TryRemove(userId, out _);
        _logger.LogInformation("Google Drive disconnected for user {UserId}.", userId);
    }

    public async Task<string> UploadBackupAsync(string userId, string fileName, Stream content)
    {
        var service = CreateDriveService(userId);
        var folderId = await GetOrCreateBackupFolderAsync(service);

        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = new List<string> { folderId },
            MimeType = BackupMimeType
        };

        var request = service.Files.Create(fileMetadata, content, BackupMimeType);
        request.Fields = "id, name";

        var result = await request.UploadAsync();

        if (result.Status != UploadStatus.Completed)
        {
            throw new InvalidOperationException(
                $"Failed to upload backup to Google Drive: {result.Exception?.Message}");
        }

        _logger.LogInformation(
            "Uploaded backup '{FileName}' to Google Drive for user {UserId}.",
            fileName,
            userId);

        return request.ResponseBody.Id;
    }

    public async Task<List<GoogleDriveFileInfo>> ListBackupsAsync(string userId)
    {
        var service = CreateDriveService(userId);
        var folderId = await GetOrCreateBackupFolderAsync(service);

        var listRequest = service.Files.List();
        listRequest.Q = $"'{folderId}' in parents and trashed = false";
        listRequest.Fields = "files(id, name, createdTime, size)";
        listRequest.OrderBy = "createdTime desc";

        var result = await listRequest.ExecuteAsync();

        return result.Files.Select(f => new GoogleDriveFileInfo
        {
            Id = f.Id,
            Name = f.Name,
            CreatedTime = f.CreatedTimeDateTimeOffset,
            Size = f.Size
        }).ToList();
    }

    public async Task<Stream> DownloadBackupAsync(string userId, string fileId)
    {
        var service = CreateDriveService(userId);
        var request = service.Files.Get(fileId);
        var stream = new MemoryStream();
        await request.DownloadAsync(stream);
        stream.Position = 0;
        return stream;
    }

    private GoogleAuthorizationCodeFlow CreateFlow()
    {
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret
            },
            Scopes = Scopes
        });
    }

    private DriveService CreateDriveService(string userId)
    {
        if (!_userTokens.TryGetValue(userId, out var token))
        {
            throw new InvalidOperationException("User is not connected to Google Drive.");
        }

        var flow = CreateFlow();
        var credential = new UserCredential(flow, userId, token);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Remotely Backup"
        });
    }

    private async Task<string> GetOrCreateBackupFolderAsync(DriveService service)
    {
        // Search for existing folder
        var listRequest = service.Files.List();
        listRequest.Q = $"name = '{AppFolderName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        listRequest.Fields = "files(id)";

        var result = await listRequest.ExecuteAsync();

        if (result.Files.Count > 0)
        {
            return result.Files[0].Id;
        }

        // Create folder
        var folderMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = AppFolderName,
            MimeType = "application/vnd.google-apps.folder"
        };

        var createRequest = service.Files.Create(folderMetadata);
        createRequest.Fields = "id";

        var folder = await createRequest.ExecuteAsync();
        return folder.Id;
    }
}
