// ============================================================
// ScrapSAE.Api - Extension Endpoints
// Nuevos endpoints para la extensión de Chrome:
//   - /api/extension/process   (procesamiento con IA)
//   - /api/layouts             (CRUD de layouts)
//   - /api/stripe/*            (checkout y webhook)
// ============================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using ScrapSAE.Core.DTOs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Api.Endpoints;

public static class ExtensionEndpoints
{
    public static void MapExtensionEndpoints(this WebApplication app)
    {
        MapExtensionProcessEndpoint(app);
        MapLayoutEndpoints(app);
        MapStripeEndpoints(app);
    }

    // ============================================================
    // POST /api/extension/process
    // Recibe productos crudos de la extensión y los procesa con IA
    // ============================================================
    private static void MapExtensionProcessEndpoint(WebApplication app)
    {
        app.MapPost("/api/extension/process", async (
            ExtensionProcessRequest request,
            IAIProcessorService aiProcessor,
            ILogger<Program> logger,
            CancellationToken token) =>
        {
            if (request.Products == null || request.Products.Count == 0)
            {
                return Results.BadRequest(new { error = "No se proporcionaron productos para procesar." });
            }

            logger.LogInformation("[Extension] Procesando {Count} productos con IA", request.Products.Count);

            try
            {
                var processedProducts = new List<ProcessedProduct>();

                foreach (var scraped in request.Products)
                {
                    try
                    {
                        var processed = await aiProcessor.ProcessProductAsync(scraped, token);
                        if (processed != null)
                        {
                            processedProducts.Add(processed);
                        }
                        else
                        {
                            // Fallback: convertir sin IA
                            processedProducts.Add(ConvertRawToProcessed(scraped));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "[Extension] Error procesando producto {Sku}, usando fallback", scraped.SkuSource);
                        processedProducts.Add(ConvertRawToProcessed(scraped));
                    }
                }

                logger.LogInformation("[Extension] Procesamiento completado: {Count} productos", processedProducts.Count);
                return Results.Ok(processedProducts);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Extension] Error general en procesamiento");
                return Results.Problem("Error al procesar los productos: " + ex.Message);
            }
        })
        .WithName("ExtensionProcess")
        .WithTags("Extension")
        .Produces<List<ProcessedProduct>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    // ============================================================
    // /api/layouts - CRUD de layouts del usuario
    // ============================================================
    private static void MapLayoutEndpoints(WebApplication app)
    {
        // GET /api/layouts?userId={userId}
        app.MapGet("/api/layouts", async (
            string userId,
            ISupabaseRestClient supabase,
            ILogger<Program> logger) =>
        {
            try
            {
                var response = await supabase.GetAsync(
                    "user_layouts",
                    $"user_id=eq.{userId}&order=created_at.desc");

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("[Extension] Error fetching layouts: {Error}", error);
                    return Results.Problem("Error al obtener layouts: " + error);
                }

                var json = await response.Content.ReadAsStringAsync();
                return Results.Content(json, "application/json");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Extension] Error en GET /api/layouts");
                return Results.Problem(ex.Message);
            }
        })
        .WithName("GetLayouts")
        .WithTags("Extension");

        // POST /api/layouts
        app.MapPost("/api/layouts", async (
            UserLayoutDto layout,
            ISupabaseRestClient supabase,
            ILogger<Program> logger) =>
        {
            try
            {
                layout.UpdatedAt = DateTime.UtcNow;
                if (string.IsNullOrEmpty(layout.Id))
                {
                    layout.Id = Guid.NewGuid().ToString();
                    layout.CreatedAt = DateTime.UtcNow;
                }

                var json = JsonSerializer.Serialize(layout, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                var response = await supabase.PostAsync("user_layouts", json);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("[Extension] Error saving layout: {Error}", error);
                    return Results.Problem("Error al guardar layout: " + error);
                }

                var result = await response.Content.ReadAsStringAsync();
                return Results.Created($"/api/layouts/{layout.Id}", result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Extension] Error en POST /api/layouts");
                return Results.Problem(ex.Message);
            }
        })
        .WithName("SaveLayout")
        .WithTags("Extension");

        // DELETE /api/layouts/{id}
        app.MapDelete("/api/layouts/{id}", async (
            string id,
            ISupabaseRestClient supabase,
            ILogger<Program> logger) =>
        {
            try
            {
                var response = await supabase.DeleteAsync("user_layouts", $"id=eq.{id}");

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return Results.Problem("Error al eliminar layout: " + error);
                }

                return Results.NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Extension] Error en DELETE /api/layouts/{Id}", id);
                return Results.Problem(ex.Message);
            }
        })
        .WithName("DeleteLayout")
        .WithTags("Extension");
    }

    // ============================================================
    // /api/stripe/* - Integración con Stripe
    // ============================================================
    private static void MapStripeEndpoints(WebApplication app)
    {
        // POST /api/stripe/create-checkout
        app.MapPost("/api/stripe/create-checkout", async (
            StripeCheckoutRequest request,
            IConfiguration config,
            ILogger<Program> logger) =>
        {
            var stripeSecretKey = config["Stripe:SecretKey"];
            if (string.IsNullOrEmpty(stripeSecretKey))
            {
                return Results.Problem("Stripe no está configurado en el servidor.");
            }

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {stripeSecretKey}");

                var priceId = request.PlanType switch
                {
                    "pro" => config["Stripe:ProPriceId"],
                    "enterprise" => config["Stripe:EnterprisePriceId"],
                    _ => null
                };

                if (string.IsNullOrEmpty(priceId))
                {
                    return Results.BadRequest(new { error = "Plan no válido." });
                }

                var formData = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["mode"] = "subscription",
                    ["success_url"] = $"{config["Web:BaseUrl"]}/success?session_id={{CHECKOUT_SESSION_ID}}",
                    ["cancel_url"] = $"{config["Web:BaseUrl"]}/pricing",
                    ["customer_email"] = request.Email ?? "",
                    ["line_items[0][price]"] = priceId,
                    ["line_items[0][quantity]"] = "1",
                    ["metadata[user_id]"] = request.UserId ?? "",
                    ["metadata[plan_type]"] = request.PlanType ?? "",
                });

                var response = await httpClient.PostAsync(
                    "https://api.stripe.com/v1/checkout/sessions",
                    formData);

                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("[Stripe] Error creating checkout: {Response}", responseJson);
                    return Results.Problem("Error al crear sesión de pago: " + responseJson);
                }

                var doc = JsonDocument.Parse(responseJson);
                var checkoutUrl = doc.RootElement.GetProperty("url").GetString();
                var sessionId = doc.RootElement.GetProperty("id").GetString();

                return Results.Ok(new
                {
                    checkoutUrl,
                    sessionId
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Stripe] Error en create-checkout");
                return Results.Problem(ex.Message);
            }
        })
        .WithName("StripeCreateCheckout")
        .WithTags("Stripe");

        // POST /api/stripe/webhook
        app.MapPost("/api/stripe/webhook", async (
            HttpRequest httpRequest,
            IConfiguration config,
            ISupabaseRestClient supabase,
            ILogger<Program> logger) =>
        {
            var webhookSecret = config["Stripe:WebhookSecret"];
            if (string.IsNullOrEmpty(webhookSecret))
            {
                return Results.Problem("Webhook secret no configurado.");
            }

            try
            {
                var body = await new StreamReader(httpRequest.Body).ReadToEndAsync();
                var signature = httpRequest.Headers["Stripe-Signature"].FirstOrDefault();

                // Nota: En producción, verificar la firma con Stripe SDK.
                // Para esta implementación, se procesa el evento directamente.
                // Se recomienda instalar Stripe.net y usar EventUtility.ConstructEvent()

                var eventDoc = JsonDocument.Parse(body);
                var eventType = eventDoc.RootElement.GetProperty("type").GetString();

                logger.LogInformation("[Stripe] Webhook recibido: {EventType}", eventType);

                switch (eventType)
                {
                    case "checkout.session.completed":
                        await HandleCheckoutCompleted(eventDoc, supabase, logger);
                        break;

                    case "customer.subscription.updated":
                        await HandleSubscriptionUpdated(eventDoc, supabase, logger);
                        break;

                    case "customer.subscription.deleted":
                        await HandleSubscriptionDeleted(eventDoc, supabase, logger);
                        break;

                    default:
                        logger.LogInformation("[Stripe] Evento no manejado: {EventType}", eventType);
                        break;
                }

                return Results.Ok(new { received = true });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Stripe] Error procesando webhook");
                return Results.Problem(ex.Message);
            }
        })
        .WithName("StripeWebhook")
        .WithTags("Stripe");

        // POST /api/stripe/portal
        app.MapPost("/api/stripe/portal", async (
            StripePortalRequest request,
            IConfiguration config,
            ILogger<Program> logger) =>
        {
            var stripeSecretKey = config["Stripe:SecretKey"];
            if (string.IsNullOrEmpty(stripeSecretKey))
            {
                return Results.Problem("Stripe no está configurado.");
            }

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {stripeSecretKey}");

                var formData = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["customer"] = request.StripeCustomerId ?? "",
                    ["return_url"] = $"{config["Web:BaseUrl"]}/account",
                });

                var response = await httpClient.PostAsync(
                    "https://api.stripe.com/v1/billing_portal/sessions",
                    formData);

                var responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return Results.Problem("Error al crear portal: " + responseJson);
                }

                var doc = JsonDocument.Parse(responseJson);
                var portalUrl = doc.RootElement.GetProperty("url").GetString();

                return Results.Ok(new { portalUrl });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Stripe] Error en portal");
                return Results.Problem(ex.Message);
            }
        })
        .WithName("StripePortal")
        .WithTags("Stripe");
    }

    // ============================================================
    // Stripe Webhook Handlers
    // ============================================================

    private static async Task HandleCheckoutCompleted(
        JsonDocument eventDoc,
        ISupabaseRestClient supabase,
        ILogger<Program> logger)
    {
        var session = eventDoc.RootElement.GetProperty("data").GetProperty("object");
        var userId = "";
        var planType = "pro";

        if (session.TryGetProperty("metadata", out var metadata))
        {
            userId = metadata.TryGetProperty("user_id", out var uid) ? uid.GetString() ?? "" : "";
            planType = metadata.TryGetProperty("plan_type", out var pt) ? pt.GetString() ?? "pro" : "pro";
        }

        var customerId = session.TryGetProperty("customer", out var cid) ? cid.GetString() : null;

        if (!string.IsNullOrEmpty(userId))
        {
            var updateJson = JsonSerializer.Serialize(new
            {
                stripe_customer_id = customerId,
                subscription_status = planType,
                plan_type = planType,
                updated_at = DateTime.UtcNow
            });

            var response = await supabase.PatchAsync(
                "user_profiles",
                $"id=eq.{userId}",
                updateJson);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("[Stripe] Usuario {UserId} actualizado a plan {Plan}", userId, planType);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogWarning("[Stripe] Error actualizando perfil: {Error}", error);
            }
        }
    }

    private static async Task HandleSubscriptionUpdated(
        JsonDocument eventDoc,
        ISupabaseRestClient supabase,
        ILogger<Program> logger)
    {
        var subscription = eventDoc.RootElement.GetProperty("data").GetProperty("object");
        var customerId = subscription.TryGetProperty("customer", out var cid) ? cid.GetString() : null;
        var status = subscription.TryGetProperty("status", out var st) ? st.GetString() : null;

        if (!string.IsNullOrEmpty(customerId))
        {
            var planType = status == "active" ? "pro" : "free";

            // Intentar obtener el plan del price
            if (subscription.TryGetProperty("items", out var items) &&
                items.TryGetProperty("data", out var itemsData) &&
                itemsData.GetArrayLength() > 0)
            {
                var priceId = itemsData[0].GetProperty("price").GetProperty("id").GetString();
                // Mapear price ID a plan type (se configura en appsettings)
            }

            var updateJson = JsonSerializer.Serialize(new
            {
                subscription_status = planType,
                plan_type = planType,
                updated_at = DateTime.UtcNow
            });

            await supabase.PatchAsync(
                "user_profiles",
                $"stripe_customer_id=eq.{customerId}",
                updateJson);

            logger.LogInformation("[Stripe] Suscripción actualizada para customer {CustomerId}: {Status}", customerId, status);
        }
    }

    private static async Task HandleSubscriptionDeleted(
        JsonDocument eventDoc,
        ISupabaseRestClient supabase,
        ILogger<Program> logger)
    {
        var subscription = eventDoc.RootElement.GetProperty("data").GetProperty("object");
        var customerId = subscription.TryGetProperty("customer", out var cid) ? cid.GetString() : null;

        if (!string.IsNullOrEmpty(customerId))
        {
            var updateJson = JsonSerializer.Serialize(new
            {
                subscription_status = "free",
                plan_type = "free",
                updated_at = DateTime.UtcNow
            });

            await supabase.PatchAsync(
                "user_profiles",
                $"stripe_customer_id=eq.{customerId}",
                updateJson);

            logger.LogInformation("[Stripe] Suscripción cancelada para customer {CustomerId}", customerId);
        }
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static ProcessedProduct ConvertRawToProcessed(ScrapedProduct raw)
    {
        return new ProcessedProduct
        {
            Sku = raw.SkuSource,
            Name = raw.Title ?? "Sin nombre",
            Brand = raw.Brand,
            Description = raw.Description ?? "",
            Features = new List<string>(),
            Specifications = raw.Attributes ?? new Dictionary<string, string>(),
            SuggestedCategory = raw.Category,
            Categories = raw.Category != null ? new List<string> { raw.Category } : new List<string>(),
            Price = raw.Price,
            Images = raw.ImageUrls ?? new List<string>(),
            Attachments = raw.Attachments ?? new List<ProductAttachment>(),
        };
    }
}

// ============================================================
// Request/Response DTOs para los endpoints de extensión
// ============================================================

public class ExtensionProcessRequest
{
    [JsonPropertyName("products")]
    public List<ScrapedProduct> Products { get; set; } = new();
}

public class UserLayoutDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("selectors")]
    public JsonElement? Selectors { get; set; }

    [JsonPropertyName("column_mapping")]
    public JsonElement? ColumnMapping { get; set; }

    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

public class StripeCheckoutRequest
{
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("planType")]
    public string? PlanType { get; set; }
}

public class StripePortalRequest
{
    [JsonPropertyName("stripeCustomerId")]
    public string? StripeCustomerId { get; set; }
}
