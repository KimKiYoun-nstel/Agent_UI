# IPC JSON/CBOR 프로토콜 (v1.0)

이 문서는 `RtpDdsGateway`의 `IpcAdapter`가 IPC 경로로 주고받는 메시지의 JSON 규격(전송은 CBOR 인코딩 가능)을 설명합니다.

- 문서 언어: 한국어(설명) + 영문 코드 토큰
- 파일: `ipc_json_protocol_v1.0.md`

## 개요

- 전송 레이어: 내부 IPC 프레임 (dkmrtp::ipc). 메시지 바디는 JSON 객체를 CBOR로 직렬화해 전송하는 것이 기본이다.
- 프레임 유형 (IPC 전송 계층):
  - REQ (요청): dkmrtp::ipc::MSG_FRAME_REQ (IpcAdapter::install_callbacks에서 수신 시 처리)
  - RSP (응답): dkmrtp::ipc::MSG_FRAME_RSP (응답 전송에 사용)
  - EVT (이벤트): dkmrtp::ipc::MSG_FRAME_EVT (DDS 수신 이벤트 브로드캐스트)
- 상호작용 방식: 요청(Request) → 처리 → 응답(Response). DDS 샘플은 별도의 EVT 프레임으로 전송.
- 각 프레임에는 corr_id(상관 ID)가 포함되어 요청/응답을 매칭함.

## 공통 규칙

- 모든 메시지는 JSON 객체를 기본으로 한다. IPC 바디에는 CBOR(binary)을 실어 전송하는 경우가 많다(성능 및 크기 절감).
- 요청에는 `op`(operation) 필드가 핵심이며, `target`과 `args`는 작업 대상/인자를 기술한다.
- 응답은 항상 `ok`(boolean)를 포함한다. 실패 시 `ok=false`와 `err`(숫자) 및 `msg`(문자열) 또는 `category`(내부 DdsResult 카테고리)를 제공할 수 있다.
- 이벤트(EVT)는 DDS에서 수신한 샘플 정보를 담아 UI에 전달한다. `display`(간략 미리보기)와 `data`(전체 데이터)를 포함한다.

## 요청(Request) 메시지 구조

기본 구조:

{
  "op": "<operation>",          // 예: "create", "write", "clear", "hello"
  "target": { ... },             // 작업 대상 메타 (optional)
  "args": { ... },               // 추가 인자 (optional)
  "data": { ... }                // 일부 작업은 data 필드 사용 (optional)
}

주요 필드 설명:

- op: 수행할 동작 식별자. IpcAdapter에서 처리되는 주요 op 값들:
  - "create": DDS 엔티티 생성 (participant, publisher, subscriber, writer, reader)
  - "write": writer를 통한 데이터 전송 (예: text publish)
  - "clear": 리소스 정리 (예: "dds_entities")
  - "get": 리소스 조회 (예: QoS 목록 조회 등)
  - "hello": 게이트웨이 기능/프로토콜 확인
- target: 생성/조작 대상의 상세. 통상 아래와 같은 키를 포함함:
  - kind: "participant" | "publisher" | "subscriber" | "writer" | "reader" | "qos"
  - topic: (writer/reader 대상일 때) 토픽 이름
  - type: (writer/reader 대상일 때) 타입 이름
  - 기타: 필요에 따라 확장 가능
- args: 동작별 인자
  - participant 생성: { "domain": <int>, "qos": "Lib::Profile" }
  - publisher/subscriber 생성: { "domain": <int>, "publisher": "pub1", "qos": "Lib::Profile" }
  - writer/reader 생성: { "domain": <int>, "publisher"|"subscriber": "name", "qos": "Lib::Profile" }
  - write (text publish): { "text": "..." } (가끔 `data` 하위에 위치할 수 있음)
  - get (예: qos 조회): { "args": { "option": "brief" } } // option은 선택적이며 추후 확장 가능

예시: participant 생성 (JSON)

{
  "op": "create",
  "target": { "kind": "participant" },
  "args": { "domain": 0, "qos": "TriadQosLib::DefaultReliable" }
}

예시: writer 생성 (JSON)

{
  "op": "create",
  "target": { "kind": "writer", "topic": "ExampleTopic", "type": "ExampleType" },
  "args": { "domain": 0, "publisher": "pub1", "qos": "TriadQosLib::DefaultReliable" }
}

예시: 텍스트 publish (JSON)

{
  "op": "write",
  "target": { "kind": "writer", "topic": "chat" },
  "data": { "text": "Hello world" }
}

예시: hello (프로토콜 확인)

{ "op": "hello" }

예시: QoS 목록 조회 (JSON)

{
  "op": "get",
  "target": { "kind": "qos" },
  "args": { "include_builtin": true, "option": "brief" }
}

## 응답(Response) 메시지 구조

공통 구조:

{
  "ok": <bool>,
  // 성공일 때
  "result": { ... },
  // 실패일 때
  "err": <int>,
  "msg": "...",
  "category": <int> // DdsResult.category가 필요한 경우
}

- ok: 처리 성공 여부
- result: 성공 시 작업 결과 메타 (action 설명 등)
- err: 고유 오류 코드 (사용자/프로토콜 정의). `IpcAdapter`에서 사용하는 예시값들:
  - 4: 내부 처리/리소스 생성 실패 (DdsResult 실패 매핑)
  - 6: 잘못된 요청(필수 필드 누락 등)
  - 7: JSON/CBOR 파싱 또는 내부 예외
