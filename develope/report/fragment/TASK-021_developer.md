# TASK-021 완료 보고서

## 완료 시각
2026-05-31

## 작업 요약
TranslationPipelineManager.ProcessRegionAsync 및 ProcessRegionsAsync를 블록 단위 OCR + 배치 번역으로 재설계하였습니다.

### 새 파이프라인 흐름
1. RecognizeChangedBlocksAsync → 변경된 블록 배열 획득
2. 블록별 캐시 조회 (캐시 히트 항목 즉시 결과 등록)
3. 캐시 미스 항목만 TranslateBatchAsync로 배치 번역
4. 번역 결과를 캐시에 저장
5. 각 블록의 Region(절대 좌표) 위치에 번역 오버레이 렌더링

### ProcessRegionsAsync 수정
- 다중 영역은 RecognizeMultipleRegionsAsync 대신 ProcessRegionAsync를 순차 호출하는 방식으로 변경 (각 영역이 독립적으로 블록 처리)

### 유지된 기능
- RunContinuousAsync (단일 영역 반복 루프)
- ClearOverlay
- MaxCallsPerSecond Rate Limit
- ResolveTranslationAsync (하위 호환용 유지, 직접 호출은 없음)
- EnforceRateLimitAsync, BuildCacheKey 내부 헬퍼

## 생성/수정 파일
- `E:\tl\develope\Translator\Core\Services\TranslationPipelineManager.cs` (수정 — ProcessRegionAsync, ProcessRegionsAsync 재설계)

## 빌드 결과
Core 프로젝트 빌드 성공 (error CS 없음)

## 예상 토큰 소모량
~3,000 tokens
