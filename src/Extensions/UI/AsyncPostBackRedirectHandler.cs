// MIT License.

namespace System.Web.UI;

internal static class AsyncPostBackRedirectHandler
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            typeof(HttpResponse)
                .GetEvent("Redirecting", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?.AddEventHandler(null, (EventHandler)OnRedirecting);
        }
    }

    private static void OnRedirecting(object sender, EventArgs e)
    {
        var response = (HttpResponse)sender;
        var context = HttpContext.Current;

        if (context == null || !PageRequestManager.IsAsyncPostBackRequest(new HttpRequestWrapper(context.Request)))
        {
            return;
        }

        string redirectLocation = response.RedirectLocation;
        var cookies = new List<HttpCookie>(response.Cookies.Count);
        for (int i = 0; i < response.Cookies.Count; i++)
        {
            cookies.Add(response.Cookies[i]);
        }

        response.ClearContent();
        response.ClearHeaders();
        for (int i = 0; i < cookies.Count; i++)
        {
            response.AppendCookie(cookies[i]);
        }

        response.Cache.SetCacheability(HttpCacheability.NoCache);
        response.ContentType = "text/plain";

        context.Items[PageRequestManager.AsyncPostBackRedirectLocationKey] = redirectLocation;
        var isRequestBeingRedirectedProperty = typeof(HttpResponse).GetProperty(nameof(HttpResponse.IsRequestBeingRedirected));
        if (isRequestBeingRedirectedProperty?.CanWrite == true)
        {
            isRequestBeingRedirectedProperty.SetValue(response, true);
        }

        PageRequestManager.EncodeString(response.Output, PageRequestManager.UpdatePanelVersionToken, String.Empty, PageRequestManager.UpdatePanelVersionNumber);
        redirectLocation = String.Join(" ", redirectLocation.Split(' ').Select(HttpUtility.UrlEncode));
        PageRequestManager.EncodeString(response.Output, PageRequestManager.PageRedirectToken, String.Empty, redirectLocation);
    }
}
