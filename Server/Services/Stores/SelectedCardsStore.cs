namespace Remotely.Server.Services.Stores;

public interface ISelectedCardsStore
{
    event EventHandler? SelectedDevicesChanged;
    IEnumerable<string> SelectedDevices { get; }
    bool Add(string deviceId);
    bool Contains(string deviceId);

    bool Remove(string deviceId);
    void Clear();
    void InvokeSelectionsChanged();
}

public class SelectedCardsStore : ISelectedCardsStore
{
    private readonly object _lock = new();
    private readonly HashSet<string> _selectedDevices = new();

    public IEnumerable<string> SelectedDevices
    {
        get
        {
            lock (_lock)
            {
                return _selectedDevices.ToArray();
            }
        }
    }

    public event EventHandler? SelectedDevicesChanged;

    // Changes raise SelectedDevicesChanged (outside the lock), e.g. so the terminal
    // can show which devices a command will run on.
    public bool Add(string deviceId)
    {
        bool added;
        lock (_lock)
        {
            added = _selectedDevices.Add(deviceId);
        }
        if (added)
        {
            InvokeSelectionsChanged();
        }
        return added;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _selectedDevices.Clear();
        }
        InvokeSelectionsChanged();
    }

    public bool Contains(string deviceId)
    {
        lock ( _lock)
        {
              return _selectedDevices.Contains(deviceId);
        }
    }

    public void InvokeSelectionsChanged()
    {
        SelectedDevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string deviceId)
    {
        bool removed;
        lock (_lock)
        {
            removed = _selectedDevices.Remove(deviceId);
        }
        if (removed)
        {
            InvokeSelectionsChanged();
        }
        return removed;
    }
}