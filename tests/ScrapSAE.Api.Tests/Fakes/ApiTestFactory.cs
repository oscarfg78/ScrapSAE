using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ScrapSAE.Api.Services;
using ScrapSAE.Api.Tests.Stubs;
using ScrapSAE.Core.Interfaces;

namespace ScrapSAE.Api.Tests.Fakes;

public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    public FakeSupabaseRestClient SupabaseClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ISupabaseRestClient>(SupabaseClient);
            services.AddSingleton<ISaeSdkService, StubSaeSdkServiceSuccess>();
            services.AddSingleton<IScrapingService, StubScrapingService>();
        });
    }
}
