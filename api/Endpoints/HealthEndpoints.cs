namespace AvatarDocReader.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new
        {
            ok = true,
            name = "Avatar Knowledge Room"
        }));

        return app;
    }
}
