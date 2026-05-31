# Product Requirement Document

---

# Project Overview

## Project Name
게임, 프로그램 실시간 번역 PC앱 (Translator)

## Project Objective
사용자가 지정한 화면 영역의 텍스트를 실시간 OCR로 인식하고 AI 번역 엔진을 통해 번역한 뒤, 원문 위치에 번역문을 오버레이하여 표시하는 Windows 실시간 화면 번역기

---

# User Requirements

## What do you want to build?
- Windows PC 애플리케이션 (로컬 실행)
- 사용자가 번역 영역을 직접 지정
- 실시간 화면 캡처 및 OCR 텍스트 추출
- OpenAI / DeepL / 로컬 LLM(Ollama, LMStudio)을 이용한 번역
- 자막 방식이 아닌 원문 위치에 번역문을 덮어써서 표시
- 게임, 에뮬레이터, 일반 프로그램 모두 지원
- 웹 서비스 아님, 완전 로컬 실행 프로그램

## Why are you building this?
번역된 글자가 자막 형태로는 원문 위치와 달라 보기 불편함. 원문이 있는 그 위치에 실시간으로 번역문이 표시되는 앱이 필요함.

## Target Users
일본어, 영어 등 외국어 게임을 즐기는 일반 사용자

---

# Core Features

## Required Features
- [x] 번역 영역 지정 (마우스 드래그로 화면 영역 선택)
- [x] 실시간 화면 캡처
- [x] OCR 텍스트 추출 (PaddleOCR)
- [x] AI 번역 엔진 연동 (OpenAI / DeepL / Ollama / LMStudio)
- [x] 원문 위치 오버레이 (원문 치환형 렌더링)
- [x] 클릭스루 투명 창 (게임 조작 방해 없음)
- [ ] 번역 캐시 (선택적 적용 — 번역 품질 저하 주의)

## Priority Feature
번역 영역 지정 — 이 기능이 구현되어야 이후 OCR, 번역, 오버레이 파이프라인이 동작함

---

# Detailed Feature Requirements

## 번역 영역 지정

### Purpose
사용자가 번역할 화면 영역을 마우스로 선택하여, 이후 모든 처리의 기준 좌표를 확정

### Expected Behavior
- 투명한 오버레이 창을 띄운 뒤 마우스 드래그로 영역 선택
- 선택된 영역의 x, y, width, height 저장
- 여러 영역 동시 지정 가능 여부는 추후 결정

### Input
마우스 드래그 이벤트

### Output
`Region { x, y, width, height }`

### Dependencies
- Dependencies: None

---

## OCR 서비스

### Purpose
지정된 영역의 화면 캡처 이미지에서 텍스트와 위치 정보를 추출

### Expected Behavior
- 화면 캡처 → PaddleOCR 처리 → 텍스트 + 좌표 반환

### Input
화면 캡처 이미지 (Bitmap)

### Output
```json
[
  { "text": "Attack", "x": 500, "y": 300, "width": 120, "height": 30 }
]
```

### Dependencies
- Dependencies: 번역 영역 지정

---

## 번역 서비스

### Purpose
OCR로 추출한 텍스트를 번역 엔진에 전달하여 번역 결과를 반환

### Expected Behavior
- ITranslator 인터페이스 기반으로 엔진 교체 가능
- OpenAI / DeepL / Ollama / LMStudio 구현체 제공

### Input
원문 텍스트 (string)

### Output
번역된 텍스트 (string)

### Dependencies
- Dependencies: OCR 서비스

---

## 오버레이 서비스

### Purpose
번역 결과를 원문 위치에 정확히 덮어써서 표시

### Expected Behavior
- 클릭스루 투명 WPF 창으로 렌더링
- 원문 위치(x, y) 기준으로 번역문 폰트/크기 조절
- 투명도 제어 가능

### Input
```json
{ "text": "공격", "x": 500, "y": 300 }
```

### Output
게임/프로그램 화면 위 번역문 렌더링

### Dependencies
- Dependencies: 번역 서비스

---

## 캐시 서비스

### Purpose
동일한 텍스트의 중복 API 호출 방지

### Expected Behavior
- `Attack → 공격` 형태로 저장
- 사용자 선택으로 활성화/비활성화 가능
- 민감 문장은 저장하지 않는 옵션 제공

### Input
원문 텍스트

### Output
캐시된 번역 결과 (없으면 null)

### Dependencies
- Dependencies: 번역 서비스

---

# UI / UX Requirements

- 설정 프로그램처럼 보이는 가벼운 유틸리티 스타일
- WPF MVVM 패턴 적용
- 주요 UI 구성:
  - 설정 창 (API Key 입력, OCR 설정, 폰트 설정, 캐시 설정)
  - 영역 선택 창
  - 상태 창 (번역 시작/중지)
- 오버레이 창은 게임 화면 위에 클릭스루로 표시

