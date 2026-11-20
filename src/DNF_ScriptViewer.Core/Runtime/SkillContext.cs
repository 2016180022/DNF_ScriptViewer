using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Expressions;
using DNF_ScriptViewer.Core.Model;

namespace DNF_ScriptViewer.Core.Runtime;

/// <summary>
/// 구간 하나의 스킬 세팅 (DefaultSetting 위에 구간 Setting을 덮어씀)과 데이터 함수
/// (SkillLearned · getSkillValue · getSkillCoolTime). 런타임·검증기가 같이 씀
/// </summary>
public sealed class SkillContext
{
    private readonly SkillInfoData? _skills;
    private readonly Dictionary<int, (int? Level, int VP)> _settings = new();
    private readonly Action<string, string>? _warnOnce;

    /// <param name="warnOnce">(중복 방지 키, 메시지) — 같은 키는 한 번만 경고</param>
    public SkillContext(SkillInfoData? skills, SegmentsData? segments, SegmentDef? segment, Action<string, string>? warnOnce = null)
    {
        _skills = skills;
        _warnOnce = warnOnce;
        void Apply(SkillSetting s)
        {
            if (s.Skill is not int k) return;
            var cur = _settings.GetValueOrDefault(k);
            _settings[k] = (s.Level ?? cur.Level, s.VP ?? cur.VP);
        }
        if (segments is not null) foreach (var s in segments.DefaultSettings) Apply(s);
        if (segment is not null) foreach (var s in segment.Settings) Apply(s);
    }

    public SkillDef? Skill(int key) => _skills?.Skills.GetValueOrDefault(key);

    /// <summary>세팅에 Level이 있으면 습득한 것으로 봄</summary>
    public bool Learned(int key) => _settings.TryGetValue(key, out var s) && s.Level is >= 1;

    public int VP(int key) => _settings.TryGetValue(key, out var s) ? s.VP : 0;

    /// <summary>사용 레벨. 세팅에 없으면 1 (최대 레벨이 1이 아닌 스킬은 경고)</summary>
    public int Level(int key)
    {
        if (_settings.TryGetValue(key, out var s) && s.Level is int lv) return lv;
        if (Skill(key) is { MaxLevel: > 1 } sk)
            _warnOnce?.Invoke($"level:{key}", $"스킬 {key} {sk.Name}의 사용 레벨이 구간 세팅에 없어 1레벨로 계산");
        return 1;
    }

    /// <summary>SkillLearned(n): 스킬 키면 습득 여부, 스킬 키 × 100 + VP면 그 VP 선택 여부</summary>
    public bool SkillLearned(int n)
    {
        if (Skill(n) is not null) return Learned(n);
        return Learned(n / 100) && VP(n / 100) == n % 100;
    }

    /// <summary>getSkillValue(키, N) = 사용 레벨의 {valueN}. 숫자가 아니면 0 + 경고</summary>
    public double SkillValue(int key, int n) => LevelValue(key, $"Value{n}", $"getSkillValue({key}, {n})");

    /// <summary>getSkillCoolTime(키) = 사용 레벨의 CoolTime (ms)</summary>
    public double SkillCoolTime(int key) => LevelValue(key, "CoolTime", $"getSkillCoolTime({key})");

    private double LevelValue(int key, string name, string what)
    {
        var info = Skill(key)?.BaseLevelInfo;
        var v = info?.GetValue(name, Level(key));
        if (v is null)
        {
            _warnOnce?.Invoke($"value:{key}:{name}", $"{what} 값을 계산할 수 없어 0으로 계산 (SkillInfo 확인)");
            return 0;
        }
        return v.Value;
    }

    /// <summary>데이터 함수 호출을 처리하면 true (런타임·검증기 공용)</summary>
    public bool TryCall(CallExpr call, Func<Expr, RtValue> eval, out RtValue result)
    {
        int? Arg(int i)
        {
            var v = eval(call.Args[i]);
            return v.Kind == RtKind.Number ? (int)v.Num : null;
        }
        result = RtValue.Unknown;
        switch (call.Name)
        {
            case "SkillLearned":
                if (call.Args.Count == 1 && Arg(0) is int n) result = RtValue.Of(SkillLearned(n));
                return true;
            case "getSkillValue":
                if (call.Args.Count == 2 && Arg(0) is int k && Arg(1) is int i) result = RtValue.Of(SkillValue(k, i));
                return true;
            case "getSkillCoolTime":
                if (call.Args.Count == 1 && Arg(0) is int c) result = RtValue.Of(SkillCoolTime(c));
                return true;
        }
        return false;
    }
}

/// <summary>BuffInfo 수치식(Remain·Param) 평가용: 데이터 함수만 있고 변수·입력은 없음</summary>
internal sealed class DataEvalHost(SkillContext skills) : IEvalHost
{
    public RtValue Ident(string name) => RtValue.Unknown;
    public RtValue Call(CallExpr call, Func<Expr, RtValue> eval) =>
        skills.TryCall(call, eval, out var r) ? r : RtValue.Unknown;
}
