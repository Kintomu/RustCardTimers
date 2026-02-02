using Microsoft.Data.Sqlite;
using RustCardTimers.Data;
using RustCardTimers.Services;
using RustCardTimers.Config;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddSingleton<MonumentRepository>();
builder.Services.Configure<CardLoggerOptions>(builder.Configuration.GetSection("CardLogger"));
builder.Services.AddHostedService<DiscordCardLoggerService>();
builder.Services.AddHostedService<ResetSchedulerService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();