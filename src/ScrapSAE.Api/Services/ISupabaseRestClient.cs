using System.Text.Json;

namespace ScrapSAE.Api.Services;

public interface ISupabaseRestClient
{
    JsonSerializerOptions JsonOptions { get; }
    Task<T[]> GetAsync<T>(string pathAndQuery);
    Task<T?> PostAsync<T>(string path, T body) where T : class;
    Task<T?> PatchAsync<T>(string pathAndQuery, object update) where T : class;
    Task DeleteAsync(string pathAndQuery);
}
