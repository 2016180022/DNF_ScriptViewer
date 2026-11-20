using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Model;

namespace DNF_ScriptViewer.Core.Loading;

internal static class XmlHelpers
{
    /// <summary>
    /// 보안: DTD를 허용하지 않고 외부 리소스를 읽지 않음 (XXE·엔티티 확장 공격 방지).
    /// 툴 데이터는 DTD를 쓰지 않으므로 DTD가 들어 있으면 형식 오류로 처리
    /// </summary>
    private static readonly XmlReaderSettings SafeSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
    };

    /// <summary>줄 번호를 보존해서 읽음. XML 형식 오류면 Error 진단을 남기고 null</summary>
    public static XDocument? Parse(string file, string text, DiagnosticBag diag)
    {
        try
        {
            using var sr = new StringReader(text);
            using var reader = XmlReader.Create(sr, SafeSettings);
            return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            diag.Error("XML001", new SourceRef(file, ex.LineNumber), $"XML 형식 오류 ({ex.LinePosition}열): {ex.Message}");
            return null;
        }
    }

    public static SourceRef Src(this XElement e, string file) => new(file, ((IXmlLineInfo)e).LineNumber);

    public static AttrList Attrs(this XElement e) =>
        new(e.Attributes().Select(a => new KeyValuePair<string, string>(a.Name.LocalName, a.Value)).ToList());

    public static string Str(this XElement e, string name) => e.Attribute(name)?.Value ?? "";
    public static string? StrOrNull(this XElement e, string name) => e.Attribute(name)?.Value;

    /// <summary>정수 속성. 없거나 빈칸이면 null, 숫자가 아니면 null + 경고</summary>
    public static int? Int(this XElement e, string name, string file, DiagnosticBag diag, string code)
    {
        var raw = e.Attribute(name)?.Value?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
        diag.Warn(code, e.Src(file), $"<{e.Name.LocalName}> {name}=\"{raw}\"는 정수가 아님");
        return null;
    }

    /// <summary>"801001, 2001" 같은 키 목록</summary>
    public static List<int> IntList(string raw, SourceRef src, string what, DiagnosticBag diag, string code)
    {
        var list = new List<int>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) list.Add(v);
            else diag.Warn(code, src, $"{what} 목록의 \"{part}\"는 정수가 아님");
        }
        return list;
    }
}
