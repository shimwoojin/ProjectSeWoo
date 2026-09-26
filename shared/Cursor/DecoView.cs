using System;
using System.Collections.Generic;
using Godot;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 장식 칸 한 개 (docs/B17-CURSOR-REWORK.md §4-3). 종류(<c>items.json</c> 의 <c>deco.kind</c>)마다 움직임이 다르고,
/// 아이템마다 코드를 쓰지 않는다 — 새 장식은 그림 + JSON 한 줄이다.
///
/// <list type="bullet">
///   <item><c>halo</c> — 커서 뒤에 깔려 천천히 돌고 숨쉬듯 커졌다 작아진다. 뒤에 흐린 빛이 한 겹 더 있다.</item>
///   <item><c>trail</c> — 커서가 움직이면 지나간 자리에 흩뿌려지고 사라진다. 입자는 <b>화면 좌표</b>에 남는다 —
///   창이 커서를 따라 움직여도 입자는 제자리다.</item>
///   <item><c>float</c> — 커서 옆에 떠서 둥실거린다. "뜬금없는 물건" 자리.</item>
/// </list>
///
/// 원점은 커서 끝이다. <see cref="MonkeyRig"/> 와 같이 초당 <see cref="MonkeyRig.UpdateHz"/> 번만 바꾼다.
/// </summary>
public partial class DecoView : Node2D
{
    private const float HaloSize = 46f, TrailSize = 16f, FloatSize = 26f;
    private const float TrailLife = 0.7f, TrailSpacing = 14f;
    private const int MaxParticles = 10;

    private sealed class Particle
    {
        public Vector2 Screen;
        public float Age, Spin, Scale;
    }

    private string _kind;
    private Texture2D _texture;
    private readonly List<Particle> _particles = new();
    private readonly Random _rng = new();
    private double _time, _stepAccum;
    private Vector2 _cursor, _lastSpawn, _origin;
    private bool _hasCursor;

    /// <summary>졸다가 멈춘 원숭이와 같이 멈춘다 (CursorLayer 가 정한다).</summary>
    public bool Frozen { get; set; }

    public void SetItem(string id, string kind)
    {
        _particles.Clear();
        _kind = id == null ? null : kind;
        string path = id == null ? null : ItemManifest.IconPath(CursorSlot.Deco, id);
        _texture = path != null && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        QueueRedraw();
    }

    /// <summary>커서의 화면 좌표와, 이 노드의 원점(커서 끝)이 화면에서 어디인지.</summary>
    public void Follow(Vector2 cursorScreen, Vector2 originScreen)
    {
        if (!_hasCursor)
        {
            _lastSpawn = cursorScreen;
            _hasCursor = true;
        }

        _cursor = cursorScreen;
        _origin = originScreen;
    }

    public void Tick(double delta)
    {
        if (_texture == null || Frozen)
        {
            return;
        }

        _stepAccum += delta;
        if (_stepAccum < 1.0 / MonkeyRig.UpdateHz)
        {
            return;
        }

        float dt = (float)Math.Min(_stepAccum, 0.25);
        _stepAccum = 0;
        _time += dt;

        if (_kind == "trail")
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                _particles[i].Age += dt;
                if (_particles[i].Age >= TrailLife)
                {
                    _particles.RemoveAt(i);
                }
            }

            // 지나온 길을 따라 일정 간격으로 떨군다 - 빨리 움직여도 끊기지 않게 한 번에 여러 개.
            float gap = _cursor.DistanceTo(_lastSpawn);
            int spawn = Math.Min(3, (int)(gap / TrailSpacing));
            for (int k = 1; k <= spawn && _particles.Count < MaxParticles; k++)
            {
                _particles.Add(new Particle
                {
                    Screen = _lastSpawn.Lerp(_cursor, (float)k / spawn) + new Vector2(_rng.Next(-5, 6), 8 + _rng.Next(-4, 5)),
                    Spin = (float)(_rng.NextDouble() * Math.Tau),
                    Scale = 0.8f + (float)_rng.NextDouble() * 0.4f,
                });
            }

            if (spawn > 0)
            {
                _lastSpawn = _cursor;
            }
            else if (_particles.Count == 0)
            {
                _lastSpawn = _cursor;
                return;   // 가만히 있으면 그릴 것이 없다 - 다시 그리지 않는다
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_texture == null)
        {
            return;
        }

        switch (_kind)
        {
            case "halo":
            {
                float pulse = 1f + Mathf.Sin((float)_time * 2.2f) * 0.05f;
                float rot = (float)_time * 0.5f;
                // 뒤의 흐린 빛 한 겹 - 셰이더 없이 크게·흐리게 한 번 더 그린다
                DrawTextureCentered(new Vector2(0, 6), HaloSize * 1.35f * pulse, rot, new Color(1f, 0.95f, 0.7f, 0.28f));
                DrawTextureCentered(new Vector2(0, 6), HaloSize * pulse, rot, Colors.White);
                break;
            }

            case "trail":
                foreach (Particle p in _particles)
                {
                    float life = 1f - p.Age / TrailLife;
                    Vector2 at = p.Screen - _origin + new Vector2(0, p.Age * 18f);   // 살짝 떨어진다
                    DrawTextureCentered(at, TrailSize * p.Scale * (0.6f + 0.4f * life), p.Spin + p.Age * 2f,
                        new Color(1f, 1f, 1f, life));
                }

                break;

            case "float":
            {
                Vector2 at = new(-34f, 20f + Mathf.Sin((float)_time * 2.0f) * 3f);
                DrawTextureCentered(at, FloatSize, Mathf.Sin((float)_time * 1.3f) * 0.12f, Colors.White);
                break;
            }
        }
    }

    private void DrawTextureCentered(Vector2 at, float size, float rotation, Color modulate)
    {
        float k = size / Math.Max(_texture.GetWidth(), _texture.GetHeight());
        DrawSetTransform(at, rotation, Vector2.One * k);
        DrawTexture(_texture, -_texture.GetSize() / 2f, modulate);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
