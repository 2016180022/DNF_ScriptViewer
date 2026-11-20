using System.Globalization;
using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Expressions;
using DNF_ScriptViewer.Core.Loading;
using DNF_ScriptViewer.Core.Model;

namespace DNF_ScriptViewer.Core.Runtime;

/// <summary>
/// 런타임: 구간 하나를 1ms 틱으로 끝까지 선계산.
/// 틱 순서 = ① 끝난 버프 제거 ② 장전 ③ 이 시각의 입력 눌림 등록 ④ 예약된 타격 ⑤ 현재 액션의 이 시각 줄 실행 ⑥ 브랜치 평가(파일 순서, 처음 참인 것)
/// ⑦ 장전 타이머 시작. 브랜치를 타면 이전 액션의 Escape 줄 → 새 액션 진입(쿨타임 시작) → 새 액션의 0ms 줄을 같은 틱에 실행. 전이는 한 틱에 한 번
/// </summary>
public static class SegmentRunner
{
    /// <summary>EndTime이 비어 있을 때 쓰는 임시값 (반드시 RUN001 경고)</summary>
    public const int TempEndTimeMs = 500;
    /// <summary>구간 길이(VideoStart·VideoEnd)가 없을 때 쓰는 임시값</summary>
    public const int TempDurationMs = 10000;

    public static SegmentResult Run(DataSet set, SegmentDef segment, InputSequence? input) =>
        new Sim(set, segment, input).Run();
}

internal sealed class Sim : IEvalHost
{
    private sealed class Press
    {
        public required InputEvent E;
        public int Time;
        public int HoldEnd;
        public bool Pending;
        public bool Done;
    }

    private sealed class ActiveBuff
    {
        public required BuffDef Def;
        public int? End;
        public int SpanIndex;
    }

    private sealed class Recharge
    {
        public required string Var;
        public double Max;
        public int Period;
        public int? TimerEnd;
        public SourceRef Src;
    }

    private readonly record struct PendingHit(int Time, int SkillKey, int Index, SourceRef Src);

    private readonly DataSet _set;
    private readonly ActionScriptData _a;
    private readonly SegmentDef _seg;
    private readonly SegmentResult _r;
    private readonly SkillContext _skills;
    private readonly DataEvalHost _dataHost;
    private readonly ExprEvaluator _ev = new();
    private readonly HashSet<string> _warned = new();
    private readonly Dictionary<string, RtValue> _vars = new();
    private readonly Dictionary<string, RtValue> _consts = new();
    private readonly List<Press> _presses = new();
    private readonly Dictionary<int, int> _cool = new();
    private readonly Dictionary<int, ActiveBuff> _playerBuffs = new();
    private readonly List<PendingHit> _pending = new();
    private readonly List<AttackFrameNode> _activated = new();
    private readonly HashSet<SourceRef> _skippedThisState = new();
    private readonly List<Recharge> _recharges = new();
    private readonly Dictionary<int, double> _multiplier = new();
    private readonly int _duration;

    private int _t;
    private ActionDef? _cur;
    private int _enter;
    private int _loop;

    public Sim(DataSet set, SegmentDef seg, InputSequence? input)
    {
        _set = set;
        _seg = seg;
        _a = set.ActionScript ?? new ActionScriptData();
        _duration = seg.DurationMs is int d && d > 0 ? d : SegmentRunner.TempDurationMs;
        _r = new SegmentResult { Segment = seg.Name, DurationMs = _duration };
        _skills = new SkillContext(set.SkillInfo, set.Segments, seg, (key, msg) => WarnOnce("RUN003", key, seg.Src, msg));
        _dataHost = new DataEvalHost(_skills);

        if (seg.DurationMs is not > 0)
            _r.Warnings.Warn("RUN001", seg.Src, $"구간 길이(VideoStart·VideoEnd)가 없어 임시값 {SegmentRunner.TempDurationMs}ms로 계산");

        InitVariables();
        InitInputs(input);
        InitRecharges();
    }

