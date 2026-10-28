using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Portarr.Api.V1;

public static class ApiV1ServiceCollectionExtensions
{
    /// <summary>Controllers and input validators of <c>/api/v1</c>.</summary>
    public static IMvcBuilder AddPortarrApiV1(this IMvcBuilder mvc)
    {
        mvc.Services.AddValidatorsFromAssemblyContaining(typeof(ApiV1ServiceCollectionExtensions), ServiceLifetime.Singleton);

        return mvc.AddApplicationPart(typeof(ApiV1ServiceCollectionExtensions).Assembly);
    }
}
