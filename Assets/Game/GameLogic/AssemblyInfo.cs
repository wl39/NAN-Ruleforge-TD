using System.Runtime.CompilerServices;

// 게임 로직의 internal 멤버는 일반 게임 코드에는 공개하지 않는다.
// 다만 EditMode 테스트 어셈블리에는 접근을 허용하여, 공개 API만으로 관찰하기 어려운
// 결정성·예산·이벤트 순서 같은 내부 규칙도 직접 검증할 수 있게 한다.
[assembly: InternalsVisibleTo("RuleforgeTD.GameLogic.EditModeTests")]
