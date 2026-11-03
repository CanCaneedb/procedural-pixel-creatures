// Procedural Pixel Creature Workshop - animation controls for the preview actor.

using System;
using Godot;
using PixelCreatures.Core;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Motion;
using PixelCreatures.Runtime;

namespace PixelCreatures.Workshop
{
    public enum MoveMode
    {
        Stand,
        Walk,
        Run,
    }

    public partial class AnimationPanel : VBoxContainer
    {
        public CreatureActor Actor { get; private set; } = null!;
        public PixelStage Stage { get; private set; } = null!;
        public MoveMode Mode { get; private set; } = MoveMode.Stand;
        public int Direction { get; private set; } = 7;
        public double SpeedBlend { get; private set; } = 0.5;
        public event Action? SettingsChanged;

        private Label _status = null!;
        private OptionButton _bgOption = null!;
        private bool _userBackground;
        private bool _autoWater;
        private Button[] _modeButtons = Array.Empty<Button>();
        private Button _restButton = null!;
        private Button _pauseButton = null!;
        private readonly Button[] _dirButtons = new Button[8];
        private HSlider _speed = null!;
        private Label _speedLabel = null!;
        private HSlider _slowmo = null!;
        private Label _slowLabel = null!;

        public void Bind(CreatureActor actor, PixelStage stage)
        {
            Actor = actor;
            Stage = stage;
            AddThemeConstantOverride("separation", 6);
            AddChild(UiKit.Header("动画"));

            // --- movement state
            var modes = new HBoxContainer();
            modes.AddThemeConstantOverride("separation", 4);
            _modeButtons = new[]
            {
                UiKit.Button("站立", () => SetMode(MoveMode.Stand), "待机：呼吸、眨眼、小幅动作（1）"),
                UiKit.Button("行走", () => SetMode(MoveMode.Walk), "慢速移动（2）"),
                UiKit.Button("奔跑", () => SetMode(MoveMode.Run), "快速移动（3）"),
            };
            foreach (var b in _modeButtons) { b.ToggleMode = true; b.SizeFlagsHorizontal = SizeFlags.ExpandFill; modes.AddChild(b); }
            AddChild(modes);
            var acts = new HBoxContainer();
            acts.AddThemeConstantOverride("separation", 4);
            var action = UiKit.Button("动作", () => Actor.TriggerAction(), "富有表现力的动作（空格）");
            var hit = UiKit.Button("受击", () => Actor.TriggerHit(DirVector(Direction), 1f), "来自正面的受击反应（H）");
            _restButton = UiKit.Toggle("休息", false, on => { Actor.Resting = on; Emit(); }, "趴下／睡觉，含过渡动作（R）");
            foreach (var b in new[] { action, hit, _restButton }) { b.SizeFlagsHorizontal = SizeFlags.ExpandFill; acts.AddChild(b); }
            AddChild(acts);

            // --- direction pad
            AddChild(UiKit.Label("朝向", 13, UiKit.TextDim));
            var grid = new GridContainer { Columns = 3 };
            grid.AddThemeConstantOverride("h_separation", 3);
            grid.AddThemeConstantOverride("v_separation", 3);
            int[] layout = { 3, 2, 1, 4, -1, 0, 5, 6, 7 };
            foreach (int d in layout)
            {
                if (d < 0)
                {
                    var turn = new Button { Text = "45°", FocusMode = FocusModeEnum.None, TooltipText = "旋转 45°（Q/E）", CustomMinimumSize = new Vector2(34, 30) };
                    turn.Pressed += () => SetDirection((Direction + 7) % 8);
                    grid.AddChild(turn);
                    continue;
                }
                int dd = d;
                var b = new Button { Icon = Icons.Direction(d, 2), ToggleMode = true, FocusMode = FocusModeEnum.None, TooltipText = PixelCamera.DirectionLabels[d], CustomMinimumSize = new Vector2(34, 30) };
                b.Pressed += () => SetDirection(dd);
                _dirButtons[d] = b;
                grid.AddChild(b);
            }
            var gridRow = new HBoxContainer();
            gridRow.AddChild(grid);
            AddChild(gridRow);

            // --- speed
            _speedLabel = UiKit.Label("Speed", 13, UiKit.TextDim);
            AddChild(_speedLabel);
            _speed = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = SpeedBlend, FocusMode = FocusModeEnum.None, TooltipText = "在行走与奔跑速度之间混合" };
            _speed.ValueChanged += v => { SpeedBlend = v; ApplyMovement(); };
            AddChild(_speed);

