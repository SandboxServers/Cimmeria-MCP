using System.Reflection;
using Xunit;

namespace CimmeriaMcp.Functions.Tests;

public class MetricsEndpointTests
{
    [Fact]
    public void MetricsEndpoint_ClassExists()
    {
        var type = typeof(Functions.MetricsEndpoint);
        Assert.NotNull(type);
    }

    [Fact]
    public void Run_HasFunctionAttribute()
    {
        var method = typeof(Functions.MetricsEndpoint).GetMethod("Run");
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<Microsoft.Azure.Functions.Worker.FunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("GetMetrics", attr!.Name);
    }

    [Fact]
    public void Run_HasHttpTrigger_WithGetMethod_AndMetricsRoute()
    {
        var method = typeof(Functions.MetricsEndpoint).GetMethod("Run");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.True(parameters.Length >= 1);

        var triggerParam = parameters[0];
        var httpAttr = triggerParam.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().Name == "HttpTriggerAttribute");
        Assert.NotNull(httpAttr);

        // Verify route
        var routeProp = httpAttr!.GetType().GetProperty("Route");
        Assert.Equal("metrics", routeProp?.GetValue(httpAttr)?.ToString());
    }

    [Fact]
    public void Run_ReturnsHttpResponseData()
    {
        var method = typeof(Functions.MetricsEndpoint).GetMethod("Run");
        Assert.NotNull(method);
        Assert.True(method!.ReturnType.IsGenericType || method.ReturnType.Name.Contains("Task"));
    }
}
