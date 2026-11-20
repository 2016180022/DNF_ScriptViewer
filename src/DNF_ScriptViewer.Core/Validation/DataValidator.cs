using System.Globalization;
using System.Text.RegularExpressions;
using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Expressions;
using DNF_ScriptViewer.Core.Loading;
using DNF_ScriptViewer.Core.Model;

namespace DNF_ScriptViewer.Core.Validation;

/// <summary>
/// 데이터 검증기. 모든 결과는 진단(경고·측정 대기·참고)으로만 남기고 툴을 멈추지 않음.
/// 진단 코드 목록은 <see cref="DiagnosticCodes"/>
/// </summary>
public static partial class DataValidator
{
    public static void Validate(DataSet set)
    {
        var v = new Ctx(set);
        if (set.ActionScript is { } a) v.CheckActionScript(a);
        if (set.SkillInfo is { } s) v.CheckSkillInfo(s);
        if (set.BuffInfo is { } b) v.CheckBuffInfo(b);
        if (set.Segments is { } g) v.CheckSegments(g);
        foreach (var seq in set.Inputs.Values) v.CheckInput(seq);
    }

    [GeneratedRegex(@"@[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex TempToken();

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]*$")]
    private static partial Regex ConstantName();

    private enum Scope
    {
        /// <summary>ActionScript: 변수·상수·입력 사용 가능</summary>
        Script,
        /// <summary>BuffInfo 수치식: 함수와 숫자만</summary>
        Data,
    }

    private sealed partial class Ctx
    {
        private readonly DataSet _set;
        private readonly DiagnosticBag _d;
        private readonly HashSet<string> _variables = new();
        private readonly HashSet<string> _constants = new();

        public Ctx(DataSet set)
        {
            _set = set;
            _d = set.Diagnostics;
            if (set.ActionScript is { } a)
            {
                foreach (var x in a.Variables) _variables.Add(x.Name);
                foreach (var x in a.Constants) _constants.Add(x.Name);
            }
        }

        private SkillDef? Skill(int key) => _set.SkillInfo?.Skills.GetValueOrDefault(key);
        private BuffDef? Buff(int key) => _set.BuffInfo?.Buffs.GetValueOrDefault(key);
        private bool HasSkillInfo => _set.SkillInfo is not null;
        private bool HasBuffInfo => _set.BuffInfo is not null;

        // ============================ ActionScript ============================

        public void CheckActionScript(ActionScriptData a)
        {
            // 캐릭터 변수·상수
            foreach (var cv in a.Variables)
            {
                if (ConstantName().IsMatch(cv.Name) && cv.Name.Contains('_'))
                    _d.Warn("AS019", cv.Src, $"캐릭터 변수 {cv.Name}이(가) 상수 이름 형식(대문자_밑줄)임");
                CheckTempToken(cv.Value, cv.Src, $"캐릭터 변수 {cv.Name}");
            }
            foreach (var c in a.Constants)
            {
                if (!ConstantName().IsMatch(c.Name))
                    _d.Warn("AS019", c.Src, $"상수 {c.Name}의 이름이 대문자_밑줄 형식이 아님");
                if (c.Value.Trim().Length == 0)
                    _d.Pending("AS011", c.Src, $"상수 {c.Name}의 Value가 비어 있음");
                CheckTempToken(c.Value, c.Src, $"상수 {c.Name}");
            }

            // Func 정의
            var referenced = new HashSet<string>();
            foreach (var f in a.FuncList)
            {
                if (f.Nodes.Count == 0) _d.Warn("AS006", f.Src, $"Func 정의 {f.Name}가 비어 있음");
                CheckNodes(a, f.Nodes, $"Func {f.Name}", referenced, block: null);
            }

            // 액션
            foreach (var act in a.ActionList)
            {
                var where = $"액션 {act.Name}";
                CheckAttrs(act.Attrs, act.Src, where);
                if (act.ActionSpeedType == ActionSpeedType.Unknown)
                    _d.Warn("AS013", act.Src, $"{where}: ActionSpeedType=\"{act.Attrs["ActionSpeedType"]}\"는 None·CastSpeed·AttackSpeed가 아님");
                if (act.GuardType == GuardType.Unknown)
                    _d.Warn("AS013", act.Src, $"{where}: GuardType=\"{act.Attrs["GuardType"]}\"는 None·Invincible·SuperArmor가 아님");
                CheckTime(act.StartTime, act.Src, where, "StartTime");
                CheckTime(act.EndTime, act.Src, where, "EndTime");
                if (act.SkillKey is int sk && HasSkillInfo && Skill(sk) is null)
                    _d.Warn("LNK001", act.Src, $"{where}: SkillKey {sk}가 SkillInfo에 없음");
                CheckCoolTime(act, where);

                CheckNodes(a, act.Nodes, where, referenced, block: null);
                CheckSafetyBranch(a, act);
            }

            foreach (var f in a.FuncList)
                if (!referenced.Contains(f.Name))
                    _d.Info("AS017", f.Src, $"Func {f.Name}는 어디에서도 참조하지 않음");
        }

        private void CheckCoolTime(ActionDef act, string where)
        {
            var ct = act.Attrs["CoolTime"];
            if (ct is null || ct.Trim().Length == 0) return;
            if (act.SkillKey is null)
                _d.Warn("AS020", act.Src, $"{where}: 스킬 액션이 아닌데 CoolTime이 있음 (CoolTime은 스킬 액션만)");
            if (ct is "None" or "Continue") return;
            if (int.TryParse(ct, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return;
            CheckExpr(ct, act.Src, $"{where} CoolTime", Scope.Script);
        }

        /// <summary>액션·Func 안의 줄을 차례로 검사. block = 지금 들어 있는 브랜치 블록 (없으면 null)</summary>
        private void CheckNodes(ActionScriptData a, IReadOnlyList<ScriptNode> nodes, string owner,
            HashSet<string> referenced, BranchBlockNode? block)
        {
            foreach (var n in nodes)
            {
                var where = $"{owner} <{n.Element}>";
                CheckAttrs(n.Attrs, n.Src, where);

                switch (n)
                {
                    case VariableNode v:
                        CheckTime(v.StartTime, v.Src, where, "StartTime");
                        CheckTime(v.EndTime, v.Src, where, "EndTime");
                        if (_constants.Contains(v.Name))
                            _d.Warn("AS010", v.Src, $"{where}: 상수 {v.Name}의 값을 바꿈 (상수는 바꿀 수 없음)");
                        else if (v.Name.Length > 0 && !_variables.Contains(v.Name))
                            _d.Warn("AS009", v.Src, $"{where}: 변수 {v.Name}가 CharacterVariables에 선언되지 않음");
                        if (v.Value.Trim().Length > 0) CheckExpr(v.Value, v.Src, $"{where} Value", Scope.Script);
                        CheckCondition(v.Condition, v.Src, where);
                        break;

                    case MoveNode m:
                        CheckTime(m.StartTime, m.Src, where, "StartTime");
                        foreach (var (attr, val) in new[] { ("Speed", m.Speed), ("Distance", m.Distance), ("Direction", m.Direction) })
                            if (!string.IsNullOrWhiteSpace(val)) CheckExpr(val, m.Src, $"{where} {attr}", Scope.Script);
                        CheckCondition(m.Condition, m.Src, where);
                        break;

                    case AttackFrameNode af:
                        if (af.AttackType == AttackType.Unknown)
                            _d.Warn("AS013", af.Src, $"{where}: AttackType=\"{af.Attrs["AttackType"]}\"는 AttackArrow·AttackRange·RepeatAttack이 아님");
                        if (af.RangeType == RangeType.Unknown)
                            _d.Warn("AS013", af.Src, $"{where}: RangeType=\"{af.Attrs["RangeType"]}\"는 Round·Rect가 아님");
                        CheckTime(af.StartTime, af.Src, where, "StartTime");
                        CheckTime(af.EndTime, af.Src, where, "EndTime");
                        CheckCondition(af.Condition, af.Src, where);
                        CheckNodes(a, af.Children, owner, referenced, block);
                        break;

                    case BuffFrameNode bf:
                        CheckTime(bf.StartTime, bf.Src, where, "StartTime");
                        CheckCondition(bf.Condition, bf.Src, where);
                        CheckBuffFrame(bf, where);
                        break;

                    case FuncRefNode fr:
                        referenced.Add(fr.FuncName);
                        if (fr.HasChildren)
                            _d.Warn("AS006", fr.Src, $"{where}: Func 참조는 빈 요소여야 함 (정의는 최상위에만)");
                        if (!a.Funcs.ContainsKey(fr.FuncName))
                            _d.Warn("AS005", fr.Src, $"{where}: 정의되지 않은 Func");
                        break;

                    case BranchNode br:
                        CheckBranch(a, br, where);
                        break;

                    case BranchBlockNode bb:
                        if (block is not null)
                            _d.Warn("AS002", bb.Src, $"{where}: 브랜치 블록 안에 브랜치 블록이 있음");
                        CheckTime(bb.StartTime, bb.Src, where, "StartTime");
                        CheckTime(bb.EndTime, bb.Src, where, "EndTime");
                        CheckNodes(a, bb.Items, owner, referenced, bb);
                        break;

                    case UnknownNode u:
                        _d.Warn("AS002", u.Src, $"{owner}: 알 수 없는 요소 <{u.Element}>");
                        break;
                }
            }
        }

        private void CheckBranch(ActionScriptData a, BranchNode br, string where)
        {
            var exists = a.Actions.ContainsKey(br.ActionName);
            if (br.ActionIgnore)
            {
                if (exists)
                    _d.Warn("AS004", br.Src, $"{where}: {br.ActionName}에 ActionIgnore=\"True\"가 있지만 액션이 이제 있음 → 표시를 지울 것");
                else
                    _d.Info("AS018", br.Src, $"{where}: 미구현 액션 {br.ActionName}으로 가는 브랜치 (런타임에서 건너뜀)");
            }
            else if (br.ActionName.Length > 0 && !exists)
            {
                _d.Warn("AS003", br.Src, $"{where}: 브랜치 대상 액션 {br.ActionName}이(가) 없음 (일부러 만들지 않은 액션이면 ActionIgnore=\"True\")");
            }
            var unimplRaw = br.Attrs["ActionIgnore"];
            if (unimplRaw is not null && unimplRaw is not ("True" or "False"))
                _d.Warn("AS013", br.Src, $"{where}: ActionIgnore=\"{unimplRaw}\"는 True·False가 아님");

            if (br.Condition.Trim().Length > 0) CheckExpr(br.Condition, br.Src, $"{where} {br.ActionName} Condition", Scope.Script);
        }

        /// <summary>
        /// 모든 액션에 ActionEnd() 조건 브랜치가 NormalBranch 안에 있어야 함,
        /// SuperCancelBranch에는 ActionEnd() 조건을 두지 않음
        /// </summary>
        private void CheckSafetyBranch(ActionScriptData a, ActionDef act)
        {
            var hasSafety = false;
            foreach (var bb in act.Nodes.OfType<BranchBlockNode>())
            {
                foreach (var br in ExpandBranches(a, bb.Items, new HashSet<string>()))
                {
                    if (!UsesActionEnd(br.Condition)) continue;
                    if (bb.Kind == BranchBlockKind.Normal) hasSafety = true;
                    else _d.Warn("AS008", br.Src, $"액션 {act.Name}: ActionEnd() 조건 브랜치({br.ActionName})가 SuperCancelBranch에 있음 (NormalBranch에 둘 것)");
                }
            }
            if (!hasSafety)
                _d.Warn("AS007", act.Src, $"액션 {act.Name}: NormalBranch에 ActionEnd() 조건의 안전장치 브랜치가 없음");
        }

        /// <summary>브랜치 블록 안의 Branch를 Func 참조까지 펼쳐서 나열</summary>
        private static IEnumerable<BranchNode> ExpandBranches(ActionScriptData a, IEnumerable<ScriptNode> items, HashSet<string> visiting)
        {
            foreach (var n in items)
            {
                if (n is BranchNode br) yield return br;
                else if (n is FuncRefNode fr && a.Funcs.TryGetValue(fr.FuncName, out var f) && visiting.Add(fr.FuncName))
                {
                    foreach (var x in ExpandBranches(a, f.Nodes, visiting)) yield return x;
                    visiting.Remove(fr.FuncName);
                }
            }
        }

        private static bool UsesActionEnd(string condition) =>
            ExprParser.TryParse(condition, out var e, out _) && e is not null
            && ExprWalker.All(e).Any(x => x is CallExpr { Name: "ActionEnd" });

        private void CheckBuffFrame(BuffFrameNode bf, string where)
        {
            if (bf.BuffIndexRaw.Trim().Length > 0 && !bf.BuffIndexValid)
                _d.Warn("LNK002", bf.Src, $"{where}: BuffIndex=\"{bf.BuffIndexRaw}\" 형식 오류 (예: \"0 1\", \"0(200)\")");

            var rawKey = bf.Attrs["SkillKey"]?.Trim() ?? "";
            if (rawKey.Length == 0) return; // 빈칸은 측정 대기로 집계됨
            if (bf.SkillKey is null)
            {
                _d.Warn("LNK001", bf.Src, $"{where}: SkillKey=\"{rawKey}\"는 정수가 아님");
                return;
            }
            if (!HasSkillInfo) return;
            var skill = Skill(bf.SkillKey.Value);
            if (skill is null)
            {
                _d.Warn("LNK001", bf.Src, $"{where}: SkillKey {bf.SkillKey}가 SkillInfo에 없음");
                return;
            }
            foreach (var entry in bf.BuffIndex)
            {
                if (entry.Index >= skill.BuffKeys.Count)
                {
                    _d.Warn("LNK003", bf.Src, $"{where}: BuffIndex {entry.Index}가 스킬 {skill.Key}의 BuffKey 목록 길이({skill.BuffKeys.Count})를 넘음");
                    continue;
                }
                var buffKey = skill.BuffKeys[entry.Index];
                if (HasBuffInfo && Buff(buffKey) is null)
                    _d.Warn("LNK004", bf.Src, $"{where}: 스킬 {skill.Key}의 {entry.Index}번 BuffKey {buffKey}가 BuffInfo에 없음");
            }
        }

        // ============================ SkillInfo / BuffInfo ============================

        public void CheckSkillInfo(SkillInfoData s)
        {
            if (!HasBuffInfo) return;
            foreach (var skill in s.SkillList)
                foreach (var key in skill.BuffKeys)
                    if (Buff(key) is null)
                        _d.Warn("LNK004", skill.Src, $"스킬 {skill.Key} {skill.Name}: BuffKey {key}가 BuffInfo에 없음");
        }

        public void CheckBuffInfo(BuffInfoData b)
        {
            foreach (var buff in b.BuffList)
            {
                var where = $"버프 {buff.Key} {buff.Name}";
                if (buff.BuffType is BuffType.Unknown or BuffType.Unset)
                    _d.Warn("BI003", buff.Src, $"{where}: BuffType=\"{buff.BuffTypeRaw}\"는 Attack·Debuff·State·BuffEdit가 아님");
                if (buff.StackType is StackType.Unknown or StackType.Unset)
                    _d.Warn("BI003", buff.Src, $"{where}: StackType=\"{buff.StackTypeRaw}\"는 None·Refresh·Stack이 아님");
                if (buff.SkillKey is int sk && HasSkillInfo && Skill(sk) is null)
                    _d.Warn("LNK001", buff.Src, $"{where}: SkillKey {sk}가 SkillInfo에 없음");

                CheckTempToken(buff.Remain, buff.Src, where);
                if (buff.Remain.Trim().Length > 0) CheckExpr(buff.Remain, buff.Src, $"{where} Remain", Scope.Data);

                for (var i = 0; i < buff.Params.Count; i++)
                {
                    var p = buff.Params[i];
                    if (p is null) continue;
                    var name = $"Param{i + 1:00}";
                    CheckTempToken(p, buff.Src, $"{where} {name}");
                    if (i == 0 && buff.BuffType is BuffType.Debuff or BuffType.State) continue; // 상태 이름
                    if (i == 0 && buff.BuffType == BuffType.BuffEdit)
                    {
                        foreach (var key in XmlKeyList(p, buff.Src, where))
                            if (Buff(key) is null)
                                _d.Warn("LNK006", buff.Src, $"{where}: BuffEdit 대상 {key}가 BuffInfo에 없음");
                        continue;
                    }
                    if (p.Trim().Length > 0) CheckExpr(p, buff.Src, $"{where} {name}", Scope.Data);
                }
            }
        }

        private IEnumerable<int> XmlKeyList(string raw, SourceRef src, string where)
        {
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var k)) yield return k;
                else _d.Warn("LNK006", src, $"{where}: BuffEdit 대상 \"{part}\"는 정수가 아님");
            }
        }

        // ============================ Segments ============================

        public void CheckSegments(SegmentsData g)
        {
            var videoIds = g.Videos.Select(x => x.Id).ToHashSet();
            foreach (var video in g.Videos)
                if (video.Source.Contains('{'))
                    _d.Pending("SEG011", video.Src, $"영상 {video.Id}: Source가 아직 자리 표시({video.Source})");

            foreach (var s in g.DefaultSettings) CheckSetting(s, "DefaultSetting");

            var orders = new HashSet<int>();
            foreach (var seg in g.Segments)
            {
                var where = $"구간 '{seg.Name}'";
                if (seg.Order is int o && !orders.Add(o))
                    _d.Warn("SEG012", seg.Src, $"{where}: Order {o} 중복");
                if (!videoIds.Contains(seg.Video))
                    _d.Warn("SEG001", seg.Src, $"{where}: Video \"{seg.Video}\"가 <Video>에 없음");
                if (seg.VideoStart is null || seg.VideoEnd is null)
                    _d.Pending("SEG002", seg.Src, $"{where}: VideoStart·VideoEnd 측정 대기");
                else if (seg.VideoStart >= seg.VideoEnd)
                    _d.Warn("SEG002", seg.Src, $"{where}: VideoStart({seg.VideoStart}) ≥ VideoEnd({seg.VideoEnd})");

                foreach (var s in seg.Settings) CheckSetting(s, $"{where} Setting");

                if (seg.Input.Length == 0)
                    _d.Pending("SEG008", seg.Src, $"{where}: Input이 비어 있음");
                else if (_set.MissingInputs.Contains(seg.Input))
                    _d.Pending("SEG008", seg.Src, $"{where}: 입력 시퀀스 {seg.Input}이(가) 아직 없음");

                CheckSegmentStart(seg, where);
                CheckMarks(seg, where);
            }
        }

        private void CheckSetting(SkillSetting s, string where)
        {
            if (s.Skill is null) { _d.Warn("SEG003", s.Src, $"{where}: Skill이 없음"); return; }
            if (!HasSkillInfo) return;
            var skill = Skill(s.Skill.Value);
            if (skill is null) { _d.Warn("SEG003", s.Src, $"{where}: 스킬 {s.Skill}가 SkillInfo에 없음"); return; }
            if (s.Level is int lv && (lv < 1 || lv > skill.MaxLevel))
                _d.Warn("SEG003", s.Src, $"{where}: 스킬 {skill.Key} {skill.Name} Level {lv}가 1~{skill.MaxLevel} 밖");
            if (s.VP is int vp && vp != 0 && skill.Evolutions.All(e => e.Type != vp))
                _d.Warn("SEG003", s.Src, $"{where}: 스킬 {skill.Key} {skill.Name}에 VP{vp}(Evolution Type {vp})가 없음");
        }

        /// <summary>
        /// 시작 상태·변수 검사. 들어오는 브랜치 조건과 값 대조는 런타임 조건 평가기와 함께 추가 예정
        /// </summary>
        private void CheckSegmentStart(SegmentDef seg, string where)
        {
            var a = _set.ActionScript;
            if (a is null) return;
            if (seg.StartState.Length == 0)
                _d.Warn("SEG004", seg.Src, $"{where}: 시작 State가 비어 있음");
            else if (!a.Actions.ContainsKey(seg.StartState))
                _d.Warn("SEG004", seg.Src, $"{where}: 시작 State {seg.StartState}가 ActionScript에 없음");

            var seen = new HashSet<string>();
            foreach (var sv in seg.StartVariables)
            {
                if (!seen.Add(sv.Name))
                    _d.Warn("SEG005", sv.Src, $"{where}: 시작 변수 {sv.Name} 중복");
                var decl = a.FindVariable(sv.Name);
                if (decl is null)
                {
                    _d.Warn("SEG005", sv.Src, _constants.Contains(sv.Name)
                        ? $"{where}: 상수 {sv.Name}는 시작값으로 바꿀 수 없음"
                        : $"{where}: 시작 변수 {sv.Name}가 CharacterVariables에 선언되지 않음");
                    continue;
                }
                var declKind = ValueKind(decl.Value);
                var startKind = ValueKind(sv.Value);
                if (sv.Value.Trim().Length == 0)
                    _d.Pending("SEG009", sv.Src, $"{where}: 시작 변수 {sv.Name} 값이 비어 있음");
                else if (declKind != "" && startKind != "" && declKind != startKind)
                    _d.Warn("SEG009", sv.Src, $"{where}: 시작 변수 {sv.Name}=\"{sv.Value}\"의 종류({startKind})가 선언값 \"{decl.Value}\"({declKind})와 다름");
            }
        }

        /// <summary>값 종류: 숫자 / 참거짓 / "" (판단하지 않음 — None 등)</summary>
        private static string ValueKind(string value)
        {
            var v = value.Trim();
            if (v is "True" or "False") return "참거짓";
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return "숫자";
            return "";
        }

        private void CheckMarks(SegmentDef seg, string where)
        {
            var a = _set.ActionScript;
            int? prev = null;
            foreach (var m in seg.Marks)
            {
                if (a is not null && !a.Actions.ContainsKey(m.State))
                    _d.Warn("SEG006", m.Src, $"{where}: Mark State {m.State}가 ActionScript에 없음");
                if (m.Time is null) { _d.Pending("SEG007", m.Src, $"{where}: Mark {m.State} Time 측정 대기"); continue; }
                if (m.Time < 0 || (seg.DurationMs is int dur && m.Time > dur))
                    _d.Warn("SEG007", m.Src, $"{where}: Mark {m.State} Time {m.Time}가 구간 길이 0~{seg.DurationMs} 밖");
                if (prev is int p && m.Time < p)
                    _d.Warn("SEG007", m.Src, $"{where}: Mark {m.State} Time {m.Time}가 앞 Mark({p})보다 이름 (시간순으로)");
                prev = m.Time;
            }
        }

        // ============================ 입력 시퀀스 ============================

        public void CheckInput(InputSequence seq)
        {
            int? prev = null;
            foreach (var i in seq.Inputs)
            {
                var where = $"입력 시퀀스 {seq.Name}";
                if (!KnownNames.IsInputCommand(i.Command))
                    _d.Warn("INP001", i.Src, $"{where}: 알 수 없는 Command \"{i.Command}\" (Jump·Attack·Move·Dash·NitroDown·Skill_{{스킬 키}})");
                else if (KnownNames.SkillCommandKey(i.Command) is int sk && HasSkillInfo && Skill(sk) is null)
                    _d.Warn("INP001", i.Src, $"{where}: {i.Command}의 스킬 {sk}가 SkillInfo에 없음");
                if (i.Time is null) { _d.Pending("INP002", i.Src, $"{where}: {i.Command} Time 비어 있음"); continue; }
                if (i.Time < 0) _d.Warn("INP002", i.Src, $"{where}: {i.Command} Time {i.Time}가 음수");
                if (prev is int p && i.Time < p) _d.Warn("INP002", i.Src, $"{where}: {i.Command} Time {i.Time}가 앞 입력({p})보다 이름");
                if (i.Hold is int h && h <= 0) _d.Warn("INP002", i.Src, $"{where}: {i.Command} Hold {h}는 0보다 커야 함");
                prev = i.Time;
            }
        }

        // ============================ 공용 ============================

        /// <summary>빈 속성은 측정 대기, 남은 임시 토큰(@…)은 경고 (ActionScript 전용)</summary>
        private void CheckAttrs(AttrList attrs, SourceRef src, string where)
        {
            foreach (var (name, value) in attrs.Items)
            {
                if (value.Trim().Length == 0) _d.Pending("AS014", src, $"{where}: {name} 비어 있음");
                else CheckTempToken(value, src, $"{where} {name}");
            }
        }

        private void CheckTempToken(string value, SourceRef src, string where)
        {
            foreach (Match m in TempToken().Matches(value))
                _d.Warn("AS012", src, $"{where}: 임시 토큰 {m.Value}이(가) 남아 있음");
        }

        private void CheckTime(TimeValue t, SourceRef src, string where, string attr)
        {
            if (t.Kind == TimeKind.Invalid)
                _d.Warn("AS015", src, $"{where}: {attr}=\"{t.Raw}\"는 ms 정수·ActionEnd()·Escape 중 하나여야 함");
        }

        private void CheckCondition(string? condition, SourceRef src, string where)
        {
            if (!string.IsNullOrWhiteSpace(condition)) CheckExpr(condition, src, $"{where} Condition", Scope.Script);
        }

        /// <summary>식 문법 + 이름 + 함수 인자 + 데이터 링크(CheckBuff·SkillLearned·getSkillValue 등)</summary>
        private void CheckExpr(string text, SourceRef src, string where, Scope scope)
        {
            if (!ExprParser.TryParse(text, out var expr, out var error) || expr is null)
            {
                _d.Warn("EXP001", src, $"{where}: 식 문법 오류 — {error} (\"{text}\")");
                return;
            }
            foreach (var node in ExprWalker.All(expr))
            {
                switch (node)
                {
                    case IdentExpr id:
                        CheckIdent(id.Name, src, where, scope);
                        break;
                    case CallExpr call:
                        CheckCall(call, src, where);
                        break;
                }
            }
        }

        private void CheckIdent(string name, SourceRef src, string where, Scope scope)
        {
            if (KnownNames.Literals.Contains(name)) return;
            if (scope == Scope.Script)
            {
                if (_variables.Contains(name) || _constants.Contains(name) || name == KnownNames.KeyDown) return;
                if (KnownNames.InputCommands.Contains(name)) return;
                if (KnownNames.SkillCommandKey(name) is int sk)
                {
                    if (HasSkillInfo && Skill(sk) is null)
                        _d.Warn("LNK007", src, $"{where}: 입력 {name}의 스킬 {sk}가 SkillInfo에 없음");
                    return;
                }
            }
            _d.Warn("EXP002", src, $"{where}: 알 수 없는 이름 {name} (변수·상수·입력 커맨드·True/False/None 아님)");
        }

        private void CheckCall(CallExpr call, SourceRef src, string where)
        {
            if (!KnownNames.Functions.TryGetValue(call.Name, out var arity))
            {
                _d.Warn("EXP002", src, $"{where}: 알 수 없는 함수 {call.Name}()");
                return;
            }
            if (call.Args.Count != arity)
            {
                _d.Warn("EXP003", src, $"{where}: {call.Name}()의 인자는 {arity}개 (지금 {call.Args.Count}개)");
                return;
            }

            int? IntArg(int i) => call.Args[i] is NumberExpr n && n.Value == Math.Floor(n.Value) ? (int)n.Value : null;

            switch (call.Name)
            {
                case "CheckBuff":
                {
                    if (IntArg(0) is not int key) { _d.Warn("EXP003", src, $"{where}: CheckBuff()의 인자는 BuffKey 정수"); break; }
                    if (!HasBuffInfo) break;
                    var buff = Buff(key);
                    if (buff is null) _d.Warn("LNK005", src, $"{where}: CheckBuff({key})의 버프가 BuffInfo에 없음");
                    else if (buff.BuffType != BuffType.State)
                        _d.Warn("LNK005", src, $"{where}: CheckBuff({key}) {buff.Name}의 BuffType이 State가 아님 ({buff.BuffTypeRaw})");
                    break;
                }
                case "SkillLearned":
                {
                    if (IntArg(0) is not int n) { _d.Warn("EXP003", src, $"{where}: SkillLearned()의 인자는 정수"); break; }
                    if (!HasSkillInfo) break;
                    // 스킬 키(3자리) = 습득 여부, 스킬 키 × 100 + VP = VP 선택
                    if (Skill(n) is not null) break;
                    var skill = Skill(n / 100);
                    var vp = n % 100;
                    if (skill is null)
                        _d.Warn("LNK007", src, $"{where}: SkillLearned({n})의 스킬({n} 또는 {n / 100})이 SkillInfo에 없음");
                    else if (skill.Evolutions.All(e => e.Type != vp))
                        _d.Warn("LNK008", src, $"{where}: SkillLearned({n}) — 스킬 {skill.Key} {skill.Name}에 VP{vp}가 없음");
                    break;
                }
                case "getSkillCoolTime":
                {
                    if (IntArg(0) is not int key) { _d.Warn("EXP003", src, $"{where}: getSkillCoolTime()의 인자는 스킬 키 정수"); break; }
                    if (HasSkillInfo && Skill(key) is null)
                        _d.Warn("LNK007", src, $"{where}: getSkillCoolTime({key})의 스킬이 SkillInfo에 없음");
                    break;
                }
                case "getSkillValue":
                {
                    if (IntArg(0) is not int key || IntArg(1) is not int n)
                    {
                        _d.Warn("EXP003", src, $"{where}: getSkillValue()의 인자는 (스킬 키, N) 정수");
                        break;
                    }
                    if (!HasSkillInfo) break;
                    var skill = Skill(key);
                    if (skill is null) _d.Warn("LNK007", src, $"{where}: getSkillValue({key}, {n})의 스킬이 SkillInfo에 없음");
                    else if (skill.BaseLevelInfo?.HasValue($"Value{n}") != true)
                        _d.Warn("LNK007", src, $"{where}: getSkillValue({key}, {n}) — 스킬 {skill.Name}에 Value{n}이 없음");
                    break;
                }
            }
        }
    }
}
