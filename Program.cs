using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockChartFunctions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker =>
    {
        worker.UseDefaultWorkerMiddleware();
    })
    .ConfigureServices(services =>
    {
        services.Configure<JsonSerializerOptions>(o =>
        {
            o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });
        services.AddHttpClient();
        services.AddSingleton<SupabaseService>();
        services.AddSingleton<PolygonService>();
        services.AddSingleton<FinnhubService>();
        services.AddSingleton<FmpService>();
        services.AddSingleton<StockFetchService>();
    })
    .Build();

host.Run();
