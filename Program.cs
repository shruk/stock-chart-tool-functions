using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StockChartFunctions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient();
        services.AddSingleton<SupabaseService>();
        services.AddSingleton<PolygonService>();
        services.AddSingleton<FinnhubService>();
        services.AddSingleton<FmpService>();
    })
    .Build();

host.Run();
