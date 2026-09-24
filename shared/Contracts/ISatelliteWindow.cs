using System;
using Godot;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 셸이 띄우고 관리하는 작은 창 하나 (B10 친구 칸). <see cref="IShell.OpenSatellite"/> 가 만든다.
///
/// <b>왜 창이 따로인가.</b> 친구 원숭이를 바탕화면 아무 데나 끌어다 두게 하려는 것이다
/// (봉고캣처럼). 처음에는 메인 창을 늘려 그 안에 친구 칸을 그렸는데, 그러면 친구가 늘
/// 내 원숭이 옆에 붙어 다닌다.
///
/// <b>OS 창에 관한 것은 전부 셸이 한다</b> - 항상 위, 투명, 포커스 안 뺏기, 끌기, 클릭 통과
/// 영역, 그리고 옵션의 배율·투명도·숨기기(트레이·전체화면)를 메인 창과 똑같이 따라가는 것.
/// 게임 레이어는 <see cref="Content"/> 에 그리기만 한다.
///
/// 메인 스레드 전용.
/// </summary>
public interface ISatelliteWindow
{
    /// <summary>
    /// 내용을 붙일 자리. 창 왼쪽 위가 (0,0) 이고 좌표는 배율 전 콘텐츠 픽셀이다 -
    /// 배율은 셸이 이 노드에 건다.
    /// </summary>
    Node2D Content { get; }

    /// <summary>창 왼쪽 위의 화면 좌표(데스크톱 픽셀).</summary>
    Vector2I ScreenPosition { get; }

    /// <summary>
    /// 창의 모양 - 다각형 하나, <see cref="Content"/> 좌표. 이 안은 클릭을 받고(= 잡아서
    /// 끌 수 있고) 밖은 클릭이 뒤의 창으로 통과한다.
    ///
    /// <b>이 밖은 그려지지도 않는다.</b> Windows 에서 Godot 의 클릭 통과 영역은 창 모양
    /// (<c>SetWindowRgn</c>)이라 그리기까지 잘린다 - 처음에 나무·원숭이 사각형만 줬더니 그
    /// 아래 이름표가 바탕화면에서 사라졌다(창 텍스처에는 멀쩡히 있었다). 그래서 보여야 하는
    /// 것 전부를 감싸게 준다. 부르기 전에는 창 전체다.
    /// </summary>
    void SetShape(Vector2[] outline);

    /// <summary>유저가 끌어서 놓았다. 인자는 새 화면 좌표. 위치를 기억할 쪽이 받는다.</summary>
    event Action<Vector2I> Moved;

    /// <summary>창을 닫는다. 닫은 뒤에는 쓰지 않는다.</summary>
    void Close();
}
