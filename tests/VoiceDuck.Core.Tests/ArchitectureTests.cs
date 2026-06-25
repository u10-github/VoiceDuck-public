using NetArchTest.Rules;
using VoiceDuck.Core;

namespace VoiceDuck.Core.Tests;

public class ArchitectureTests
{
    private static readonly string ExtensionsNamespace = "VoiceDuck.Extensions";
    private static readonly string InfrastructureNamespace = "VoiceDuck.Infrastructure";
    private static readonly string AppNamespace = "VoiceDuck.App";

    [Fact]
    public void Core_should_not_depend_on_App()
    {
        var result = Types.InAssembly(typeof(IVoiceDuckCore).Assembly)
            .ShouldNot()
            .HaveDependencyOn(AppNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Core_should_not_depend_on_Extensions()
    {
        var result = Types.InAssembly(typeof(IVoiceDuckCore).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ExtensionsNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Core_should_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(IVoiceDuckCore).Assembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Core_should_not_depend_on_Windows_specific_namespaces()
    {
        var result = Types.InAssembly(typeof(IVoiceDuckCore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(["System.Windows", "System.Windows.Forms", "NAudio", "Microsoft.Win32"])
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
