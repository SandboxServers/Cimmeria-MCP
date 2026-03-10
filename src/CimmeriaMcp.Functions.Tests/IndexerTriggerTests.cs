using System.Reflection;
using Xunit;

namespace CimmeriaMcp.Functions.Tests;

public class IndexerTriggerTests
{
    [Fact]
    public void IndexerTrigger_ClassExists()
    {
        var type = typeof(Functions.IndexerTrigger);
        Assert.NotNull(type);
    }

    [Fact]
    public void Run_HasFunctionAttribute()
    {
        var method = typeof(Functions.IndexerTrigger).GetMethod("Run");
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<Microsoft.Azure.Functions.Worker.FunctionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("IndexerTrigger", attr!.Name);
    }

    [Fact]
    public void Run_HasCosmosDBTrigger_OnCodeChunksContainer()
    {
        var method = typeof(Functions.IndexerTrigger).GetMethod("Run");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.True(parameters.Length >= 1, "Run should have at least one parameter");

        var triggerParam = parameters[0];
        var cosmosAttr = triggerParam.GetCustomAttributes()
            .FirstOrDefault(a => a.GetType().Name == "CosmosDBTriggerAttribute");
        Assert.NotNull(cosmosAttr);

        // Verify it targets the code-chunks container
        var containerProp = cosmosAttr!.GetType().GetProperty("ContainerName");
        Assert.Equal("code-chunks", containerProp?.GetValue(cosmosAttr)?.ToString());

        // Verify lease container
        var leaseProp = cosmosAttr.GetType().GetProperty("LeaseContainerName");
        Assert.Equal("leases", leaseProp?.GetValue(cosmosAttr)?.ToString());
    }

    [Fact]
    public void DebounceDelay_Is30Seconds()
    {
        var field = typeof(Functions.IndexerTrigger)
            .GetField("DebounceDelay", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var delay = (TimeSpan)field!.GetValue(null)!;
        Assert.Equal(TimeSpan.FromSeconds(30), delay);
    }
}
