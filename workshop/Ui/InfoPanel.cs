// Procedural Pixel Creature Workshop - genome, anatomy and runtime information of the current creature.

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using PixelCreatures.Core;
using PixelCreatures.Core.Palettes;
using PixelCreatures.Runtime;

namespace PixelCreatures.Workshop
{
    public partial class InfoPanel : HBoxContainer
    {
        private Main _main = null!;
        private RichTextLabel _text = null!;
        private LineEdit _name = null!;

        public void Bind(Main main)
        {
            _main = main;
            AddThemeConstantOverride("separation", 10);
            var left = UiKit.Column(4);
            left.CustomMinimumSize = new Vector2(240, 0);
            left.AddChild(UiKit.Label("名称", 13, UiKit.TextDim));
            _name = new LineEdit { PlaceholderText = "生物名称" };
            _name.TextSubmitted += n => _main.State.Set(_main.State.Genome.WithName(n.Trim()), $"已改名为 {n.Trim()}");
            left.AddChild(_name);
            left.AddChild(UiKit.Button("应用名称", () => _main.State.Set(_main.State.Genome.WithName(_name.Text.Trim()), $"已改名为 {_name.Text.Trim()}")));
            left.AddChild(UiKit.Button("随机新名称", () =>
            {
                var g = _main.State.Genome;
                _main.State.Set(g.WithName(NameGenerator.Generate(new Rng(g.ContentHash ^ (ulong)DateTime.UtcNow.Ticks))), "新名称");
            }));
            AddChild(left);
            _text = new RichTextLabel { BbcodeEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, ScrollActive = true, SelectionEnabled = true };
            _text.AddThemeFontSizeOverride("normal_font_size", 13);
            AddChild(_text);
        }

        public void Refresh()
        {
            if (_text == null) return;
            var g = _main.State.Genome;
            if (!_name.HasFocus()) _name.Text = g.Name;
            var sb = new StringBuilder();
            var fam = CreatureRuntime.Registry.GetFamily(g.FamilyId);
            sb.Append($"[color=#e2ac4c]{fam.DisplayName}[/color]  ·  内容 ID {g.ContentId}  ·  生成器版本 v{g.GeneratorVersion}\n");
            sb.Append($"种子：结构 {g.AnatomySeed}，颜色 {g.ColorSeed}，花纹 {g.PatternSeed}，动作 {g.MotionSeed}\n");
            var lin = g.Lineage;
            sb.Append($"来源：{Origin(lin.Origin)}，第 {lin.Generation} 代");
            if (lin.ParentIds.Count > 0) sb.Append($"，亲代 {string.Join(" + ", lin.ParentIds.Select(p => p.Substring(0, Math.Min(8, p.Length))))}");
            if (lin.Provenance.Count > 0) sb.Append($"\n遗传：{string.Join("，", lin.Provenance.Select(kv => $"{kv.Key}={kv.Value}"))}");
            sb.Append('\n');
            var m = _main.PreviewActor.Model;
            if (m != null && m.Genome.ContentHash == g.ContentHash)
            {
                var a = m.Anatomy;
                sb.Append($"特征：{string.Join("、", a.Traits.Select(t => TraitLabels.Label(g, t)))}\n");
                sb.Append($"骨骼 {a.Skeleton.Count}，图元 {a.Primitives.Count}，分组 {a.Groups.Count}，面部特征 {a.Features.Count}，画布 {a.Canvas}\n");
                sb.Append(string.Format(CultureInfo.InvariantCulture, "体长 {0:0} px，腿长 {1:0} px，行走 {2:0} px/s，奔跑 {3:0} px/s，时间缩放 {4:0.00}\n",
                    a.Metrics.BodyLength, a.Metrics.LegLength, m.Motion.WalkSpeed, m.Motion.RunSpeed, m.Motion.TimeScale));
                int visible = CountVisibleColors();
                sb.Append($"调色板：定义了 {m.Palette.Colors.Count - CreaturePalette.FirstColorIndex} 种颜色，当前帧可见 {visible} 种\n");
                if (a.Repairs.Count > 0) sb.Append($"[color=#78b0e2]修正：[/color] {string.Join(" | ", a.Repairs)}\n");
                if (m.Warnings.Count > 0) sb.Append($"[color=#e26860]警告：[/color] {string.Join(" | ", m.Warnings)}\n");
                var st = _main.PreviewActor.LastStats;
                sb.Append($"上一帧：{st.PrimitivesDrawn} 个图元，{st.PixelsTested} 次像素测试，覆盖 {st.PixelsCovered} 个像素{(st.Overflow ? "，[color=#e26860]溢出[/color]" : "")}；画布扩展次数：{_main.PreviewActor.CanvasGrowths}\n");
            }
            var f = CreatureRuntime.Factory;
            var (h, miss, n) = f.AnatomyCacheStats;
            sb.Append($"结构缓存：{n} 条，命中 {h} 次，未命中 {miss} 次 · 丢弃的过期构建：{_main.PreviewActor.DiscardedBuilds}\n");
            sb.Append($"撤销：{_main.State.History.Index + 1}/{_main.State.History.Count} · 已保存：{(_main.State.SavedPath ?? "–")}{(_main.State.IsDirty ? "（已修改）" : "")}");
            _text.Text = sb.ToString();
        }

        private int CountVisibleColors()
        {
            var f = _main.PreviewActor.Frame;
            if (f == null) return 0;
            var seen = new bool[256];
            int count = 0;
            foreach (var b in f.Indices)
            {
                if (b < CreaturePalette.FirstColorIndex || seen[b]) continue;
                seen[b] = true;
                count++;
            }
            return count;
        }

        private static string Origin(string o) => o switch
        {
            "sampled" => "由种子生成",
            "mutation" => "变异",
            "crossover" => "杂交",
            "edited" => "手工编辑",
            "regenerated" => "部分重新生成",
            _ => o,
        };
    }
}
