// Procedural Pixel Creatures - genes shared by every family (colour, pattern, motion) and their sampling.
//
// All families use the same colour/pattern/motion genes. Families only bias the sampling through
// FamilyStyleHints, which keeps palettes and motion personalities comparable across body plans.

using System;
using System.Collections.Generic;

namespace PixelCreatures.Core.Genetics
{
    /// <summary>
    /// A coat / skin colour family in gene units (OKLCH hue in degrees, saturation and value genes 0..1).
    /// Sampling picks a coat first and then the three colour genes inside its ranges, so natural archetypes
    /// get believable colours (black, cream, red, grey ...) instead of independent mid-range dice.
    /// </summary>
    public readonly struct CoatPrior
    {
        public readonly string Id;
        public readonly double Weight, Hue, HueSpread, SatMin, SatMax, ValueMin, ValueMax;

        public CoatPrior(string id, double weight, double hue, double hueSpread, double satMin, double satMax, double valueMin, double valueMax)
        {
            Id = id; Weight = weight; Hue = hue; HueSpread = hueSpread; SatMin = satMin; SatMax = satMax; ValueMin = valueMin; ValueMax = valueMax;
        }

        public CoatPrior WithWeight(double w) => new CoatPrior(Id, w, Hue, HueSpread, SatMin, SatMax, ValueMin, ValueMax);
    }

    /// <summary>Library of coat colours (natural fur, scales, feathers, chitin) and fantasy colours.</summary>
    public static class Coats
    {
        // mammal fur
        public static CoatPrior Black(double w) => new CoatPrior("black", w, 265, 20, 0.02, 0.12, 0.0, 0.08);
        public static CoatPrior Chocolate(double w) => new CoatPrior("chocolate", w, 50, 6, 0.25, 0.42, 0.08, 0.22);
        public static CoatPrior Brown(double w) => new CoatPrior("brown", w, 56, 7, 0.28, 0.5, 0.22, 0.42);
        public static CoatPrior Red(double w) => new CoatPrior("red", w, 44, 6, 0.6, 0.85, 0.36, 0.52);
        public static CoatPrior Ginger(double w) => new CoatPrior("ginger", w, 58, 6, 0.55, 0.8, 0.48, 0.64);
        public static CoatPrior Golden(double w) => new CoatPrior("golden", w, 76, 6, 0.38, 0.58, 0.55, 0.72);
        public static CoatPrior Tan(double w) => new CoatPrior("tan", w, 72, 8, 0.22, 0.4, 0.46, 0.66);
        public static CoatPrior Cream(double w) => new CoatPrior("cream", w, 84, 8, 0.06, 0.2, 0.8, 0.96);
        public static CoatPrior Grey(double w) => new CoatPrior("grey", w, 250, 25, 0.02, 0.1, 0.3, 0.62);
        public static CoatPrior Slate(double w) => new CoatPrior("slate", w, 245, 15, 0.08, 0.2, 0.2, 0.42);
        // scales, skin, chitin, feathers
        public static CoatPrior Olive(double w) => new CoatPrior("olive", w, 108, 10, 0.3, 0.55, 0.28, 0.5);
        public static CoatPrior Green(double w) => new CoatPrior("green", w, 140, 14, 0.45, 0.8, 0.35, 0.62);
        public static CoatPrior Sand(double w) => new CoatPrior("sand", w, 78, 8, 0.25, 0.45, 0.58, 0.78);
        public static CoatPrior Rust(double w) => new CoatPrior("rust", w, 38, 8, 0.5, 0.75, 0.3, 0.48);
        public static CoatPrior Yellow(double w) => new CoatPrior("yellow", w, 95, 6, 0.65, 0.9, 0.65, 0.82);
        public static CoatPrior Crimson(double w) => new CoatPrior("crimson", w, 25, 8, 0.65, 0.9, 0.38, 0.55);
        public static CoatPrior Blue(double w) => new CoatPrior("blue", w, 250, 15, 0.5, 0.85, 0.38, 0.62);
        public static CoatPrior Teal(double w) => new CoatPrior("teal", w, 195, 12, 0.45, 0.8, 0.4, 0.64);
        public static CoatPrior Violet(double w) => new CoatPrior("violet", w, 305, 15, 0.4, 0.75, 0.36, 0.6);
        public static CoatPrior Pink(double w) => new CoatPrior("pink", w, 5, 10, 0.3, 0.55, 0.62, 0.8);
        public static CoatPrior Silver(double w) => new CoatPrior("silver", w, 235, 20, 0.02, 0.1, 0.62, 0.82);
        public static CoatPrior Albino(double w) => new CoatPrior("albino", w, 20, 15, 0.04, 0.14, 0.85, 0.96);
        /// <summary>Saturated colour of any hue (fantasy creatures).</summary>
        public static CoatPrior Vivid(double w) => new CoatPrior("vivid", w, 0, 360, 0.55, 0.9, 0.4, 0.72);
        /// <summary>Muted colour of any hue (fantasy creatures).</summary>
        public static CoatPrior Muted(double w) => new CoatPrior("muted", w, 0, 360, 0.18, 0.42, 0.3, 0.62);
    }