    // ============================ 초기화 ============================

    private void InitVariables()
    {
        foreach (var cv in _a.Variables)
        {
            var v = RtValue.ParseLiteral(cv.Value);
            if (v is null) WarnOnce("RUN003", $"var:{cv.Name}", cv.Src, $"캐릭터 변수 {cv.Name}의 값 \"{cv.Value}\"를 읽을 수 없어 None으로 계산");
            _vars[cv.Name] = v ?? RtValue.None;
        }
        foreach (var sv in _seg.StartVariables)
        {
            if (!_vars.ContainsKey(sv.Name)) continue; // 검증기 SEG005
            _vars[sv.Name] = RtValue.ParseLiteral(sv.Value) ?? _vars[sv.Name];
        }
        foreach (var (k, v) in _vars) _r.InitialVars[k] = v;

        foreach (var c in _a.Constants)
        {
            var v = RtValue.ParseLiteral(c.Value);
            if (c.Value.Trim().Length == 0 || v is null)
            {
                _r.Warnings.Warn("RUN001", c.Src, $"상수 {c.Name}의 Value가 비어 있어 임시값 0으로 계산");
                v = RtValue.Of(0);
            }
            _consts[c.Name] = v.Value;
        }
    }

    private void InitInputs(InputSequence? input)
    {
        if (input is null)
        {
            _r.Warnings.Warn("RUN004", _seg.Src, $"입력 시퀀스({_seg.Input})가 없어 입력 없이 계산");
            return;
        }
        foreach (var e in input.Inputs.Where(e => e.Time is >= 0).OrderBy(e => e.Time))
            _presses.Add(new Press { E = e, Time = e.Time!.Value, HoldEnd = e.Time!.Value + (e.Hold ?? 0) });
    }

    /// <summary>장전(StockRecharge): 배운 스킬이면 구간 시작부터 상시 적용</summary>
    private void InitRecharges()
    {
        if (_set.BuffInfo is null) return;
        foreach (var b in _set.BuffInfo.BuffList.Where(b => b.BuffType == BuffType.StockRecharge))
        {
            if (b.SkillKey is int sk && !_skills.Learned(sk)) continue;
            var name = b.Params.ElementAtOrDefault(0)?.Trim() ?? "";
            if (!_vars.ContainsKey(name)) continue; // 검증기 LNK009
            var max = EvalData(b.Params.ElementAtOrDefault(1), b.Src, $"버프 {b.Key} Param02");
            var period = (int)Math.Round(EvalData(b.Params.ElementAtOrDefault(2), b.Src, $"버프 {b.Key} Param03"));
            if (period <= 0)
            {
                WarnOnce("RUN003", $"recharge:{b.Key}", b.Src, $"버프 {b.Key} {b.Name}: 장전 시간이 0 이하라 장전하지 않음");
                continue;
            }
            _recharges.Add(new Recharge { Var = name, Max = max, Period = period, Src = b.Src });
        }
    }

    // ============================ 실행 ============================

    public SegmentResult Run()
    {
        if (!_a.Actions.TryGetValue(_seg.StartState, out var start))
        {
            _r.Warnings.Warn("RUN003", _seg.Src, $"시작 State {_seg.StartState}가 없어 계산하지 않음");
            return _r;
        }

        _t = 0;
        Enter(start);
        for (_t = 0; _t <= _duration; _t++)
        {
            ExpireBuffs();
            RechargeTick();
            RegisterPresses();
            FireHits();
            var lt = _t - _enter;
            if (lt > 0)
            {
                RunNodes(lt);
                EvaluateBranches(lt);
            }
            RechargeStart();
        }
        Finish();
        return _r;
    }

