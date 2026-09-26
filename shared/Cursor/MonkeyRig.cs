using System;
using Godot;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 커서 원숭이 리그 (docs/B17-CURSOR-REWORK.md §4-2, §5). <b>원점이 원숭이가 바나나를 잡은 손(grip)이다.</b>
///
/// 머리만 그림(<see cref="MonkeySkin.Head"/>)이고 몸통·팔·다리·손발·꼬리는 여기서 그린다 — 본편 원숭이와 같은
/// 그림체(굵은 진갈색 외곽선 + 단색 + 밝은 배)라 어떤 자세든 되고, 스킨은 색만 바꾼다.
///
/// <b>기본 자세는 한 팔 매달림이다.</b> 머리가 커서(너비 약 44px) 두 팔로 매달리면 팔이 머리 뒤에 숨는다. 오른팔로
/// 잡고 머리는 왼쪽으로 기울인다 — 빈 왼팔이 반응(흔들기·신남)을 맡는다.
///
/// 움직임 대부분은 물리다: 잡은 손을 축으로 한 진자에 커서의 가속도가 걸린다(<see cref="Follow"/>). 상태는 커서
/// 속도·가속도와 입력(횟수만 — <see cref="Keystrokes"/>)으로 정한다.
///
/// <b>저부하 (A7):</b> 초당 <see cref="UpdateHz"/> 번만 모양을 바꾼다 — 저전력 모드라 안 바뀐 프레임은 다시 그리지
/// 않는다. 졸기에 들어가 몇 초 지나면 아예 멈춘다(<see cref="IsFrozen"/>). 오래 켜 두는 시간의 대부분이 졸기다.
/// </summary>
public partial class MonkeyRig : Node2D
{
    public enum Pose
    {
        /// <summary>커서 정지. 숨쉬기 + 가끔 대기 동작(흔들기·두리번·발차기).</summary>
        Hang,

        /// <summary>커서 이동. 진자 흔들림.</summary>
        Swing,

        /// <summary>빠른 이동. 다리 버둥.</summary>
        Flail,

        /// <summary>순간 가속에 손을 놓쳤다가 다시 잡는다.</summary>
        Drop,

        /// <summary>입력 반응. 빈 팔을 들고 신남.</summary>
        Cheer,

        /// <summary>오래 아무 일 없음. 눈 감고 느린 숨, 곧 멈춘다.</summary>
        Sleep,
    }

    // --- 조정 표 (체감으로 맞춘다 - B17 §5) ---------------------------------------------------

    public const float UpdateHz = 15f;

    /// <summary>커서 속도(px/s)가 이보다 크면 흔들림, 그 위는 버둥.</summary>
    private const float SwingSpeed = 60f, FlailSpeed = 1100f;

    /// <summary>커서 가속도(px/s²)가 이보다 크면 손을 놓친다.</summary>
    private const float DropAccel = 22000f;

    /// <summary>입력·이동이 이만큼(초) 없으면 존다. 졸기 들어가고 이만큼 뒤 멈춘다.</summary>
    private const float SleepAfter = 180f, FreezeAfter = 4f;

    private const float Gravity = 1600f, Damping = 3.8f, MaxAccel = 12000f, MaxAngle = 0.95f;

    /// <summary>머리 너비(px). 그림(207px)을 이만큼으로 줄인다.</summary>
    private const float HeadWidth = 44f;

    // --- 기본 자세의 뼈대 (원점 = 잡은 손, y 아래) — 진자 각도 0 일 때 ------------------------------

    private static readonly Vector2 ShoulderR = new(4, 36);   // 잡은 팔의 어깨
    private static readonly Vector2 ShoulderL = new(-17, 41);  // 빈 팔의 어깨 (머리 아래)
    private static readonly Vector2 Neck = new(-20, 39);
    private static readonly Vector2 Chest = new(-7, 50);
    private static readonly Vector2 HipL = new(-13, 60), HipR = new(-2, 62);
    private static readonly Vector2 TailRoot = new(1, 58);
    private const float PendulumLength = 48f;
    private const float ArmWidth = 6f, LegWidth = 6.5f, LegLength = 15f, FreeArmLength = 20f;
    private const float TailSegment = 4.6f;

