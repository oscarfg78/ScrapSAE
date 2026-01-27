using System.Globalization;
using System.Text.Json;
using FirebirdSql.Data.FirebirdClient;
using ScrapSAE.Api.Models;
using ScrapSAE.Core.Entities;

namespace ScrapSAE.Api.Services;

public sealed class FirebirdSaeSdkService : ISaeSdkService
{
    private readonly IConfiguration _configuration;
    private readonly SettingsStore _settingsStore;
    private readonly ILogger<FirebirdSaeSdkService> _logger;

    public FirebirdSaeSdkService(
        IConfiguration configuration,
        SettingsStore settingsStore,
        ILogger<FirebirdSaeSdkService> logger)
    {
        _configuration = configuration;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        try
        {
            await using var connection = new FbConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new FbCommand("SELECT 1 FROM RDB$DATABASE", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing Firebird SAE connection.");
            return false;
        }
    }

    public async Task<bool> SendProductAsync(StagingProduct product, CancellationToken cancellationToken = default)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("Firebird SAE connection not configured.");
            return false;
        }

        var sku = product.SkuSae ?? product.SkuSource;
        if (string.IsNullOrWhiteSpace(sku))
        {
            _logger.LogWarning("Staging product {Id} missing SKU.", product.Id);
            return false;
        }

        var payload = ParsePayload(product.AIProcessedJson);
        var description = payload.Title ?? payload.Description ?? payload.Name ?? sku;
        var price = payload.Price;
        var lineCode = payload.LineCode ?? GetDefaultLineCode();

        if (string.IsNullOrWhiteSpace(lineCode))
        {
            _logger.LogWarning("Default SAE line code not configured.");
            return false;
        }

        try
        {
            await using var connection = new FbConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var exists = await SkuExistsAsync(connection, sku, cancellationToken);
            if (exists)
            {
                return await UpdateProductAsync(connection, sku, description, price, cancellationToken);
            }

            return await InsertProductAsync(connection, sku, description, lineCode, price, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending product {Sku} to SAE.", sku);
            return false;
        }
    }

