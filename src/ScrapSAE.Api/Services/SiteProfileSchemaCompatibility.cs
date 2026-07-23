using System.Text.Json;
using System.Text.Json.Nodes;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Api.Services;

public static class SiteProfileSchemaCompatibility
{
    private const string LegacySecondarySelectorsKey = "__legacySecondarySelectors";
    private const string LegacyStrategiesKey = "__legacyStrategies";
    private const string LegacyStrategyTypeKey = "__legacyStrategyType";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static SiteProfile NormalizeFromStorage(SiteProfile site)
    {
        EnsureCollections(site);

        var selectors = ParseSelectors(site.Selectors);

        if (site.SecondarySelectors.Count == 0 &&
            TryDeserialize<Dictionary<string, List<string>>>(selectors[LegacySecondarySelectorsKey], out var legacySecondarySelectors) &&
            legacySecondarySelectors != null)
        {
            site.SecondarySelectors = legacySecondarySelectors;
        }

        if (site.Strategies.Count == 0 &&
            TryDeserialize<List<ScrapingStrategyDefinition>>(selectors[LegacyStrategiesKey], out var legacyStrategies) &&
            legacyStrategies != null)
        {
            site.Strategies = legacyStrategies;
        }

        if ((string.IsNullOrEmpty(site.StrategyType) || site.StrategyType == "Generic") &&
            selectors.TryGetPropertyValue(LegacyStrategyTypeKey, out var legacyStrategyTypeNode))
        {
            var kind = legacyStrategyTypeNode?.GetValueKind();
            Console.WriteLine($"[DEBUG] NormalizeFromStorage: Found {LegacyStrategyTypeKey}. Kind: {kind}, Value: {legacyStrategyTypeNode}");
            
            if (kind == JsonValueKind.String || kind == JsonValueKind.Object)
            {
                site.StrategyType = legacyStrategyTypeNode?.ToString() ?? "Generic";
                Console.WriteLine($"[DEBUG] NormalizeFromStorage: Restored StrategyType to {site.StrategyType}");
            }
        }

        selectors.Remove(LegacySecondarySelectorsKey);
        selectors.Remove(LegacyStrategiesKey);
        selectors.Remove(LegacyStrategyTypeKey);
        site.Selectors = selectors;

        EnsureCollections(site);
        return site;
    }

    public static void NormalizeForPersistence(SiteProfile site)
    {
        EnsureCollections(site);

        var selectors = ParseSelectors(site.Selectors);
        selectors.Remove(LegacySecondarySelectorsKey);
        selectors.Remove(LegacyStrategiesKey);
        site.Selectors = selectors;
    }

