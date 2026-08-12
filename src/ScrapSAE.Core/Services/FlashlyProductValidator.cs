using System;
using System.Collections.Generic;
using ScrapSAE.Core.DTOs;

namespace ScrapSAE.Core.Services;

public class FlashlyProductValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; set; } = new();
    public string Summary => string.Join("; ", Errors);
}

public interface IFlashlyProductValidator
{
    FlashlyProductValidationResult Validate(FlashlyProductSyncPayload payload);
}

public class FlashlyProductValidator : IFlashlyProductValidator
{
    public FlashlyProductValidationResult Validate(FlashlyProductSyncPayload payload)
    {
        var result = new FlashlyProductValidationResult();

        if (payload == null)
        {
            result.Errors.Add("El objeto de producto es nulo.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(payload.SourceSku))
        {
            result.Errors.Add("SKU de origen (source_sku) es requerido.");
        }

        if (string.IsNullOrWhiteSpace(payload.Name))
        {
            result.Errors.Add("El nombre del producto es requerido.");
        }

        if (payload.PurchasePrice < 0)
        {
            result.Errors.Add($"El precio de compra no puede ser negativo ({payload.PurchasePrice}).");
        }

        if (string.IsNullOrWhiteSpace(payload.Currency) || payload.Currency.Trim().Length != 3)
        {
            result.Errors.Add("El código de moneda debe tener exactamente 3 caracteres (ej. MXN).");
        }

        if (!string.IsNullOrWhiteSpace(payload.ProductUrl) &&
            !Uri.TryCreate(payload.ProductUrl, UriKind.RelativeOrAbsolute, out _))
        {
            result.Errors.Add($"La URL del producto es inválida ({payload.ProductUrl}).");
        }

        if (payload.ImageUrls != null)
        {
            foreach (var imgUrl in payload.ImageUrls)
            {
                if (!string.IsNullOrWhiteSpace(imgUrl) &&
                    !Uri.TryCreate(imgUrl, UriKind.RelativeOrAbsolute, out _))
                {
                    result.Errors.Add($"URL de imagen inválida ({imgUrl}).");
                }
            }
        }

        return result;
    }
}
