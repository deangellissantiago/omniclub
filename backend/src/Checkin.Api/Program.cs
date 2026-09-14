using System.Text;
using Checkin.Api.Middleware;
using Checkin.Api.Security;
using Checkin.Api.Seed;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Security;
using Checkin.Application.UseCases.Auth;
using Checkin.Application.UseCases.Billing;
using Checkin.Application.UseCases.Bookings;
using Checkin.Application.UseCases.CheckinPoints;
using Checkin.Application.UseCases.Checkins;
using Checkin.Application.UseCases.Dashboard;
using Checkin.Application.UseCases.Reports;
using Checkin.Application.UseCases.Students;
using Checkin.Infrastructure.Persistence.Mongo;
using Checkin.Infrastructure.Stripe;
using Checkin.Infrastructure.Wellhub;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serializa enums (App, Status) como string no JSON (ex.: "Wellhub"), não como número.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Checkin API",
        Version = "v1",
        Description = "Login em /api/auth/login, clique em Authorize acima e cole \"Bearer {token}\" " +
                      "para testar os endpoints autenticados (inclusive /api/simulate/wellhub/*)."
    });

    const string bearerScheme = "Bearer";
    options.AddSecurityDefinition(bearerScheme, new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = bearerScheme,
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Cole \"Bearer {seu token}\" (o /api/auth/login retorna o token pronto).",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = bearerScheme } },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddHttpContextAccessor();

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? new[] { "http://localhost:5173" };

    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Infraestrutura (adapters de saída)
builder.Services.AddMongoPersistence(builder.Configuration);
builder.Services.AddWellhubIntegration(builder.Configuration);
builder.Services.AddBillingIntegration(builder.Configuration);

// Segurança
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ICurrentTenantContext, CurrentTenantContext>();

// Casos de uso (Application)
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<StudentService>();
builder.Services.AddScoped<CheckinPointService>();
builder.Services.AddScoped<CheckinService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ReportService>();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret não configurado.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger disponível sempre (não só em Development): pedido explícito para testar/simular a
// integração Wellhub pela UI (/swagger) em vez de só por curl/script. Os endpoints continuam
// atrás de JWT normalmente — Swagger só documenta o contrato, não abre acesso extra.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SubscriptionGateMiddleware>();
app.MapControllers();

await DataSeeder.SeedAsync(app.Services);

app.Run();
