// Procedural Pixel Creature Workshop - PNG and spritesheet export with metadata driven playback check.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using PixelCreatures.Core;
using PixelCreatures.Core.Export;
using PixelCreatures.Runtime;

namespace PixelCreatures.Workshop
{
    public partial class ExportPanel : VBoxContainer
    {
        private Main _main = null!;
        private readonly Dictionary<ClipKind, CheckBox> _clips = new Dictionary<ClipKind, CheckBox>();
        private OptionButton _dirs = null!, _fps = null!, _scale = null!;
        private CheckBox _shadow = null!;
        private ProgressBar _progress = null!;
        private Button _exportSheet = null!, _cancel = null!;
        private Label _result = null!;
        private CancellationTokenSource? _cts;
        private SpritesheetPlayer _player = null!;
        private string _lastSheetJson = string.Empty;

        public const string ExportRoot = "user://exports";

        public void Bind(Main main)
        {
            _main = main;
            AddThemeConstantOverride("separation", 4);
            var top = new HBoxContainer();
            top.AddThemeConstantOverride("separation", 10);
            AddChild(top);

            var left = UiKit.Column(4);
            left.AddChild(UiKit.Label("动画片段", 13, UiKit.TextDim));
            var clipGrid = new GridContainer { Columns = 4 };
            foreach (ClipKind k in Enum.GetValues(typeof(ClipKind)))
            {
                var cb = new CheckBox { Text = ClipLabel(k), ButtonPressed = true, FocusMode = FocusModeEnum.None };
                _clips[k] = cb;
                clipGrid.AddChild(cb);
            }
            left.AddChild(clipGrid);
            _dirs = new OptionButton { FocusMode = FocusModeEnum.None };
            _dirs.AddItem("8 个方向");
            _dirs.AddItem("4 个方向（东、北、西、南）");
            _dirs.AddItem("仅当前方向");
            _fps = new OptionButton { FocusMode = FocusModeEnum.None };
            foreach (int f in new[] { 6, 10, 12, 15, 20, 30 }) _fps.AddItem($"{f} fps");
            _fps.Selected = 2;
            _scale = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "单帧 PNG 导出的缩放倍数" };
            foreach (int s in new[] { 1, 2, 4, 8 }) _scale.AddItem($"PNG {s}x");
            _shadow = new CheckBox { Text = "阴影", ButtonPressed = true, FocusMode = FocusModeEnum.None };
            left.AddChild(UiKit.Row(6, _dirs, _fps, _scale, _shadow));
            var buttons = new HBoxContainer();
            buttons.AddThemeConstantOverride("separation", 6);
            buttons.AddChild(UiKit.Button("PNG：当前帧", ExportPng, "导出预览的当前帧"));
            _exportSheet = UiKit.Button("导出精灵图集", ExportSheet, "把选中的状态与方向导出为精灵图集与 JSON 元数据");
            buttons.AddChild(_exportSheet);
            _cancel = UiKit.Button("取消", () => _cts?.Cancel(), "取消正在进行的导出");
            _cancel.Disabled = true;
            buttons.AddChild(_cancel);
            buttons.AddChild(UiKit.Button("打开文件夹", () => OS.ShellOpen(ProjectSettings.GlobalizePath(ExportRoot)), "在文件管理器中打开导出文件夹"));
            left.AddChild(buttons);
            _progress = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, CustomMinimumSize = new Vector2(300, 14), ShowPercentage = true };
            left.AddChild(_progress);
            _result = UiKit.Label("导出文件保存在 user://exports/<名称>/", 12, UiKit.TextDim);
            _result.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _result.CustomMinimumSize = new Vector2(420, 0);
            left.AddChild(_result);
            top.AddChild(left);

            _player = new SpritesheetPlayer { CustomMinimumSize = new Vector2(260, 180), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            top.AddChild(_player);
            DirAccess.MakeDirRecursiveAbsolute(ExportRoot);
        }

        public static string ClipLabel(ClipKind k) => k switch
        {
            ClipKind.Idle => "站立",
            ClipKind.Walk => "行走",
            ClipKind.Run => "奔跑",
            ClipKind.Action => "动作",
            ClipKind.Hit => "受击",
            ClipKind.RestEnter => "趴下",
            ClipKind.Sleep => "睡觉",
            _ => "起身",
        };

        private string CreatureFolder()
        {
            var g = _main.State.Genome;
            string name = string.IsNullOrEmpty(g.Name) ? "creature" : g.Name.ToLowerInvariant();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string dir = $"{ExportRoot}/{name}_{g.ContentId.Substring(0, 8)}";
            DirAccess.MakeDirRecursiveAbsolute(dir);
            return dir;
        }