    private MonkeySkin _skin;
    private Sprite2D _head;
    private Node2D _face;
    private readonly Random _rng = new();

    // 물리
    private float _angle, _angularVelocity;
    private Vector2 _cursor, _lastStepCursor, _velocity, _accel;
    private bool _hasCursor;

    // 상태
    private double _stepAccum, _time, _stateTime, _sinceActivity, _cheerLeft, _dropLeft, _idleActionLeft, _nextIdleAction = 6;
    private int _idleAction;   // 0 없음, 1 손 흔들기, 2 두리번, 3 발차기
    private float _dropY, _freeArmAngle, _headTilt, _legPhase, _bob;
    private readonly Vector2[] _tail = new Vector2[6];

    public Pose State { get; private set; } = Pose.Hang;

    /// <summary>졸다가 멈췄다 — 이 동안은 다시 그리지 않는다.</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>친구 창처럼 커서가 없는 곳. 진자·버둥·놓침이 없고 매달림·반응·졸기만 한다.</summary>
    public bool Still { get; set; }

    public override void _Ready()
    {
        _head = new Sprite2D { Centered = false, ShowBehindParent = false };
        AddChild(_head);
        _face = new Node2D();
        _face.Draw += DrawFace;
        _head.AddChild(_face);

        for (int i = 0; i < _tail.Length; i++)
        {
            _tail[i] = TailRoot + new Vector2(4 + i * 4, 2 + i * 2);
        }

        ApplySkin();
    }

    public void SetSkin(MonkeySkin skin)
    {
        _skin = skin;
        if (IsInsideTree())
        {
            ApplySkin();
        }
    }

    private void ApplySkin()
    {
        if (_skin?.Head == null)
        {
            _head.Texture = null;
            return;
        }

        _head.Texture = _skin.Head;
        float s = HeadWidth / _skin.Head.GetWidth();
        _head.Scale = Vector2.One * s;
        _head.Offset = -_skin.Neck;
        Wake();
        QueueRedraw();
    }

    /// <summary>커서의 화면 좌표를 매 프레임 넣는다. 속도·가속도는 여기서 잰다.</summary>
    public void Follow(Vector2 screenPos)
    {
        if (!_hasCursor)
        {
            _lastStepCursor = screenPos;
            _hasCursor = true;
        }

        _cursor = screenPos;
    }

    /// <summary>타건·클릭이 왔다 (횟수만). 신남 반응, 자고 있었으면 깬다.</summary>
    public void Keystrokes(int count)
    {
        if (count <= 0)
        {
            return;
        }

        Wake();
        if (State is Pose.Drop)
        {
            return;
        }

        _cheerLeft = 0.45;
        _angularVelocity += (_rng.Next(2) == 0 ? -1 : 1) * 0.5f;
        _bob = 2.5f;
    }

    private void Wake()
    {
        _sinceActivity = 0;
        if (State == Pose.Sleep)
        {
            SetState(Pose.Hang);
            _freeArmAngle = -2.6f;   // 기지개
        }

        IsFrozen = false;
    }

    /// <summary>매 프레임. 모양은 초당 <see cref="UpdateHz"/> 번만 바꾼다.</summary>
    public void Tick(double delta)
    {
        if (IsFrozen)
        {
            if (_hasCursor && _cursor.DistanceSquaredTo(_lastStepCursor) > 4f)
            {
                Wake();
            }
            else
            {
                return;
            }
        }

        _stepAccum += delta;
        double step = 1.0 / UpdateHz;
        if (_stepAccum < step)
        {
            return;
        }

        double dt = Math.Min(_stepAccum, 0.25);
        _stepAccum = 0;
        Step((float)dt);
        QueueRedraw();
        _face.QueueRedraw();
    }

