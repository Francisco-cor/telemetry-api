using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Telemetry.Api.Middleware;

namespace Telemetry.Api.Swagger;

public sealed class AddCorrelationHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = CorrelationIdMiddleware.HeaderName,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Optional correlation id. If omitted, the server will generate one.",
            Schema = new OpenApiSchema { Type = "string" }
        });
    }
}
