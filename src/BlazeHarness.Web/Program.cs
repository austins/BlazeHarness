using BlazeHarness.Core;
using BlazeHarness.Core.Chats;
using BlazeHarness.Core.Chats.Titles;
using BlazeHarness.Core.Config;
using BlazeHarness.Core.Folders;
using BlazeHarness.Core.Git;
using BlazeHarness.Core.Mcp;
using BlazeHarness.Core.Mcp.Config;
using BlazeHarness.Core.Mcp.Tools;
using BlazeHarness.Core.Permissions;
using BlazeHarness.Core.Providers;
using BlazeHarness.Core.Sandbox;
using BlazeHarness.Core.Secrets;
using BlazeHarness.Core.Skills;
using BlazeHarness.Core.Web;
using BlazeHarness.Web.Components;
using BlazeHarness.Web.Hosting;
using BlazeHarness.Web.Resources;
using BlazeHarness.Web.Services;
using Microsoft.AspNetCore.Hosting.Server;
using System.Globalization;

// Code colors are matched with patterns from a library, against text a model wrote, so a slow match must give up.
AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(2));

const int MaxHubMessageBytes = 4 * 1024 * 1024;
const string WebSearchClientName = "web-search";

// The app's folder, not the working directory, so a launch from anywhere finds wwwroot and appsettings.json.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

var listenAddress = HostLauncher.ConfiguredHost(args, builder.Configuration);
var listenOnAllInterfaces = Equals(listenAddress, System.Net.IPAddress.Any);
if (listenOnAllInterfaces)
{
    builder.Configuration["AllowedHosts"] = "*"; // LocalAccessMiddleware does the Host check in this mode.
}

var listenPort = HostLauncher.ConfiguredPort(args, builder.Configuration);
HostLauncher.ConfigureKestrel(builder, listenAddress, listenPort);
builder.Services.AddSingleton(new AccessPolicy(listenOnAllInterfaces) { MachineName = Environment.MachineName });

var paths = AppPaths.FromConfiguration(builder.Configuration);
HostLauncher.ConfigureLogging(builder.Logging, paths.LogsDirectory, TimeProvider.System);
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
builder.Services.AddScoped<SidePanelState>();
builder.Services.AddSingleton(sp => new GitChangesReader(sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<ChatClientSource>();
builder.Services.AddSingleton(sp => new ChatStore(paths, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IShellSandbox>(sp => new MxcShellSandbox(() => sp.GetRequiredService<PermissionDefaults>().Sensitive()));
builder.Services.AddSingleton(sp => new McpConfigFile(paths.McpFile, sp.GetRequiredService<ILogger<McpConfigFile>>(), time: sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new CustomInstructionsFile(paths.InstructionsFile, sp.GetRequiredService<ILogger<CustomInstructionsFile>>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IMcpConnector, SdkMcpConnector>();
builder.Services.AddSingleton(sp => new McpToolCache(paths, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<McpManager>();
builder.Services.AddSingleton<McpServerEditor>();
builder.Services.AddSingleton(sp => new SkillCatalog(paths, sp.GetRequiredService<JsonFileStore<AppConfig>>(), sp.GetRequiredService<ILogger<SkillCatalog>>(), time: sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new WebServices(
    () => sp.GetRequiredService<JsonFileStore<AppConfig>>().Current.Web,
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(WebSearchClientName),
    sp.GetRequiredService<ILoggerFactory>(),
    sp.GetRequiredService<TimeProvider>(),
    paths.BrowserDirectory,
    HostLauncher.ListeningPort(sp.GetRequiredService<IServer>())));
builder.Services.AddSingleton(sp => new ChatService(
    sp.GetRequiredService<ChatStore>(),
    sp.GetRequiredService<ChatClientSource>().CreateAsync,
    sp.GetRequiredService<ILoggerFactory>(),
    sp.GetRequiredService<TimeProvider>(),
    new ModelTitleGenerator(sp.GetRequiredService<ChatClientSource>().CreateAsync, sp.GetRequiredService<TimeProvider>()),
    sp.GetRequiredService<IShellSandbox>(),
    sp.GetRequiredService<PermissionDefaults>(),
    sp.GetRequiredService<McpManager>(),
    sp.GetRequiredService<SkillCatalog>(),
    () => sp.GetRequiredService<JsonFileStore<AppConfig>>().Current.Compaction,
    sp.GetRequiredService<WebServices>(),
    () => sp.GetRequiredService<JsonFileStore<AppConfig>>().Current.TodosEnabled,
    () => sp.GetRequiredService<CustomInstructionsFile>().Text,
    sp.GetRequiredService<ProviderService>(),
    sp.GetRequiredService<FolderRegistry>()));
builder.Services.AddSingleton(sp => new FolderRegistry(paths, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(WebSearchClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
builder.Services.AddHttpClient(ChatClientSource.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = MaxHubMessageBytes);
builder.Services.AddLocalization();
var uiLanguages = UiLanguages.Discover();
builder.Services.AddSingleton(uiLanguages);

await using var app = builder.Build();

app.UseMiddleware<LocalAccessMiddleware>();
var formattingCultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
    .Select(culture => culture.Name)
    .Concat(uiLanguages.SupportedCultures)
    .Distinct(StringComparer.Ordinal)
    .ToArray();
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en-US")
    .AddSupportedCultures(formattingCultures)
    .AddSupportedUICultures([.. uiLanguages.SupportedCultures]));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseWebSockets();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAttachments();
app.MapBrowser();
app.MapExport();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var sandbox = app.Services.GetRequiredService<IShellSandbox>();
// Probing here means the first chat does not wait for it. The probe reports a failure as a status and never throws.
_ = Task.Run(() => sandbox.Status);

await HostLauncher.RunAsync(app, args);
