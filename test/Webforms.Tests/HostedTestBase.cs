// MIT License.

using System.Web;
using System.Web.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WebForms.Tests;

public abstract class HostedTestBase
{
    protected async Task<string> RunPage<TPage>(Action<IServiceCollection>? servicesConfigure = null, string? path = null, string? handlerPath = null)
        where TPage : Page, new()
    {
        path ??= "/";
        handlerPath ??= "/";
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(app =>
            {
                app.UseTestServer();
                app.Configure(app =>
                {
                    app.UseRouting();
                    app.UseSession();
                    app.UseSystemWebAdapters();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHttpHandlers();
                    });
                });
                app.ConfigureServices(services =>
                {
                    services.AddDistributedMemoryCache();
                    services.AddRouting();
                    services.AddSystemWebAdapters()
                        .AddWrappedAspNetCoreSession()
                        .AddHttpApplication<HttpApplication>()
                        .AddHttpHandler<TPage>(handlerPath)
                        .AddWebForms();

                    servicesConfigure?.Invoke(services);

                });
            })
            .StartAsync();

        using var client = host.GetTestClient();

        return await client.GetStringAsync(path);
    }

    protected async Task<string> RunPagePost<TPage>(string path, string body, string? handlerPath = null, Action<IServiceCollection>? servicesConfigure = null)
        where TPage : Page, new()
    {
        handlerPath ??= "/";
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHost(app =>
            {
                app.UseTestServer();
                app.Configure(app =>
                {
                    app.UseRouting();
                    app.UseSession();
                    app.UseSystemWebAdapters();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHttpHandlers();
                    });
                });
                app.ConfigureServices(services =>
                {
                    services.AddDistributedMemoryCache();
                    services.AddRouting();
                    services.AddSystemWebAdapters()
                        .AddWrappedAspNetCoreSession()
                        .AddHttpApplication<HttpApplication>()
                        .AddHttpHandler<TPage>(handlerPath)
                        .AddWebForms();

                    servicesConfigure?.Invoke(services);
                });
            })
            .StartAsync();

        using var client = host.GetTestClient();
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await client.PostAsync(path, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
