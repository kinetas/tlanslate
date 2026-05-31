# TASK-015 완료 보고서

## 완료 시각
2026-05-31T00:00:00+09:00

## 작업 요약
RegionSelectorWindow (전체화면 반투명 오버레이 영역 선택 UI) 및 RegionSelectorViewModel 구현 완료.

## 주요 결정사항
1. **전체화면 오버레이**: WindowStyle=None, AllowsTransparency=True, WindowState=Maximized, Topmost=True, Background="#7F000000" (반투명 검정)으로 구현. Cursor="Cross"로 사용성 향상.
2. **마우스 드래그 Rectangle**: Canvas 위에 Rectangle을 배치하고 SelectionLeft/Top/Width/Height를 ViewModel 프로퍼티에 바인딩. IsSelecting → BoolToVisibilityConverter로 표시 제어.
3. **Region 확정**: OnMouseUp()에서 Math.Min/Abs로 역방향 드래그를 처리하여 항상 양수 좌표/크기를 보장. Width/Height <= 0이면 자동 취소.
4. **ESC 취소**: Window.InputBindings에 KeyBinding(Key=Escape, Command=CancelCommand) 등록.
5. **코드비하인드 역할 최소화**: MouseLeftButtonDown/Move/Up 이벤트를 ViewModel의 OnMouseDown/OnMouseMove/OnMouseUp으로 위임만 함.
6. **CloseRequested 이벤트**: bool? = DialogResult(true: 확정, null: 취소)로 창 닫기를 ViewModel에서 제어.
7. **RelayCommand** 공유 인프라 클래스 신규 작성 (동기/비동기 모두 지원, CanExecute 재평가 지원).
8. **BoolToVisibilityConverter** 공유 인프라 클래스 신규 작성.

## 생성/수정 파일 목록
- `develope/Translator/UI/RegionSelectorWindow.xaml` (신규)
- `develope/Translator/UI/RegionSelectorWindow.xaml.cs` (신규)
- `develope/Translator/UI/RegionSelectorViewModel.cs` (신규)
- `develope/Translator/UI/RelayCommand.cs` (신규 — 공유 인프라)
- `develope/Translator/UI/BoolToVisibilityConverter.cs` (신규 — 공유 인프라)
- `develope/Translator/UI/UI.csproj` (신규)
- `develope/Translator/Translator.sln` (수정 — UI 프로젝트 등록)

## 예상 토큰 소모량
중 (약 3,000~4,000 토큰)