        private void ExportPng()
        {
            var actor = _main.PreviewActor;
            if (actor.Frame == null || actor.Model == null) return;
            int scale = new[] { 1, 2, 4, 8 }[Math.Max(0, _scale.Selected)];
            var f = actor.Frame;
            var lut = new byte[1024];
            actor.Model.Palette.BuildLut(lut, 0);
            var rgba = new byte[f.Width * f.Height * 4];
            f.ToRgba(lut, rgba, actor.Model.Palette.WaterTint);
            var png = PngEncoder.Encode(PngEncoder.Upscale(rgba, f.Width, f.Height, scale), f.Width * scale, f.Height * scale,
                ("Software", "Procedural Pixel Creatures " + CreatureFramework.FrameworkVersion), ("Creature", actor.Model.Genome.ContentId),
                ("Origin", $"{f.OriginX * scale},{f.OriginY * scale}"));
            string state = actor.State.ToString().ToLowerInvariant();
            string path = $"{CreatureFolder()}/frame_{state}_{PixelCamera.DirectionNames[_main.AnimPanel.Direction]}_{scale}x.png";
            WriteBytes(path, png);
            _result.Text = $"PNG 已保存：{path}（{f.Width * scale}x{f.Height * scale}，原点 {f.OriginX * scale},{f.OriginY * scale}）";
            _main.Status("PNG 已导出。", UiKit.Good);
        }

        private static void WriteBytes(string path, byte[] data)
        {
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            file?.StoreBuffer(data);
        }

