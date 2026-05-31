# TASK-011 Developer Report — Ollama Translator

## 완료 시각
2026-05-31T00:00:00+09:00

## 작업 요약
ITranslator 인터페이스를 구현하는 OllamaTranslator 클래스를 작성했다.
로컬에서 실행되는 Ollama 서버의 /api/generate 엔드포인트를 호출한다.
로컬 서비스이므로 HTTP를 허용하며, 모델명은 AppSettings에서 읽는다.

## 주요 결정사항
- BaseUrl: AppSettings.TranslationApi.Ollama.BaseUrl (기본: http://localhost:11434)
- 모델명: AppSettings.TranslationApi.Ollama.ModelId (기본: llama3)
- 로컬 서비스이므로 HTTP/HTTPS 모두 허용 (스킴 유효성만 검증)
- API Key 불필요 — Authorization 헤더 없음
- stream=false 설정으로 단일 JSON 응답 수신 (스트리밍 미사용)
- 응답에서 "response" 필드 추출
- SnakeCaseLower 직렬화 정책 사용 (Ollama API 규격 일치)
- 내부 요청 모델 OllamaGenerateRequest를 private sealed class로 캡슐화
- 빈 catch 블록 없음, 로컬 서버 미실행 시 명확한 에러 메시지 포함

## 생성/수정 파일 목록
- 생성: develope/Translator/Translation/OllamaTranslator.cs
- 수정: develope/Translator/Core/Models/AppSettings.cs (OllamaTranslationSettings 추가)

## 예상 토큰 소모량
소 (단일 파일 구현, 로컬 API 패턴)
