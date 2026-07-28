using System;
using System.Collections.Generic;
using System.Linq;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Scraping.Pipeline;

public class ReconciliationEngine : IReconciliationEngine
{
    public List<ReconciledProduct> Reconcile(List<ProductObservation> observations)
    {
        var reconciledProducts = new List<ReconciledProduct>();
        
        // Group by SourceUrl for now as an identity approximation
        var groups = observations.GroupBy(o => o.SourceUrl);

        foreach (var group in groups)
        {
            var product = new ReconciledProduct { SourceUrl = group.Key };

            foreach (var obs in group)
            {
                if (string.IsNullOrWhiteSpace(obs.Field)) continue;
                
                product.FieldProvenance[obs.Field] = obs;

                switch (obs.Field.ToLowerInvariant())
                {
                    case "title":
                    case "name":
                        product.Title = obs.NormalizedValue ?? obs.RawValue;
                        break;
                    case "sku":
                        product.Sku = obs.NormalizedValue ?? obs.RawValue;
                        break;
                    case "price":
                        if (decimal.TryParse(obs.NormalizedValue ?? obs.RawValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
                        {
                            product.Price = price;
                        }
                        break;
                    case "image":
                    case "imageurl":
                    case "img":
                        var imgUrl = obs.NormalizedValue ?? obs.RawValue;
                        if (!string.IsNullOrWhiteSpace(imgUrl))
                        {
                            product.ImageUrl ??= imgUrl;
                            if (!product.ImageUrls.Contains(imgUrl))
                            {
                                product.ImageUrls.Add(imgUrl);
                            }
                        }
                        break;
                    case "description":
                    case "characteristics":
                        var desc = obs.NormalizedValue ?? obs.RawValue;
                        if (!string.IsNullOrWhiteSpace(desc))
                        {
                            product.FieldProvenance["description"] = obs;
                        }
                        break;
                }
            }

            reconciledProducts.Add(product);
        }

        return reconciledProducts;
    }

    public QualityGateEvaluation EvaluateQualityGate(List<ReconciledProduct> products)
    {
        var eval = new QualityGateEvaluation { Result = QualityGateResult.Pass };
        
        if (!products.Any())
        {
            eval.Result = QualityGateResult.Fail;
            eval.Reasons.Add("No products reconciled.");
            return eval;
        }

        foreach (var p in products)
        {
            if (string.IsNullOrWhiteSpace(p.Title) && string.IsNullOrWhiteSpace(p.Sku))
            {
                eval.Result = QualityGateResult.Fail;
                eval.Reasons.Add($"Product at {p.SourceUrl} missing both Title and Sku.");
            }
        }

        return eval;
    }
}
