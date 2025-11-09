using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Telemetry.Api.Swagger;

public sealed class AddTooManyRequestsResponseOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Solo agrega si el endpoint no es /health
        if (operation?.Responses is null) return;
        if (context.ApiDescription.RelativePath?.StartsWith("health", StringComparison.OrdinalIgnoreCase) == true)
            return;

        if (!operation.Responses.ContainsKey("429"))
            operation.Responses.Add("429", new OpenApiResponse { Description = "Too Many Requests" });
    }
}