            // --- time controls
            AddChild(UiKit.Label("时间", 13, UiKit.TextDim));
            var time = new HBoxContainer();
            time.AddThemeConstantOverride("separation", 4);
            _pauseButton = new Button { Icon = Icons.Get("pause"), ToggleMode = true, FocusMode = FocusModeEnum.None, TooltipText = "暂停（P）" };
            _pauseButton.Toggled += on => { Actor.Paused = on; _pauseButton.Icon = Icons.Get(on ? "play" : "pause"); Emit(); };
            var step = new Button { Icon = Icons.Get("step"), FocusMode = FocusModeEnum.None, TooltipText = "单步：前进一帧（N）" };
            step.Pressed += () => { if (!Actor.Paused) _pauseButton.ButtonPressed = true; Actor.StepFrames(1); };
            time.AddChild(_pauseButton);
            time.AddChild(step);
            _slowmo = new HSlider { MinValue = 0.05, MaxValue = 1.0, Step = 0.05, Value = 1.0, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None, TooltipText = "慢动作" };
            _slowLabel = UiKit.Label("100%", 12, UiKit.TextDim);
            _slowmo.ValueChanged += v => { Actor.TimeScale = (float)v; _slowLabel.Text = $"{v * 100:0}%"; };
            time.AddChild(_slowmo);
            time.AddChild(_slowLabel);
            AddChild(time);

            var rate = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "像素动画帧率：每秒重新光栅化新姿势的次数" };
            int[] rates = { 8, 12, 15, 24, 30, 60 };
            foreach (int r in rates) rate.AddItem($"{r} fps");
            rate.Selected = Array.IndexOf(rates, Actor.AnimationFps) >= 0 ? Array.IndexOf(rates, Actor.AnimationFps) : 2;
            rate.ItemSelected += i => Actor.AnimationFps = rates[i];
            var stepped = UiKit.Toggle("步进式", Actor.SteppedMovement, on => Actor.SteppedMovement = on, "仅在动画帧更新位置（经典像素观感）");
            AddChild(UiKit.Row(4, rate, stepped));

