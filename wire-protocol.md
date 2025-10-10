# Wire Protocol Reference (UI ↔ Gateway) — v2

본 문서는 레거시 프로젝트(`ConnextControlUI` + `DkmRtpIpc` + `RtpDdsGateway`) 분석에 기반한 **최신 정합 스펙**입니다.  
v2에서는 `target` 필드가 **문자열이 아닌 객체(Map)** 이어야 함을 명확히 했습니다.

---

## 1) 데이터 경로 개요
UI(Client) ──UDP/RIPC──▶ Gateway(Server)  
- 프레이밍: **RIPC 헤더(24B, Big-endian)** + **CBOR Map 페이로드**  
- 메시지 방향: UI → REQ, Server → RSP/EVT

---

## 2) RIPC 프레임 헤더 (고정 24바이트, 네트워크 바이트오더)

| 필드     | 타입 | 설명/값 |
|---------|------|--------|
| `magic` | u32  | `0x52495043` (ASCII 'RIPC') |
| `version` | u16| `0x0001` |
| `type`  | u16  | `0x1000`=REQ, `0x1001`=RSP, `0x1002`=EVT |
| `corr_id` | u32| 요청–응답 상관관계 ID(클라에서 증가) |
| `length` | u32 | 뒤따르는 CBOR 페이로드 길이(bytes) |
| `ts_ns` | u64  | 송신 시각(ns), 진단용 |

> 전 필드 **Big-endian(network order)**.

---

## 3) CBOR 페이로드 (Map 구조)

### 3.1 REQ(JSON 의미)
```json
{
  "op": "hello" | "create" | "write" | "...",
  "target": {
    "kind": "agent" | "participant" | "publisher" | "subscriber" | "writer" | "reader",
    // 필요 시 확장 필드:
    "topic": "<TopicName>",
    "type": "C_<TypeName>",
    "...": "..."
  },
  "args": { /* domain/name/qos/participant 등 */ },
  "data": { /* publish payload */ },
  "proto": 1
}
```
- **중요**: `target`은 **반드시 객체(Map)** 입니다. 문자열 `"agent"` 형태는 **금지**.  
- 레거시 서버는 내부에서 `target.value("kind", ...)`, `target.value("topic", ...)`로 접근합니다.

### 3.2 RSP(JSON 의미; 대표)
```json
{ "ok": true,  "result": { "action": "...", "...": "..." } }
{ "ok": false, "err": <int or string>, "category": <int>, "msg": "reason" }
```

### 3.3 EVT(JSON 의미; 대표)
```json
{ "evt": "data", "topic": "<topic>", "type": "<type>", "display": { ... } }
```

---

## 4) 소켓/네트워크
- **IPv4(AF_INET)** 소켓 사용 권장
- Client: `Connect(remote)` + `Bind(local)`(임의 포트 OK) → `Send(header+payload)`
- Server: `bind(serverPort)` → `recvfrom` → `sendto(peer, header+payload)`
- 기본 유니캐스트(브로드캐스트/멀티캐스트 미사용)

---

## 5) hello 왕복
- **REQ 예**:
```json
{ "op":"hello", "target":{"kind":"agent"}, "proto":1 }
```
- **RSP 예(의미)**:
```json
{ "ok":true, "result":{"action":"hello","proto":1,"cap":[...] } }
```

---

## 6) 구현 가이드(요지)
- 인코딩: `op/target/args/data/proto` 키는 소문자 유지. `target`은 `{ "kind": ..., ... }`.  
- 디코딩: `ok/result/err` → RSP, `evt/topic/type/display` → EVT.  
- 헤더: `magic/ver/type/corr_id/length/ts_ns`를 **network order**로 write/read.  
- 타임아웃: 요청별 CTS(권장 5s).

---

## 7) 진단 팁
- 송신 직전 **헤더 + payload 앞 32B HEX** 로깅 → 레거시 Qt와 비교.  
- 수신 시 헤더 검증 실패(매직/버전/type/length) → 즉시 drop + 이유 로그.  
- `target`이 문자열일 경우 서버에서 `json.type_error.306` 발생(교훈: 항상 Map).

---

## 8) 근거 소스(분석 기반)
- `DkmRtpIpc/include/dkmrtp_ipc_messages.hpp`, `DkmRtpIpc/src/dkmrtp_ipc.cpp`  
- `ConnextControlUI/include/rpc_envelope.hpp`  
- `RtpDdsGateway/src/ipc_adapter.cpp`, `gateway.cpp`

본 문서 v2는 `target` 구조(객체형)를 명문화해, 레거시 서버와의 정합 문제(type_error.306)를 예방합니다.
