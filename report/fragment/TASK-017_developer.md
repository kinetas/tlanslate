# TASK-017 Developer Report

## 완료 시각
2026-05-31T02:30:00+09:00

## 작업 요약
TASK-017 메인 창 + MainViewModel + DI 배선 + 앱 진입점 구현 완료.
Microsoft.Extensions.DependencyInjection 기반 DI 컨테이너 구성, WPF MVVM 메인 창, 시스템 트레이 아이콘을 포함한 전체 main_shell 세그먼트 구현.

## 주요 결정사항

1. **DI 팩토리 패턴**: ITranslator 등록 시 `settings.TranslationApi.Provider` 값 기반 런타임 switch expression으로 OpenAI/DeepL/Ollama/LMStudio 선택. 미지정 시 OpenAI 기본값.

2. **2단계 부트스트랩**: 설정을 로드하기 위해 먼저 임시 부트스트랩 컨테이너(AppSettingsManager만 포함)를 생성 → 설정값 로드 후 전체 DI 컨테이너 구성. 닭-달걀 순환 의존 방지.

3. **MainViewModel 위치**: `Translator.UI.ViewModels` 네임스페이스 (파일 경로: UI/ViewModels/MainViewModel.cs) — MVVM 구조 명확화.

4. **BoolToVisibilityConverter 확장**: parameter="Inverse" 지원, int(ActiveRegionsCount) 입력 지원 추가 — 영역 수 0 여부를 XAML 바인딩으로 처리.

5. **트레이 아이콘**: H.NotifyIcon.Wpf 패키지 사용. X 버튼 클릭 시 트레이로 최소화, TrayIcon.Dispose()는 _forceClose=true 종료 시만 호출.

6. **RegionSelectorWindow/SettingsWindow 연동**: DI가 생성자에서 ViewModel을 자동 주입하므로 MainViewModel에서는 window.DataContext를 캐스팅하여 이벤트만 구독.

7. **누락 .csproj 생성**: Translation.csproj, OCR.csproj, Overlay.csproj가 없어 신규 생성. 솔루션 파일(Translator.sln)에 7개 프로젝트 모두 등록.

## 생성/수정 파일 목록

### 신규 생성
- `develope/Translator/Translator.csproj` — 메인 WPF 진입점 프로젝트, 7개 ProjectReference + 5개 NuGet
- `develope/Translator/App.xaml` — Application 루트
- `develope/Translator/App.xaml.cs` — DI 컨테이너 구성, 2단계 부트스트랩, 예외 핸들러
- `develope/Translator/UI/MainWindow.xaml` — 상태 표시줄(토글/엔진/영역수), 버튼 4개, 영역 리스트, TaskbarIcon
- `develope/Translator/UI/MainWindow.xaml.cs` — 트레이 최소화/복원/종료 이벤트 처리
- `develope/Translator/UI/ViewModels/MainViewModel.cs` — StartTranslationCommand, StopTranslationCommand, OpenRegionSelectorCommand, OpenSettingsCommand, ClearRegionsCommand, IsRunning/SelectedEngine/ActiveRegionsCount 바인딩
- `develope/Translator/Translation/Translation.csproj` — Translation 프로젝트 파일
- `develope/Translator/OCR/OCR.csproj` — OCR 프로젝트 파일 (PaddleOCRSharp 포함)
- `develope/Translator/Overlay/Overlay.csproj` — Overlay 프로젝트 파일

### 수정
- `develope/Translator/Translator.sln` — 4개 프로젝트 추가 (Translation, OCR, Overlay, Translator), x64 플랫폼 구성
- `develope/Translator/UI/UI.csproj` — H.NotifyIcon.Wpf, DependencyInjection.Abstractions 패키지 추가, Overlay.csproj 참조 추가
- `develope/Translator/UI/BoolToVisibilityConverter.cs` — ConverterParameter="Inverse" 지원, int 입력 지원

## 예상 토큰 소모량
대 (Large) — 9개 파일 신규 생성, 3개 파일 수정, 기존 16개 파일 전체 분석 후 DI 배선 결정
