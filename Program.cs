using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SOPMSApp.Data;
using SOPMSApp.Helpers;
using SOPMSApp.Models;
using SOPMSApp.Services;
using System.Reflection;
using System.Runtime.InteropServices;

internal class Program
{
    private static void Main(string[] args)
    {
 
        // 1. APPLICATION SETUP AND CONFIGURATION

        var builder = WebApplication.CreateBuilder(args);

        // Configure logging first to capture any startup issues
        ConfigureLogging(builder);

        // Load PDF library 
        bool dllLoaded = LoadPdfLibrary();

        // Register PDF converter service if library loaded successfully
        RegisterPdfServices(builder, dllLoaded);

        // Configure database contexts
        ConfigureDatabaseServices(builder);

        // Configure all other application services
        ConfigureApplicationServices(builder, dllLoaded);

        // 2. BUILD APPLICATION AND SETUP MIDDLEWARE

        var app = builder.Build();

        // Configure the HTTP request pipeline
        ConfigureMiddlewarePipeline(app);

        // Seed database with initial data
        SeedDatabase(app);


        // 3. RUN THE APPLICATION
        app.Run();
    }



    // =============================================
    // CONFIGURATION METHODS
    // =============================================


    // Configures logging providers for the application
    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();
        builder.Logging.AddEventLog();
        Console.WriteLine("✅ Logging configured");
    }

   
    // Loads the PDF library with architecture detection and fallback paths
    // <returns>True if library loaded successfully, false otherwise</returns>
    private static bool LoadPdfLibrary()
    {
        try
        {
            // Determine system architecture to load correct DLL version
            string architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "32bit",
                Architecture.X64 => "64bit",
                Architecture.Arm64 => "64bit",
                _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.ProcessArchitecture}")
            };

            // Primary DLL path
            string dllPath = Path.Combine(AppContext.BaseDirectory, "runtimes", $"win-{architecture}", "native", "libwkhtmltox.dll");
            Console.WriteLine($"Looking for DLL at: {dllPath}");
            Console.WriteLine($"File exists: {File.Exists(dllPath)}");

            // Fallback to alternative path if primary not found
            if (!File.Exists(dllPath))
            {
                dllPath = Path.Combine(AppContext.BaseDirectory, "DinkToPdf", architecture, "libwkhtmltox.dll");
                Console.WriteLine($"Fallback path: {dllPath}");
                Console.WriteLine($"File exists: {File.Exists(dllPath)}");
            }

            // Load the DLL if found
            if (File.Exists(dllPath))
            {
                var context = new CustomAssemblyLoadContext();
                context.LoadUnmanagedLibrary(dllPath);
                Console.WriteLine($"✅ PDF library loaded from: {dllPath}");
                return true;
            }

            Console.WriteLine($"⚠️ WARNING: PDF library not found at: {dllPath}");
            return false;
        }
        catch (Exception ex)
        {
            // Log error details for troubleshooting
            Console.WriteLine($"❌ PDF library loading failed: {ex.Message}");
            Directory.CreateDirectory("logs");
            File.AppendAllText("logs/pdf_errors.log", $"[{DateTime.UtcNow:u}] {ex}\n\n");
            return false;
        }
    }

 
    // Registers PDF services if the library loaded successfully
    private static void RegisterPdfServices(WebApplicationBuilder builder, bool dllLoaded)
    {
        if (dllLoaded && !builder.Services.Any(x => x.ServiceType == typeof(IConverter)))
        {
            builder.Services.AddSingleton(typeof(IConverter), new SynchronizedConverter(new PdfTools()));
            Console.WriteLine("✅ IConverter registered in DI container.");
        }
        else
        {
            Console.WriteLine("⚠️ PDF library failed to load. IConverter NOT registered.");
        }
    }

    
    // Configures database contexts with connection strings
    private static void ConfigureDatabaseServices(WebApplicationBuilder builder)
    {
        // Main application database
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        }, ServiceLifetime.Scoped);

        // Additional databases for the application
        builder.Services.AddDbContext<entTTSAPDbContext>(options =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("entTTSAPConnection"));
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, ServiceLifetime.Scoped);

        builder.Services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("LoginConnection"));
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }, ServiceLifetime.Scoped);

        Console.WriteLine("✅ Database services configured");
    }


    // Configures all application services including authentication, MVC, and file handling
    private static void ConfigureApplicationServices(WebApplicationBuilder builder, bool dllLoaded)
    {
        const long TwoGigabytes = 2L * 1024 * 1024 * 1024; // 2 GB in bytes


        // Register PDF service if library loaded
        if (dllLoaded && !builder.Services.Any(x => x.ServiceType == typeof(IConverter)))
        {
            builder.Services.AddSingleton(typeof(IConverter), new SynchronizedConverter(new PdfTools()));
            Console.WriteLine("✅ IConverter registered in ConfigureServices.");
        }

        // Configure view location for Razor pages
        builder.Services.Configure<RazorViewEngineOptions>(options =>
        {
            options.ViewLocationFormats.Add("/Views/Shared/{0}" + RazorViewEngine.ViewExtension);
        });

        // Configure MVC with proper NULL handling
        builder.Services.AddControllersWithViews(options =>
        {
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

        // Configure authentication with cookie-based scheme
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
       .AddCookie(options =>
       {
           options.Cookie.Name = "SOPAuth";
           options.Cookie.HttpOnly = true;
           options.SlidingExpiration = true;
           options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
           options.LoginPath = "/Account/Login";
           options.AccessDeniedPath = "/Account/AccessDenied";
       });

        // Configure Kestrel server limits for large file uploads
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Limits.MaxRequestBodySize = TwoGigabytes;
            serverOptions.Limits.MaxRequestHeadersTotalSize = 131072; // 128 KB
            serverOptions.Limits.MaxRequestLineSize = 32768; // 32 KB
            serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
        });

        // Configure form options for large file uploads
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = TwoGigabytes;
            options.MultipartHeadersCountLimit = 1000;
            options.MultipartHeadersLengthLimit = 256 * 1024; // 256 KB
            options.ValueLengthLimit = 256 * 1024; // 256 KB
            options.ValueCountLimit = 10000; // high but not unlimited
        });

        // Add to your services configuration
        builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("StorageSettings"));
       

        // Configure IIS options for hosting on IIS
        builder.Services.Configure<IISServerOptions>(options =>
        {
            options.MaxRequestBodySize = TwoGigabytes;
            options.AllowSynchronousIO = true; // Only if absolutely necessary
        });

        // Register application-specific services
        builder.Services.AddScoped<DocRegisterService>();
        builder.Services.AddScoped<IDocRevisionService, DocRevisionService>();
        builder.Services.AddScoped<DocFileService>();
        builder.Services.AddScoped<FileRestoreService>();
        builder.Services.AddScoped<FilePermanentDeleteService>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSession();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddControllersWithViews();
        builder.Services.AddRazorPages();

        Console.WriteLine("✅ Application services configured");
    }


    // Configures the middleware pipeline for HTTP request processing
    private static void ConfigureMiddlewarePipeline(WebApplication app)
    {
        // Configure content type mappings for static files
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".mp4"] = "video/mp4";
        provider.Mappings[".webm"] = "video/webm";
        provider.Mappings[".ogg"] = "video/ogg";
        provider.Mappings[".mov"] = "video/quicktime";
        provider.Mappings[".avi"] = "video/x-msvideo";

        // Development vs Production configuration
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }
        else
        {
            app.UseDeveloperExceptionPage();
        }

        // Middleware pipeline configuration
        app.UseHttpsRedirection();
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = provider,
            OnPrepareResponse = ctx =>
            {
                // Disable caching for static files
                ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store";
                ctx.Context.Response.Headers["Expires"] = "-1";
            }
        });

        app.UseRouting();
        app.UseSession();
        app.UseAuthentication();
        app.UseAuthorization();

        // Configure default route
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        Console.WriteLine("✅ Middleware pipeline configured");
    }


    // Seeds the database with initial data and ensures proper schema
    private static void SeedDatabase(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            // Ensure database is created and migrated to latest version
            db.Database.Migrate();

            // Ensure Areas table exists (in case it was not created by migrations)
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF OBJECT_ID(N'dbo.Areas', N'U') IS NULL
                    CREATE TABLE [dbo].[Areas] ([Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY, [AreaName] nvarchar(max) NOT NULL);
                ");
            }
            catch { /* Table may already exist */ }

            // Set default values for NULL boolean fields
            try
            {
                if (db.DocRegisters.Any(d => d.IsArchived == null))
                {
                    db.Database.ExecuteSqlRaw("UPDATE DocRegisters SET IsArchived = 0 WHERE IsArchived IS NULL");
                }
            }
            catch { /* DocRegisters may not exist yet */ }

            // Seed initial area data if none exists
            try
            {
                if (!db.Areas.Any())
                {
                    db.Areas.AddRange(
                        new Area { AreaName = "HR" },
                        new Area { AreaName = "Production" },
                        new Area { AreaName = "Maintenance" }
                    );
                    db.SaveChanges();
                    Console.WriteLine("✅ Database seeded with initial data");
                }
            }
            catch (Exception ex)
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogWarning(ex, "Could not seed Areas (table may not exist yet)");
            }
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while seeding the database");
        }
    }



    // =============================================
    // HELPER CLASSES
    // =============================================

    // Custom assembly load context for loading unmanaged DLLs
    public class CustomAssemblyLoadContext : System.Runtime.Loader.AssemblyLoadContext
    {
        public IntPtr LoadUnmanagedLibrary(string absolutePath)
        {
            return LoadUnmanagedDll(absolutePath);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            return LoadUnmanagedDllFromPath(unmanagedDllName);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            return null; // Not used for unmanaged DLLs
        }
    }
}