// MIT License.

using System.Net;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace WebForms.Extensions.Tests;

[TestClass]
public sealed class AsyncPostBackRedirectTests
{
    [TestMethod]
    public async Task RedirectDuringAsyncPostBackWritesPageRedirectDelta()
    {
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
                        .AddHttpHandler<RedirectPage>("/")
                        .AddWebForms()
                        .AddScriptManager();
                });
            })
            .StartAsync();

        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/");
        request.Headers.Add("X-MicrosoftAjax", "Delta=true");
        request.Content = new StringContent("__ASYNCPOST=true", Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/plain", response.Content.Headers.ContentType?.MediaType);
        StringAssert.StartsWith(body, "1|#||4|");
        StringAssert.Contains(body, "pageRedirect");
        StringAssert.Contains(body, "%2fnext.aspx");
        Assert.IsFalse(body.Contains("Object moved", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RedirectWithoutEndingResponseDuringAsyncPostBackDoesNotAppendPageHtml()
    {
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
                        .AddHttpHandler<RedirectWithoutEndPage>("/")
                        .AddWebForms()
                        .AddScriptManager();
                });
            })
            .StartAsync();

        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/");
        request.Headers.Add("X-MicrosoftAjax", "Delta=true");
        request.Content = new StringContent("__ASYNCPOST=true", Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/plain", response.Content.Headers.ContentType?.MediaType);
        StringAssert.StartsWith(body, "1|#||4|");
        StringAssert.Contains(body, "pageRedirect");
        StringAssert.Contains(body, "%2fnext.aspx");
        Assert.IsFalse(body.Contains("Object moved", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("should-not-render", StringComparison.Ordinal));
    }

    private sealed class RedirectPage : System.Web.UI.Page
    {
        protected override void OnLoad(EventArgs e)
        {
            Response.Redirect("/next.aspx", endResponse: true);
        }
    }

    private sealed class RedirectWithoutEndPage : System.Web.UI.Page
    {
        protected override void OnLoad(EventArgs e)
        {
            Response.Redirect("/next.aspx", endResponse: false);
        }

        protected override void Render(System.Web.UI.HtmlTextWriter writer)
        {
            writer.Write("<div>should-not-render</div>");
        }
    }
}
