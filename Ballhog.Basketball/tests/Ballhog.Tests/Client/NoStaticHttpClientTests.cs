using System.Net.Http;
using System.Reflection;
using Ballhog.Basketball;
using Xunit;

namespace Ballhog.Tests.Client;

/// <summary>
/// Architecture guard for the no-static-HttpClient invariant (requirement R2, Jira BALLHOG-9,
/// chunk C-003; test T-006, invariant I-001). The library must obtain its transport through
/// <c>IHttpClientFactory</c> in DI, so no type in the Ballhog assembly may hold a
/// <c>static</c> field or <c>static</c> property whose type is assignable to
/// <see cref="HttpMessageInvoker"/> (the base class of <see cref="HttpClient"/>).
/// </summary>
/// <remarks>
/// The scan is a reflection sweep over every type (public and internal) in
/// <see cref="Team"/>'s assembly. It inspects declared static members only; instance
/// <see cref="HttpClient"/> fields are legitimate because they are supplied by the container.
/// </remarks>
public sealed class NoStaticHttpClientTests
{
    [Fact]
    public void Ballhog_library_declares_no_static_http_message_invoker()
    {
        Assembly assembly = typeof(Team).Assembly;
        Type invokerType = typeof(HttpMessageInvoker);

        const BindingFlags StaticDeclared =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var offenders = new List<string>();

        foreach (Type type in assembly.GetTypes())
        {
            foreach (FieldInfo field in type.GetFields(StaticDeclared))
            {
                if (invokerType.IsAssignableFrom(field.FieldType))
                {
                    offenders.Add($"{type.FullName}.{field.Name} ({field.FieldType})");
                }
            }

            foreach (PropertyInfo property in type.GetProperties(StaticDeclared))
            {
                if (invokerType.IsAssignableFrom(property.PropertyType))
                {
                    offenders.Add($"{type.FullName}.{property.Name} ({property.PropertyType})");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The Ballhog library must not hold a static/shared HTTP client; route transport through " +
            "IHttpClientFactory via DI. Offending members: " + string.Join(", ", offenders));
    }
}
