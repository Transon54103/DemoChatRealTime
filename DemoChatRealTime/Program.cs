using DemoChatRealTime.Data;
using DemoChatRealTime.Hubs;
using DemoChatRealTime.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =====================================================
// NOTE - SERVICE REGISTRATION (Dependency Injection Container)
// =====================================================
// ??ng ký t?t c? services vào DI container.
// ASP.NET Core dùng Constructor Injection.
// 3 lifetime: Transient (m?i l?n inject = instance m?i),
//             Scoped (1 instance/request),
//             Singleton (1 instance/app lifetime)
// =====================================================

// === 1. MVC Controllers + Views ===
builder.Services.AddControllersWithViews();

// === 2. Entity Framework Core + SQL Server ===
// NOTE: Connection string nên l?y t? appsettings.json ho?c Environment Variables
// Production: dùng User Secrets, Azure Key Vault, ho?c Environment Variables
// KHÔNG hardcode connection string trong code
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            // NOTE: Retry logic cho SQL connection - t? reconnect khi m?t k?t n?i t?m th?i
            // R?t quan tr?ng cho cloud environments (Azure SQL, AWS RDS)
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        }));

// === 3. Authentication (Cookie-based) ===
// NOTE: Cookie Auth flow:
// Login ? Server t?o encrypted cookie ? Browser l?u cookie
// ? M?i request, browser g?i cookie ? Server decrypt ? bi?t user là ai
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";           // Redirect khi ch?a login
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);  // Cookie s?ng 7 ngày
        options.SlidingExpiration = true;             // NOTE: Reset expiry m?i l?n request (user active)

        // NOTE: Cookie options cho b?o m?t
        options.Cookie.HttpOnly = true;              // JS không ??c ???c cookie (ch?ng XSS)
        options.Cookie.SameSite = SameSiteMode.Lax;  // NOTE: Lax thay vì Strict ?? SignalR ho?t ??ng
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // HTTPS ? production

        // NOTE: SignalR không g?i cookie qua WebSocket initial handshake bình th??ng
        // C?n handle Events.OnRedirectToLogin ?? tr? 401 thay vì redirect cho API/Hub
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                // N?u request là API ho?c SignalR ? tr? 401
                if (context.Request.Path.StartsWithSegments("/chatHub") ||
                    context.Request.Headers.Accept.ToString().Contains("application/json"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                // N?u là browser request ? redirect t?i login page
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });

// === 4. Memory Cache ===
// NOTE: IMemoryCache - In-process cache, nhanh nh?t nh?ng ch? single server
// Khi scale ra nhi?u servers ? chuy?n sang IDistributedCache (Redis)
// builder.Services.AddStackExchangeRedisCache(options => { ... }); // cho Redis
builder.Services.AddMemoryCache();

// === 5. SignalR ===
// NOTE: SignalR t? ??ng ch?n transport t?t nh?t: WebSocket > Server-Sent Events > Long Polling
// WebSocket: bidirectional, lowest latency
// Khi scale: c?n Backplane (Redis ho?c Azure SignalR Service)
// builder.Services.AddSignalR().AddStackExchangeRedis("redis-connection-string"); // cho Redis backplane
builder.Services.AddSignalR(options =>
{
    // NOTE: Tune các options cho production
    options.MaximumReceiveMessageSize = 64 * 1024;    // Max message size: 64KB
    options.KeepAliveInterval = TimeSpan.FromSeconds(15); // Heartbeat interval
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30); // Client timeout
    options.EnableDetailedErrors = builder.Environment.IsDevelopment(); // Chi ti?t l?i ch? ? dev
});

// === 6. Application Services (DI Registration) ===
// NOTE: Scoped = 1 instance per HTTP request. Phù h?p cho service dùng DbContext (c?ng Scoped)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

// =====================================================
// NOTE - MIDDLEWARE PIPELINE
// =====================================================
// Th? t? middleware R?T QUAN TR?NG!
// Request ?i qua t? trên xu?ng, Response ?i ng??c t? d??i lên.
// UseAuthentication PH?I tr??c UseAuthorization
// UseRouting PH?I tr??c MapControllerRoute và MapHub
// =====================================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// NOTE: Authentication ? xác ??nh user là ai (decrypt cookie)
// Authorization ? ki?m tra user có quy?n truy c?p không ([Authorize])
app.UseAuthentication();
app.UseAuthorization();

// === Map Routes ===
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// NOTE: Map SignalR Hub endpoint
// Client connect t?i /chatHub ? server t?o WebSocket connection
app.MapHub<ChatHub>("/chatHub");

// === Initialize Database ===
// NOTE: T?o DB và seed data khi app start
// Production nên dùng migration command riêng, không auto-migrate trong code
await DbInitializer.InitializeAsync(app.Services);

app.Run();
