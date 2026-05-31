# TASK-016 완료 보고서

## 완료 시각
2026-05-31T00:00:00+09:00

## 작업 요약
SettingsWindow (탭 구성 설정 UI) 및 SettingsViewModel (INotifyPropertyChanged, AppSettingsManager 연동, DPAPI API Key 처리) 구현 완료.

## 주요 결정사항
1. **탭 구성**: 번역 엔진 선택, API Key 입력, OCR 설정, 표시 설정(폰트/투명도/색상), 캐시 설정 총 5탭.
2. **API Key PasswordBox**: PasswordBox는 MVVM 바인딩 불가(SecureString/평문 노출 위험)이므로 코드비하인드 `ApiKeyPasswordBox_PasswordChanged` 이벤트에서 `ViewModel.OnApiKeyChanged(password)` 호출. 저장 시 DPAPI 암호화 후 평문 즉시 파기(`_pendingPlainApiKey = null`).
3. **기존 Key placeholder**: `HasExistingApiKey` 프로퍼티로 기존 암호화 키 존재 여부를 뷰에 알리고, `OnWindowLoaded`에서 `ApiKeyPlaceholder("••••••••••••••••")` 표시 — 평문 복원 없이 마스크만.
4. **엔진별 조건부 표시**: `IsOpenAiSelected`, `IsDeepLSelected`, `IsOllamaSelected`, `IsLmStudioSelected` 프로퍼티 + BoolToVisibilityConverter로 해당 엔진 설정 섹션만 표시.
5. **저장 로직**: `LoadAsync()`로 현재 설정을 읽어온 후 변경된 프로퍼티만 덮어쓰는 방식으로 다른 세그먼트의 설정 유지. `_apiKeyChanged` 플래그로 placeholder와 실제 변경 구분.
6. **캐시 활성화 토글**: `IsCacheEnabled`가 false이면 `MaxCacheItems = 0`으로 저장하여 캐시 비활성화를 모델 레벨에서 표현.
7. **SaveCommand async 보호**: `_isSaving` 플래그로 중복 저장 방지, 저장 중 버튼 비활성화.
8. **IDisposable**: Dispose 시 `_pendingPlainApiKey = null`로 메모리에서 평문 API Key 파기.

## 생성/수정 파일 목록
- `develope/Translator/UI/SettingsWindow.xaml` (신규)
- `develope/Translator/UI/SettingsWindow.xaml.cs` (신규)
- `develope/Translator/UI/SettingsViewModel.cs` (신규)

## 예상 토큰 소모량
중 (약 4,000~5,000 토큰)