    public sealed class FamilyStyleHints
    {
        /// <summary>
        /// Coat colours with weights. When set, they replace Hues/Saturation/Value: a coat is picked first and
        /// the colour genes are sampled inside its ranges.
        /// </summary>
        public CoatPrior[] Coats = Array.Empty<CoatPrior>();
        /// <summary>Chances of the independent marks: socks/stockings, dark face mask, piebald patches.</summary>
        public double SocksChance = 0.1, MaskChance = 0.05, PiebaldChance = 0.05;
        /// <summary>Preferred hue centres (degrees) with weights. Empty = uniform.</summary>
        public (double hue, double spread, double weight)[] Hues = Array.Empty<(double, double, double)>();
        public double SaturationMin = 0.25, SaturationMax = 0.85;
        public double ValueMin = 0.25, ValueMax = 0.8;
        /// <summary>Weights per surface material option (see SharedGenes.Materials).</summary>
        public Dictionary<string, double> Materials = new Dictionary<string, double> { ["hide"] = 1 };
        /// <summary>Weights per pattern option (see SharedGenes.Patterns).</summary>
        public Dictionary<string, double> Patterns = new Dictionary<string, double> { ["none"] = 1, ["stripes"] = 1, ["spots"] = 1 };
        /// <summary>Weights per accent style.</summary>
        public Dictionary<string, double> Accents = new Dictionary<string, double> { ["bone"] = 2, ["dark"] = 1, ["vivid"] = 1 };
        /// <summary>Weights per colour scheme (see SharedGenes.Schemes). Natural bodies favour natural schemes.</summary>
        public Dictionary<string, double> Schemes = new Dictionary<string, double>
        {
            ["natural"] = 3.8, ["analogous"] = 2.0, ["complementary"] = 1.4, ["split"] = 1.0, ["triadic"] = 0.8, ["monochrome"] = 1.0,
        };
        public double GlowChance = 0.1;
        public double CountershadeMin = 0.2, CountershadeMax = 0.8;
        public double EnergyMin = 0.2, EnergyMax = 0.9;
        public double WeightMin = 0.1, WeightMax = 0.9;
    }

    public static class SharedGenes
    {
        public static readonly (string id, string label)[] Schemes =
        {
            ("natural", "自然"), ("analogous", "邻近色"), ("complementary", "互补色"),
            ("triadic", "三角配色"), ("split", "分裂互补"), ("monochrome", "单色"),
        };

        public static readonly (string id, string label)[] Accents =
        {
            ("bone", "骨白色"), ("dark", "深色"), ("vivid", "鲜亮"), ("gold", "金色"),
        };

        public static readonly (string id, string label)[] Materials =
        {
            ("fur", "毛皮"), ("hide", "皮革"), ("scales", "鳞片"), ("chitin", "甲壳质"), ("stone", "石质"),
            ("slime", "黏液"), ("feather", "羽毛"), ("bark", "树皮"), ("leaf", "叶片"),
        };

        public static readonly (string id, string label)[] Patterns =
        {
            ("none", "无"), ("stripes", "条纹"), ("spots", "斑点"), ("rosettes", "花斑"),
            ("saddle", "鞍形斑"), ("dorsal", "背脊纹"), ("mottled", "斑驳"), ("bands", "横带"),
            ("blotches", "大斑块"),
        };

        public static void AddColorGenes(GeneSchemaBuilder b)
        {
            b.Group("palette", "颜色", GeneStream.Color)
             .Hue("color.hue", "主色相", 30, "主色在 OKLCH 中的色相。")
             .Float("color.saturation", "饱和度", 0, 1, 0.45, "主色的彩度。", 0.14)
             .Float("color.value", "明度", 0, 1, 0.5, "主色的明度。", 0.12)
             .Choice("color.scheme", "配色方案", Schemes, "natural", "腹部、斑纹与点缀色相对主色的关系。", 0.15)
             .Float("color.secondaryShift", "次色相偏移", -1, 1, 0, "在配色方案内对次色相做微调。", 0.2, unit: DisplayUnit.Plain)
             .Float("color.bellyLight", "腹部明度", 0, 1, 0.5, "腹部与浅色斑纹的明度。", 0.12)
             .Float("color.markingDark", "斑纹对比度", 0, 1, 0.5, "深色斑纹的暗度与对比度。", 0.12)
             .Choice("color.accent", "角与爪材质", Accents, "bone", "角、爪、棘刺与喙的材质。", 0.12)
             .Hue("color.eyeHue", "眼睛色相", 50, "虹膜的色相。")
             .Float("color.glow", "自发光", 0, 1, 0, "发光部位与发光眼睛的强度。", 0.12)
             .Float("color.hueShift", "明暗色相偏移", 0, 1, 0.6, "经典像素画色阶中较暖的高光与较冷的阴影。", 0.1)
             .Float("color.outline", "描边暗度", 0, 1, 0.55, "外描边的暗度。", 0.1);
        }

