# IPC JSON 연동 규격 (v3.0, 통합)

본 문서는 UI가 사용하는 JSON 기반 IPC 연동 규격을 현재 Gateway(Agent) 구현과 합쳐서 간결히 규정합니다. 전송 경로(고정 헤더)와 바디(JSON→CBOR), 메시지 카테고리(REQ/RSP/EVT), 공통 스키마 및 op별 필수/선택 필드, 요청/응답 상관 규칙과 샘플을 정의합니다.

수정일: 2025-10-31

---

## 1. 범위/원칙

- 대상: UI ↔ Gateway 간 IPC 바디(JSON)의 형태 및 의미. 서버/클라이언트 역할 서술은 배제하고, 메시지 자체의 형식을 규정.
- 기본 원칙
  - 모든 바디는 최상위 JSON 객체(Object).
  - 전송은 JSON을 CBOR로 직렬화해 IPC 바디로 사용(CBOR Map 권장).
  - REQ/RSP 매칭은 헤더 corr_id로 수행.
  - 응답은 항상 ok(boolean) 포함. 오류 시 err(int)와 msg(string), 필요 시 category(int) 포함.

---

## 2. 전송 계층(IPC 프레임)

- 고정 헤더(네트워크 바이트오더)
  - magic: 0x52495043 ('RIPC')
  - version: 0x0001
  - type: 0x1000(REQ) | 0x1001(RSP) | 0x1002(EVT)
  - corr_id: 32-bit 요청/응답 상관 ID
  - length: 바디 길이(byte, 32-bit)
  - ts_ns: 전송 시각(UTC ns, 64-bit)

- 바디 인코딩
  - 표준: CBOR Map(키-값 쌍 그대로 직렬화).
  - 호환: ByteString 안에 JSON 바이트 포장 가능(비권장).

- 메시지 카테고리(방향성 표기만 사용)
  - REQ: UI → Agent
  - RSP: Agent → UI
  - EVT: Agent → UI(비동기)

- 상관 규칙
  - REQ: 임의 corr_id 할당 → RSP: 동일 corr_id로 반환.
  - EVT: corr_id는 매칭과 무관(관례적으로 0).

---

## 3. 공통 바디 스키마

### 3.1 Request (REQ)

- 필드
  - op: string, 필수 — 수행 동작
  - target: object, 선택 — 대상 메타(표준형만 지원; 문자열 축약형은 허용하지 않음)
  - args: object|null, 선택 — 추가 인자
  - data: object|null, 선택 — 전송 데이터(객체 우선)
  - proto: integer, 선택 — 프로토콜 버전 힌트(기본 1)

- target 표준형(object)
  - kind: string — "participant" | "publisher" | "subscriber" | "writer" | "reader" | "qos"
  - topic: string — writer/reader에 필요
  - type: string — writer/reader에 필요
  - 확장 키 허용

- target 축약형(string)
  - 미지원. 모든 target은 object 형태를 사용합니다.

주의: data는 객체(Object)를 사용. 문자열 내부에 JSON을 중첩하지 않음.

### 3.2 Response (RSP)

- 필드
  - ok: boolean, 필수 — 처리 결과
  - result: object|array, 선택 — 주 결과 컨테이너
  - detail: object|array, 선택 — 부가 정보(예: QoS 상세)
  - err: integer, 선택 — 오류 코드(아래 표 참조)
  - msg: string, 선택 — 오류 메시지(사람 가독용)
  - category: integer, 선택 — 내부 카테고리(DdsResult 매핑)

- 에러 코드
  - 4: 내부 처리 실패(DdsManager 실패 등)
  - 6: 잘못된 요청(필수 필드 누락/유형 불일치)
  - 7: JSON/CBOR 파싱 또는 내부 예외

참고: UI 코덱은 result/detail/data(레거시 키) 모두 수용 가능하나, Agent는 result(+detail)만 사용.

### 3.3 Event (EVT)