            // --- overlays
            AddChild(UiKit.Label("显示", 13, UiKit.TextDim));
            var ov = new HBoxContainer();
            ov.AddThemeConstantOverride("separation", 4);
            ov.AddChild(UiKit.Toggle("骨架", false, on => Actor.ShowRig = on, "显示骨架的骨骼"));
            ov.AddChild(UiKit.Toggle("接触点", false, on => Actor.ShowContacts = on, "显示脚部接触点（绿色 = 着地）与画布边界"));
            ov.AddChild(UiKit.Toggle("阴影", true, on => { Actor.DrawShadow = on; Actor.RequestRender(); }, "地面阴影"));
            AddChild(ov);
            var bg = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "预览背景（水生生物会自动切换到水面）" };
            string[] bgs = { "草地", "草甸", "泥土", "沙地", "水面", "中性", "深色" };
            foreach (var b in bgs) bg.AddItem(b);
            bg.ItemSelected += i => { Stage.Background = (StageBackground)(int)i; _userBackground = true; Emit(); };
            _bgOption = bg;
            var zoomOut = UiKit.Button("-", () => Stage.SetZoom(Math.Max(1, Stage.EffectiveZoom - 1)), "缩小");
            var zoomAuto = UiKit.Button("自动", () => Stage.SetZoom(0), "自动使用整数缩放");
            var zoomIn = UiKit.Button("+", () => Stage.SetZoom(Math.Min(16, Stage.EffectiveZoom + 1)), "放大");
            AddChild(UiKit.Row(4, bg, zoomOut, zoomAuto, zoomIn));

            _status = UiKit.Label("", 12, UiKit.TextDim);
            _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _status.CustomMinimumSize = new Vector2(200, 0);
            AddChild(_status);
            SetMode(MoveMode.Stand);
            SetDirection(7);
        }

        private void Emit() => SettingsChanged?.Invoke();

        public static Vector2 DirVector(int dir8)
        {
            double yaw = PixelCamera.DirectionYaw(dir8);
            return new Vector2((float)DMath.Cos(yaw), (float)-DMath.Sin(yaw));
        }

        public void SetMode(MoveMode mode)
        {
            Mode = mode;
            for (int i = 0; i < _modeButtons.Length; i++) _modeButtons[i].SetPressedNoSignal(i == (int)mode);
            if (mode == MoveMode.Walk) _speed.SetValueNoSignal(SpeedBlend = 0.0);
            if (mode == MoveMode.Run) _speed.SetValueNoSignal(SpeedBlend = 1.0);
            if (mode != MoveMode.Stand && _restButton.ButtonPressed) _restButton.ButtonPressed = false;
            ApplyMovement();
        }

        public void SetDirection(int dir8)
        {
            Direction = ((dir8 % 8) + 8) % 8;
            for (int i = 0; i < 8; i++) _dirButtons[i]?.SetPressedNoSignal(i == Direction);
            Actor.FaceDirection(Direction);
            ApplyMovement();
        }

        public void ToggleRest() => _restButton.ButtonPressed = !_restButton.ButtonPressed;
        public void TogglePause() => _pauseButton.ButtonPressed = !_pauseButton.ButtonPressed;

        /// <summary>Swimmers are previewed over water, everything else keeps the chosen ground.</summary>
        public void AutoBackground()
        {
            var model = Actor.Model;
            if (model == null || _bgOption == null) return;
            bool swimmer = model.Anatomy.Rig.Locomotion == LocomotionKind.Swim;
            if (swimmer && Stage.Background != StageBackground.Water && (!_userBackground || _autoWater))
            {
                Stage.Background = StageBackground.Water;
                _bgOption.Selected = (int)StageBackground.Water;
                _autoWater = true;
            }
            else if (!swimmer && _autoWater && Stage.Background == StageBackground.Water)
            {
                Stage.Background = StageBackground.Grass;
                _bgOption.Selected = (int)StageBackground.Grass;
                _autoWater = false;
            }
        }

        public void ApplyMovement()
        {
            var model = Actor.Model;
            if (model == null) return;
            double speed = 0;
            if (Mode != MoveMode.Stand)
                speed = DMath.Lerp(model.Motion.WalkSpeed, model.Motion.RunSpeed, SpeedBlend);
            Actor.MoveVelocity = DirVector(Direction) * (float)speed;
            _speedLabel.Text = Mode == MoveMode.Stand ? "速度（站立）" : $"速度 {speed:0} px/s";
            Emit();
        }

        /// <summary>Localized name of a motion state (shared with the test area HUD).</summary>
        public static string StateLabel(MotionStateKind state, LocomotionKind loco, bool airborne) => state switch
        {
            MotionStateKind.Idle => loco == LocomotionKind.Swim ? "水中漂浮" : (airborne ? "悬停" : "站立"),
            MotionStateKind.Walk => loco switch { LocomotionKind.Swim => "游动", LocomotionKind.Serpentine => "蜿蜒滑行", LocomotionKind.Hop => "跳跃", LocomotionKind.Float => "滑翔", _ => airborne ? "飞行" : "行走" },
            MotionStateKind.Run => loco switch { LocomotionKind.Swim => "快速游动", LocomotionKind.Serpentine => "快速滑行", LocomotionKind.Hop => "远距离跳跃", LocomotionKind.Float => "快速滑翔", _ => airborne ? "飞行" : "奔跑" },
            MotionStateKind.Action => "动作",
            MotionStateKind.Hit => "受击",
            MotionStateKind.Rest => "休息／睡眠",
            _ => "过渡",
        };

        public override void _Process(double delta)
        {
            if (Actor?.Motor == null || _status == null) return;
            var m = Actor.Motor;
            var loco = Actor.Model?.Anatomy.Rig.Locomotion ?? LocomotionKind.Legged;
            bool airborne = m.Altitude > 2.0 && loco != LocomotionKind.Swim;
            string state = StateLabel(m.State, loco, airborne);
            _status.Text = $"状态：{state}\n速度：{m.Speed:0.0} px/s  朝向：{PixelCamera.DirectionLabels[PixelCamera.YawToDirection8(m.Yaw)]}\n帧率：{Actor.AnimationFps} fps  缩放：{Stage.EffectiveZoom}×\n帧尺寸：{Actor.Frame?.Width}×{Actor.Frame?.Height}";
        }
    }
}