- category: DdsManager에서 반환한 `DdsResult::category`를 전송하여 상세 원인 제공

예시: 성공 응답 (participant 생성)

{"ok": true, "result": {"action": "participant created", "domain": 0}}

예시: 실패 응답

{"ok": false, "err": 4, "category": 2, "msg": "creation failed reason"}

## 이벤트(Event) 메시지 구조 (MSG_FRAME_EVT)

DDS에서 수신된 샘플을 UI로 전달할 때 사용합니다. 구조는 다음과 같습니다:

{
  "evt": "data",
  "topic": "<topic name>",
  "type": "<type name>",
  "display": <json|null>,    // 최신 미리보기(없을 수 있음)
  "data": <json object>     // 전체 데이터(타입별로 JSON으로 변환됨)
}

- evt: 고정값 "data" (DDS 데이터 이벤트)
- topic: DDS 토픽 이름
- type: DDS 타입 이름
- display: 일정 길이로 잘린(프리뷰) 요약 표현 또는 null
- data: 타입에 따라 JSON으로 변환된 전체 샘플

예시 이벤트 (간단한 text 메시지)

{
  "evt":"data",
  "topic":"chat",
  "type":"StringMsg",
  "display":"Hello world",
  "data": { "text": "Hello world" }
}

## CBOR 전송과 corr_id

- IPC 레이어는 body를 CBOR로 인코딩하여 전송하는 것을 권장합니다. 수신자는 CBOR → JSON 디코딩 후 처리합니다.
- corr_id: IPC Header의 corr_id는 요청/응답 매칭에 사용됩니다. 요청이 들어올 때 corr_id를 보존하여 응답에 동일하게 돌려줘야 합니다.
  - 요청 프레임의 corr_id는 IpcAdapter의 `on_request` 콜백에서 `ev.corr_id`로 읽혀진다.
  - 응답 전송 시 `ipc_.send_frame(MSG_FRAME_RSP, corr_id, ...)`로 대응한다.

## QoS 문자열 형식

- QoS는 문자열로 전달되며 보통 `Lib::Profile` 형식을 따른다. 예: `TriadQosLib::DefaultReliable`.
- 코드에서는 이 문자열을 `::`로 분리하여 `lib`(라이브러리)와 `prof`(프로파일)로 분해한다.

## 오류 코드(권장 확장)

- 4: 리소스 생성 실패 / 내부 처리 실패 (DdsResult 실패)
- 6: 요청 필드 누락/잘못된 인자
- 7: JSON/CBOR 파싱 에러 또는 내부 예외
- 필요한 경우 프로젝트 공통 에러 표준을 추가로 정의하여 확장할 것

## 매핑 요약 (IpcAdapter 기준)

- Request op -> 처리 함수(요약)
  - create (participant) -> DdsManager::create_participant
  - create (publisher) -> DdsManager::create_publisher
  - create (subscriber) -> DdsManager::create_subscriber
  - create (writer) -> DdsManager::create_writer
  - create (reader) -> DdsManager::create_reader
  - write (writer) -> DdsManager::publish_text
  - clear (dds_entities) -> DdsManager::clear_entities
  - hello -> 프로토콜/기능 정보 반환
  - get (qos) -> 미정(TODO)

### 추가: QoS 목록 조회 (op = "get", target = {"kind":"qos"})

목적: UI(발신자)가 gateway(수신자)에게 자신이 보유한 QoS 프로파일 이름 목록을 요청하면 gateway는 목록과 각 프로파일의 세부 정보를 응답한다.

요청 예시 (UI -> Gateway)

{
  "op": "get",
  "target": { "kind": "qos" },
  "args": { "option": "brief" }  // option은 선택적이며 추후 확장 가능
}

응답 예시 (Gateway -> UI)

{
  "ok": true,
  "result": [ "TriadQosLib::DefaultReliable", "TriadQosLib::BestEffort" ],
  "detail": {
    "TriadQosLib::DefaultReliable": { "reliability": "RELIABLE", "durability": "VOLATILE" },
    "TriadQosLib::BestEffort": { "reliability": "BEST_EFFORT", "durability": "VOLATILE" }
  }
}

응답 구조 설명:

- result: QoS 이름 문자열 배열
- detail: QoS 이름별 추가 메타(선택적). 항목은 필요 시 확장 가능

## 예제 시나리오

1) UI가 participant 생성 요청
   - UI -> REQ: CBOR({"op":"create","target":{"kind":"participant"},"args":{"domain":1,"qos":"TriadQosLib::DefaultReliable"}})
   - Gateway -> 처리 -> RSP: CBOR({"ok":true, "result":{"action":"participant created","domain":1}})

2) DDS StringMsg 수신 시
   - Gateway -> EVT: CBOR({"evt":"data","topic":"chat","type":"StringMsg","display":"Hello","data":{"text":"Hello"}})

## 부록: 구현 참고

- 참조 파일: `RtpDdsGateway/src/ipc_adapter.cpp` 의 `process_request` 와 `emit_evt_from_sample` 구현을 기준으로 규격을 정리했습니다.
- 확장 시:
  - 새로운 `op` 또는 `target.kind`를 추가할 수 있음
  - 에러 코드 표를 중앙화하여 `qos_store` 또는 `dds_manager`와 일관되게 유지 권장

---

작성자: 자동 생성 (repo inspection 기반)
생성일: 2025-10-14