- 필드
  - evt: string, 필수 — 현재 "data"만 정의
  - topic: string, 필수 — 데이터 소스 토픽명
  - type: string, 필수 — 데이터 타입명
  - data: object, 필수 — 샘플 전체 JSON 객체

---

## 4. 오퍼레이션별 요구사항(op)

아래는 실제 Agent 코드 구현을 기준으로 한 최소/선택 필드와 응답 구조입니다.

### 4.1 hello

- 목적: 프로토콜/기능 핸드셰이크 확인
- 요청
  - op = "hello"
  - target/args/data: 생략 가능
- 응답(요약)
  - ok: true
  - result: { proto: 1, cap: array } — cap 항목은 구조화된 예제(example) 포함

샘플

```json
{ "op": "hello" }
```

```json
{
  "ok": true,
  "result": {
    "proto": 1,
    "cap": [
      { "name": "create.participant", "example": { "op":"create", "target":{"kind":"participant"}, "args":{"domain":0, "qos":"TriadQosLib::DefaultReliable"} } },
      { "name": "create.publisher",   "example": { "op":"create", "target":{"kind":"publisher"},  "args":{"domain":0, "publisher":"pub1", "qos":"TriadQosLib::DefaultReliable"} } },
      { "name": "create.subscriber",  "example": { "op":"create", "target":{"kind":"subscriber"}, "args":{"domain":0, "subscriber":"sub1", "qos":"TriadQosLib::DefaultReliable"} } },
      { "name": "create.writer",      "example": { "op":"create", "target":{"kind":"writer", "topic":"ExampleTopic", "type":"ExampleType"}, "args":{"domain":0, "publisher":"pub1", "qos":"TriadQosLib::DefaultReliable"} } },
      { "name": "create.reader",      "example": { "op":"create", "target":{"kind":"reader", "topic":"ExampleTopic", "type":"ExampleType"},   "args":{"domain":0, "subscriber":"sub1", "qos":"TriadQosLib::DefaultReliable"} } },
      { "name": "write",              "example": { "op":"write",  "target":{"kind":"writer", "topic":"chat"}, "data": {"text":"Hello world"} } },
      { "name": "get.qos",            "example": { "op":"get",    "target":{"kind":"qos"} } },
      { "name": "evt.data",           "description": "DDS samples are sent as EVT with {evt,topic,type,data}" }
    ]
  }
}
```

### 4.2 get (QoS 조회)

- 목적: QoS 프로파일 목록 및 선택적 상세 조회
- 요청
  - op = "get"
  - target.kind = "qos"
  - args: { include_builtin: boolean, detail: boolean } (선택, 기본 false)
- 응답
  - ok: true/false
  - result: string 배열 (예: ["Lib::Profile", ...])
  - detail: array|object, 선택 — 프로파일 상세(요청 시에만 포함)

샘플

```json
{ "op": "get", "target": { "kind": "qos" }, "args": { "include_builtin": true, "detail": true } }
```

```json
{ "ok": true, "result": [ "TriadQosLib::DefaultReliable" ], "detail": [ { "TriadQosLib::DefaultReliable": {} } ] }
```

예외 시

```json
{ "ok": false, "err": 4, "msg": "failed to build qos list" }
```

### 4.3 create (엔티티 생성)

- 공통
  - op = "create"
  - args.domain: integer, 선택(기본 0)
  - args.qos: string, 선택 — "Lib::Profile" 형식(기본 "TriadQosLib::DefaultReliable")
  - 응답: ok=true 시 result.action = "... created" 및 생성 요약 필드 포함
  - 실패 시 err=4, category, msg 포함 가능; 요청 결함은 err=6

- participant
  - target.kind = "participant"
  - args: { domain, qos }
  - RSP result 예: { action: "participant created", domain }

- publisher
  - target.kind = "publisher"
  - args.publisher: string, 선택(기본 "pub1")
  - RSP result 예: { action: "publisher created", domain, publisher }