    private void Step(float dt)
    {
        _time += dt;
        _stateTime += dt;
        _sinceActivity += dt;

        // --- 커서 움직임 ---
        Vector2 v = Vector2.Zero;
        if (_hasCursor && !Still)
        {
            v = (_cursor - _lastStepCursor) / dt;
            _lastStepCursor = _cursor;
        }

        Vector2 a = (v - _velocity) / dt;
        _velocity = v;
        _accel = a.LimitLength(MaxAccel);
        float speed = v.Length();
        if (speed > 5f)
        {
            _sinceActivity = 0;
        }

        // --- 상태 ---
        _cheerLeft = Math.Max(0, _cheerLeft - dt);
        if (State == Pose.Drop)
        {
            _dropLeft -= dt;
            if (_dropLeft <= 0)
            {
                SetState(Pose.Hang);
            }
        }
        else if (!Still && a.Length() > DropAccel)
        {
            SetState(Pose.Drop);
            _dropLeft = 0.9;
        }
        else if (_sinceActivity > SleepAfter)
        {
            SetState(Pose.Sleep);
        }
        else if (speed > FlailSpeed)
        {
            SetState(Pose.Flail);
        }
        else if (speed > SwingSpeed)
        {
            SetState(Pose.Swing);
        }
        else if (_cheerLeft > 0)
        {
            SetState(Pose.Cheer);
        }
        else if (State != Pose.Sleep)
        {
            SetState(Pose.Hang);
        }

        if (State == Pose.Sleep && _stateTime > FreezeAfter)
        {
            IsFrozen = true;
        }

        // --- 진자 (잡은 손이 축). 몇 번 쪼개서 적분해야 15Hz 에서 안 튄다 ---
        const int Sub = 4;
        float h = dt / Sub;
        for (int i = 0; i < Sub; i++)
        {
            float torque = ((-_accel.X) * Mathf.Cos(_angle) - (Gravity - _accel.Y) * Mathf.Sin(_angle)) / PendulumLength;
            _angularVelocity += (torque - Damping * _angularVelocity) * h;
            _angle = Mathf.Clamp(_angle + _angularVelocity * h, -MaxAngle, MaxAngle);
        }

        // --- 손을 놓쳤다가 다시 잡기 ---
        if (State == Pose.Drop)
        {
            float t = (float)(0.9 - _dropLeft);
            _dropY = t < 0.25f ? Mathf.Lerp(0, 16, t / 0.25f) : Mathf.Lerp(16, 0, Mathf.SmoothStep(0, 1, (t - 0.25f) / 0.65f));
        }
        else
        {
            _dropY = 0;
        }

        // --- 빈 팔 · 머리 · 다리 ---
        UpdateIdleAction(dt);

        float targetArm = State switch
        {
            Pose.Cheer => -2.4f + Mathf.Sin((float)_time * 18f) * 0.35f,            // 번쩍 들고 흔든다
            Pose.Drop => -2.9f,                                                    // 바나나를 향해 뻗는다
            Pose.Flail => -1.6f + Mathf.Sin((float)_time * 22f) * 0.6f,
            Pose.Sleep => 0.2f,
            _ when _idleAction == 1 => -2.2f + Mathf.Sin((float)_time * 9f) * 0.45f,   // 손 흔들기
            _ => 0.35f + Mathf.Sin((float)_time * 1.6f) * 0.08f,                    // 늘어뜨림
        };
        _freeArmAngle = Mathf.Lerp(_freeArmAngle, targetArm, 1f - Mathf.Exp(-12f * dt));

        float targetTilt = State switch
        {
            Pose.Sleep => 0.28f,
            Pose.Cheer => Mathf.Sin((float)_time * 16f) * 0.1f,
            _ when _idleAction == 2 => Mathf.Sin((float)_stateTime * 2.2f) * 0.22f,
            _ => 0f,
        };
        _headTilt = Mathf.Lerp(_headTilt, targetTilt, 1f - Mathf.Exp(-8f * dt));

        float legSpeed = State == Pose.Flail || State == Pose.Drop ? 20f : _idleAction == 3 ? 8f : 2.2f;
        _legPhase += dt * legSpeed;
        _bob = Mathf.Lerp(_bob, 0f, 1f - Mathf.Exp(-10f * dt));

        UpdateTail(dt);
    }