        public void ExportSheet()
        {
            var model = _main.PreviewActor.Model;
            if (model == null) return;
            var opt = new SpritesheetOptions
            {
                Clips = _clips.Where(kv => kv.Value.ButtonPressed).Select(kv => kv.Key).ToList(),
                Fps = new[] { 6, 10, 12, 15, 20, 30 }[Math.Max(0, _fps.Selected)],
                DrawShadow = _shadow.ButtonPressed,
                Directions = _dirs.Selected switch
                {
                    1 => new List<int> { 0, 2, 4, 6 },
                    2 => new List<int> { _main.AnimPanel.Direction },
                    _ => new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 },
                },
            };
            if (opt.Clips.Count == 0) { _result.Text = "请至少选择一个动画片段。"; return; }
            string dir = CreatureFolder();
            string baseName = "spritesheet";
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            _exportSheet.Disabled = true;
            _cancel.Disabled = false;
            _progress.Value = 0;
            var registry = CreatureRuntime.Registry;
            var progress = new Progress<double>(v => Callable.From(() => { if (IsInstanceValid(_progress)) _progress.Value = v; }).CallDeferred());
            var started = DateTime.UtcNow;
            Task.Run(() => SpritesheetExporter.Export(model, registry, opt, baseName, cts.Token, progress), cts.Token).ContinueWith(t =>
            {
                Callable.From(() =>
                {
                    if (!IsInstanceValid(this)) return;
                    _exportSheet.Disabled = false;
                    _cancel.Disabled = true;
                    if (t.IsCanceled || cts.IsCancellationRequested) { _result.Text = "导出已取消。"; _progress.Value = 0; return; }
                    if (t.IsFaulted) { _result.Text = "导出失败：" + t.Exception?.GetBaseException().Message; return; }
                    var r = t.Result;
                    for (int i = 0; i < r.Pages.Count; i++)
                    {
                        var (rgba, w, h) = r.Pages[i];
                        string file = r.Pages.Count == 1 ? $"{baseName}.png" : $"{baseName}_{i}.png";
                        WriteBytes($"{dir}/{file}", PngEncoder.Encode(rgba, w, h, ("Software", "Procedural Pixel Creatures")));
                    }
                    using (var f = Godot.FileAccess.Open($"{dir}/{baseName}.json", Godot.FileAccess.ModeFlags.Write)) f?.StoreString(r.MetadataJson);
                    CreatureRuntime.SavePreset($"{dir}/genome.json", model.Genome);
                    _lastSheetJson = $"{dir}/{baseName}.json";
                    double secs = (DateTime.UtcNow - started).TotalSeconds;
                    _result.Text = $"精灵图集：{dir}/{baseName}.png + .json\n{r.Clips.Count} 个片段，共 {r.TotalFrames} 帧，单帧 {r.FrameWidth}x{r.FrameHeight}，原点（{r.OriginX},{r.OriginY}），耗时 {secs:0.0} 秒";
                    _player.Load(_lastSheetJson);
                    _main.Status("精灵图集已导出并载入，可用于回放校验。", UiKit.Good);
                }).CallDeferred();
            });
        }
    }

    /// <summary>Plays a clip of an exported spritesheet purely from its metadata (verifies the export).</summary>
    public partial class SpritesheetPlayer : VBoxContainer
    {
        private TextureRect _view = null!;
        private OptionButton _clipSelect = null!;
        private Label _info = null!;
        private readonly List<Texture2D> _pages = new List<Texture2D>();
        private readonly List<(string name, bool loop, List<(int x, int y, int page, int ms)> frames, double vx, double vy)> _clips = new List<(string, bool, List<(int, int, int, int)>, double, double)>();
        private int _fw, _fh, _ox, _oy;
        private int _frame;
        private double _t;
        private double _drift;

        public override void _Ready()
        {
            AddThemeConstantOverride("separation", 4);
            AddChild(UiKit.Label("回放校验（仅依据元数据）", 13, UiKit.TextDim));
            _clipSelect = new OptionButton { FocusMode = FocusModeEnum.None };
            _clipSelect.ItemSelected += _ => { _frame = 0; _t = 0; _drift = 0; };
            AddChild(_clipSelect);
            var bg = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
            _view = new TextureRect { TextureFilter = TextureFilterEnum.Nearest, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, SizeFlagsVertical = SizeFlags.ExpandFill };
            bg.AddChild(_view);
            AddChild(bg);
            _info = UiKit.Label("尚未导出精灵图集。", 11, UiKit.TextDim);
            _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            AddChild(_info);
        }

        public void Load(string jsonPath)
        {
            _clips.Clear();
            _pages.Clear();
            _clipSelect.Clear();
            using var f = Godot.FileAccess.Open(jsonPath, Godot.FileAccess.ModeFlags.Read);
            if (f == null) { _info.Text = "无法读取元数据。"; return; }
            var root = JsonNode.Parse(f.GetAsText())!.AsObject();
            _fw = (int)root["frameWidth"]!; _fh = (int)root["frameHeight"]!;
            _ox = (int)root["originX"]!; _oy = (int)root["originY"]!;
            string dir = jsonPath.GetBaseDir();
            foreach (var p in root["pages"]!.AsArray())
            {
                var img = Image.LoadFromFile($"{dir}/{(string)p!["file"]!}");
                _pages.Add(ImageTexture.CreateFromImage(img));
            }
            foreach (var c in root["clips"]!.AsArray())
            {
                var frames = new List<(int, int, int, int)>();
                foreach (var fr in c!["frames"]!.AsArray()) frames.Add(((int)fr!["x"]!, (int)fr["y"]!, (int)fr["page"]!, (int)fr["durationMs"]!));
                var rm = c["rootMotion"]!;
                _clips.Add(((string)c["name"]!, (bool)c["loop"]!, frames, (double)rm["screenVelocityX"]!, (double)rm["screenVelocityY"]!));
                _clipSelect.AddItem($"{ClipStateLabel((string)c["state"]!)} · {(string)c["directionName"]!}");
            }
            _info.Text = $"{_clips.Count} 个片段，单帧 {_fw}x{_fh}，原点（{_ox},{_oy}），{_pages.Count} 张图集。回放使用 JSON 中的时间信息。";
            _frame = 0;
            _t = 0;
        }

        /// <summary>Chinese label for a clip state of an exported spritesheet (the JSON keeps the English state id).</summary>
        private static string ClipStateLabel(string state) => state switch
        {
            "idle" => "站立",
            "walk" => "行走",
            "run" => "奔跑",
            "action" => "动作",
            "hit" => "受击",
            "rest_enter" => "趴下",
            "sleep" => "睡觉",
            _ => "起身",
        };

        public override void _Process(double delta)
        {
            if (_clips.Count == 0 || _clipSelect.Selected < 0) return;
            var clip = _clips[_clipSelect.Selected];
            if (clip.frames.Count == 0) return;
            _t += delta * 1000.0;
            while (_t >= clip.frames[_frame].ms)
            {
                _t -= clip.frames[_frame].ms;
                _frame++;
                if (_frame >= clip.frames.Count) _frame = clip.loop ? 0 : clip.frames.Count - 1;
                if (!clip.loop && _frame == clip.frames.Count - 1) { _t = 0; break; }
            }
            var fr = clip.frames[_frame];
            _view.Texture = new AtlasTexture { Atlas = _pages[fr.page], Region = new Rect2(fr.x, fr.y, _fw, _fh) };
        }
    }
}
