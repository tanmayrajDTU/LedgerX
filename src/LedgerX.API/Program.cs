using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Prometheus;
using Serilog;
using StackExchange.Redis;
using LedgerX.Core.Interfaces;
using LedgerX.Infrastructure.Caching;
using LedgerX.Infrastructure.Data;
using LedgerX.Infrastructure.Messaging;
using LedgerX.Infrastructure.Services;
using LedgerX.API.Middleware;

namespace LedgerX.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            try
            {
                Log.Information("Starting LedgerX Web API...");

                var builder = WebApplication.CreateBuilder(args);

                // Configure Serilog
                builder.Host.UseSerilog((ctx, lc) => lc.WriteTo.Console().ReadFrom.Configuration(ctx.Configuration));

                // Add Database Context (EF Core + Postgres)
                builder.Services.AddDbContext<LedgerXDbContext>(options =>
                    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

                // Add Redis Cache Connection
                builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
                    try
                    {
                        return ConnectionMultiplexer.Connect(connectionString);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to connect to Redis at {Conn}", connectionString);
                        return null!;
                    }
                });

                // Register Core and Infrastructure Services
                builder.Services.AddSingleton<ICacheService, RedisCacheService>();
                builder.Services.AddSingleton<IMessageBroker, RabbitMQMessageBroker>();
                builder.Services.AddHttpClient<IMarketDataProvider, MarketDataProvider>();
                builder.Services.AddScoped<IJobTracker, JobTracker>();
                builder.Services.AddScoped<TransactionService>();
                builder.Services.AddSingleton<JwtTokenService>();

                // Scheduler triggers are now decoupled via RabbitMQ messages

                builder.Services.AddControllers();
                builder.Services.AddEndpointsApiExplorer();

                // Configure Swagger with JWT Authorize option
                builder.Services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new OpenApiInfo { Title = "LedgerX API", Version = "v1" });
                    
                    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                    {
                        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
                        Name = "Authorization",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.ApiKey,
                        Scheme = "Bearer"
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

                // Configure JWT Authentication
                var jwtKey = builder.Configuration["Jwt:Key"] ?? "super_secret_ledgerx_key_1234567890_security_key";
                var key = Encoding.ASCII.GetBytes(jwtKey);
                builder.Services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.SaveToken = true;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(key),
                        ValidateIssuer = true,
                        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "LedgerX",
                        ValidateAudience = true,
                        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "LedgerXUsers",
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero
                    };
                });

                // Configure CORS
                builder.Services.AddCors(options =>
                {
                    options.AddPolicy("AllowAll", policy =>
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    });
                });

                var app = builder.Build();

                // Prometheus request latency and throughput tracking
                app.UseHttpMetrics();

                // Global Exception Handling Middleware
                app.UseMiddleware<ExceptionHandlingMiddleware>();

                // Configure HTTP Pipeline
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "LedgerX API v1"));

                app.UseCors("AllowAll");

                app.UseAuthentication();
                app.UseAuthorization();

                app.MapControllers();
                
                // Expose Prometheus metrics endpoint
                app.MapMetrics();

                // Seed database on startup
                using (var scope = app.Services.CreateScope())
                {
                    var services = scope.ServiceProvider;
                    try
                    {
                        var context = services.GetRequiredService<LedgerXDbContext>();
                        DbSeeder.SeedAsync(context).GetAwaiter().GetResult();
                        Log.Information("Database successfully migrated and seeded.");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "An error occurred during database seeding on startup.");
                    }
                }

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Web API host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