    private void Finish()
    {
        _t = _duration;
        CloseState();
        foreach (var p in _presses.Where(p => !p.Done))
            _r.Inputs.Add(new InputUse(p.Time, p.E.Command, InputResult.Unused, null, p.E.Src, null));
        _r.Inputs.Sort((x, y) => x.PressTime.CompareTo(y.PressTime));

        // Mark 대조: Mark 순서대로, 앞 Mark와 짝지은 상태 다음부터 같은 상태로 처음 "바뀐" 시각
        // (같은 액션으로 다시 들어간 것 — Loop·안전장치 재진입 — 은 상태 변경이 아니므로 제외. Mark는 상태 변경만 기록)
        var idx = 0;
        foreach (var m in _seg.Marks)
        {
            int? sim = null;
            for (var j = idx; j < _r.States.Count; j++)
            {
                if (_r.States[j].State != m.State) continue;
                if (j > 0 && _r.States[j - 1].State == m.State) continue;
                sim = _r.States[j].Start;
                idx = j + 1;
                break;
            }
            _r.Marks.Add(new MarkCompare(m, sim));
        }
    }

    // ---------- 상태 진입·전이 ----------

    private int _stateStart;

    private void CloseState()
    {
        if (_cur is not null) _r.States.Add(new StateSpan(_cur.Name, _stateStart, _t, _loop, _cur.Src));
    }

    private void Enter(ActionDef a)
    {
        var sameAction = _cur == a;
        CloseState();
        _loop = sameAction ? _loop + 1 : 0;
        _cur = a;
        _enter = _t;
        _stateStart = _t;
        _activated.Clear();
        _skippedThisState.Clear();
        StartCoolTime(a);
        RunNodes(0);
    }

