using System.Net;
using DNF_ScriptViewer.Core.Loading;

namespace DNF_ScriptViewer.Web;

/// <summary>배포된 wwwroot/Data/ 를 HTTP로 읽음 (파일이 없으면 null)</summary>
public sealed class HttpDataSource(HttpClient http, string basePath = "Data/") : IDataSource
{
    public async Task<string?> ReadTextAsync(string relativePath)
    {
        // 다시 불러오기에서 바뀐 데이터를 바로 읽도록 브라우저 캐시를 쓰지 않음
        using var req = new HttpRequestMessage(HttpMethod.Get, basePath + relativePath);
        req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var res = await http.SendAsync(req);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }
}
