using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;
using UglyToad.PdfPig;

namespace ScrapSAE.Infrastructure.Services;

public sealed class PdfAttachmentAnalyzer : IPdfAttachmentAnalyzer
{
    private static readonly Regex KeyValueRegex = new(
        @"^\s*([A-Za-zÁÉÍÓÚáéíóúÑñ0-9][^:]{1,60})\s*[:=]\s*(.{1,200})\s*$",
        RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PdfAttachmentAnalyzer> _logger;

    public PdfAttachmentAnalyzer(
        IHttpClientFactory httpClientFactory,
        ILogger<PdfAttachmentAnalyzer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<Dictionary<string, string>> ExtractSpecificationsAsync(
        IEnumerable<ProductAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pdfCandidates = attachments
            .Where(IsPdfAttachment)
            .DistinctBy(a => a.FileUrl)
            .Take(3)
            .ToList();

        if (pdfCandidates.Count == 0)
        {
            return result;
        }

        var client = _httpClientFactory.CreateClient("AttachmentAnalyzer");
        foreach (var attachment in pdfCandidates)
        {
            if (string.IsNullOrWhiteSpace(attachment.FileUrl))
            {
                continue;
            }

            try
            {
                using var response = await client.GetAsync(attachment.FileUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length == 0)
                {
                    continue;
                }

                using var stream = new MemoryStream(bytes);
                using var document = PdfDocument.Open(stream);
                var pages = document.GetPages().Take(4).ToList();
                foreach (var page in pages)
                {
                    var text = page.Text ?? string.Empty;
                    ExtractKeyValues(text, result);
                    if (result.Count >= 40)
                    {
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No se pudo analizar PDF {PdfUrl}", attachment.FileUrl);
            }
        }

        return result;
    }

    private static bool IsPdfAttachment(ProductAttachment attachment)
    {
        if (!string.IsNullOrWhiteSpace(attachment.FileType) &&
            attachment.FileType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(attachment.FileUrl) &&
               attachment.FileUrl.Contains(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static void ExtractKeyValues(string text, Dictionary<string, string> result)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var lines = text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Select(x => x.Trim())
            .Where(x => x.Length >= 4 && x.Length <= 260);

        foreach (var line in lines)
        {
            if (result.Count >= 40)
            {
                return;
            }

            var match = KeyValueRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var key = NormalizeSpecKey(match.Groups[1].Value);
            var value = match.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!result.ContainsKey(key))
            {
                result[key] = value;
            }
        }
    }

    private static string NormalizeSpecKey(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\s+", "_");
        normalized = Regex.Replace(normalized, @"[^a-z0-9_áéíóúñ]", string.Empty);
        return normalized.Trim('_');
    }
}
