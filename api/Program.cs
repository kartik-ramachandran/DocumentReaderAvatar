using AvatarDocReader.Api.Endpoints;
using AvatarDocReader.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://localhost:5174",
                "http://127.0.0.1:5174",
                "http://localhost:5175",
                "http://127.0.0.1:5175")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddHttpClient();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<KnowledgeStore>();
builder.Services.AddSingleton<AnswerService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();

app
    .MapHealthEndpoints()
    .MapLibraryEndpoints()
    .MapChatEndpoints()
    .MapAvatarEndpoints();

app.Run();
