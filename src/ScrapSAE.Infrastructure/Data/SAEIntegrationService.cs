using System.Data;
using System.Data.Odbc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using ScrapSAE.Core.Entities;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Infrastructure.Data;

/// <summary>
/// Servicio de integración con Aspel SAE vía ODBC
/// </summary>
public class SAEIntegrationService : ISAEIntegrationService
{
    private readonly string _connectionString;
    private readonly ILogger<SAEIntegrationService> _logger;
    private readonly int _timeout;

    public SAEIntegrationService(IConfiguration configuration, ILogger<SAEIntegrationService> logger)
    {
        _connectionString = configuration["SAE:ConnectionString"] 
            ?? throw new ArgumentNullException("SAE:ConnectionString not configured");
        _timeout = configuration.GetValue<int>("SAE:Timeout", 30);
        _logger = logger;
    }

    private IDbConnection CreateConnection()
    {
        var connection = new OdbcConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var connection = CreateConnection();
            var result = await connection.ExecuteScalarAsync<int>("SELECT 1");
            return result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing SAE connection");
            return false;
        }
    }

    public async Task<IEnumerable<ProductSAE>> GetAllProductsAsync()
    {
        try
        {
            using var connection = CreateConnection();
            const string sql = @"
                SELECT CVE_ART, DESCR, LIN_PROD, EXIST, PREC_X_MAY, PREC_X_MEN, 
                       ULT_COSTO, CTRL_ALM, STATUS, FCH_ULTCOM, FCH_ULTVTA,
                       CAMPO_LIBRE1, CAMPO_LIBRE2, CAMPO_LIBRE3
                FROM INVE01
                WHERE STATUS = 'A'";
            
            return await connection.QueryAsync<ProductSAE>(sql, commandTimeout: _timeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all products from SAE");
            throw;
        }
    }

    public async Task<ProductSAE?> GetProductBySkuAsync(string sku)
    {
        try
        {
            using var connection = CreateConnection();
            const string sql = @"
                SELECT CVE_ART, DESCR, LIN_PROD, EXIST, PREC_X_MAY, PREC_X_MEN, 
                       ULT_COSTO, CTRL_ALM, STATUS, FCH_ULTCOM, FCH_ULTVTA,
                       CAMPO_LIBRE1, CAMPO_LIBRE2, CAMPO_LIBRE3
                FROM INVE01
                WHERE CVE_ART = ?";
            
            return await connection.QueryFirstOrDefaultAsync<ProductSAE>(sql, new { sku }, commandTimeout: _timeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product {Sku} from SAE", sku);
            throw;
        }
    }

    public async Task<IEnumerable<ProductLine>> GetProductLinesAsync()
    {
        try
        {
            using var connection = CreateConnection();
            const string sql = "SELECT CVE_LIN, DESC_LIN FROM CLIN01";
            
            return await connection.QueryAsync<ProductLine>(sql, commandTimeout: _timeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product lines from SAE");
            throw;
        }
    }

    public async Task<bool> UpdateProductAsync(ProductUpdate product)
    {
        try
        {
            using var connection = CreateConnection();
            const string sql = @"
                UPDATE INVE01 
                SET DESCR = ?, EXIST = ?, PREC_X_MAY = ?, PREC_X_MEN = ?
                WHERE CVE_ART = ?";
            
            var affected = await connection.ExecuteAsync(sql, new 
            { 
                product.DESCR, 
                product.EXIST, 
                product.PREC_X_MAY, 
                product.PREC_X_MEN,
                product.CVE_ART 
            }, commandTimeout: _timeout);
            
            return affected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product {Sku} in SAE", product.CVE_ART);
            throw;
        }
    }

    public async Task<bool> CreateProductAsync(ProductCreate product)
    {
        try
        {
            using var connection = CreateConnection();
            const string sql = @"
                INSERT INTO INVE01 (CVE_ART, DESCR, LIN_PROD, EXIST, PREC_X_MAY, PREC_X_MEN, ULT_COSTO, STATUS)
                VALUES (?, ?, ?, ?, ?, ?, ?, 'A')";
            
            var affected = await connection.ExecuteAsync(sql, new 
            { 
                product.CVE_ART,
                product.DESCR, 
                product.LIN_PROD,
                product.EXIST, 
                product.PREC_X_MAY, 
                product.PREC_X_MEN,
                product.ULT_COSTO
            }, commandTimeout: _timeout);
            
            return affected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating product {Sku} in SAE", product.CVE_ART);
            throw;
        }
    }

    public async Task<bool> UpdateStockAsync(string sku, decimal quantity)
    {
        try
        {
            using var connection = CreateConnection();
            const string sql = "UPDATE INVE01 SET EXIST = ? WHERE CVE_ART = ?";
            
            var affected = await connection.ExecuteAsync(sql, new { quantity, sku }, commandTimeout: _timeout);
            return affected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating stock for {Sku} in SAE", sku);
            throw;
        }
    }

    public async Task<bool> UpdatePriceAsync(string sku, decimal price)
    {
        try
        {
            using var connection = CreateConnection();
            const string sql = "UPDATE INVE01 SET PREC_X_MEN = ? WHERE CVE_ART = ?";
            
            var affected = await connection.ExecuteAsync(sql, new { price, sku }, commandTimeout: _timeout);
            return affected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating price for {Sku} in SAE", sku);
            throw;
        }
    }

    public async Task<bool> ValidateSkuExistsAsync(string sku)
    {
        try
        {
            using var connection = CreateConnection();
            const string sql = "SELECT COUNT(*) FROM INVE01 WHERE CVE_ART = ?";
            
            var count = await connection.ExecuteScalarAsync<int>(sql, new { sku }, commandTimeout: _timeout);
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating SKU {Sku} in SAE", sku);
            throw;
        }
    }
}
