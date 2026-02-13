using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Api.Services;

/// <summary>
/// Implementación de IStagingService para el API que utiliza SupabaseTableService
/// </summary>
public sealed class ApiStagingService : IStagingService
{
    private readonly SupabaseTableService<StagingProduct> _productsTable;
    private readonly SupabaseTableService<SiteProfile> _sitesTable;

    public ApiStagingService(
        SupabaseTableService<StagingProduct> productsTable,
        SupabaseTableService<SiteProfile> sitesTable)
    {
        _productsTable = productsTable;
        _sitesTable = sitesTable;
    }

    public async Task<StagingProduct> CreateProductAsync(StagingProduct product)
    {
        if (product.Id == Guid.Empty) product.Id = Guid.NewGuid();
        product.CreatedAt = DateTime.UtcNow;
        product.UpdatedAt = DateTime.UtcNow;
        
        var created = await _productsTable.CreateAsync(product);
        return created ?? product;
    }

    public async Task<StagingProduct> UpsertProductAsync(StagingProduct product)
    {
        var existing = await GetProductBySourceSkuAsync(product.SiteId, product.SkuSource ?? "");
        if (existing != null)
        {
            existing.RawData = product.RawData;
            existing.AIProcessedJson = product.AIProcessedJson;
            existing.SourceUrl = product.SourceUrl;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.Status = product.Status; // Mantener estado o resetear a pending? User dijo "actualizar"
            await _productsTable.UpdateAsync(existing.Id, existing);
            return existing;
        }

        return await CreateProductAsync(product);
    }

    public async Task<StagingProduct?> GetProductBySourceSkuAsync(Guid siteId, string skuSource)
    {
        var all = await _productsTable.GetAllAsync();
        // Nota: Idealmente SupabaseTableService debería admitir filtros más complejos, 
        // pero por ahora seguimos el patrón del proyecto.
        return all.FirstOrDefault(p => p.SiteId == siteId && p.SkuSource == skuSource);
    }

    public async Task<IEnumerable<StagingProduct>> GetPendingProductsAsync()
    {
        var all = await _productsTable.GetAllAsync();
        return all.Where(p => p.Status == "pending");
    }

    public async Task<IEnumerable<StagingProduct>> GetProductsByStatusAsync(string status)
    {
        var all = await _productsTable.GetAllAsync();
        return all.Where(p => string.Equals(p.Status, status, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpdateProductStatusAsync(Guid id, string status, string? notes = null)
    {
        var product = await _productsTable.GetByIdAsync(id);
        if (product != null)
        {
            product.Status = status;
            product.ValidationNotes = notes;
            product.UpdatedAt = DateTime.UtcNow;
            await _productsTable.UpdateAsync(id, product);
        }
    }

    public async Task UpdateProductsStatusAsync(IEnumerable<Guid> ids, string status, string? notes = null)
    {
        var idSet = ids.Where(id => id != Guid.Empty).ToHashSet();
        if (idSet.Count == 0)
        {
            return;
        }

        var all = await _productsTable.GetAllAsync();
        foreach (var product in all.Where(p => idSet.Contains(p.Id)))
        {
            product.Status = status;
            product.ValidationNotes = notes;
            product.UpdatedAt = DateTime.UtcNow;
            await _productsTable.UpdateAsync(product.Id, product);
        }
    }

    public async Task UpdateFlashlySyncInfoAsync(Guid id, string syncStatus, Guid? flashlyProductId, DateTime? syncedAt, string? notes = null)
    {
        var product = await _productsTable.GetByIdAsync(id);
        if (product != null)
        {
            product.FlashlySyncStatus = syncStatus;
            product.FlashlyProductId = flashlyProductId;
            product.FlashlySyncedAt = syncedAt;
            product.ValidationNotes = notes;
            product.UpdatedAt = DateTime.UtcNow;
            await _productsTable.UpdateAsync(id, product);
        }
    }

    public async Task UpdateProductDataAsync(Guid id, string aiProcessedJson)
    {
        var product = await _productsTable.GetByIdAsync(id);
        if (product != null)
        {
            product.AIProcessedJson = aiProcessedJson;
            product.UpdatedAt = DateTime.UtcNow;
            await _productsTable.UpdateAsync(id, product);
        }
    }

    public async Task<IEnumerable<SiteProfile>> GetActiveSitesAsync()
    {
        var all = await _sitesTable.GetAllAsync();
        return all.Where(s => s.IsActive);
    }
}
