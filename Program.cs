using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Diagnostics;
using System.Net.Http.Json;

var host = CreateHostBuilder(args).Build();
host.Run();
IHostBuilder CreateHostBuilder(string[] args)
{
    return Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureLogging((hostingContext, logging) =>
        {
            Log.Logger = new LoggerBuilder().Build(hostingContext.Configuration!).CreateLogger();
        })
        .ConfigureServices((hostBuilderContext, services) =>
        {
            services
                .AddTransient<SampleProvider1DelegatingHandler>()
                .AddTransient<SampleProvider2DelegatingHandler>()
                .AddHttpClient("provider1", httpClient =>
                {
                    // set your api url for this provider
                    httpClient.BaseAddress = new Uri("https://provider1.com");
                }).AddHttpMessageHandler<SampleProvider1DelegatingHandler>();

            services
                .AddTransient<SampleProvider2DelegatingHandler>()
                .AddHttpClient("provider2", httpClient =>
                {
                    // set your api url for this provider
                    httpClient.BaseAddress = new Uri("https://provider2.com");
                }).AddHttpMessageHandler<SampleProvider2DelegatingHandler>();

            services.TryAddEnumerable(ServiceDescriptor.Scoped<IBaseInformationApiProxyService, Provider1Proxy>());
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IBaseInformationApiProxyService, Provider2Proxy>());
            services.TryAddSingleton<IBaseInformationApiProxyServiceFactory, BaseInformationApiProxyServiceFactory>();

            services.AddHostedService<ServiceWorker>();
        });
}

internal sealed class ServiceWorker : BackgroundService
{
    private readonly IBaseInformationApiProxyServiceFactory _baseInformationApiProxyServiceFactory;
    private readonly ILogger<ServiceWorker> _logger;

    public ServiceWorker(IBaseInformationApiProxyServiceFactory baseInformationApiProxyServiceFactory, ILogger<ServiceWorker> logger)
    {
        this._baseInformationApiProxyServiceFactory = baseInformationApiProxyServiceFactory;
        this._logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var provider = "provider1";
        var proxy = this._baseInformationApiProxyServiceFactory.Create(provider);
        var response = await proxy.GetBaseInformation(stoppingToken).ConfigureAwait(false);

        this._logger.LogInformation(Serialize(response));

        provider = "provider2";
        proxy = this._baseInformationApiProxyServiceFactory.Create(provider);
        response = await proxy.GetBaseInformation(stoppingToken).ConfigureAwait(false);

        this._logger.LogInformation(Serialize(response));

        provider = "provider1";
        proxy = this._baseInformationApiProxyServiceFactory.Create(provider);
        response = await proxy.GetBaseInformation(stoppingToken).ConfigureAwait(false);

        this._logger.LogInformation(Serialize(response));
    }

    private static string Serialize(object? obj) => obj is null ? "" : System.Text.Json.JsonSerializer.Serialize(obj!);
}

public readonly record struct ApiResponseDTO(string Name);

/*
    This is a sample delegating handler that returns a list of applications. You must remove this delegating handler from the HTTP client pipeline when using a real API to retrieve the list of applications.
 */
public sealed class SampleProvider1DelegatingHandler : DelegatingHandler
{
    private readonly ILogger<SampleProvider1DelegatingHandler> _logger;

    public SampleProvider1DelegatingHandler(ILogger<SampleProvider1DelegatingHandler> logger)
    {
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this._logger.LogInformation("SampleProvider1DelegatingHandler");

        return Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new List<ApiResponseDTO>() { new() { Name =  "Provider1_Name1" }, new() { Name = "Provider1_Name2" } }), System.Text.Encoding.UTF8, "application/json")
            }
        );
    }
}

/*
    This is a sample delegating handler that returns a list of applications. You must remove this delegating handler from the HTTP client pipeline when using a real API to retrieve the list of applications.
 */
public sealed class SampleProvider2DelegatingHandler : DelegatingHandler
{
    private readonly ILogger<SampleProvider2DelegatingHandler> _logger;

    public SampleProvider2DelegatingHandler(ILogger<SampleProvider2DelegatingHandler> logger)
    {
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this._logger.LogInformation("SampleProvider2DelegatingHandler");

        return Task.FromResult(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new List<ApiResponseDTO>() { new() { Name = "Provider2_Name1" }, new() { Name = "Provider2_Name2" }, new() { Name = "Provider2_Name3" } }), System.Text.Encoding.UTF8, "application/json")
            }
        );
    }
}

public interface IBaseInformationApiProxyService
{
    string ProviderName { get; }
    Task<IEnumerable<ApiResponseDTO>> GetBaseInformation(CancellationToken stoppingToken);
}
public abstract class BaseInformationApiProxy : IBaseInformationApiProxyService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BaseInformationApiProxy> _logger;

    public abstract string ProviderName { get; }
    public abstract string BaseInformationEndpoint { get; }

    public BaseInformationApiProxy(IHttpClientFactory httpClientFactory, ILogger<BaseInformationApiProxy> logger )
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IEnumerable<ApiResponseDTO>> GetBaseInformation(CancellationToken stoppingToken)
    {
        using var client = this._httpClientFactory.CreateClient(ProviderName);
        return (await client.GetFromJsonAsync<IEnumerable<ApiResponseDTO>>(this.BaseInformationEndpoint, stoppingToken).ConfigureAwait(false)) ?? Enumerable.Empty<ApiResponseDTO>();
    }
}
public sealed class Provider1Proxy : BaseInformationApiProxy, IBaseInformationApiProxyService
{
    public override string ProviderName => "provider1";
    public override string BaseInformationEndpoint => "endpoint1";
    
    public Provider1Proxy(IHttpClientFactory httpClientFactory, ILogger<BaseInformationApiProxy> logger) : base(httpClientFactory, logger){ }
}
public sealed class Provider2Proxy : BaseInformationApiProxy, IBaseInformationApiProxyService
{
    public override string ProviderName => "provider2";
    public override string BaseInformationEndpoint => "endpoint2";

    public Provider2Proxy(IHttpClientFactory httpClientFactory, ILogger<BaseInformationApiProxy> logger) : base(httpClientFactory, logger) { }
}

public interface IBaseInformationApiProxyServiceFactory
{
    IBaseInformationApiProxyService Create(string providerName);
}
public sealed class BaseInformationApiProxyServiceFactory : IBaseInformationApiProxyServiceFactory
{
    private static Dictionary<string, IBaseInformationApiProxyService> ResolvedProxies = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly IEnumerable<IBaseInformationApiProxyService> _proxyServices;

    public BaseInformationApiProxyServiceFactory(IEnumerable<IBaseInformationApiProxyService> proxyServices)
    {
        this._proxyServices = proxyServices;
    }
    
    public IBaseInformationApiProxyService Create(string providerName)
    {
        if (ResolvedProxies.TryGetValue(providerName, out var r)) return r;

        var result = this._proxyServices.FirstOrDefault(x => x.ProviderName.Equals(providerName, StringComparison.InvariantCultureIgnoreCase));
        if (result is null) throw new UnreachableException($"invalid providername : \"{providerName}\"");

        ResolvedProxies.Add(providerName, result);
        return result;

    }
}