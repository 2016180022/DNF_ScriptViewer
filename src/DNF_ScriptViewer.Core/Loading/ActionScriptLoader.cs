using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Model;

namespace DNF_ScriptViewer.Core.Loading;

/// <summary>
/// ActionScript.xml 로더. 최상위: CharacterVariables / Constants / &lt;이름_Func&gt;(정의) / 그 외는 액션.
/// 액션 안의 &lt;이름_Func /&gt;는 참조 — 정의·참조는 위치로 구분
/// </summary>
public static partial class ActionScriptLoader
{
    public const string FuncSuffix = "_Func";

    public static ActionScriptData? Load(string file, string text, DiagnosticBag diag)
    {
        var doc = XmlHelpers.Parse(file, text, diag);
        if (doc?.Root is null) return null;

        var data = new ActionScriptData { File = file };
        foreach (var e in doc.Root.Elements())
        {
            var name = e.Name.LocalName;
            var src = e.Src(file);
            switch (name)
            {
                case "CharacterVariables":
                    foreach (var v in e.Elements())
                    {
                        if (v.Name.LocalName != "Variable")
                        {
                            diag.Warn("AS002", v.Src(file), $"CharacterVariables 안의 알 수 없는 요소 <{v.Name.LocalName}>");
                            continue;
                        }
                        var vn = v.Str("Name");
                        if (data.FindVariable(vn) is not null) diag.Warn("AS001", v.Src(file), $"캐릭터 변수 이름 중복: {vn}");
                        data.Variables.Add(new CharacterVariable(vn, v.Str("Value"), v.Src(file)));
                    }
                    break;

                case "Constants":
                    foreach (var c in e.Elements())
                    {
                        if (c.Name.LocalName != "Constant")
                        {
                            diag.Warn("AS002", c.Src(file), $"Constants 안의 알 수 없는 요소 <{c.Name.LocalName}>");
                            continue;
                        }
                        var cn = c.Str("Name");
                        if (data.FindConstant(cn) is not null) diag.Warn("AS001", c.Src(file), $"상수 이름 중복: {cn}");
                        data.Constants.Add(new Constant(cn, c.Str("Value"), c.Str("Desc"), c.Src(file)));
                    }
                    break;

                default:
                    if (name.EndsWith(FuncSuffix, StringComparison.Ordinal))
                    {
                        var func = new FuncDef(name, ParseNodes(e.Elements(), file, diag), src);
                        if (!data.Funcs.TryAdd(name, func)) diag.Warn("AS001", src, $"Func 정의 중복: {name}");
                        data.FuncList.Add(func);
                    }
                    else
                    {
                        var action = ParseAction(e, file, diag);
                        if (!data.Actions.TryAdd(name, action)) diag.Warn("AS001", src, $"액션 이름 중복: {name}");
                        data.ActionList.Add(action);
                    }
                    break;
            }
        }
        return data;
    }

    private static ActionDef ParseAction(XElement e, string file, DiagnosticBag diag)
    {
        var skillRaw = e.Str("SkillKey").Trim();
        int? skillKey = null;
        if (skillRaw.Length > 0 && skillRaw != "None")
        {
            if (int.TryParse(skillRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k)) skillKey = k;
            else diag.Warn("AS016", e.Src(file), $"<{e.Name.LocalName}> SkillKey=\"{skillRaw}\"는 정수 또는 None이 아님");
        }
        return new ActionDef(e.Name.LocalName, e.Attrs(), e.Src(file),
            skillKey, e.Str("CoolTime"), e.Str("Animation"),
            TimeValue.Parse(e.StrOrNull("StartTime")), TimeValue.Parse(e.StrOrNull("EndTime")),
            EnumParse.Parse<ActionSpeedType>(e.StrOrNull("ActionSpeedType")),
            EnumParse.Parse<GuardType>(e.StrOrNull("GuardType")),
            ParseNodes(e.Elements(), file, diag));
    }

    internal static List<ScriptNode> ParseNodes(IEnumerable<XElement> elements, string file, DiagnosticBag diag)
    {
        var list = new List<ScriptNode>();
        foreach (var e in elements) list.Add(ParseNode(e, file, diag));
        return list;
    }

    private static ScriptNode ParseNode(XElement e, string file, DiagnosticBag diag)
    {
        var name = e.Name.LocalName;
        var attrs = e.Attrs();
        var src = e.Src(file);
        switch (name)
        {
            case "Variable":
                return new VariableNode(name, attrs, src, e.Str("Name"),
                    TimeValue.Parse(e.StrOrNull("StartTime")), TimeValue.Parse(e.StrOrNull("EndTime")),
                    e.Str("Value"), e.StrOrNull("Condition"));

            case "Move":
                return new MoveNode(name, attrs, src, TimeValue.Parse(e.StrOrNull("StartTime")),
                    e.StrOrNull("Speed"), e.StrOrNull("Distance"), e.StrOrNull("Direction"), e.StrOrNull("Condition"));

            case "AttackFrame":
                return new AttackFrameNode(name, attrs, src,
                    EnumParse.Parse<AttackType>(e.StrOrNull("AttackType")),
                    EnumParse.Parse<RangeType>(e.StrOrNull("RangeType")),
                    TimeValue.Parse(e.StrOrNull("StartTime")), TimeValue.Parse(e.StrOrNull("EndTime")),
                    e.StrOrNull("Condition"), ParseNodes(e.Elements(), file, diag));

            case "BuffFrame":
            {
                var raw = e.Str("BuffIndex");
                var (entries, valid) = ParseBuffIndex(raw);
                int? key = int.TryParse(e.Str("SkillKey").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var k) ? k : null;
                return new BuffFrameNode(name, attrs, src, key, raw, entries, valid,
                    TimeValue.Parse(e.StrOrNull("StartTime")), e.StrOrNull("Condition"));
            }

            case "Branch":
                return new BranchNode(name, attrs, src, e.Str("ActionName"), e.Str("Condition"),
                    e.Str("ActionIgnore") == "True");

            case "NormalBranch":
            case "SuperCancelBranch":
                return new BranchBlockNode(name, attrs, src,
                    name == "NormalBranch" ? BranchBlockKind.Normal : BranchBlockKind.SuperCancel,
                    TimeValue.Parse(e.StrOrNull("StartTime")), TimeValue.Parse(e.StrOrNull("EndTime")),
                    ParseNodes(e.Elements(), file, diag));

            default:
                if (name.EndsWith(FuncSuffix, StringComparison.Ordinal))
                    return new FuncRefNode(name, attrs, src, name, e.HasElements);
                return new UnknownNode(name, attrs, src);
        }
    }

    [GeneratedRegex(@"^(\d+)(?:\((\d+)\))?$")]
    private static partial Regex BuffIndexToken();

    /// <summary>"0 1" / "0(200) 0(400)" — 공백 구분, 괄호는 딜레이 ms</summary>
    public static (List<BuffIndexEntry> Entries, bool Valid) ParseBuffIndex(string raw)
    {
        var list = new List<BuffIndexEntry>();
        var valid = true;
        foreach (var token in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = BuffIndexToken().Match(token);
            if (!m.Success) { valid = false; continue; }
            var delay = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            list.Add(new BuffIndexEntry(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), delay));
        }
        return (list, valid);
    }
}
