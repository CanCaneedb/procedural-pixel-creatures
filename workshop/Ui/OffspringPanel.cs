// Procedural Pixel Creature Workshop - "Parent + 4 Children": four animated mutations of the current
// genome with adjustable mutation strength and stream selection. Any child can become the new parent.

using System;
using System.Globalization;
using Godot;
using PixelCreatures.Core;
using PixelCreatures.Core.Genetics;

namespace PixelCreatures.Workshop
{
    public partial class OffspringPanel : VBoxContainer
    {
        private Main _main = null!;
        private CreatureCard _parent = null!;
        private readonly CreatureCard[] _children = new CreatureCard[4];
        private readonly MutationSettings _settings = new MutationSettings { Strength = 0.35 };
        private bool _respectLocks = true;
        private ulong _litter = 1;
        private double _debounce = -1;
        private Label _strengthLabel = null!;

        public void Bind(Main main)
        {
            _main = main;
            AddThemeConstantOverride("separation", 4);
            var controls = new HBoxContainer();
            controls.AddThemeConstantOverride("separation", 6);
            _strengthLabel = UiKit.Label("变异强度 35%", 13);
            _strengthLabel.CustomMinimumSize = new Vector2(160, 0);
            controls.AddChild(_strengthLabel);
            var strength = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = _settings.Strength, CustomMinimumSize = new Vector2(150, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None };
            strength.ValueChanged += v => { _settings.Strength = v; _strengthLabel.Text = $"变异强度 {v * 100:0}%"; Schedule(); };
            controls.AddChild(strength);
            controls.AddChild(StreamToggle("结构", v => _settings.Anatomy = v, true));
            controls.AddChild(StreamToggle("颜色", v => _settings.Color = v, true));
            controls.AddChild(StreamToggle("花纹", v => _settings.Pattern = v, true));
            controls.AddChild(StreamToggle("动作", v => _settings.Motion = v, true));
            controls.AddChild(UiKit.Toggle("遵循锁定", true, v => { _respectLocks = v; Schedule(); }, "已锁定的特征不会变异"));
            controls.AddChild(UiKit.Spacer(0, 0, true));
            var reroll = new Button { Text = "重新掷种", Icon = Icons.Get("dice", 1), FocusMode = FocusModeEnum.None, TooltipText = "重新生成四个亲代变异体" };
            reroll.Pressed += () => { _litter++; Regenerate(); };
            controls.AddChild(reroll);
            AddChild(controls);

            var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 6);
            AddChild(row);
            _parent = new CreatureCard { SizeFlagsStretchRatio = 0.8f };
            row.AddChild(_parent);
            _parent.Build(main, "亲代", new Vector2(200, 120));
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
                c.AddButton("设为亲代", () => Adopt(idx), "这个子代成为新的亲代（可撤销）");
                _children[i] = c;
            }
            ParentChanged();
        }

        private Button StreamToggle(string label, Action<bool> set, bool initial)
        {
            return UiKit.Toggle(label, initial, v => { set(v); Schedule(); }, $"{label}可发生变异");
        }

        public void ParentChanged() => Schedule();

        private void Schedule() => _debounce = 0.15;

        public override void _Process(double delta)
        {
            if (_debounce >= 0)
            {
                _debounce -= delta;
                if (_debounce < 0 && IsVisibleInTree()) Regenerate();
                else if (_debounce < 0) _debounce = 0.3; // regenerate when the tab becomes visible
            }
            if (_main?.AnimPanel == null || !IsVisibleInTree()) return;
            var ap = _main.AnimPanel;
            _parent?.Mirror(ap.Mode, ap.Direction, ap.SpeedBlend);
            foreach (var c in _children) c?.Mirror(ap.Mode, ap.Direction, ap.SpeedBlend);
        }

        private void Regenerate()
        {
            var parent = _main.State.Genome;
            _parent.SetGenome(parent, $"亲代：{DisplayName(parent)}", $"第 {parent.Lineage.Generation} 代");
            for (int i = 0; i < 4; i++)
            {
                ulong seed = StableHash.Derive(StableHash.Combine(parent.ContentHash, _litter), "litter", i);
                var child = GenomeOps.Mutate(parent, _settings, seed, _respectLocks ? _main.State.Locks : null);
                child = child.WithName(parent.Name + " " + ToRoman(i + 1));
                _children[i].SetGenome(child, $"子代 {i + 1}", Describe(parent, child));
            }
        }

        private static string DisplayName(CreatureGenome g) => string.IsNullOrEmpty(g.Name) ? "（未命名）" : g.Name;

        private static string ToRoman(int i) => i switch { 1 => "I", 2 => "II", 3 => "III", _ => "IV" };

        /// <summary>Lists the most changed gene groups for a quick read of what differs.</summary>
        private static string Describe(CreatureGenome parent, CreatureGenome child)
        {
            var schema = parent.Schema;
            double body = 0, color = 0, pattern = 0, motion = 0;
            for (int i = 0; i < schema.Count; i++)
            {
                var d = schema.Genes[i];
                double diff = Math.Abs(parent[i] - child[i]) / Math.Max(1e-9, d.Range);
                if (d.Kind == GeneKind.Choice && parent[i] != child[i]) diff = 0.5;
                switch (d.Stream)
                {
                    case GeneStream.Anatomy: body += diff; break;
                    case GeneStream.Color: color += diff; break;
                    case GeneStream.Pattern: pattern += diff; break;
                    default: motion += diff; break;
                }
            }
            return string.Format(CultureInfo.InvariantCulture, "结构 {0:0.0} · 颜色 {1:0.0} · 花纹 {2:0.0}", body, color, pattern);
        }

        private void Adopt(int i)
        {
            var g = _children[i].Genome;
            if (g == null) return;
            _main.State.Set(g, $"子代 {i + 1} 已设为新的亲代");
            _litter++;
        }
    }
}
