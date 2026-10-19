using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Remotely.Server.Hubs;
using Remotely.Server.Models.Messages;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Utilities;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Remotely.Server.Components.Pages;

public partial class DeviceDetails : AuthComponentBase
{
    private readonly ConcurrentQueue<string> _logLines = new();
    private readonly ConcurrentQueue<ScriptResult> _scriptResults = new();

    private string? _alertMessage;
    private Device? _device;
    private bool _userHasAccess;
    private string? _inputDeviceId;
    private bool _isLoading = true;
    private DeviceGroup[] _deviceGroups = Array.Empty<DeviceGroup>();

    [Parameter]
    public string ActiveTab { get; set; } = string.Empty;

    [Parameter]
    public string DeviceId { get; set; } = string.Empty;

    [Inject]
    private ICircuitConnection CircuitConnection { get; set; } = null!;

    [Inject]
    private IDataService DataService { get; set; } = null!;


    [Inject]
    private IJsInterop JsInterop { get; set; } = null!;

    [Inject]
    private IModalService ModalService { get; set; } = null!;

    [Inject]
    private NavigationManager NavManager { get; set; } = null!;

    [Inject]
    private IToastService ToastService { get; set; } = null!;


    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        EnsureUserSet();

        if (!string.IsNullOrWhiteSpace(DeviceId))
        {
            var deviceResult = await DataService.GetDevice(DeviceId);
            if (deviceResult.IsSuccess)
            {
                _device = deviceResult.Value;
                _userHasAccess = DataService.DoesUserHaveAccessToDevice(_device.ID, User);
            }
            else
            {
                ToastService.ShowToast2(deviceResult.Reason, Enums.ToastType.Warning);
            }
        }

        _deviceGroups = DataService.GetDeviceGroups(UserName);
        await Register<ReceiveLogsMessage, string>(
            CircuitConnection.ConnectionId,
            HandleReceiveLogsMessage);

        _isLoading = false;
    }

    private async Task HandleReceiveLogsMessage(object subscriber, ReceiveLogsMessage message)
    {
        _logLines.Enqueue(message.LogChunk);
        await InvokeAsync(StateHasChanged);
    }

    private async Task DeleteLogs()
    {
        if (_device is null)
        {
            return;
        }

        var result = await JsInterop.Confirm("Are you sure you want to delete the remote logs?");
        if (result)
        {
            await CircuitConnection.DeleteRemoteLogs(_device.ID);
            ToastService.ShowToast("Delete command sent.");
        }
    }

    private void EditFormKeyDown()
    {
        _alertMessage = string.Empty;
    }

    private void EvaluateDeviceIdInputKeyDown(KeyboardEventArgs args)
    {
        if (args.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            NavManager.NavigateTo($"/device-details/{_inputDeviceId}");
        }
    }

    private void GetRemoteLogs()
    {
        if (_device is null)
        {
            return;
        }

        _logLines.Clear();

        if (_device.IsOnline)
        {
            CircuitConnection.GetRemoteLogs(_device.ID);
        }
    }

    private void GetScriptHistory()
    {
        if (_device is null)
        {
            return;
        }

        EnsureUserSet();

        _scriptResults.Clear();

        if (User.IsAdministrator)
        {
            var results = DataService
                .GetAllScriptResults(User.OrganizationID, _device.ID)
                .OrderByDescending(x => x.TimeStamp);

            foreach (var result in results)
            {
                _scriptResults.Enqueue(result);
            }
        }
        else
        {
            var results = DataService
                .GetAllCommandResultsForUser(User.OrganizationID, UserName, _device.ID)
                .OrderByDescending(x => x.TimeStamp);

            foreach (var result in results)
            {
                _scriptResults.Enqueue(result);
            }
        }
    }

    private string GetTrimmedText(string? source, int stringLength)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "(none)";
        }

        if (source.Length <= stringLength)
        {
            return source;
        }

        return source[0..25] + "...";
    }

    private string GetTrimmedText(string[]? source, int stringLength)
    {
        source ??= Array.Empty<string>();
        return GetTrimmedText(string.Join("", source), stringLength);
    }

    private Task HandleValidSubmit()
    {
        if (_device is null)
        {
            return Task.CompletedTask;
        }

        DataService.UpdateDevice(
            _device.ID,
            _device.Tags,
            _device.Alias,
            _device.DeviceGroupID,
            _device.Notes);

        _alertMessage = "Device details saved.";
        ToastService.ShowToast("Device details saved.");

        return Task.CompletedTask;
    }

    private void NavigateToDeviceId()
    {
        NavManager.NavigateTo($"/device-details/{_inputDeviceId}");
    }

    private void ShowAllDisks()
    {
        if (_device is null)
        {
            return;
        }

        var disksString = JsonSerializer.Serialize(_device.Drives, JsonSerializerHelper.IndentedOptions);
        void modalBody(RenderTreeBuilder builder)
        {
            // Drive data comes from the device: add it as text so it's always HTML-encoded.
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "overflow-x-auto font-mono text-xs whitespace-pre");
            builder.AddContent(2, disksString);
            builder.CloseElement();
        }
        ModalService.ShowModal($"All Disks for {_device.DeviceName}", modalBody);
    }

    private void ShowFullScriptOutput(ScriptResult result)
    {
        void outputModal(RenderTreeBuilder builder)
        {
            var output = string.Join("\r\n", result.StandardOutput ?? Array.Empty<string>());
            var error = string.Join("\r\n", result.ErrorOutput ?? Array.Empty<string>());
            var textareaClass = "sr-textarea h-48 font-mono text-xs whitespace-pre";

            // Script input and output come from users and devices: add them as text so
            // they're HTML-encoded, never as markup.
            var seq = 0;
            void AddSection(string title, string text, bool first)
            {
                builder.OpenElement(seq++, "h5");
                builder.AddAttribute(seq++, "class", first ? "mb-1 text-sm font-semibold" : "mb-1 mt-4 text-sm font-semibold");
                builder.AddContent(seq++, title);
                builder.CloseElement();
                builder.OpenElement(seq++, "textarea");
                builder.AddAttribute(seq++, "readonly", true);
                builder.AddAttribute(seq++, "class", textareaClass);
                builder.AddAttribute(seq++, "value", text);
                builder.CloseElement();
            }

            AddSection("Input", result.ScriptInput ?? string.Empty, first: true);
            AddSection("Standard Output", output, first: false);
            AddSection("Error Output", error, first: false);
        }

        ModalService.ShowModal("Script Input/Output", outputModal);
    }
}