    public static bool IsMissingAdvancedColumnError(Exception ex)
    {
        var message = ex.ToString();
        return message.Contains("secondary_selectors", StringComparison.OrdinalIgnoreCase)
            || message.Contains("strategies", StringComparison.OrdinalIgnoreCase)
            || message.Contains("strategy_type", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<SiteProfile?> CreateWithFallbackAsync(
        SupabaseTableService<SiteProfile> service,
        ISupabaseRestClient supabase,
        SiteProfile site)
    {
        NormalizeForPersistence(site);

        try
        {
            var created = await service.CreateAsync(site);
            return created == null ? null : NormalizeFromStorage(created);
        }
        catch (Exception ex) when (IsMissingAdvancedColumnError(ex))
        {
            await supabase.PostAsync<object>("config_sites", BuildLegacyPayload(site));
            var reloaded = await service.GetByIdAsync(site.Id);
            return NormalizeFromStorage(reloaded ?? site);
        }
    }

    public static async Task<SiteProfile?> UpdateWithFallbackAsync(
        SupabaseTableService<SiteProfile> service,
        ISupabaseRestClient supabase,
        Guid id,
        SiteProfile site)
    {
        site.Id = id;
        NormalizeForPersistence(site);

        try
        {
            var updated = await service.UpdateAsync(id, site);
            return updated == null ? null : NormalizeFromStorage(updated);
        }
        catch (Exception ex) when (IsMissingAdvancedColumnError(ex))
        {
            await supabase.PatchAsync<object>($"config_sites?id=eq.{id}", BuildLegacyPayload(site));
            var reloaded = await service.GetByIdAsync(id);
            return NormalizeFromStorage(reloaded ?? site);
        }
    }

    public static SiteProfileLegacyPayload BuildLegacyPayload(SiteProfile site)
    {
        NormalizeForPersistence(site);
        var selectors = ParseSelectors(site.Selectors);
        selectors[LegacySecondarySelectorsKey] = JsonSerializer.SerializeToNode(site.SecondarySelectors, JsonOptions) ?? new JsonObject();
        selectors[LegacyStrategiesKey] = JsonSerializer.SerializeToNode(site.Strategies, JsonOptions) ?? new JsonArray();
        
        if (!string.IsNullOrEmpty(site.StrategyType))
        {
            selectors[LegacyStrategyTypeKey] = site.StrategyType;
        }

        return new SiteProfileLegacyPayload
        {
            Id = site.Id,
            Name = site.Name,
            BaseUrl = site.BaseUrl,
            LoginUrl = site.LoginUrl,
            Selectors = selectors,
            CronExpression = site.CronExpression,
            RequiresLogin = site.RequiresLogin,
            CredentialsEncrypted = site.CredentialsEncrypted,
            IsActive = site.IsActive,
            MaxProductsPerScrape = site.MaxProductsPerScrape,
            CreatedAt = site.CreatedAt,
            UpdatedAt = site.UpdatedAt
        };
    }

    private static JsonObject ParseSelectors(object? rawSelectors)
    {
        try
        {
            if (rawSelectors is JsonObject jsonObject)
            {
                return (JsonObject)jsonObject.DeepClone();
            }

            if (rawSelectors is JsonNode jsonNode)
            {
                if (jsonNode is JsonObject jsonNodeObject)
                {
                    return (JsonObject)jsonNodeObject.DeepClone();
                }

                if (jsonNode is JsonValue jsonNodeValue &&
                    jsonNodeValue.TryGetValue<string>(out var embeddedNodeJson) &&
                    !string.IsNullOrWhiteSpace(embeddedNodeJson))
                {
                    var parsedNodeJson = JsonNode.Parse(embeddedNodeJson);
                    if (parsedNodeJson is JsonObject parsedNodeObject)
                    {
                        return parsedNodeObject;
                    }
                }
            }

            if (rawSelectors is JsonElement jsonElement)
            {
                var parsedFromElement = ParseJsonElement(jsonElement);
                if (parsedFromElement is JsonObject jsonElementObject)
                {
                    return jsonElementObject;
                }
            }

            if (rawSelectors is string rawText && !string.IsNullOrWhiteSpace(rawText))
            {
                var parsedText = JsonNode.Parse(rawText);
                if (parsedText is JsonObject parsedObject)
                {
                    return parsedObject;
                }
            }

            if (rawSelectors != null)
            {
                var serialized = JsonSerializer.SerializeToNode(rawSelectors, JsonOptions);
                if (serialized is JsonObject serializedObject)
                {
                    return serializedObject;
                }

                if (serialized is JsonValue serializedValue &&
                    serializedValue.TryGetValue<string>(out var embeddedSerializedJson) &&
                    !string.IsNullOrWhiteSpace(embeddedSerializedJson))
                {
                    var parsedSerialized = JsonNode.Parse(embeddedSerializedJson);
                    if (parsedSerialized is JsonObject parsedSerializedObject)
                    {
                        return parsedSerializedObject;
                    }
                }
            }
        }
        catch
        {
            // Keep compatibility parsing best-effort and return an empty object on errors.
        }

        return new JsonObject();
    }

    private static JsonNode? ParseJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return JsonNode.Parse(element.GetRawText());
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return new JsonObject();
            }

            try
            {
                return JsonNode.Parse(text);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static bool TryDeserialize<T>(JsonNode? node, out T? value)
    {
        value = default;
        if (node == null)
        {
            return false;
        }

        try
        {
            value = node.Deserialize<T>(JsonOptions);
            return value != null;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureCollections(SiteProfile site)
    {
        site.SecondarySelectors ??= new Dictionary<string, List<string>>();
        site.Strategies ??= new List<ScrapingStrategyDefinition>();

        var normalizedSecondarySelectors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in site.SecondarySelectors)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }

            var key = entry.Key.Trim();
            if (!normalizedSecondarySelectors.TryGetValue(key, out var values))
            {
                values = new List<string>();
                normalizedSecondarySelectors[key] = values;
            }

            foreach (var selector in entry.Value ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(selector))
                {
                    continue;
                }

                var normalizedSelector = selector.Trim();
                if (!values.Contains(normalizedSelector, StringComparer.OrdinalIgnoreCase))
                {
                    values.Add(normalizedSelector);
                }
            }
        }

        site.SecondarySelectors = normalizedSecondarySelectors;

        site.Strategies = site.Strategies
            .Where(strategy => !string.IsNullOrWhiteSpace(strategy.StrategyName))
            .OrderBy(strategy => strategy.Priority)
            .ToList();
    }
}

public sealed class SiteProfileLegacyPayload
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? LoginUrl { get; set; }
    public JsonObject Selectors { get; set; } = new();
    public string? CronExpression { get; set; }
    public bool RequiresLogin { get; set; }
    public string? CredentialsEncrypted { get; set; }
    public bool IsActive { get; set; } = true;
    public int MaxProductsPerScrape { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
