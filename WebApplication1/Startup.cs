using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using WebApplication1.Hubs;
using WebApplication1.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WebApplication1.Data;
using WebApplication1.Services.Interfaces;
using Microsoft.AspNetCore.Http;

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Add DbContext using SQL Server provider
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

        // Configure session options
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30); // Set session timeout as needed
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        // Add in-memory caching
        services.AddDistributedMemoryCache();

        // Add controllers
        services.AddControllers();

        // Add SignalR
        services.AddSignalR();

        // Add HttpClient
        services.AddHttpClient();

        // Register TradingStrategyContext as a singleton
        services.AddSingleton<TradingStrategyContext>();

        // Add logging
        services.AddLogging();

        // Add the BinanceService to the dependency injection container
        services.AddScoped<BinanceService>();

        // Register Strategy
        services.AddScoped<ITradingStrategy, ICTStrategy>();
        services.AddScoped<Scalping>();

        services.AddSingleton<WebSocketService>();

        services.AddControllersWithViews();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        // Serve static files from wwwroot
        app.UseStaticFiles();

        app.UseRouting();

        app.UseSession();

        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapHub<PriceHub>("/priceHub");
            endpoints.MapFallbackToFile("index.html");
        });
    }
}