using Ecommerce.Data;
using Ecommerce.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
var builder = WebApplication.CreateBuilder(args);


builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddScoped<CartService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IChatService, ChatService>();


// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDbContext<CategoryContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CategoryContext") ?? throw new InvalidOperationException("Connection string 'CategoryContext' not found.")));
builder.Services.AddDbContext<ProductContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ProductContext") ?? throw new InvalidOperationException("Connection string 'ProductContext' not found.")));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(configuration);
});
builder.Services.AddScoped<RedisCacheService>();
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseSession();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();


// Add Chat API Endpoint
app.MapPost("/api/chat/message", async (ChatRequest request, IChatService chatService) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request?.Message))
        {
            return Results.BadRequest(new { reply = "Message cannot be empty." });
        }

        var reply = await chatService.GetChatResponseAsync(request.Message);
        return Results.Ok(new { reply });
    }
    catch (Exception ex)
    {
        Console.WriteLine("===== CHAT API ERROR =====");
        Console.WriteLine(ex.ToString()); 
        Console.WriteLine("==========================");
        Console.WriteLine($"Chat API Error: {ex.Message}");
        return Results.Problem("Sorry, I encountered an error. Please try again.");
    }
}); 



app.Run();
public record ChatRequest(string Message);
