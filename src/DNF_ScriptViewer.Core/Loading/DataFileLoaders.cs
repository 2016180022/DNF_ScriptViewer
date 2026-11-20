using System.Globalization;
using System.Xml.Linq;
using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Model;

namespace DNF_ScriptViewer.Core.Loading;

public static class SkillInfoLoader
{
    public static SkillInfoData? Load(string file, string text, DiagnosticBag diag)
    {
        var doc = XmlHelpers.Parse(file, text, diag);
        if (doc?.Root is null) return null;
        var data = new SkillInfoData { File = file };

        foreach (var e in doc.Root.Elements("Skill"))
        {
            var src = e.Src(file);
            var key = e.Int("SkillKey", file, diag, "SI002");
            if (key is null) { diag.Warn("SI002", src, "SkillKey가 없는 Skill"); continue; }

            var levelInfos = e.Elements("LevelInfo").Select(li => new LevelInfoDef(
                li.Str("Variant"),
                AttrDict(li.Element("Base")),
                AttrDict(li.Element("Grows")),
                li.Elements("Row")
                  .Where(r => int.TryParse(r.Str("Level"), out _))
                  .ToDictionary(r => int.Parse(r.Str("Level"), CultureInfo.InvariantCulture),
                                r => (IReadOnlyDictionary<string, string>)AttrDict(r)),
                li.Src(file))).ToList();

            var evolutions = e.Elements("Evolution")
                .Select(ev => new EvolutionDef(ev.Int("Type", file, diag, "SI002") ?? 0, ev.Str("Name"), ev.Src(file)))
                .ToList();

            var skill = new SkillDef(key.Value, e.Str("Name"), e.Str("Type"),
                e.Int("MaxLevel", file, diag, "SI002") ?? 0,
                XmlHelpers.IntList(e.Str("BuffKey"), src, "BuffKey", diag, "SI002"),
                levelInfos, evolutions, src);

            if (!data.Skills.TryAdd(skill.Key, skill)) diag.Warn("SI001", src, $"SkillKey 중복: {skill.Key}");
            data.SkillList.Add(skill);
        }
        return data;
    }

    private static Dictionary<string, string> AttrDict(XElement? e) =>
        e is null ? new() : e.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
}

public static class BuffInfoLoader
{
    public static BuffInfoData? Load(string file, string text, DiagnosticBag diag)
    {
        var doc = XmlHelpers.Parse(file, text, diag);
        if (doc?.Root is null) return null;
        var data = new BuffInfoData { File = file };

        foreach (var e in doc.Root.Elements("Buff"))
        {
            var src = e.Src(file);
            var key = e.Int("BuffKey", file, diag, "BI002");
            if (key is null) { diag.Warn("BI002", src, "BuffKey가 없는 Buff"); continue; }

            // Param01, Param02 ... 번호 순서대로. 빠진 번호는 null
            var paramAttrs = e.Attributes()
                .Where(a => a.Name.LocalName.StartsWith("Param", StringComparison.Ordinal)
                         && int.TryParse(a.Name.LocalName.AsSpan(5), out _))
                .ToDictionary(a => int.Parse(a.Name.LocalName.AsSpan(5), CultureInfo.InvariantCulture), a => a.Value);
            var max = paramAttrs.Count == 0 ? 0 : paramAttrs.Keys.Max();
            var pars = Enumerable.Range(1, max).Select(i => paramAttrs.TryGetValue(i, out var v) ? v : null).ToList();

            var buff = new BuffDef(key.Value, e.Str("BuffName"),
                EnumParse.Parse<BuffType>(e.StrOrNull("BuffType")), e.Str("BuffType"),
                e.Str("BuffGroup"), e.Int("SkillKey", file, diag, "BI002"), e.Str("Remain"),
                EnumParse.Parse<StackType>(e.StrOrNull("StackType")), e.Str("StackType"),
                e.Str("Desc"), pars, src);

            if (!data.Buffs.TryAdd(buff.Key, buff)) diag.Warn("BI001", src, $"BuffKey 중복: {buff.Key}");
            data.BuffList.Add(buff);
        }
        return data;
    }
}

public static class SegmentsLoader
{
    public static SegmentsData? Load(string file, string text, DiagnosticBag diag)
    {
        var doc = XmlHelpers.Parse(file, text, diag);
        if (doc?.Root is null) return null;
        var data = new SegmentsData { File = file };

        foreach (var e in doc.Root.Elements())
        {
            switch (e.Name.LocalName)
            {
                case "Video":
                    data.Videos.Add(new VideoDef(e.Str("Id"), e.Str("Source"), e.Src(file)));
                    break;
                case "DefaultSetting":
                    data.DefaultSettings.Add(Setting(e, file, diag));
                    break;
                case "Segment":
                    data.Segments.Add(Segment(e, file, diag));
                    break;
                default:
                    diag.Warn("SEG000", e.Src(file), $"알 수 없는 요소 <{e.Name.LocalName}>");
                    break;
            }
        }
        return data;
    }

    private static SkillSetting Setting(XElement e, string file, DiagnosticBag diag) =>
        new(e.Int("Skill", file, diag, "SEG010"), e.Int("Level", file, diag, "SEG010"), e.Int("VP", file, diag, "SEG010"), e.Src(file));

    private static SegmentDef Segment(XElement e, string file, DiagnosticBag diag)
    {
        var start = e.Element("Start");
        var startVars = start?.Elements("Variable")
            .Select(v => new StartVariable(v.Str("Name"), v.Str("Value"), v.Src(file))).ToList() ?? new();
        if (start is null) diag.Warn("SEG004", e.Src(file), $"구간 '{e.Str("Name")}'에 <Start>가 없음");

        foreach (var c in e.Elements())
            if (c.Name.LocalName is not ("Setting" or "Start" or "Mark"))
                diag.Warn("SEG000", c.Src(file), $"Segment 안의 알 수 없는 요소 <{c.Name.LocalName}>");

        return new SegmentDef(e.Str("Name"), e.Int("Order", file, diag, "SEG010"), e.Str("Combo"), e.Str("Video"),
            e.Int("VideoStart", file, diag, "SEG010"), e.Int("VideoEnd", file, diag, "SEG010"), e.Str("Input"),
            e.Elements("Setting").Select(s => Setting(s, file, diag)).ToList(),
            start?.Str("State") ?? "", startVars,
            e.Elements("Mark").Select(m => new MarkDef(m.Int("Time", file, diag, "SEG010"), m.Str("State"), m.Src(file))).ToList(),
            e.Src(file));
    }
}

public static class InputSequenceLoader
{
    public static InputSequence? Load(string file, string text, DiagnosticBag diag)
    {
        var doc = XmlHelpers.Parse(file, text, diag);
        if (doc?.Root is null) return null;
        var seq = new InputSequence { File = file, Name = doc.Root.Str("Name") };
        foreach (var e in doc.Root.Elements())
        {
            if (e.Name.LocalName != "Input")
            {
                diag.Warn("INP000", e.Src(file), $"알 수 없는 요소 <{e.Name.LocalName}>");
                continue;
            }
            seq.Inputs.Add(new InputEvent(e.Int("Time", file, diag, "INP003"), e.Str("Command"),
                e.Int("Hold", file, diag, "INP003"), e.Src(file)));
        }
        return seq;
    }
}
