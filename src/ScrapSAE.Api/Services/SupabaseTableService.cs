namespace ScrapSAE.Api.Services;

public sealed class SupabaseTableService<T> where T : class
{
    private readonly ISupabaseRestClient _client;
    private readonly string _tableName;

    public SupabaseTableService(ISupabaseRestClient client, string tableName)
    {
        _client = client;
        _tableName = tableName;
    }

    public async Task<IReadOnlyList<T>> GetAllAsync()
    {
        var result = await _client.GetAsync<T>($"{_tableName}?select=*");
        return result;
    }

    public async Task<T?> GetByIdAsync(Guid id)
    {
        var result = await _client.GetAsync<T>($"{_tableName}?id=eq.{id}&select=*");
        return result.FirstOrDefault();
    }

    public Task<T?> CreateAsync(T entity)
    {
        return _client.PostAsync(_tableName, entity);
    }

    public Task<T?> UpdateAsync(Guid id, T entity)
    {
        return _client.PatchAsync<T>($"{_tableName}?id=eq.{id}", entity!);
    }

    public Task DeleteAsync(Guid id)
    {
        return _client.DeleteAsync($"{_tableName}?id=eq.{id}");
    }
}
