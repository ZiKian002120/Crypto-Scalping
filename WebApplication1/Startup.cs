using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using WebApplication1.Hubs;
using WebApplication1.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WebApplication1.Data;

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30); // Set session timeout as needed
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        // Add in-memory caching
        services.AddDistributedMemoryCache();

        services.AddControllers();
        
        services.AddSignalR();
   
        services.AddHttpClient();

        // Add the BinanceService to the dependency injection container
        services.AddScoped<BinanceService>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {

        app.UseHttpsRedirection();
        app.UseAuthorization();

        app.UseRouting();

        app.UseSession();

        app.UseStaticFiles(); // Serve static files from wwwroot

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapHub<PriceHub>("/priceHub");
            endpoints.MapFallbackToFile("index.html"); 
            //endpoints.MapControllerRoute(
            //name: "default",
            //pattern: "{controller=Admin}/{action=Accounts}/{id?}");
        });
    }
}