- subscriber
  - target.kind = "subscriber"
  - args.subscriber: string, 선택(기본 "sub1")
  - RSP result 예: { action: "subscriber created", domain, subscriber }

- writer
  - target.kind = "writer"
  - target.topic: string, 필수
  - target.type: string, 필수
  - args.publisher: string, 선택(기본 "pub1")
  - RSP result 예: { action: "writer created", domain, publisher, topic, type, id }

- reader
  - target.kind = "reader"
  - target.topic: string, 필수
  - target.type: string, 필수
  - args.subscriber: string, 선택(기본 "sub1")
  - RSP result 예: { action: "reader created", domain, subscriber, topic, type, id }

누락 오류 예(공통)

```json
{ "ok": false, "err": 6, "msg": "Missing topic or type tag" }
```

### 4.4 write (발행)

- 목적: 데이터 전송(발행)
- 요청
  - op = "write"
  - target.kind = "writer"
  - target.topic: string, 필수
  - data: object, 필수 — 발행할 JSON 객체
  - args: { domain, publisher, qos }는 호환 목적으로 허용되나 Agent는 본 op에서 사용하지 않음
- 응답
  - ok: true/false
  - result 예: { action: "publish ok", topic }

누락 오류 예

```json
{ "ok": false, "err": 6, "msg": "Missing or invalid data object" }
```

### 4.5 clear (자원 정리)

- 요청
- op = "clear"
- target: { "kind": "dds_entities" } (object) — 문자열 축약형("dds_entities")는 더 이상 허용되지 않음
- 응답

```json
{ "ok": true, "result": { "action": "dds entities cleared" } }
```

---

## 5. REQ/RSP 규칙 확장

- corr_id: 동일 corr_id로 REQ↔RSP 매칭.
- 타임아웃: 구현 합의에 따름(본 규격 비포함).
- 결과 컨테이너: Agent는 result(+detail) 사용. UI는 result/detail/data 키를 병합해 소비 가능.

---

## 6. 샘플 모음(전송 시 CBOR 직렬화)

- hello

```json
{ "op": "hello" }
```

```json
{ "ok": true, "result": { "proto": 1, "cap": [ { "name": "create.writer" } ] } }
```

- get qos

```json
{ "op": "get", "target": { "kind": "qos" }, "args": { "include_builtin": true, "detail": true } }
```

```json
{ "ok": true, "result": [ "TriadQosLib::DefaultReliable" ], "detail": [ { "TriadQosLib::DefaultReliable": {} } ] }
```

- create.writer

```json
{ "op": "create", "target": { "kind": "writer", "topic": "ExampleTopic", "type": "ExampleType" }, "args": { "domain": 0, "publisher": "pub1", "qos": "TriadQosLib::DefaultReliable" } }
```

```json
{ "ok": true, "result": { "action": "writer created", "domain": 0, "publisher": "pub1", "topic": "ExampleTopic", "type": "ExampleType", "id": 1 } }
```

- write

```json
{ "op": "write", "target": { "kind": "writer", "topic": "chat" }, "data": { "text": "Hello world" } }
```

```json
{ "ok": true, "result": { "action": "publish ok", "topic": "chat" } }
```

- clear

```json
{ "op": "clear", "target": { "kind": "dds_entities" } }
```

```json
{ "ok": true, "result": { "action": "dds entities cleared" } }
```

- EVT(데이터 샘플)

```json
{ "evt": "data", "topic": "chat", "type": "StringMsg", "data": { "text": "Hello world" } }
```

---

## 7. 호환/확장 메모

- hello의 result.proto는 현재 1. 차기 메이저 변경 시 값 증가 가능.
- 새로운 op 추가 시, hello의 cap에 항목 추가 권장.
- QoS 문자열은 "Lib::Profile" 형식이며 Agent 내부에서 lib/prof로 분해.
- 이벤트는 항상 전체 샘플을 data(object)로 제공. 별도 display 필드는 사용하지 않음.
