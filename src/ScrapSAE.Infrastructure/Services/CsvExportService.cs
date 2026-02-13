using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Services;

public class CsvExportService : ICsvExportService
{
    private readonly ILogger<CsvExportService> _logger;

    public CsvExportService(ILogger<CsvExportService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExportProductsToCsvAsync(IEnumerable<StagingProduct> products, string outputPath, CancellationToken cancellationToken = default)
    {
        var list = products.ToList();
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var records = list.Select(p =>
        {
            var dto = FlashlyProductMapper.ToFlashlyDto(p);
            return new ProductCsvRecord
            {
                SourceSku = dto.SourceSku,
                Name = dto.Name,
                Description = dto.Description,
                PurchasePrice = dto.PurchasePrice,
                Currency = dto.Currency,
                Categories = string.Join("|", dto.Categories),
                ProductUrl = dto.ProductUrl,
                ImageUrls = string.Join("|", dto.ImageUrls),
                SupplierName = dto.SupplierName,
                SpecificationsJson = dto.SpecificationsJson
            };
        }).ToList();

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = true,
            Encoding = Encoding.UTF8
        };

        await using var streamWriter = new StreamWriter(outputPath, false, Encoding.UTF8);
        await using var csvWriter = new CsvWriter(streamWriter, csvConfig);
        csvWriter.Context.RegisterClassMap<ProductCsvRecordMap>();
        await csvWriter.WriteRecordsAsync(records, cancellationToken);

        _logger.LogInformation("CSV export completed. {Count} records written to {Path}", records.Count, outputPath);
        return outputPath;
    }

    private sealed class ProductCsvRecordMap : ClassMap<ProductCsvRecord>
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
}