---

# Technical Requirements

## Preferred Language
C# (.NET 8)

## Preferred Framework
WPF

## Database
미정 — 번역 캐시는 초기에 텍스트 파일(txt) 형식으로 저장 후 필요 시 SQLite로 전환 고려

## Infrastructure
없음 (완전 로컬 실행)

---

# Architecture

```
┌─────────────────────────────┐
│          WPF UI             │
│  설정창 │ 영역선택 │ 상태창  │
└──────────────┬──────────────┘
               │
               ▼
┌─────────────────────────────┐
│      Application Layer      │
│  OCR Manager                │
│  Translation Manager        │
│  Overlay Manager            │
│  Cache Manager              │
└──────────────┬──────────────┘
               │
    ┌──────────┼──────────┐
    ▼          ▼          ▼
┌────────┐ ┌─────────┐ ┌────────┐
│  OCR   │ │Translate│ │Overlay │
│Service │ │ Service │ │Service │
└────────┘ └─────────┘ └────────┘
    │           │           │
    ▼           ▼           ▼
PaddleOCR   OpenAI      WPF Overlay
            DeepL
            Ollama
            LMStudio
```

## 폴더 구조
```
Translator
├─ Core
│   ├─ Interfaces
│   ├─ Models
│   └─ Services
├─ OCR
│   ├─ PaddleOCR
│   └─ Providers
├─ Translation
│   ├─ OpenAI
│   ├─ DeepL
│   └─ Ollama
├─ Overlay
├─ UI
│   ├─ Views
│   ├─ ViewModels
│   └─ Controls
└─ Config
```

## 번역 플로우
```
영역 선택 → 화면 캡처 → OCR → 텍스트 변경 확인
→ 캐시 조회 → (없으면) 번역 API 호출
→ 번역 결과 저장 → 오버레이 렌더링
```

---

# External Tools / APIs

| Tool/API | Purpose |
|---|---|
| PaddleOCR | 화면 텍스트 추출 (OCR) |
| OpenAI API | AI 번역 엔진 |
| DeepL API | AI 번역 엔진 |
| Ollama | 로컬 LLM 번역 엔진 |
| LMStudio | 로컬 LLM 번역 엔진 |

---

# Development Rules

## Coding Style
- 클래스/메서드: PascalCase
- 변수: camelCase
- 상수: PascalCase (C# 스타일, `DefaultRefreshInterval`)
- 인터페이스: I 접두사 필수 (`ITranslator`, `IOcrProvider`)
- 비동기 메서드: Async 접미사 필수 (`TranslateAsync`, `CaptureScreenAsync`)
- 로깅: `ILogger<T>` 사용, `Console.WriteLine` 금지
- 설정값 하드코딩 금지 — 설정 파일(`settings.json`) 사용
- 의존성: 인터페이스를 통해 접근, 구현체 직접 참조 금지
- 예외 처리: 빈 catch 블록 금지, 반드시 로깅 포함

## Architecture Style
- WPF MVVM (View → ViewModel → Service)
- 코드비하인드에 OCR/번역 로직 금지
- 번역 엔진은 ITranslator 인터페이스 구현 강제

---

# Performance Requirements

## Expected Scale
로컬 단일 사용자 환경

## Optimization Priority
- API 호출 최소화 (텍스트 변경 시에만 번역)
- 동일 문장 재호출 방지
- 초당 최대 호출 횟수 제한 설정 가능

---

# Security Requirements

- **API Key 보호**: Windows DPAPI(`ProtectedData.Protect`)로 암호화 저장, 평문 저장 금지
- **로그 보안**: API Key 및 민감 정보 로그 기록 금지
- **통신 보안**: 외부 번역 API HTTPS 강제, HTTP 금지
- **화면 캡처**: 디스크 저장 금지, 메모리에서만 처리 후 즉시 폐기
- **OCR 데이터**: 원문 텍스트 저장 금지, 메모리에서만 처리
- **번역 캐시**: 기본 허용, 사용자 선택으로 비활성화 가능
- **설정 파일 위치**: `AppData\Roaming\` 사용
- **번역 모듈 접근**: ITranslator 인터페이스를 통해서만 접근, 직접 파일 접근 금지

---

# Future Plans

- 원문 위치에 주변 배경색으로 백그라운드를 칠한 뒤, 원래 글자 색과 유사한 색으로 번역문을 렌더링하는 몰입형 오버레이 기능
- 그 외 추후 추가 예정

---

# Boss AI Instructions

Boss AI must:

1. Analyze all requirements.
2. Split tasks into smaller units.
3. Assign suitable AI for each task.
4. Spawn Manager AI per segment to coordinate Sub AI.
5. Follow Coding Rule.txt strictly.
6. Prioritize stability and readability.
7. Generate task workflow before development starts.

---

# Final Notes
