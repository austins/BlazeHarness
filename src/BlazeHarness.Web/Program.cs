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
using BlazeHarness.Web.Components;
using BlazeHarness.Web.Hosting;
using BlazeHarness.Web.Services;

// Code colors are matched with patterns from a library, against text a model wrote, so a slow match must give up.
AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(2));

const int MaxHubMessageBytes = 4 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

HostLauncher.ConfigureKestrel(builder, HostLauncher.ConfiguredPort(args));

var paths = AppPaths.FromEnvironment();
builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLoggerProvider(paths.LogsDirectory, TimeProvider.System));
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(await SecretStoreFactory.CreateAsync(paths));
builder.Services.AddSingleton<ApiKeyResolver>();
builder.Services.AddSingleton(sp => new JsonFileStore<AppConfig>(
    paths.ConfigFile,
    sp.GetRequiredService<ILogger<JsonFileStore<AppConfig>>>()));
builder.Services.AddSingleton<ProviderService>();
builder.Services.AddSingleton(sp => new PermissionDefaults(sp.GetRequiredService<JsonFileStore<AppConfig>>(), paths));
builder.Services.AddSingleton<MarkdownRenderer>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddSingleton<ChatClientSource>();
builder.Services.AddSingleton(sp => new SessionStore(paths, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<IShellSandbox>(sp => new MxcShellSandbox(() => sp.GetRequiredService<PermissionDefaults>().Sensitive()));
builder.Services.AddSingleton(sp => new McpConfigFile(paths.McpFile, sp.GetRequiredService<ILogger<McpConfigFile>>()));
builder.Services.AddSingleton<IMcpConnector, SdkMcpConnector>();
builder.Services.AddSingleton<McpManager>();
builder.Services.AddSingleton(sp => new SkillCatalog(paths, sp.GetRequiredService<JsonFileStore<AppConfig>>(), sp.GetRequiredService<ILogger<SkillCatalog>>()));
builder.Services.AddSingleton(sp => new SessionService(
    sp.GetRequiredService<SessionStore>(),
    sp.GetRequiredService<ChatClientSource>().CreateAsync,
    sp.GetRequiredService<ILoggerFactory>(),
    sp.GetRequiredService<TimeProvider>(),
    new ModelTitleGenerator(sp.GetRequiredService<ChatClientSource>().CreateAsync),
    sp.GetRequiredService<IShellSandbox>(),
    sp.GetRequiredService<PermissionDefaults>(),
    sp.GetRequiredService<McpManager>(),
    sp.GetRequiredService<SkillCatalog>()));
builder.Services.AddSingleton(sp => new FolderRegistry(paths, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<LaunchCredentials>();
builder.Services.AddHttpClient();
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

_ = Task.Run(() => app.Services.GetRequiredService<IShellSandbox>().Status);

await HostLauncher.RunAsync(app, args);
