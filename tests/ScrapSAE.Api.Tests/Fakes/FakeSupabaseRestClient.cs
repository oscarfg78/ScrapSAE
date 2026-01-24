using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ScrapSAE.Api.Services;

namespace ScrapSAE.Api.Tests.Fakes;

public sealed class FakeSupabaseRestClient : ISupabaseRestClient
{
    private readonly ConcurrentDictionary<string, List<object>> _tables = new(StringComparer.OrdinalIgnoreCase);

    public JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public Task<T[]> GetAsync<T>(string pathAndQuery)
    {
        var (table, filters) = ParsePath(pathAndQuery);
        var list = GetTable(table);
        var results = list.OfType<T>().Where(item => MatchesFilters(item, filters)).ToArray();
        return Task.FromResult(results);
    }

    public Task<T?> PostAsync<T>(string path, T body) where T : class
    {
        var list = GetTable(path);
        list.Add(body);
        return Task.FromResult<T?>(body);
    }

    public Task<T?> PatchAsync<T>(string pathAndQuery, object update) where T : class
    {
        var (table, filters) = ParsePath(pathAndQuery);
        var list = GetTable(table);
        var item = list.OfType<T>().FirstOrDefault(entry => MatchesFilters(entry, filters));
        if (item == null)
        {
            return Task.FromResult<T?>(null);
        }

        ApplyUpdate(item, update);
        return Task.FromResult<T?>(item);
    }

    public Task DeleteAsync(string pathAndQuery)
    {
        var (table, filters) = ParsePath(pathAndQuery);
        var list = GetTable(table);
        var toRemove = list.Where(entry => MatchesFilters(entry, filters)).ToList();
        foreach (var item in toRemove)
        {
            list.Remove(item);
        }

        return Task.CompletedTask;
    }

    public void Seed<T>(string table, params T[] items) where T : class
    {
        var list = GetTable(table);
        foreach (var item in items)
        {
            list.Add(item);
        }
    }

    public void Reset()
    {
        _tables.Clear();
    }

    private List<object> GetTable(string table)
    {
        return _tables.GetOrAdd(table, _ => new List<object>());
    }

    private static (string Table, Dictionary<string, string> Filters) ParsePath(string pathAndQuery)
    {
        var parts = pathAndQuery.Split('?', 2);
        var table = parts[0];
        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parts.Length < 2)
        {
            return (table, filters);
        }

        foreach (var chunk in parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (chunk.StartsWith("select=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var kv = chunk.Split("=eq.", 2, StringSplitOptions.RemoveEmptyEntries);
            if (kv.Length == 2)
            {
                filters[kv[0]] = Uri.UnescapeDataString(kv[1]);
            }
        }

        return (table, filters);
    }

    private static bool MatchesFilters<T>(T item, Dictionary<string, string> filters)
    {
        foreach (var filter in filters)
        {
            var value = GetPropertyValue(item!, filter.Key);
            if (value == null)
            {
                return false;
            }

            if (value is Guid guidValue)
            {
                if (!Guid.TryParse(filter.Value, out var parsed) || parsed != guidValue)
                {
                    return false;
                }
            }
            else
            {
                if (!string.Equals(value.ToString(), filter.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static object? GetPropertyValue(object item, string snakeCaseName)
    {
        var propertyName = SnakeToPascal(snakeCaseName);
        var property = item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return property?.GetValue(item);
    }

    private static void ApplyUpdate(object target, object update)
    {
        foreach (var sourceProperty in update.GetType().GetProperties())
        {
            var targetPropertyName = SnakeToPascal(sourceProperty.Name);
            var targetProperty = target.GetType().GetProperty(targetPropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (targetProperty == null || !targetProperty.CanWrite)
            {
                continue;
            }

            var value = sourceProperty.GetValue(update);
            targetProperty.SetValue(target, value);
        }
    }

    private static string SnakeToPascal(string name)
    {
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
    }
}