        public static void AddPatternGenes(GeneSchemaBuilder b, string defaultMaterial)
        {
            b.Group("pattern", "花纹与材质", GeneStream.Pattern)
             .Choice("surface.material", "表面材质", Materials, defaultMaterial, "表面质感：毛皮、鳞片、甲壳质、石质等。", 0.08)
             .Float("surface.texture", "表面纹理", 0, 1, 0.5, "细微纹理强度：鳞列、毛丝与甲片。", 0.12)
             .Choice("pattern.type", "花纹类型", Patterns, "none", "附着于身体的花纹类型。", 0.12)
             .Float("pattern.size", "花纹大小", 0, 1, 0.5, "花纹的尺寸。", 0.12)
             .Float("pattern.density", "花纹密度", 0, 1, 0.5, "花纹覆盖的面积。", 0.12)
             .Bool("pattern.dark", "深色花纹", true, "开启时为深色花纹，关闭时为浅色花纹。", 0.08)
             .Float("pattern.countershade", "反荫蔽", 0, 1, 0.5, "腹部浅色区域的范围。", 0.12)
             .Float("pattern.tips", "末梢花纹", 0, 1, 0, "尾巴、耳朵与足端的对比色。", 0.12)
             .Float("pattern.muzzle", "口鼻花纹", 0, 1, 0, "较浅的口鼻区域。", 0.12)
             .Float("pattern.socks", "腿部花纹", 0, 1, 0, "小腿的对比色：短袜或长袜。", 0.12)
             .Bool("pattern.socksLight", "浅色腿纹", true, "开启时为白色短袜，关闭时为深色长袜。", 0.08)
             .Float("pattern.mask", "眼罩斑纹", 0, 1, 0, "横跨眼部的深色眼罩。", 0.12)
             .Float("pattern.piebald", "白斑", 0, 1, 0, "边缘分明的大块白色斑块。", 0.12);
        }

        public static void AddMotionGenes(GeneSchemaBuilder b)
        {
            b.Group("motion", "动作", GeneStream.Motion)
             .Float("motion.energy", "活力", 0, 1, 0.5, "活跃度：待机动作速度与小动作的频率。", 0.12)
             .Float("motion.weight", "重量感", 0, 1, 0.5, "感知重量：压缩、预备与跟随动作。", 0.12)
             .Float("motion.stiffness", "动作刚度", 0, 1, 0.5, "尾巴、耳朵与柔软附肢的弹性刚度。", 0.12)
             .Float("motion.bounce", "弹跳", 0, 1, 0.5, "垂直起伏与挤压拉伸的强度。", 0.12)
             .Float("motion.aggression", "攻击性", 0, 1, 0.5, "动作与面部表情的强度。", 0.12)
             .Float("motion.curiosity", "好奇心", 0, 1, 0.5, "四处张望、抖耳与嗅探的频率。", 0.12)
             .Float("motion.speed", "动作速度", 0, 1, 0.5, "相对最大速度。", 0.1);
        }

        private static string PickWeighted(Rng rng, Dictionary<string, double> weights, (string id, string label)[] options)
        {
            var keys = new List<string>();
            var w = new List<double>();
            foreach (var o in options)
            {
                if (weights.TryGetValue(o.id, out double v) && v > 0)
                {
                    keys.Add(o.id);
                    w.Add(v);
                }
            }
            if (keys.Count == 0) return options[0].id;
            return keys[rng.Weighted(w)];
        }

        private static int IndexOf((string id, string label)[] options, string id)
        {
            for (int i = 0; i < options.Length; i++) if (options[i].id == id) return i;
            return 0;
        }

