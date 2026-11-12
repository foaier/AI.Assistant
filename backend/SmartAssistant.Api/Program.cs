using SmartAssistant;
using SmartAssistant.Models.Common;
using SmartAssistant.Persistence;
using SmartAssistant.Platform;
using SmartAssistant.Platform.Assets;
using SmartAssistant.Platform.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    options.JsonSerializerOptions.Converters.Add(new LocalDateTimeJsonConverter());
});
builder.Services.AddAssistant(builder.Configuration);

var app = builder.Build();
var dataDirectory = LocalSecurity.Configure(builder.Configuration, builder.Environment.ContentRootPath);
LocalDatabase.Migrate(dataDirectory);
app.Use(async (context, next) =>
{
    try
    {
        /*
         * 浏览器发起管理写操作时拒绝跨站请求，避免外部页面触发本地数据修改。
         */
        if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS") &&
            context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
        { context.Response.StatusCode = 403; return; }
        await next();
    }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        var code = ex is BizResultException business ? business.BusinessCode : 1001;
        var status = ex is BizResultException ? code switch { 1002 => 404, 1003 => 403, 1004 => 409, _ => 400 }
            : ex is ArgumentException or InvalidDataException ? 400 : 500;
        app.Logger.Log(status == 500 ? LogLevel.Error : LogLevel.Debug, ex, "SmartAssistant request failed");
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new
        {
            isSuccess = false,
            businessCode = code,
            businessMessage = status == 500 ? "assistant_server_error" : ex.Message,
            returnObj = (object?)null
        });
    }
});
app.UseWebSockets();
app.MapGet("/api/health", () => new { status = "ok", application = "SmartAssistant", storage = "SQLite" });
app.MapPost("/api/assets/upload", async (HttpRequest request, AssetApplicationService assets, CancellationToken ct) =>
{
    var form = await request.ReadFormAsync(ct);
    var file = form.Files.GetFile("file") ?? throw new BizResultException(1001, "file_required");
    var result = await assets.UploadAsync(file, null,
        form["category"].FirstOrDefault() ?? "assistant-image", cancellationToken: ct);
    return Results.Ok(BizResult.BusinessSuccess(result));
});
app.MapGet("/api/assets/{id}/content", async (string id, AssetApplicationService assets, CancellationToken ct) =>
{
    var content = await assets.GetPublicContentAsync(id, ct);
    return Results.File(content.Content, content.ContentType);
});
app.MapControllers();
var webRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../frontend/dist"));
if (Directory.Exists(webRoot))
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot) });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(webRoot) });
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) { context.Response.StatusCode = 404; return; }
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(Path.Combine(webRoot, "index.html"));
    });
}
app.Logger.LogInformation("SmartAssistant data: {DataDirectory}", dataDirectory);
app.Run();

/// <summary>
/// 应用宿主入口，同时向集成测试公开可创建测试服务器的入口类型。
/// </summary>
public partial class Program;
