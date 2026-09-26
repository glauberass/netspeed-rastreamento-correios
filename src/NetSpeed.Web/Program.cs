using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetSpeed.DependencyInjection;
using NetSpeed.Web.Endpoints;
using NetSpeed.Web.Jobs;
using Serilog;

// Host web: API REST + interface simples para consultar códigos de rastreamento.
var builder = WebApplication.CreateBuilder(args);

Log.Logger = LogConfiguracao.Criar(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(opcoes =>
{
    opcoes.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opcoes.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    opcoes.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    opcoes.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddNetSpeed(builder.Configuration);
builder.Services.AddSingleton<FilaDeJobs>();
builder.Services.AddHostedService<ProcessadorDeJobs>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapRastreamentos();

await app.RunAsync();
