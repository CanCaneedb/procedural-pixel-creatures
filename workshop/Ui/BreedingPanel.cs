// Procedural Pixel Creature Workshop - crossing two compatible parents and comparing their offspring.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PixelCreatures.Core;
using PixelCreatures.Core.Genetics;
using PixelCreatures.Runtime;

namespace PixelCreatures.Workshop
{
    public partial class BreedingPanel : VBoxContainer
    {
        private Main _main = null!;
        private CreatureCard _a = null!, _b = null!;
        private readonly CreatureCard[] _children = new CreatureCard[4];
        private CreatureGenome? _partner;
        private ulong _partnerSeed = 17;
        private ulong _cross = 1;
        private double _mutation = 0.08;
        private OptionButton _source = null!;
        private Label _message = null!;
        private readonly List<CreatureGenome> _samples = new List<CreatureGenome>();
        private double _debounce = -1;

        public void Bind(Main main)
        {
            _main = main;
            AddThemeConstantOverride("separation", 4);
            var controls = new HBoxContainer();
            controls.AddThemeConstantOverride("separation", 6);
            controls.AddChild(UiKit.Label("亲代 B：", 13));
            _source = new OptionButton { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 0), TooltipText = "第二个亲代（体型类别必须相同）" };
            _source.ItemSelected += _ => PickPartnerFromSource();
            controls.AddChild(_source);
            controls.AddChild(UiKit.Button("换个配偶", () => { _partnerSeed++; _source.Selected = 0; PickPartnerFromSource(); }, "随机生成一个同类别的新配偶"));
            controls.AddChild(UiKit.Button("从文件…", LoadPartner, "从预设载入配偶"));
            controls.AddChild(UiKit.Label("变异", 13, UiKit.TextDim));
            var mut = new HSlider { MinValue = 0, MaxValue = 0.6, Step = 0.01, Value = _mutation, CustomMinimumSize = new Vector2(90, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None };
            mut.ValueChanged += v => { _mutation = v; Schedule(); };
            controls.AddChild(mut);
            controls.AddChild(UiKit.Spacer(0, 0, true));
            var again = new Button { Text = "再杂交一次", Icon = Icons.Get("dice", 1), FocusMode = FocusModeEnum.None };
            again.Pressed += () => { _cross++; Regenerate(); };
            controls.AddChild(again);
            AddChild(controls);
            _message = UiKit.Label("", 12, UiKit.TextDim);
            AddChild(_message);

            var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 6);
            AddChild(row);
            var parents = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.8f };
            row.AddChild(parents);
            _a = new CreatureCard { SizeFlagsVertical = SizeFlags.ExpandFill }; parents.AddChild(_a); _a.Build(main, "亲代 A", new Vector2(180, 60));
            _b = new CreatureCard { SizeFlagsVertical = SizeFlags.ExpandFill }; parents.AddChild(_b); _b.Build(main, "亲代 B", new Vector2(180, 60));
            _b.AddButton("编辑 B", () => { if (_partner != null) _main.LoadGenome(_partner, "亲代 B 已载入编辑器"); }, "在编辑器中打开亲代 B");
            row.AddChild(new VSeparator());
            var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2.2f };
            grid.AddThemeConstantOverride("h_separation", 6);
            grid.AddThemeConstantOverride("v_separation", 4);
            row.AddChild(grid);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var c = new CreatureCard { SizeFlagsVertical = SizeFlags.ExpandFill };
                grid.AddChild(c);
                c.Build(main, $"子代 {i + 1}", new Vector2(160, 70));
                c.AddButton("采用", () => Adopt(idx), "把这个子代载入编辑器");
                _children[i] = c;
            }
            RefreshSources();
            PickPartnerFromSource();
        }

        private void RefreshSources()
        {
            _samples.Clear();
            _source.Clear();
            _source.AddItem("随机配偶");
            string fam = _main.State.Genome.FamilyId;
            foreach (var s in SampleLibrary.Load().Where(x => x.Genome.FamilyId == fam))
            {
                _samples.Add(s.Genome);
                _source.AddItem("样本：" + s.Genome.Name);
            }
            _source.Selected = 0;
        }

        private void PickPartnerFromSource()
        {
            var fam = _main.State.Family;
            int sel = _source.Selected;
            if (sel <= 0 || sel - 1 >= _samples.Count)
                _partner = GenomeFactory.Sample(fam, StableHash.Combine(_partnerSeed, 0xB2EEDUL));
            else _partner = _samples[sel - 1];
            Regenerate();
        }

        private void LoadPartner()
        {
            var dlg = new FileDialog
            {
                Access = FileDialog.AccessEnum.Userdata,
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Filters = new[] { "*.json ; 生物预设" },
                CurrentDir = "user://presets",
                Title = "载入亲代 B",
                Size = new Vector2I(720, 480),
                UseNativeDialog = false,
            };
            dlg.FileSelected += path =>
            {
                var res = CreatureRuntime.LoadPreset(path);
                if (!res.Ok) { _message.Text = "载入失败：" + res.Describe(); return; }
                _partner = res.Genome;
                Regenerate();
                dlg.QueueFree();
            };
            AddChild(dlg);
            dlg.PopupCentered();
        }

        public void ParentChanged()
        {
            if (_main == null) return;
            if (_partner != null && _partner.FamilyId != _main.State.Genome.FamilyId)
            {
                RefreshSources();
                PickPartnerFromSource();
                return;
            }
            Schedule();
        }

        private void Schedule() => _debounce = 0.2;

        public override void _Process(double delta)
        {
            if (_debounce >= 0)
            {
                _debounce -= delta;
                if (_debounce < 0 && IsVisibleInTree()) Regenerate();
                else if (_debounce < 0) _debounce = 0.3;
            }
            if (_main?.AnimPanel == null || !IsVisibleInTree()) return;
            var ap = _main.AnimPanel;
            _a?.Mirror(ap.Mode, ap.Direction, ap.SpeedBlend);
            _b?.Mirror(ap.Mode, ap.Direction, ap.SpeedBlend);
            foreach (var c in _children) c?.Mirror(ap.Mode, ap.Direction, ap.SpeedBlend);
        }

        private void Regenerate()
        {
            var a = _main.State.Genome;
            _a.SetGenome(a, "亲代 A：" + a.Name, a.FamilyId);
            if (_partner == null) return;
            _b.SetGenome(_partner, "亲代 B：" + _partner.Name, _partner.FamilyId);
            if (!GenomeOps.CanCross(a, _partner, out string reason))
            {
                _message.Text = reason;
                foreach (var c in _children) c.SetGenome(null, "—", "");
                return;
            }
            _message.Text = "关联的特征组会一起遗传（来自 A、B 或 A+B 混合）。";
            for (int i = 0; i < 4; i++)
            {
                ulong seed = StableHash.Derive(StableHash.Combine(StableHash.Combine(a.ContentHash, _partner.ContentHash), _cross), "cross", i);
                var r = GenomeOps.Cross(a, _partner, seed, new MutationSettings { Strength = _mutation });
                var child = r.Child.WithName(NameGenerator.Generate(new Rng(seed)));
                _children[i].SetGenome(child, $"子代 {i + 1}：{child.Name}", Provenance(a.Schema, r.Provenance));
            }
        }

        private static string Provenance(GeneSchema schema, IReadOnlyDictionary<string, string> prov)
        {
            var parts = new List<string>();
            foreach (var g in schema.Groups)
            {
                if (!prov.TryGetValue(g.Id, out var src)) continue;
                parts.Add($"{g.Label} {src}");
            }
            return string.Join(" · ", parts);
        }

        private void Adopt(int i)
        {
            var g = _children[i].Genome;
            if (g == null) return;
            _main.State.Set(g, $"已采用子代 {i + 1}");
        }
    }
}
