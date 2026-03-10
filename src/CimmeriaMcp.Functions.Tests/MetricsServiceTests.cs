using System.Reflection;
using Xunit;

namespace CimmeriaMcp.Functions.Tests;

public class MetricsServiceTests
{
    [Fact]
    public void MetricsService_ClassExists()
    {
        var type = typeof(Services.MetricsService);
        Assert.NotNull(type);
    }

    [Fact]
    public void GetMetricsAsync_MethodExists_WithCorrectReturnType()
    {
        var method = typeof(Services.MetricsService).GetMethod("GetMetricsAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<object>), method!.ReturnType);
    }

    [Fact]
    public void CacheDurationSeconds_Is60()
    {
        var field = typeof(Services.MetricsService)
            .GetField("CacheDurationSeconds", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        Assert.NotNull(field);
        Assert.Equal(60, (int)field!.GetValue(null)!);
    }

    [Fact]
    public void Service_HasInternalConstructor_ForTesting()
    {
        var ctors = typeof(Services.MetricsService).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Contains(ctors, c => c.GetParameters().Length == 1);
    }
}