        public static void SampleColor(GeneSchema schema, double[] values, Rng rng, FamilyStyleHints hints)
        {
            double hue, sat, val;
            if (hints.Coats.Length > 0)
            {
                var weights = new List<double>();
                foreach (var c in hints.Coats) weights.Add(c.Weight);
                var coat = hints.Coats[rng.Weighted(weights)];
                hue = coat.HueSpread >= 180 ? rng.Range(0.0, 360.0) : ColorWrap(coat.Hue + rng.Gaussian(0, coat.HueSpread, 2.0));
                sat = rng.Range(coat.SatMin, coat.SatMax);
                val = rng.Range(coat.ValueMin, coat.ValueMax);
            }
            else
            {
                if (hints.Hues.Length > 0)
                {
                    var weights = new List<double>();
                    foreach (var h in hints.Hues) weights.Add(h.weight);
                    var pick = hints.Hues[rng.Weighted(weights)];
                    hue = ColorWrap(pick.hue + rng.Gaussian(0, pick.spread, 2.0));
                }
                else hue = rng.Range(0.0, 360.0);
                sat = rng.Range(hints.SaturationMin, hints.SaturationMax);
                val = rng.Range(hints.ValueMin, hints.ValueMax);
            }
            Set(schema, values, "color.hue", hue);
            Set(schema, values, "color.saturation", sat);
            Set(schema, values, "color.value", val);
            string scheme = PickWeighted(rng, hints.Schemes, Schemes);
            Set(schema, values, "color.scheme", IndexOf(Schemes, scheme));
            Set(schema, values, "color.secondaryShift", rng.Range(-0.6, 0.6));
            Set(schema, values, "color.bellyLight", rng.Range(0.25, 0.85));
            Set(schema, values, "color.markingDark", rng.Range(0.3, 0.85));
            Set(schema, values, "color.accent", IndexOf(Accents, PickWeighted(rng, hints.Accents, Accents)));
            double eye = rng.NextDouble();
            // OKLCH hues: amber/gold 65-95, red-orange 30-50, green-teal 140-200, violet 285-320
            double eyeHue = eye < 0.45 ? rng.Range(65.0, 95.0) : eye < 0.65 ? rng.Range(30.0, 50.0) : eye < 0.85 ? rng.Range(140.0, 200.0) : rng.Range(285.0, 320.0);
            Set(schema, values, "color.eyeHue", eyeHue);
            Set(schema, values, "color.glow", rng.Chance(hints.GlowChance) ? rng.Range(0.5, 1.0) : 0.0);
            Set(schema, values, "color.hueShift", rng.Range(0.4, 0.9));
            Set(schema, values, "color.outline", rng.Range(0.35, 0.8));
        }

        public static void SamplePattern(GeneSchema schema, double[] values, Rng rng, FamilyStyleHints hints)
        {
            Set(schema, values, "surface.material", IndexOf(Materials, PickWeighted(rng, hints.Materials, Materials)));
            Set(schema, values, "surface.texture", rng.Range(0.2, 0.8));
            Set(schema, values, "pattern.type", IndexOf(Patterns, PickWeighted(rng, hints.Patterns, Patterns)));
            Set(schema, values, "pattern.size", rng.Range(0.2, 0.8));
            Set(schema, values, "pattern.density", rng.Range(0.3, 0.8));
            Set(schema, values, "pattern.dark", rng.Chance(0.72) ? 1 : 0);
            Set(schema, values, "pattern.countershade", rng.Range(hints.CountershadeMin, hints.CountershadeMax));
            Set(schema, values, "pattern.tips", rng.Chance(0.3) ? rng.Range(0.3, 0.9) : 0.0);
            Set(schema, values, "pattern.muzzle", rng.Chance(0.3) ? rng.Range(0.3, 0.9) : 0.0);
            Set(schema, values, "pattern.socks", rng.Chance(hints.SocksChance) ? rng.Range(0.3, 1.0) : 0.0);
            Set(schema, values, "pattern.socksLight", rng.Chance(0.7) ? 1 : 0);
            Set(schema, values, "pattern.mask", rng.Chance(hints.MaskChance) ? rng.Range(0.35, 1.0) : 0.0);
            Set(schema, values, "pattern.piebald", rng.Chance(hints.PiebaldChance) ? rng.Range(0.25, 0.85) : 0.0);
        }

        public static void SampleMotion(GeneSchema schema, double[] values, Rng rng, FamilyStyleHints hints)
        {
            Set(schema, values, "motion.energy", rng.Range(hints.EnergyMin, hints.EnergyMax));
            Set(schema, values, "motion.weight", rng.Range(hints.WeightMin, hints.WeightMax));
            Set(schema, values, "motion.stiffness", rng.Range(0.2, 0.8));
            Set(schema, values, "motion.bounce", rng.Range(0.2, 0.8));
            Set(schema, values, "motion.aggression", rng.Range(0.2, 0.9));
            Set(schema, values, "motion.curiosity", rng.Range(0.2, 0.9));
            Set(schema, values, "motion.speed", rng.Range(0.3, 0.8));
        }

        private static double ColorWrap(double h)
        {
            h %= 360.0;
            if (h < 0) h += 360.0;
            return h;
        }

        public static void Set(GeneSchema schema, double[] values, string id, double v)
        {
            int i = schema.IndexOf(id);
            if (i >= 0) values[i] = schema.Genes[i].Quantize(v);
        }
    }
}
