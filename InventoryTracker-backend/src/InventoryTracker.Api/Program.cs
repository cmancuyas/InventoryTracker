using InventoryTracker.Api.Auth;
using InventoryTracker.Infrastructure.Auditing;
using InventoryTracker.Infrastructure.Idempotency.Services;
using InventoryTracker.Infrastructure.Identity;
using InventoryTracker.Infrastructure.Inventory;
using InventoryTracker.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Swagger + JWT Bearer support in Swagger UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Prevent schema name collisions (e.g., nested Row types)
    c.CustomSchemaIds(t => t.FullName!.Replace("+", "."));

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter: Bearer {your JWT token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// CORS (for Next.js dev)
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("web", p =>
        p.WithOrigins("http://localhost:3000")
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials());
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// DbContexts
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Data")));

builder.Services.AddDbContext<IdentityAppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Identity")));

// Identity
builder.Services.AddIdentityCore<ApplicationUser>()
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<IdentityAppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// JWT options + service
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// JWT auth
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>();
if (jwt is null || string.IsNullOrWhiteSpace(jwt.Key))
    throw new InvalidOperationException("Missing Jwt configuration (Jwt:Key).");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromMinutes(2),

            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
        };

        opt.IncludeErrorDetails = true;
    });

builder.Services.AddAuthorization();

// App services
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<IInventoryMovementService, InventoryMovementService>();

// Seeder
builder.Services.AddScoped<IdentitySeeder>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
    await seeder.SeedAsync();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseCors("web");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
