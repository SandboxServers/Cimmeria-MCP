using CimmeriaMcp.Functions.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton<CimmeriaSearchService>();
builder.Services.AddSingleton<CimmeriaGraphService>();
builder.Services.AddSingleton<CimmeriaSummarizationService>();
builder.Services.AddSingleton<MetricsService>();
builder.Services.AddSingleton<SignalRBroadcastService>();
builder.Services.AddHttpClient();

builder.Build().Run();