    private void UpdateIdleAction(float dt)
    {
        if (State != Pose.Hang)
        {
            _idleAction = 0;
            return;
        }

        if (_idleAction != 0)
        {
            _idleActionLeft -= dt;
            if (_idleActionLeft <= 0)
            {
                _idleAction = 0;
                _nextIdleAction = 5 + _rng.NextDouble() * 7;
            }

            return;
        }

        _nextIdleAction -= dt;
        if (_nextIdleAction <= 0)
        {
            _idleAction = 1 + _rng.Next(3);
            _idleActionLeft = _idleAction == 2 ? 2.4 : 1.6;
            _stateTime = 0;
        }
    }

    private void SetState(Pose next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        _stateTime = 0;
    }

    /// <summary>꼬리: 뿌리를 따라 늦게 따라오는 사슬. 속도에 따라 휘고 끝이 말린다.</summary>
    private void UpdateTail(float dt)
    {
        Vector2 root = At(TailRoot);
        _tail[0] = root;
        // 끝으로 갈수록 더 말린다 (물음표 모양). 흔들리면 반대쪽으로 조금 펴진다.
        float curl = 0.5f + Mathf.Sin((float)_time * 1.3f) * 0.08f;
        Vector2 dir = new Vector2(0.9f, 0.45f).Rotated(_angle * 0.5f);
        for (int i = 1; i < _tail.Length; i++)
        {
            dir = dir.Rotated(-curl * (0.35f + i * 0.12f) + _angularVelocity * 0.04f);
            Vector2 target = _tail[i - 1] + dir * TailSegment;
            _tail[i] = _tail[i].Lerp(target, 1f - Mathf.Exp(-18f * dt));

            // 마디 길이는 고정 - 몸이 빨리 움직이면(놓침) 늦게 따라오는 점이 사슬을 막대처럼 늘였다.
            Vector2 link = _tail[i] - _tail[i - 1];
            if (link.Length() > TailSegment)
            {
                _tail[i] = _tail[i - 1] + link.Normalized() * TailSegment;
            }
        }
    }

    // --- 그리기 ---------------------------------------------------------------------------------

    /// <summary>기본 자세의 점을 지금 자세로: 진자 각도만큼 돌리고, 놓침·숨쉬기만큼 내린다.</summary>
    private Vector2 At(Vector2 p) => p.Rotated(_angle) + new Vector2(0, _dropY + Breath());

    private float Breath() => State == Pose.Sleep ? Mathf.Sin((float)_time * 1.2f) * 0.8f : Mathf.Sin((float)_time * 2.4f) * 0.5f + _bob;

