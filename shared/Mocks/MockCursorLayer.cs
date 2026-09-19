using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Shared.Mocks;

/// <summary>
/// <see cref="ICursorLayer"/> 목 구현. 장착 상태만 들고 있고 아무것도 그리지 않는다.
///
/// 실물은 <c>platform/CursorLayer.cs</c>다 (A5 완료, 2026-09-15). A2 스파이크 코드
/// (불투명 채우기, 렌더 타깃 읽기, 클릭 통과 방식 순환)는 A5에서 걷어냈다.
/// </summary>
public sealed class MockCursorLayer : ICursorLayer
{
    private readonly string[] _equipped = new string[3];

    private bool _enabled = true;

    /// <summary>
    /// 목은 항상 지원한다고 답한다. **false 경로를 시험하려면 여기를 꺼 본다** —
    /// 을은 커서 축이 죽은 환경에서도 게임이 성립하는지 확인해야 한다 (§1.3 폴백).
    /// </summary>
    public bool IsSupported { get; set; } = true;

    public void Equip(CursorSlot slot, string assetId)
    {
        if (!IsSupported)
        {
            return;
        }

        _equipped[(int)slot] = assetId;
        GD.Print($"[mock-cursor] {slot} = {assetId ?? "(비움)"}");
    }

    public void SetEnabled(bool on)
    {
        _enabled = on;
        GD.Print($"[mock-cursor] enabled = {on}");
    }

    /// <summary>목 상태 조회. 게임 레이어 테스트에서 장착이 반영됐는지 확인할 때 쓴다.</summary>
    public string GetEquipped(CursorSlot slot) => _equipped[(int)slot];

    public bool IsEnabled => _enabled;
}
