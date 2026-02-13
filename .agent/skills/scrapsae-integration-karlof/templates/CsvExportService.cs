// ============================================
// CsvExportService - ScrapSAE
// Servicio para exportar productos a CSV/Excel
// ============================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;

namespace ScrapSAE.Infrastructure.Services
{
    public class CsvExportService : ICsvExportService
    {
        private readonly ILogger<CsvExportService> _logger;

        public CsvExportService(ILogger<CsvExportService> logger)
        {
            _logger = logger;
        }

        public async Task<string> ExportProductsToCsvAsync(
            IEnumerable<StagingProduct> products,
            string outputPath)
        {
            try
            {
                _logger.LogInformation(
                    "Exportando {Count} productos a CSV: {Path}",
                    products.Count(),
                    outputPath);

                var csvRecords = products.Select(p => new ProductCsvRecord
                {
                    SourceSku = p.SkuSource,
                    Name = ExtractFromJson(p.AiProcessedJson, "name"),
                    Description = ExtractFromJson(p.AiProcessedJson, "description"),
                    PurchasePrice = ExtractDecimalFromJson(p.AiProcessedJson, "price"),
                    Currency = "MXN",
                    Categories = string.Join("|", ExtractArrayFromJson(p.AiProcessedJson, "categories")),
                    ProductUrl = ExtractFromJson(p.AiProcessedJson, "url"),
                    ImageUrls = string.Join("|", ExtractArrayFromJson(p.AiProcessedJson, "images")),
                    SupplierName = ExtractFromJson(p.AiProcessedJson, "supplier"),
                    SpecificationsJson = ExtractFromJson(p.AiProcessedJson, "specifications")
                }).ToList();

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = ",",
                    HasHeaderRecord = true,
                    Encoding = Encoding.UTF8
                };

                await using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
                await using var csv = new CsvWriter(writer, config);

                // Registrar el mapa de columnas
                csv.Context.RegisterClassMap<ProductCsvRecordMap>();

                await csv.WriteRecordsAsync(csvRecords);

                _logger.LogInformation(
                    "Exportación completada: {Count} productos exportados a {Path}",
                    csvRecords.Count,
                    outputPath);

                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exportando productos a CSV");
                throw;
            }
        }

        // Métodos auxiliares (mismos que FlashlySyncService)
        private string ExtractFromJson(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(key, out var element))
                {
                    return element.GetString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extrayendo '{Key}' del JSON", key);
            }

            return string.Empty;
        }

        private decimal ExtractDecimalFromJson(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return 0;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(key, out var element))
                {
                    if (element.TryGetDecimal(out var value))
                        return value;

                    var strValue = element.GetString();
                    if (decimal.TryParse(strValue, out var parsedValue))
                        return parsedValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extrayendo decimal '{Key}' del JSON", key);
            }

            return 0;
        }

        private List<string> ExtractArrayFromJson(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return new List<string>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(key, out var element))
                {
                    if (element.ValueKind == JsonValueKind.Array)
                    {
                        return element.EnumerateArray()
                            .Select(e => e.GetString())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extrayendo array '{Key}' del JSON", key);
            }

            return new List<string>();
        }
    }

    // ============================================
    // Modelo CSV
    // ============================================

    public class ProductCsvRecord
    {
        public string SourceSku { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal PurchasePrice { get; set; }
        public string Currency { get; set; }
        public string Categories { get; set; }
        public string ProductUrl { get; set; }
        public string ImageUrls { get; set; }
        public string SupplierName { get; set; }
        public string SpecificationsJson { get; set; }
    }

    // ============================================
    // Mapa de Columnas CSV
    // ============================================

    public class ProductCsvRecordMap : ClassMap<ProductCsvRecord>
    {
        public ProductCsvRecordMap()
        {
            Map(m => m.SourceSku).Name("source_sku");
            Map(m => m.Name).Name("name");
            Map(m => m.Description).Name("description");
            Map(m => m.PurchasePrice).Name("purchase_price");
            Map(m => m.Currency).Name("currency");
            Map(m => m.Categories).Name("categories");
            Map(m => m.ProductUrl).Name("product_url");
            Map(m => m.ImageUrls).Name("image_urls");
            Map(m => m.SupplierName).Name("supplier_name");
            Map(m => m.SpecificationsJson).Name("specifications_json");
        }
    }

    // ============================================
    // Interface
    // ============================================

    public interface ICsvExportService
    {
        Task<string> ExportProductsToCsvAsync(
            IEnumerable<StagingProduct> products,
            string outputPath);
    }
}
