using System.Text;
using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.Hubs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

var supabaseConnection = DatabaseConfiguration.ReadRaw(builder.Configuration);
if (string.IsNullOrWhiteSpace(supabaseConnection)
    || supabaseConnection.Contains("ai-voice-portal.db", StringComparison.OrdinalIgnoreCase)
    || supabaseConnection.Contains("YOUR_", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Supabase is not configured. Copy backend/appsettings.Local.json.example to backend/appsettings.Local.json and paste the URI from Supabase → Project Settings → Database.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSupabase(supabaseConnection));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-only-change-this-key-32bytes-min!!";
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
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "AiVoicePortal",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "AiVoicePortal",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = System.Security.Claims.ClaimTypes.Name
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var frontend = builder.Configuration["FrontendUrl"] ?? "http://localhost:5173";
builder.Services.AddCors(options =>
{
    options.AddPolicy("spa", policy =>
        policy.WithOrigins(frontend, "http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IEmailSender, EmailService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddSingleton<IVoiceSessionStore, VoiceSessionStore>();
builder.Services.AddScoped<IVoiceAgentService, VoiceAgentService>();
builder.Services.AddHttpClient<ISarvamAiService, SarvamAiService>();
builder.Services.AddHttpClient<IExotelMediaService, ExotelMediaService>();

var app = builder.Build();

await DbSeeder.SeedAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("spa");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<DashboardHub>(DashboardHub.Path);
app.MapGet("/health", async (AppDbContext db) =>
{
    var connected = await db.Database.CanConnectAsync();
    return Results.Ok(new
    {
        status = connected ? "ok" : "degraded",
        service = "AiVoicePortal",
        database = "Supabase PostgreSQL",
        connected
    });
});

app.Run();
