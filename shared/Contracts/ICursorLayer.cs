namespace ProjectSeWoo.Shared;

/// <summary>
/// 커서 장식 레이어 (기획확정-일감분배-260907.md §8-2). 갑 제공 → 을 소비.
///
/// 커서 **본체**는 건드리지 않는다. Windows 기본 커서를 그대로 두고, 커서를
/// 따라다니는 클릭 통과 투명 창에 장식만 그린다 (§1.3 C안).
/// A2 스파이크에서 이 방식이 성립하는 것을 확인했다 — docs/A2-CURSOR-SPIKE.md.
/// </summary>
public interface ICursorLayer
{
    /// <summary>
    /// 슬롯에 에셋을 끼운다. <paramref name="assetId"/> 가 null 이면 그 슬롯을 비운다.
    /// </summary>
    void Equip(CursorSlot slot, string assetId);

    /// <summary>커서 장식 전체 On/Off. 옵션 화면에서 유저가 끌 수 있어야 한다 (§7-4).</summary>
    void SetEnabled(bool on);

    /// <summary>
    /// false 면 게임 레이어는 커서 꾸미기 UI 를 숨기고 재화 소비처를 바꾼다.
    ///
    /// A2 에서 Go 가 나왔지만 플래그는 남긴다. 남의 PC 에서 도는 상주 앱이라
    /// 이 환경에서 됐다는 것이 모든 환경에서 된다는 뜻이 아니다.
    /// </summary>
    bool IsSupported { get; }
}