    /// <summary>
    /// 쿨타임: 스킬 키 단위. 값과 시작 시점은 들어간 액션의 CoolTime (숫자 ms 또는 식).
    /// None은 타이머를 만들지 않고, Continue는 앞 액션의 쿨타임을 이어감
    /// </summary>
    private void StartCoolTime(ActionDef a)
    {
        if (a.SkillKey is not int k) return;
        var ct = a.CoolTime.Trim();
        if (ct.Length == 0)
        {
            WarnOnce("RUN001", $"cool:{a.Name}", a.Src, $"스킬 액션 {a.Name}의 CoolTime이 비어 있어 쿨타임 없이 계산");
            return;
        }
        if (ct is "None" or "Continue") return;
        var ms = int.TryParse(ct, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n : (int)Math.Round(_ev.Eval(ct, this).AsNumber);
        _cool[k] = _t + ms;
        if (ms > 0) _r.CoolTimes.Add(new CoolTimeSpan(k, a.Name, _t, _t + ms, a.Src)); // 0ms(스탠바이 투척 등)는 기록하지 않음
    }

    private void Transition(ActionDef target, BranchNode br, int index)
    {
        var cur = _cur!;
        // 이전 액션을 나갈 때(Escape): 액션의 줄 → 발동한 AttackFrame 안의 줄
        foreach (var v in cur.Nodes.OfType<VariableNode>().Where(v => v.StartTime.Kind == TimeKind.Escape)) ApplyVariable(v);
        foreach (var af in _activated)
            foreach (var v in af.Children.OfType<VariableNode>().Where(v => v.StartTime.Kind == TimeKind.Escape)) ApplyVariable(v);

        _r.Transitions.Add(new Transition(_t, cur.Name, target.Name, index, br.Condition, br.Src));

        // 선입력: 상태가 바뀌면 쓰지 않은 눌림은 버림
        foreach (var p in _presses.Where(p => p.Pending))
        {
            p.Pending = false;
            p.Done = true;
            _r.Inputs.Add(new InputUse(p.Time, p.E.Command, InputResult.Discarded, _t, p.E.Src, null));
        }
        Enter(target);
    }

    // ---------- 액션의 줄 실행 ----------

    private void RunNodes(int lt)
    {
        var a = _cur!;
        foreach (var n in a.Nodes)
        {
            switch (n)
            {
                case VariableNode v when v.StartTime.Kind != TimeKind.Escape:
                {
                    var s = Time(v.StartTime, v.Src, "Variable StartTime", 0);
                    var ranged = v.EndTime.Kind is TimeKind.Ms or TimeKind.ActionEnd;
                    if (ranged ? (s <= lt && lt <= Time(v.EndTime, v.Src, "Variable EndTime", 0)) : lt == s)
                        ApplyVariable(v);
                    break;
                }
                case AttackFrameNode af:
                    if (lt == Time(af.StartTime, af.Src, "AttackFrame StartTime", 0)) Activate(af, _t);
                    break;
                case BuffFrameNode bf:
                {
                    var s = bf.Attrs.Has("StartTime") ? Time(bf.StartTime, bf.Src, "BuffFrame StartTime", 0) : 0;
                    if (lt == s && Cond(bf.Condition)) ScheduleBuffFrame(bf, _t);
                    break;
                }
                // Move: 위치는 계산하지 않음 (기록만). Func 참조·브랜치는 EvaluateBranches에서
            }
        }
    }

    private void ApplyVariable(VariableNode v)
    {
        if (!Cond(v.Condition)) return;
        if (_consts.ContainsKey(v.Name)) return; // 상수는 바꾸지 않음 (검증기 AS010)
        if (v.Value.Trim().Length == 0)
        {
            WarnOnce("RUN005", $"empty:{v.Src}", v.Src, $"{_cur?.Name} <Variable {v.Name}>의 Value가 비어 있어 건너뜀");
            return;
        }
        Set(v.Name, _ev.Eval(v.Value, this), v.Src);
    }

    private void Set(string name, RtValue value, SourceRef src)
    {
        var old = _vars.GetValueOrDefault(name, RtValue.None);
        _vars[name] = value;
        if (old != value) _r.Vars.Add(new VarChange(_t, name, old, value, src));
    }

    // ---------- 공격·버프 ----------

    /// <summary>AttackFrame 발동: 그 시점에 범위를 독립적으로 판정 (캐릭터 이동·액션 변경과 무관하게 끝까지 맞음). 안의 AttackFrame도 같음</summary>
    private void Activate(AttackFrameNode af, int at)
    {
        if (!Cond(af.Condition)) return;
        _activated.Add(af);
        foreach (var child in af.Children)
        {
            switch (child)
            {
                case BuffFrameNode bf:
                {
                    if (!Cond(bf.Condition)) break;
                    var off = bf.Attrs.Has("StartTime") ? Time(bf.StartTime, bf.Src, "BuffFrame StartTime", 0) : 0;
                    if (af.AttackType == AttackType.RepeatAttack)
                    {
                        // 지속 공격: 시작 후 RepeatTick마다, EndTime까지 (Ex. 3000/500 → 500·1000·…·3000ms에 6번)
                        var end = Time(af.EndTime, af.Src, "RepeatAttack EndTime", 0);
                        if (!int.TryParse(af.Attrs["RepeatTick"], out var tick) || tick <= 0)
                        {
                            WarnOnce("RUN003", $"tick:{af.Src}", af.Src, "RepeatAttack의 RepeatTick이 0보다 큰 정수가 아니라 타격하지 않음");
                            break;
                        }
                        for (var k = tick; k <= end; k += tick) ScheduleBuffFrame(bf, at + off + k);
                    }
                    else ScheduleBuffFrame(bf, at + off);
                    break;
                }
                case AttackFrameNode nested:
                    Activate(nested, at + Time(nested.StartTime, nested.Src, "AttackFrame StartTime", 0));
                    break;
                case VariableNode { StartTime.Kind: not TimeKind.Escape } v:
                    WarnOnce("RUN003", $"afvar:{v.Src}", v.Src, "AttackFrame 안의 Variable은 Escape만 계산함");
                    break;
            }
        }
    }

    private void ScheduleBuffFrame(BuffFrameNode bf, int at)
    {
        if (bf.SkillKey is not int k) return; // 검증기 LNK001
        foreach (var e in bf.BuffIndex)
        {
            var hit = new PendingHit(at + e.DelayMs, k, e.Index, bf.Src);
            if (hit.Time <= _t) Fire(hit);
            else _pending.Add(hit);
        }
    }

    private void FireHits()
    {
        if (_pending.Count == 0) return;
        var due = _pending.Where(h => h.Time == _t).ToList();
        if (due.Count == 0) return;
        _pending.RemoveAll(h => h.Time == _t);
        foreach (var h in due) Fire(h);
    }

    /// <summary>데이터 링크: BuffFrame(스킬 키, BuffIndex) → SkillInfo BuffKey 목록 → BuffInfo</summary>
    private void Fire(PendingHit h)
    {
        var skill = _skills.Skill(h.SkillKey);
        if (skill is null || h.Index >= skill.BuffKeys.Count) return; // 검증기 LNK001·LNK003
        var key = skill.BuffKeys[h.Index];
        if (_set.BuffInfo?.Buffs.GetValueOrDefault(key) is not { } buff) return; // 검증기 LNK004

        switch (buff.BuffType)
        {
            case BuffType.Attack:
                _r.Hits.Add(new HitRecord(_t, h.SkillKey, key, buff.Name,
                    EvalData(buff.Params.ElementAtOrDefault(0), buff.Src, $"버프 {key} Param01"), Multiplier(key), h.Src));
                break;
            case BuffType.Debuff:
                // 상태 이상: 허수아비에 걸린 것으로 기록만
                _r.Buffs.Add(new BuffSpan(key, buff.Name, BuffTarget.Enemy, _t, RemainEnd(buff), h.Src));
                break;
            case BuffType.State:
                ApplyPlayerBuff(buff, h.Src);
                break;
            default:
                WarnOnce("RUN003", $"bufftype:{key}", h.Src, $"버프 {key} {buff.Name}({buff.BuffTypeRaw})는 BuffFrame으로 거는 종류가 아님");
                break;
        }
    }

    private int? RemainEnd(BuffDef b) =>
        b.Remain.Trim().Length == 0 ? null : _t + (int)Math.Round(EvalData(b.Remain, b.Src, $"버프 {b.Key} Remain"));

    /// <summary>지속 상태(CheckBuff로 확인). StackType None = 이미 있으면 그대로, Refresh·Stack = 끝나는 시각 갱신</summary>
    private void ApplyPlayerBuff(BuffDef b, SourceRef src)
    {
        var end = RemainEnd(b);
        if (_playerBuffs.TryGetValue(b.Key, out var ab))
        {
            if (b.StackType == StackType.None) return;
            ab.End = end;
            _r.Buffs[ab.SpanIndex] = _r.Buffs[ab.SpanIndex] with { End = end };
            return;
        }
        _r.Buffs.Add(new BuffSpan(b.Key, b.Name, BuffTarget.Player, _t, end, src));
        _playerBuffs[b.Key] = new ActiveBuff { Def = b, End = end, SpanIndex = _r.Buffs.Count - 1 };
    }

    private void ExpireBuffs()
    {
        foreach (var key in _playerBuffs.Where(kv => kv.Value.End is int e && e <= _t).Select(kv => kv.Key).ToList())
            _playerBuffs.Remove(key);
    }

    /// <summary>패시브 공격력 증가(BuffEdit, 배운 스킬만)의 곱연산 배율</summary>
    private double Multiplier(int buffKey)
    {
        if (_multiplier.TryGetValue(buffKey, out var m)) return m;
        m = 1;
        foreach (var b in _set.BuffInfo?.BuffList.Where(b => b.BuffType == BuffType.BuffEdit) ?? Enumerable.Empty<BuffDef>())
        {
            if (b.SkillKey is int sk && !_skills.Learned(sk)) continue;
            var targets = (b.Params.ElementAtOrDefault(0) ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!targets.Contains(buffKey.ToString(CultureInfo.InvariantCulture))) continue;
            m *= 1 + EvalData(b.Params.ElementAtOrDefault(1), b.Src, $"버프 {b.Key} Param02") / 100;
        }
        _multiplier[buffKey] = m;
        return m;
    }

    // ---------- 장전 ----------

    /// <summary>타이머가 다 되면 +1, 아직 최대보다 적으면 이어서 다시 시작</summary>
    private void RechargeTick()
    {
        foreach (var r in _recharges)
        {
            if (r.TimerEnd != _t) continue;
            var now = _vars[r.Var].AsNumber + 1;
            Set(r.Var, RtValue.Of(now), r.Src);
            r.TimerEnd = now < r.Max ? _t + r.Period : null;
        }
    }

    /// <summary>스택이 최대보다 적어진 순간 타이머 시작. 도는 중에 더 줄어도 초기화하지 않음</summary>
    private void RechargeStart()
    {
        foreach (var r in _recharges)
            if (r.TimerEnd is null && _vars[r.Var].AsNumber < r.Max) r.TimerEnd = _t + r.Period;
    }

    // ---------- 입력 ----------

    private void RegisterPresses()
    {
        foreach (var p in _presses)
            if (p.Time == _t) p.Pending = true;
    }

    private void Consume(BranchNode br)
    {
        foreach (var cmd in _ev.PressCommands(br.Condition))
        {
            var p = _presses.FirstOrDefault(p => p.Pending && p.E.Command == cmd);
            if (p is null) continue;
            p.Pending = false;
            p.Done = true;
            _r.Inputs.Add(new InputUse(p.Time, cmd, InputResult.Used, _t, p.E.Src, br.Src));
        }
    }

    // ---------- 브랜치 ----------

    /// <summary>파일 순서대로, 처음 참이 되는 브랜치를 탐. 액션 맨 위의 Func 참조(강제 착지 등)는 항상 열려 있음, 블록은 StartTime~EndTime 동안만</summary>
    private void EvaluateBranches(int lt)
    {
        var a = _cur!;
        var list = new List<BranchNode>();
        foreach (var n in a.Nodes)
        {
            switch (n)
            {
                case BranchNode br: list.Add(br); break;
                case FuncRefNode fr: Expand(fr, list, new HashSet<string>()); break;
                case BranchBlockNode bb:
                {
                    var s = Time(bb.StartTime, bb.Src, $"{bb.Element} StartTime", 0);
                    var e = bb.EndTime.Kind == TimeKind.Empty ? EndMs(a) : Time(bb.EndTime, bb.Src, $"{bb.Element} EndTime", EndMs(a));
                    if (s <= lt && lt <= e)
                        foreach (var item in bb.Items)
                        {
                            if (item is BranchNode b) list.Add(b);
                            else if (item is FuncRefNode f) Expand(f, list, new HashSet<string>());
                        }
                    break;
                }
            }
        }

        for (var i = 0; i < list.Count; i++)
        {
            var br = list[i];
            if (br.Condition.Trim().Length == 0) continue;
            if (!Cond(br.Condition)) continue;

            if (br.ActionIgnore) { Skip(br, "미구현 액션 (ActionIgnore)"); continue; }
            if (!_a.Actions.TryGetValue(br.ActionName, out var target)) { Skip(br, "대상 액션 없음"); continue; }
            if (target.SkillKey is int k && target.CoolTime.Trim() != "Continue"
                && _cool.TryGetValue(k, out var end) && end > _t)
            {
                Skip(br, $"스킬 {k} 쿨타임 {end - _t}ms 남음");
                continue;
            }
            Consume(br);
            Transition(target, br, i);
            return;
        }
    }

    private void Expand(FuncRefNode fr, List<BranchNode> list, HashSet<string> visiting)
    {
        if (!_a.Funcs.TryGetValue(fr.FuncName, out var f) || !visiting.Add(fr.FuncName)) return;
        foreach (var n in f.Nodes)
        {
            if (n is BranchNode b) list.Add(b);
            else if (n is FuncRefNode inner) Expand(inner, list, visiting);
        }
        visiting.Remove(fr.FuncName);
    }

    private void Skip(BranchNode br, string reason)
    {
        if (_skippedThisState.Add(br.Src)) _r.Skips.Add(new BranchSkip(_t, _cur!.Name, br.ActionName, reason, br.Src));
    }

    // ============================ 식 평가 ============================

    private bool Cond(string? condition) =>
        string.IsNullOrWhiteSpace(condition) || _ev.Eval(condition, this).IsTruthy;

    public RtValue Ident(string name)
    {
        if (_vars.TryGetValue(name, out var v)) return v;
        if (_consts.TryGetValue(name, out var c)) return c;
        if (KnownNames.IsInputCommand(name)) return RtValue.Of(_presses.Any(p => p.Pending && p.E.Command == name));
        WarnOnce("RUN002", $"ident:{name}", _cur?.Src ?? _seg.Src, $"알 수 없는 이름 {name}을(를) None으로 계산");
        return RtValue.None;
    }

    public RtValue Call(CallExpr call, Func<Expr, RtValue> eval)
    {
        switch (call.Name)
        {
            case "ActionEnd": return RtValue.Of(_t - _enter >= EndMs(_cur!));
            case "LoopCount": return RtValue.Of(_loop);
            case "CheckBuff":
            {
                var k = call.Args.Count == 1 ? eval(call.Args[0]) : RtValue.None;
                return RtValue.Of(k.Kind == RtKind.Number && _playerBuffs.TryGetValue((int)k.Num, out var ab) && (ab.End is null || ab.End > _t));
            }
            case "keyDown":
                return RtValue.Of(call.Args.Count == 1 && call.Args[0] is IdentExpr id
                    && _presses.Any(p => p.E.Command == id.Name && p.Time <= _t && _t < p.HoldEnd));
            case "getPlayerDirection": return RtValue.Of(1);   // 방향 입력은 아직 없음 — 오른쪽 고정
            case "getCharacterMoveSpeed": return RtValue.Of(0); // 위치는 계산하지 않음
        }
        if (_skills.TryCall(call, eval, out var r)) return r;
        WarnOnce("RUN002", $"call:{call.Name}", _cur?.Src ?? _seg.Src, $"알 수 없는 함수 {call.Name}()을(를) None으로 계산");
        return RtValue.None;
    }

    private double EvalData(string? text, SourceRef src, string what)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var v = _ev.Eval(text, _dataHost);
        if (v.IsUnknown)
        {
            WarnOnce("RUN003", $"data:{what}", src, $"{what} 식을 계산할 수 없어 0으로 계산 (\"{text}\")");
            return 0;
        }
        return v.AsNumber;
    }

