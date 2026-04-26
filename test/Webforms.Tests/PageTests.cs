// MIT License.

using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;

namespace WebForms.Tests;

[TestClass]
public class PageTests : HostedTestBase
{
    [TestMethod]
    public async Task EmptyPage()
    {
        // Arrange/Act
        var result = await RunPage<Page1>();

        // Assert
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public async Task CustomRender()
    {
        // Arrange/Act
        var result = await RunPage<Page2>();

        // Assert
        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public async Task PageLoadAddControl()
    {
        // Arrange/Act
        var result = await RunPage<Page3>();

        // Assert
        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public async Task PageApplicationIsAvailableDuringRequest()
    {
        var result = await RunPage<Page5>();

        Assert.AreEqual("ok", result);
    }

    [TestMethod]
    public async Task PageIsDisposedAfterRequest()
    {
        DisposablePage.ResetDisposed();

        var result = await RunPage<DisposablePage>();

        Assert.AreEqual("disposed", result);
        Assert.IsTrue(DisposablePage.DisposeSignal.Task.IsCompleted);
    }

    [TestMethod]
    public async Task HtmlFormActionPreservesQueryString()
    {
        var result = await RunPage<Page4>(path: "/?a=b&c=d");

        StringAssert.Contains(result, "action=\"./?a=b&amp;c=d\"");
    }

    [TestMethod]
    public async Task HtmlFormActionUsesExecutionPathRelativeToClientPathAfterRewrite()
    {
        var result = await RunPage<Page6>(path: "/friendly?a=b&c=d", handlerPath: "/friendly");

        StringAssert.Contains(result, "action=\"rewritten.aspx?a=b&amp;c=d\"");
    }

    [TestMethod]
    public async Task HtmlFormActionKeepsRewriteQueryOnFriendlyPath()
    {
        var result = await RunPage<Page7>(path: "/~/sample-site/ui/friendly-view.test?id=12345", handlerPath: "/~/sample-site/ui/friendly-view.test");

        StringAssert.Contains(result, "action=\"../../../internal/base.aspx?id=12345&amp;formname=RewrittenForm\"");
    }

    [TestMethod]
    public async Task HtmlFormActionKeepsEncodedNestedReturnUrlOnFriendlyRewrite()
    {
        var retUrl = "%2Fpreview%2Fitem.html%3Fbranch%3Dbranch-under-test";
        var path = "/~/sample-site/ui/friendly-view.test?id=12345&returl=" + retUrl;
        var result = await RunPage<Page8>(path: path, handlerPath: "/~/sample-site/ui/friendly-view.test");

        StringAssert.Contains(result, "action=\"../../../internal/base.aspx?id=12345&amp;returl=%2Fpreview%2Fitem.html%3Fbranch%3Dbranch-under-test&amp;formname=RewrittenForm\"");
    }

    [TestMethod]
    public async Task PostbackPreservesQueryStringOnRewriteTarget()
    {
        var result = await RunPagePost<Page9>(
            path: "/internal/base.aspx?id=12345&returl=%2Fpreview%2Fitem.html%3Fbranch%3Dbranch-under-test&formname=RewrittenForm",
            body: "__EVENTTARGET=&__EVENTARGUMENT=",
            handlerPath: "/internal/base.aspx");

        Assert.AreEqual("RewrittenForm|/preview/item.html?branch=branch-under-test", result);
    }

    [TestMethod]
    public async Task EncodedNestedReturnUrlDoesNotBecomeTopLevelQueryParameter()
    {
        var result = await RunPage<Page10>(
            path: "/path?id=12345&returl=%2Fpreview%2Fitem.html%3Fbranch%3Dbranch-under-test&formname=RewrittenForm",
            handlerPath: "/path");

        Assert.AreEqual("False||/preview/item.html?branch=branch-under-test", result);
    }

    [TestMethod]
    public void ChildControlPageFallsBackToParentPage()
    {
        var page = new Page1();
        var form = new HtmlForm();
        var child = new TextBox();

        page.Controls.Add(form);
        form.Controls.Add(child);
        child.Page = null;

        Assert.AreSame(page, child.Page);
    }

    [TestMethod]
    public void PageReferencesItself()
    {
        var page = new Page1();

        Assert.AreSame(page, page.Page);
    }

    [TestMethod]
    [Ignore("Currently not working")]
    public async Task PageWithForm()
    {
        // Arrange/Act
        var result = await RunPage<Page4>();

        // Assert
        Assert.AreEqual("<form method=\"post\" action=\"/path\"><div class=\"aspNetHidden\"</div></form>", result);
    }

    private sealed class PageWithRoutingAPI : Page
    {
        protected override void FrameworkInitialize()
        {
            Controls.Add(new LiteralControl("hello"));
            Label lbl = new Label();
            Controls.Add(lbl);
            Controls[1].ID = Controls[0].GetRouteUrl("ProductsByCategoryRoute", new { categoryName = "MyTest" });
        }
    }
    private sealed class Page1 : Page
    {
    }

    private sealed class Page2 : Page
    {
        protected override void Render(HtmlTextWriter writer)
        {
            writer.Write("hello");
        }
    }

    private sealed class Page3 : Page
    {
        protected override void FrameworkInitialize()
        {
            Controls.Add(new LiteralControl("hello"));
        }
    }

    private sealed class Page4 : Page
    {
        protected override void FrameworkInitialize()
        {
            base.FrameworkInitialize();

            var form = new HtmlForm();
            form.Controls.Add(new TextBox());

            Controls.Add(form);
        }
    }

    private sealed class Page5 : Page
    {
        protected override void Render(HtmlTextWriter writer)
        {
            writer.Write(Application != null ? "ok" : "null");
        }
    }

    private sealed class Page6 : Page
    {
        protected override void OnPreInit(EventArgs e)
        {
            Context.RewritePath("/rewritten.aspx", string.Empty, "a=b&c=d", false);
            base.OnPreInit(e);
        }

        protected override void FrameworkInitialize()
        {
            base.FrameworkInitialize();

            var form = new HtmlForm();
            form.Controls.Add(new TextBox());

            Controls.Add(form);
        }
    }

    private sealed class Page7 : Page
    {
        protected override void OnPreInit(EventArgs e)
        {
            Context.RewritePath("/internal/base.aspx", string.Empty, "id=12345&formname=RewrittenForm", false);
            base.OnPreInit(e);
        }

        protected override void FrameworkInitialize()
        {
            base.FrameworkInitialize();

            var form = new HtmlForm();
            form.Controls.Add(new TextBox());

            Controls.Add(form);
        }
    }

    private sealed class Page8 : Page
    {
        protected override void OnPreInit(EventArgs e)
        {
            Context.RewritePath("/internal/base.aspx", string.Empty, "id=12345&returl=%2Fpreview%2Fitem.html%3Fbranch%3Dbranch-under-test&formname=RewrittenForm", false);
            base.OnPreInit(e);
        }

        protected override void FrameworkInitialize()
        {
            base.FrameworkInitialize();

            var form = new HtmlForm();
            form.Controls.Add(new TextBox());

            Controls.Add(form);
        }
    }

    private sealed class Page9 : Page
    {
        protected override void Render(HtmlTextWriter writer)
        {
            writer.Write($"{Request.QueryString["formname"]}|{Request.QueryString["returl"]}");
        }
    }

    private sealed class Page10 : Page
    {
        protected override void Render(HtmlTextWriter writer)
        {
            writer.Write((Request.QueryString["branch"] != null) + "|" + Request.QueryString["branch"] + "|" + Request.QueryString["returl"]);
        }
    }

    private sealed class DisposablePage : Page
    {
        public static TaskCompletionSource<object?> DisposeSignal { get; private set; } = CreateDisposedSource();

        public static void ResetDisposed()
        {
            DisposeSignal = CreateDisposedSource();
        }

        protected override void FrameworkInitialize()
        {
            Controls.Add(new LiteralControl("disposed"));
        }

        public override void Dispose()
        {
            try
            {
                DisposeSignal.TrySetResult(null);
            }
            finally
            {
                base.Dispose();
            }
        }

        private static TaskCompletionSource<object?> CreateDisposedSource()
        {
            return new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

}
