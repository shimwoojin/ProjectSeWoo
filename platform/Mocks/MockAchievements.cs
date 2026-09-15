using System.Collections.Generic;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform.Mocks;

/// <summary>
/// <see cref="IAchievements"/> 목 구현. 해금 상태를 메모리에만 들고 있고 스팀을 안 쓴다.
///
/// 실물은 <c>platform/SteamService.cs</c>다 (A8 완료, 2026-09-15).
/// **을이 이걸 써야 하는 이유가 다른 목들보다 하나 더 있다** — 도전과제 API Name 은
/// A15 에서 스팀 파트너 사이트에 등록되기 전까지 실물에서도 해금이 안 된다
/// (<see cref="SteamService.SpacewarAppId"/> 주석). 목은 이름과 무관하게 해금되므로
/// 해금 조건 로직(도감 100%, 타수 마일스톤)을 지금 끝까지 시험할 수 있다.
/// </summary>
public sealed class MockAchievements : IAchievements
{
    private readonly HashSet<string> _unlocked = new();

    /// <summary>
    /// 목은 항상 붙어 있다고 답한다. **스팀이 꺼진 경로를 시험하려면 여기를 꺼 본다** —
    /// 이 앱은 스팀 없이 켜져 있는 시간이 더 길다(SteamService 주석). 꺼도 게임 쪽이
    /// 멀쩡한지가 을이 확인할 것이다.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    public void Unlock(string id)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id))
        {
            return;
        }

        if (_unlocked.Add(id))
        {
            GD.Print($"[mock-ach] 해금 {id}");
        }
    }

    public bool IsUnlocked(string id) => IsAvailable && !string.IsNullOrEmpty(id) && _unlocked.Contains(id);

    public void IndicateProgress(string id, int current, int max)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id) || max <= 0)
        {
            return;
        }

        GD.Print($"[mock-ach] 진행 {id} {current}/{max}");
    }

    /// <summary>목 상태 초기화. 해금 조건을 반복해서 시험할 때 쓴다.</summary>
    public void Reset() => _unlocked.Clear();
}