    // ============================ 시간 값 ============================

    /// <summary>시간 값 → 액션 안 ms. 빈칸·형식 오류는 임시값 + RUN001 경고. Escape는 int.MinValue</summary>
    private int Time(TimeValue tv, SourceRef src, string what, int temp)
    {
        switch (tv.Kind)
        {
            case TimeKind.Ms: return tv.Ms;
            case TimeKind.ActionEnd: return EndMs(_cur!);
            case TimeKind.Escape: return int.MinValue;
            default:
                WarnOnce("RUN001", $"time:{src}:{what}", src,
                    $"{_cur?.Name} {what}{(tv.Kind == TimeKind.Invalid ? $"=\"{tv.Raw}\" 형식 오류" : " 비어 있음")} → 임시값 {temp}ms로 계산");
                return temp;
        }
    }

    /// <summary>액션의 EndTime(ms). 비어 있으면 임시값 500ms + RUN001 경고</summary>
    private int EndMs(ActionDef a)
    {
        if (a.EndTime.Kind == TimeKind.Ms) return a.EndTime.Ms;
        WarnOnce("RUN001", $"end:{a.Name}", a.Src, $"액션 {a.Name}의 EndTime이 비어 있음 → 임시값 {SegmentRunner.TempEndTimeMs}ms로 계산");
        return SegmentRunner.TempEndTimeMs;
    }

    private void WarnOnce(string code, string key, SourceRef src, string message)
    {
        if (_warned.Add(code + "|" + key)) _r.Warnings.Warn(code, src, message);
    }
}
