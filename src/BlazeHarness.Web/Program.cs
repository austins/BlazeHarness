using BlazeHarness.Core;
using BlazeHarness.Core.Config;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Secrets;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Web.Components;
using BlazeHarness.Web.Hosting;
using BlazeHarness.Web.Services;

var builder = WebApplication.CreateBuilder(args);

HostLauncher.ConfigureKestrel(builder, HostLauncher.ConfiguredPort(args));

var paths = AppPaths.FromEnvironment();
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(await SecretStoreFactory.CreateAsync(paths));
builder.Services.AddSingleton<ApiKeyResolver>();
builder.Services.AddSingleton(sp => new JsonFileStore<AppConfig>(
    paths.ConfigFile,
    sp.GetRequiredService<ILogger<JsonFileStore<AppConfig>>>()));
builder.Services.AddSingleton<ProviderService>();
builder.Services.AddSingleton<MarkdownRenderer>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddSingleton<ChatClientSource>();
builder.Services.AddSingleton(sp => new SessionStore(paths, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new SessionService(
    sp.GetRequiredService<SessionStore>(),
    sp.GetRequiredService<ChatClientSource>().CreateAsync,
    sp.GetRequiredService<ILoggerFactory>(),
    sp.GetRequiredService<TimeProvider>(),
    new ModelTitleGenerator(sp.GetRequiredService<ChatClientSource>().CreateAsync)));
builder.Services.AddSingleton(sp => new FolderRegistry(paths, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<LaunchCredentials>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(ChatClientSource.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 4 * 1024 * 1024);

await using var app = builder.Build();

app.UseMiddleware<LocalAccessMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await HostLauncher.RunAsync(app, args);
