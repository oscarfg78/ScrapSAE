using System.Runtime.CompilerServices;
using System.Text;
using ExcelDataReader;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Services;

/// <summary>
/// Servicio de ingesta de archivos Excel usando ExcelDataReader (streaming).
/// Lee fila a fila sin cargar el archivo completo en memoria.
/// Soporta .xlsx y .xls.
/// </summary>
public class ExcelIngestionService : IExcelIngestionService
{
    public ExcelIngestionService()
    {
        // Registro de codificación requerido por ExcelDataReader en .NET
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <inheritdoc/>
    public async Task<ExcelPreviewResult> PreviewAsync(
        string filePath,
        int maxPreviewRows = 10,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return new ExcelPreviewResult { ErrorMessage = $"Archivo no encontrado: {filePath}" };

                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = CreateReader(stream, filePath);

                if (reader == null)
                    return new ExcelPreviewResult { ErrorMessage = "Formato de archivo no soportado. Use .xlsx o .xls." };

                var result = new ExcelPreviewResult();
                bool headerRead = false;
                int rowCount = 0;
                int totalDataRows = 0;

                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!headerRead)
                    {
                        // Primera fila = cabeceras
                        result.ColumnHeaders = Enumerable.Range(0, reader.FieldCount)
                            .Select(i => GetCellString(reader, i) ?? $"Columna_{i + 1}")
                            .ToArray();
                        headerRead = true;
                        continue;
                    }

                    totalDataRows++;

                    if (rowCount < maxPreviewRows)
                    {
                        var row = Enumerable.Range(0, reader.FieldCount)
                            .Select(i => GetCellString(reader, i) ?? string.Empty)
                            .ToArray();
                        result.PreviewRows.Add(row);
                        rowCount++;
                    }
                }

                result.TotalRowCount = totalDataRows;
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ExcelPreviewResult { ErrorMessage = $"Error al leer el archivo: {ex.Message}" };
            }
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ExcelProductRecord> StreamRowsAsync(
        string filePath,
        ExcelColumnMapping mapping,
        int startRowIndex = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rows = await Task.Run(() => ReadRows(filePath, mapping, startRowIndex, cancellationToken), cancellationToken);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    private static List<ExcelProductRecord> ReadRows(
        string filePath,
        ExcelColumnMapping mapping,
        int startRowIndex,
        CancellationToken cancellationToken)
    {
        var records = new List<ExcelProductRecord>();

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = CreateReader(stream, filePath);

        if (reader == null) return records;

        bool headerSkipped = false;
        int dataRowIndex = 0;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!headerSkipped)
            {
                headerSkipped = true;
                continue;
            }

            if (dataRowIndex < startRowIndex)
            {
                dataRowIndex++;
                continue;
            }

            var sku = GetCellString(reader, mapping.SkuColumnIndex);
            if (string.IsNullOrWhiteSpace(sku))
            {
                dataRowIndex++;
                continue;
            }

            var costoStr = GetCellString(reader, mapping.CostoColumnIndex);
            decimal.TryParse(costoStr?.Replace(",", ".").Replace("$", "").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var costo);

            decimal? marginPct = null;
            if (mapping.MarginColumnIndex.HasValue)
            {
                var marginStr = GetCellString(reader, mapping.MarginColumnIndex.Value);
                if (decimal.TryParse(marginStr?.Replace("%", "").Replace(",", ".").Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedMargin))
                {
                    marginPct = parsedMargin;
                }
            }

            var optional = new Dictionary<string, string>();
            foreach (var (key, colIdx) in mapping.OptionalColumns)
            {
                var val = GetCellString(reader, colIdx);
                if (!string.IsNullOrWhiteSpace(val))
                    optional[key] = val;
            }

            if (mapping.CategoryColumnIndex.HasValue)
            {
                var categoryStr = GetCellString(reader, mapping.CategoryColumnIndex.Value);
                if (!string.IsNullOrWhiteSpace(categoryStr))
                {
                    if (categoryStr.Contains('/'))
                    {
                        var parts = categoryStr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (parts.Length > 0)
                            categoryStr = parts[0];
                    }
                    optional["Categoria"] = categoryStr.Trim();
                }
            }

            records.Add(new ExcelProductRecord
            {
                RowIndex = dataRowIndex,
                Sku = sku.Trim(),
                CostoProveedor = costo,
                MarginPercentage = marginPct,
                OptionalAttributes = optional
            });

            dataRowIndex++;
        }

        return records;
    }

    private static IExcelDataReader? CreateReader(Stream stream, string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".xlsx" => ExcelReaderFactory.CreateOpenXmlReader(stream),
                ".xls"  => ExcelReaderFactory.CreateBinaryReader(stream),
                _       => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? GetCellString(IExcelDataReader reader, int colIndex)
    {
        if (colIndex < 0 || colIndex >= reader.FieldCount) return null;
        if (reader.IsDBNull(colIndex)) return null;
        return reader.GetValue(colIndex)?.ToString();
    }
}