    private async Task<bool> SkuExistsAsync(FbConnection connection, string sku, CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("SELECT COUNT(*) FROM INVE01 WHERE CVE_ART = @sku", connection);
        command.Parameters.AddWithValue("@sku", sku);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<bool> UpdateProductAsync(
        FbConnection connection,
        string sku,
        string description,
        decimal? price,
        CancellationToken cancellationToken)
    {
        var sql = price.HasValue
            ? "UPDATE INVE01 SET DESCR = @descr, PREC_X_MEN = @price, PREC_X_MAY = @price WHERE CVE_ART = @sku"
            : "UPDATE INVE01 SET DESCR = @descr WHERE CVE_ART = @sku";

        await using var command = new FbCommand(sql, connection);
        command.Parameters.AddWithValue("@descr", description);
        command.Parameters.AddWithValue("@sku", sku);
        if (price.HasValue)
        {
            command.Parameters.AddWithValue("@price", price.Value);
        }

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    private static async Task<bool> InsertProductAsync(
        FbConnection connection,
        string sku,
        string description,
        string lineCode,
        decimal? price,
        CancellationToken cancellationToken)
    {
        var sql = @"
            INSERT INTO INVE01 (CVE_ART, DESCR, LIN_PROD, EXIST, PREC_X_MAY, PREC_X_MEN, ULT_COSTO, STATUS)
            VALUES (@sku, @descr, @line, @exist, @priceMay, @priceMen, @cost, 'A')";

        var effectivePrice = price ?? 0m;
        await using var command = new FbCommand(sql, connection);
        command.Parameters.AddWithValue("@sku", sku);
        command.Parameters.AddWithValue("@descr", description);
        command.Parameters.AddWithValue("@line", lineCode);
        command.Parameters.AddWithValue("@exist", 0m);
        command.Parameters.AddWithValue("@priceMay", effectivePrice);
        command.Parameters.AddWithValue("@priceMen", effectivePrice);
        command.Parameters.AddWithValue("@cost", 0m);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    private string? BuildConnectionString()
    {
        var settings = GetEffectiveSettings();
        if (string.IsNullOrWhiteSpace(settings.SaeDbPath))
        {
            return null;
        }

        var port = settings.SaeDbPort.HasValue && settings.SaeDbPort.Value > 0
            ? settings.SaeDbPort.Value
            : 3050;
        var dialect = settings.SaeDbDialect.HasValue && settings.SaeDbDialect.Value > 0
            ? settings.SaeDbDialect.Value
            : 3;

        var builder = new FbConnectionStringBuilder
        {
            Database = settings.SaeDbPath,
            DataSource = settings.SaeDbHost ?? "localhost",
            UserID = settings.SaeDbUser ?? "SYSDBA",
            Password = settings.SaeDbPassword ?? "masterkey",
            Port = port,
            Dialect = dialect,
            Charset = settings.SaeDbCharset ?? "ISO8859_1",
            Pooling = true
        };

        var timeoutSeconds = _configuration.GetValue("SAE:TimeoutSeconds", 30);
        if (timeoutSeconds > 0)
        {
            builder.ConnectionTimeout = timeoutSeconds;
        }

        return builder.ToString();
    }

    private string? GetDefaultLineCode()
    {
        var settings = GetEffectiveSettings();
        return settings.SaeDefaultLineCode ?? _configuration["SAE:DefaultLineCode"];
    }

    private AppSettingsDto GetEffectiveSettings()
    {
        var stored = _settingsStore.Get();
        return new AppSettingsDto
        {
            SaeDbHost = stored?.SaeDbHost ?? _configuration["SAE:DbHost"],
            SaeDbPath = stored?.SaeDbPath ?? _configuration["SAE:DbPath"],
            SaeDbUser = stored?.SaeDbUser ?? _configuration["SAE:DbUser"],
            SaeDbPassword = stored?.SaeDbPassword ?? _configuration["SAE:DbPassword"],
            SaeDbCharset = stored?.SaeDbCharset ?? _configuration["SAE:DbCharset"],
            SaeDbPort = stored?.SaeDbPort ?? _configuration.GetValue("SAE:DbPort", 3050),
            SaeDbDialect = stored?.SaeDbDialect ?? _configuration.GetValue("SAE:DbDialect", 3),
            SaeDefaultLineCode = stored?.SaeDefaultLineCode ?? _configuration["SAE:DefaultLineCode"]
        };
    }

    private static Payload ParsePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Payload();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new Payload
            {
                Title = GetString(root, "Title"),
                Name = GetString(root, "Name"),
                Description = GetString(root, "Description"),
                LineCode = GetString(root, "LineCode") ?? GetString(root, "SaeLineCode"),
                Price = GetDecimal(root, "Price")
            };
        }
        catch
        {
            return new Payload();
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        if (!TryGetPropertyIgnoreCase(root, name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            _ => null
        };
    }

    private static decimal? GetDecimal(JsonElement root, string name)
    {
        if (!TryGetPropertyIgnoreCase(root, name, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var numeric))
        {
            return numeric;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return TryParsePrice(element.GetString());
        }

        return null;
    }

    private static decimal? TryParsePrice(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var filtered = new string(raw.Where(ch => char.IsDigit(ch) || ch is '.' or ',' or '-').ToArray());
        if (string.IsNullOrWhiteSpace(filtered))
        {
            return null;
        }

        if (filtered.Contains(',') && filtered.Contains('.'))
        {
            filtered = filtered.Replace(",", string.Empty);
        }
        else if (filtered.Contains(','))
        {
            filtered = filtered.Replace(',', '.');
        }

        return decimal.TryParse(
            filtered,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private sealed class Payload
    {
        public string? Title { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? LineCode { get; init; }
        public decimal? Price { get; init; }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement element)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                element = property.Value;
                return true;
            }
        }

        element = default;
        return false;
    }
}
