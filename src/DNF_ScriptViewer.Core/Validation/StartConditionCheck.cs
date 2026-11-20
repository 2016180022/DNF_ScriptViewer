using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Expressions;
using DNF_ScriptViewer.Core.Loading;
using DNF_ScriptViewer.Core.Model;
using DNF_ScriptViewer.Core.Runtime;

namespace DNF_ScriptViewer.Core.Validation;

/// <summary>
/// 구간 시작값과 "시작 State로 들어오는 브랜치 조건" 대조 (SEG014, 경고만).
/// 시작 시점에 알 수 없는 것(입력·ActionEnd()·LoopCount()·CheckBuff()·keyDown())은 판단을 보류(Unknown)하고,
/// 들어오는 브랜치가 모두 시작값만으로 확실히 거짓일 때만 경고
/// </summary>
internal static class StartConditionCheck
{
    public static void Check(DataSet set, SegmentDef seg, string where, DiagnosticBag diag)
    {
        var a = set.ActionScript!;
        var incoming = AllBranches(a).Where(b => b.ActionName == seg.StartState && !b.ActionIgnore && b.Condition.Trim().Length > 0).ToList();
        if (incoming.Count == 0) return;

        var host = new Host(a, seg, new SkillContext(set.SkillInfo, set.Segments, seg));
        var ev = new ExprEvaluator();
        var results = incoming.Select(b => ev.Eval(b.Condition, host)).ToList();
        if (results.Any(r => r.IsUnknown || r.IsTruthy)) return;

        diag.Warn("SEG014", seg.Src,
            $"{where}: 시작 State {seg.StartState}로 들어오는 브랜치 {incoming.Count}개의 조건이 시작값으로는 모두 거짓 " +
            $"(Ex. {incoming[0].Src} \"{incoming[0].Condition}\")");
    }

    private static IEnumerable<BranchNode> AllBranches(ActionScriptData a)
    {
        IEnumerable<BranchNode> Walk(IEnumerable<ScriptNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n is BranchNode b) yield return b;
                else if (n is BranchBlockNode bb) foreach (var x in Walk(bb.Items)) yield return x;
            }
        }
        return a.ActionList.SelectMany(x => Walk(x.Nodes)).Concat(a.FuncList.SelectMany(f => Walk(f.Nodes)));
    }

    /// <summary>시작값으로 알 수 있는 것만 값으로, 나머지는 Unknown</summary>
    private sealed class Host : IEvalHost
    {
        private readonly Dictionary<string, RtValue> _known = new();
        private readonly SkillContext _skills;

        public Host(ActionScriptData a, SegmentDef seg, SkillContext skills)
        {
            _skills = skills;
            foreach (var v in a.Variables) _known[v.Name] = RtValue.ParseLiteral(v.Value) ?? RtValue.Unknown;
            foreach (var v in seg.StartVariables)
                if (_known.ContainsKey(v.Name)) _known[v.Name] = RtValue.ParseLiteral(v.Value) ?? RtValue.Unknown;
            foreach (var c in a.Constants)
                _known[c.Name] = c.Value.Trim().Length == 0 ? RtValue.Unknown : RtValue.ParseLiteral(c.Value) ?? RtValue.Unknown;
        }

        public RtValue Ident(string name) => _known.GetValueOrDefault(name, RtValue.Unknown);

        public RtValue Call(CallExpr call, Func<Expr, RtValue> eval) =>
            _skills.TryCall(call, eval, out var r) ? r : RtValue.Unknown;
    }
}