    public override void _Draw()
    {
        if (_skin == null)
        {
            return;
        }

        Color ol = _skin.Outline, fur = _skin.Fur, belly = _skin.Belly;

        // 꼬리 (맨 뒤)
        DrawPolyline(_tail, ol, 6.5f, true);
        DrawPolyline(_tail, fur, 3.5f, true);

        // 다리
        for (int side = 0; side < 2; side++)
        {
            Vector2 hip = At(side == 0 ? HipL : HipR);
            float swing = Mathf.Sin(_legPhase + side * Mathf.Pi) * (State is Pose.Flail or Pose.Drop ? 0.7f : _idleAction == 3 ? 0.55f : 0.08f);
            Vector2 foot = hip + new Vector2(side == 0 ? -3 : 3, LegLength).Rotated(_angle * 0.5f + swing);
            Limb(hip, foot, LegWidth, ol, fur);
            Oval(foot + new Vector2(side == 0 ? -2 : 2, 1), 5.5f, 3.6f, 0, belly, ol);
        }

        // 잡은 팔 (손은 원점에 붙어 있다 — 놓친 동안은 위로 뻗은 채 같이 떨어진다)
        Vector2 grip = State == Pose.Drop ? new Vector2(0, _dropY) : Vector2.Zero;
        Limb(At(ShoulderR), grip, ArmWidth, ol, fur);

        // 몸통
        Vector2 chest = At(Chest);
        Oval(chest, 12.5f, 14.5f, _angle - 0.35f, fur, ol);
        Oval(chest + new Vector2(1.5f, 2.5f).Rotated(_angle), 7.5f, 9.5f, _angle - 0.35f, belly, null);

        // 빈 팔
        Vector2 shoulderL = At(ShoulderL);
        Vector2 hand = shoulderL + new Vector2(0, FreeArmLength).Rotated(_angle + _freeArmAngle);
        Limb(shoulderL, hand, ArmWidth, ol, fur);
        Hand(hand, belly, ol);

        // 잡은 손 (바나나 위에)
        Hand(grip, belly, ol);

        // 머리는 자식 스프라이트 (몸 위에 그려진다)
        _head.Position = At(Neck);
        _head.Rotation = _angle * 0.8f - 0.22f + _headTilt;
    }

    private void Limb(Vector2 a, Vector2 b, float width, Color outline, Color fill)
    {
        DrawLine(a, b, outline, width + 3.5f, true);
        DrawCircle(a, (width + 3.5f) / 2, outline);
        DrawCircle(b, (width + 3.5f) / 2, outline);
        DrawLine(a, b, fill, width, true);
        DrawCircle(a, width / 2, fill);
        DrawCircle(b, width / 2, fill);
    }

    private void Hand(Vector2 at, Color fill, Color outline)
    {
        DrawCircle(at, 5.2f, outline);
        DrawCircle(at, 3.6f, fill);
    }

    private void Oval(Vector2 c, float rx, float ry, float rot, Color fill, Color? outline)
    {
        const int N = 20;
        var pts = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            float t = Mathf.Tau * i / N;
            pts[i] = c + new Vector2(Mathf.Cos(t) * rx, Mathf.Sin(t) * ry).Rotated(rot);
        }

        if (outline is Color ol)
        {
            var edge = new Vector2[N];
            for (int i = 0; i < N; i++)
            {
                float t = Mathf.Tau * i / N;
                edge[i] = c + new Vector2(Mathf.Cos(t) * (rx + 1.8f), Mathf.Sin(t) * (ry + 1.8f)).Rotated(rot);
            }

            DrawColoredPolygon(edge, ol);
        }

        DrawColoredPolygon(pts, fill);
    }

    /// <summary>표정을 머리 그림 위에 덧그린다 (머리 그림의 픽셀 좌표). 졸기 = 감은 눈, 신남 = 웃는 눈.</summary>
    private void DrawFace()
    {
        if (_skin == null || _skin.Eyes.Length == 0 || State is not (Pose.Sleep or Pose.Cheer))
        {
            return;
        }

        Color skin = _skin.Belly, ink = _skin.Outline;
        foreach (Vector2 eye in _skin.Eyes)
        {
            Vector2 at = eye - _skin.Neck;   // 머리 스프라이트의 원점이 목이다 (Offset = -Neck)
            _face.DrawCircle(at, 17f, skin);
            if (State == Pose.Sleep)
            {
                _face.DrawArc(at + new Vector2(0, -4), 11f, 0.25f, Mathf.Pi - 0.25f, 10, ink, 6f, true);    // ‿
            }
            else
            {
                _face.DrawArc(at + new Vector2(0, 6), 11f, Mathf.Pi + 0.25f, Mathf.Tau - 0.25f, 10, ink, 6f, true);   // ^
            }
        }
    }
}
