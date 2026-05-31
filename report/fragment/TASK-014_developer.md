# TASK-014 Developer Report

## 완료 시각
2026-05-31T00:05:00+09:00

## 작업 요약
번역 파이프라인 전체 흐름을 조율하는 TranslationPipelineManager를 작성하였다.
Region → OCRManager(ScreenCapture + OCR + 텍스트변경감지) → CacheCheck → TranslateAsync → OverlayRender
순서로 처리하며, ITranslator / OCRManager / ICacheService / IOverlayRenderer를 생성자 주입받는다.
슬라이딩 윈도우 방식의 Rate Limiter(초당 최대 호출 횟수 제한)와 CancellationToken을 완전히 지원한다.

## 주요 결정사항
- **단계별 파이프라인 분리**: ProcessRegionAsync (단일 영역), ProcessRegionsAsync (다중 영역), RunContinuousAsync (루프) 세 가지 진입점 제공.
- **캐시 우선 전략**: 번역 API 호출 전 ICacheService 조회. 히트 시 Rate Limit 소모 없이 즉시 반환.
- **슬라이딩 윈도우 Rate Limiter**: ConcurrentQueue<DateTimeOffset>으로 최근 1초 내 호출 타임스탬프를 추적. MaxCallsPerSecond=0이면 비활성화.
- **SemaphoreSlim으로 Rate Limiter 스레드 안전 보장**: 여러 영역을 동시 처리할 때 카운터 경쟁 조건 방지.
- **텍스트 변경 감지 위임**: OCRManager.RecognizeChangedTextAsync / RecognizeMultipleRegionsAsync에 위임하여 파이프라인에서 중복 구현 제거.
- **오류 격리**: 번역 API 실패 시 해당 항목만 건너뛰고 루프는 유지. OperationCanceledException은 상위로 전파.
- **CacheKey 형식**: `{targetLanguage}:{text}` — 언어별 캐시 키 충돌 방지.
- **RenderTranslations 일괄 전달**: 모든 번역 결과를 수집 후 한 번의 RenderTranslations 호출로 오버레이 깜박임 최소화.

## 생성/수정 파일 목록
| 경로 | 상태 |
|------|------|
| develope/Translator/Core/Services/TranslationPipelineManager.cs | 신규 생성 |

## 예상 토큰 소모량
중 (단일 파일, 복잡한 비동기 로직 + Rate Limiter 구현)
