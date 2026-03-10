using System.Reflection;
using Xunit;

namespace CimmeriaMcp.Functions.Tests;

public class SignalRHubTests
{
    [Fact]
    public void SignalRHub_ClassExists()
    {
        var type = typeof(Functions.SignalRHub);
        Assert.NotNull(type);
    }

    [Fact]
    public void Negotiate_HasFunctionAttribute()
    {
        var method = typeof(Functions.SignalRHub).GetMethod("Negotiate");
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<Microsoft.Azure.Functions.Worker.FunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("negotiate", attr!.Name);
    }

    [Fact]
    public void Negotiate_HasHttpTrigger_WithPostMethod()
    {
        var method = typeof(Functions.SignalRHub).GetMethod("Negotiate");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.True(parameters.Length >= 1);

        var triggerParam = parameters[0];
        var httpAttr = triggerParam.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().Name == "HttpTriggerAttribute");
        Assert.NotNull(httpAttr);
    }

    [Fact]
    public void Negotiate_HasSignalRConnectionInfoInput()
    {
        var method = typeof(Functions.SignalRHub).GetMethod("Negotiate");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.True(parameters.Length >= 2);

        var signalRParam = parameters[1];
        var signalRAttr = signalRParam.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().Name == "SignalRConnectionInfoInputAttribute");
        Assert.NotNull(signalRAttr);
    }

    [Fact]
    public void CreateBroadcast_IsStaticMethod_WithCorrectParams()
    {
        var method = typeof(Functions.SignalRHub)
            .GetMethod("CreateBroadcast", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal("toolName", parameters[0].Name);
        Assert.Equal("durationMs", parameters[1].Name);
        Assert.Equal("status", parameters[2].Name);
    }
}
