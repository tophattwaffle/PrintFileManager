namespace PrintFileManager;

public sealed class AsyncSafeList<T>
{
    private List<T> _list = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Add an item to the list.</summary>
    public async Task AddAsync(T item, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _list.Add(item);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task ResetListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _list = new List<T>();
        }
        finally
        {
            _lock.Release();
        }
    }
    
    public async Task AddRangeAsync(IEnumerable<T> items, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _list.AddRange(items);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Remove the first matching item from the list.
    /// Returns true if removed, false if not found.
    /// </summary>
    public async Task<bool> RemoveAsync(T item, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _list.Remove(item);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Returns a snapshot of the list at this moment (safe copy).
    /// </summary>
    public async Task<List<T>> GetSnapshotAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _list.ToList(); // snapshot copy
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Get the count safely.</summary>
    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _list.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ContainsAsync(T item, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _list.Contains(item);
        }
        finally
        {
            _lock.Release();
        }
    }
}