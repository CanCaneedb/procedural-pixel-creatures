// Procedural Pixel Creature Workshop - display names for the anatomy trait tags of a creature.
//
// The anatomy builders tag a creature with short technical ids ("digitigrade", "horns:curved",
// "shell:spiked"). Those ids are stored in no file and only exist for the info panel and the overview
// inspector, so they are resolved to the Chinese labels of the matching gene options for display.

using System;
using System.Collections.Generic;
using PixelCreatures.Core.Genetics;

namespace PixelCreatures.Workshop
{
    public static class TraitLabels
    {
        /// <summary>Prefixes of composite tags ("horns:curved" -> "角：弯角").</summary>
        private static readonly Dictionary<string, string> Prefixes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cap"] = "菌盖",
            ["horns"] = "角",
            ["back"] = "背部",
            ["weapon"] = "武器",
            ["outfit"] = "服装",
            ["headgear"] = "头饰",
            ["shell"] = "甲壳",
        };

        /// <summary>Tags that have no gene option behind them.</summary>
        private static readonly Dictionary<string, string> Standalone = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["elytra"] = "鞘翅",
            ["lure"] = "发光诱饵",
            ["shell"] = "有甲壳",
            ["wings"] = "有翼",
            ["hood"] = "颈盾",
            ["rattle"] = "响环",
            ["beard"] = "胡须",
            ["shield"] = "盾牌",
            ["spiked"] = "多刺",
        };

        /// <summary>Readable label for one anatomy trait tag; ids without a translation are shown as they are.</summary>
        public static string Label(CreatureGenome genome, string trait)
        {
            int colon = trait.IndexOf(':');
            if (colon <= 0) return Resolve(genome, trait) ?? trait;
            string head = trait.Substring(0, colon);
            string value = Resolve(genome, trait.Substring(colon + 1)) ?? trait.Substring(colon + 1);
            return Prefixes.TryGetValue(head, out string? prefix) ? $"{prefix}：{value}" : $"{head}：{value}";
        }

        /// <summary>Label of the choice option with this id (the gene labels are already localized), or null.</summary>
        private static string? Resolve(CreatureGenome genome, string id)
        {
            if (Standalone.TryGetValue(id, out string? standalone)) return standalone;
            foreach (var gene in genome.Schema.Genes)
            {
                if (gene.Kind != GeneKind.Choice) continue;
                foreach (var option in gene.Options)
                    if (option.Id == id) return option.Label;
            }
            return null;
        }
    }
}