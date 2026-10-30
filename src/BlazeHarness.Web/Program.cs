using BlazeHarness.Core;
using BlazeHarness.Core.Config;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Logging;
using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Secrets;
using BlazeHarness.Core.Sessions;
using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Web;
using BlazeHarness.Web.Components;
using BlazeHarness.Web.Hosting;
using BlazeHarness.Web.Services;

// Code colors are matched with patterns from a library, against text a model wrote, so a slow match must give up.
AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(2));

const int MaxHubMessageBytes = 4 * 1024 * 1024;
const string WebSearchClientName = "web-search";

var builder = WebApplication.CreateBuilder(args);

var listenAddress = HostLauncher.ConfiguredHost(args, builder.Configuration);
var listenOnAllInterfaces = Equals(listenAddress, System.Net.IPAddress.Any);
if (listenOnAllInterfaces)
{
    builder.Configuration["AllowedHosts"] = "*"; // LocalAccessMiddleware does the Host check in this mode.
}

HostLauncher.ConfigureKestrel(builder, listenAddress, HostLauncher.ConfiguredPort(args, builder.Configuration));
builder.Services.AddSingleton(new AccessPolicy(listenOnAllInterfaces) { MachineName = Environment.MachineName });

var paths = AppPaths.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(paths.LogsDirectory, TimeProvider.System));
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(await SecretStoreFactory.CreateAsync(paths));
builder.Services.AddSingleton<ApiKeyResolver>();
builder.Services.AddSingleton(sp => new JsonFileStore<AppConfig>(
    paths.ConfigFile,
    sp.GetRequiredService<ILogger<JsonFileStore<AppConfig>>>(),
    time: sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new JsonFileStore<ProvidersDocument>(
    paths.ProvidersFile,
    sp.GetRequiredService<ILogger<JsonFileStore<ProvidersDocument>>>(),
    time: sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<ProviderService>();
builder.Services.AddSingleton(sp => new PermissionDefaults(sp.GetRequiredService<JsonFileStore<AppConfig>>(), paths));
builder.Services.AddSingleton<MarkdownRenderer>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<SettingsDialogState>();
builder.Services.AddSingleton<ChatClientSource>();
builder.Services.AddSingleton(sp => new SessionStore(paths, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IShellSandbox>(sp => new MxcShellSandbox(() => sp.GetRequiredService<PermissionDefaults>().Sensitive()));
builder.Services.AddSingleton(sp => new McpConfigFile(paths.McpFile, sp.GetRequiredService<ILogger<McpConfigFile>>(), time: sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IMcpConnector, SdkMcpConnector>();
builder.Services.AddSingleton<McpManager>();
builder.Services.AddSingleton<McpServerEditor>();
builder.Services.AddSingleton(sp => new SkillCatalog(paths, sp.GetRequiredService<JsonFileStore<AppConfig>>(), sp.GetRequiredService<ILogger<SkillCatalog>>(), time: sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new WebServices(
    () => sp.GetRequiredService<JsonFileStore<AppConfig>>().Current.Web,
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(WebSearchClientName),
    sp.GetRequiredService<ILoggerFactory>(),
    sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new SessionService(
    sp.GetRequiredService<SessionStore>(),
    sp.GetRequiredService<ChatClientSource>().CreateAsync,
    sp.GetRequiredService<ILoggerFactory>(),
    sp.GetRequiredService<TimeProvider>(),
    new ModelTitleGenerator(sp.GetRequiredService<ChatClientSource>().CreateAsync, sp.GetRequiredService<TimeProvider>()),
    sp.GetRequiredService<IShellSandbox>(),
    sp.GetRequiredService<PermissionDefaults>(),
    sp.GetRequiredService<McpManager>(),
    sp.GetRequiredService<SkillCatalog>(),
    () => sp.GetRequiredService<JsonFileStore<AppConfig>>().Current.Compaction,
    sp.GetRequiredService<WebServices>()));
builder.Services.AddSingleton(sp => new FolderRegistry(paths, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(WebSearchClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddHttpClient(ChatClientSource.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = MaxHubMessageBytes);

await using var app = builder.Build();

app.UseMiddleware<LocalAccessMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapAttachments();
app.MapExport();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var sandbox = app.Services.GetRequiredService<IShellSandbox>();
_ = Task.Run(() => sandbox.Status);

await HostLauncher.RunAsync(app, args);
