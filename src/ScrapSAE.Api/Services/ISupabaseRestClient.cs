using System.Text.Json;

namespace ScrapSAE.Api.Services;

public interface ISupabaseRestClient
{
    JsonSerializerOptions JsonOptions { get; }
    Task<T[]> GetAsync<T>(string pathAndQuery);
    Task<string> GetAsync(string pathAndQuery); // Raw JSON response
    Task<T?> PostAsync<T>(string path, T body) where T : class;
    Task<T?> PatchAsync<T>(string pathAndQuery, object update) where T : class;
    Task PatchAsync(string pathAndQuery, string jsonBody); // Raw JSON update
    Task DeleteAsync(string pathAndQuery);
}

