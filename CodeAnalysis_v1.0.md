# 코드 분석 리포트 v1.0

목표: 현재 프로젝트(`Agent.UI.Wpf`)의 구조와 구현(특히 1~3단계로 추가된 기능들)을 심도 있게 분석하여, WPF/비동기/네트워크 관점에서 문제점, 개선점, 운영상 주의사항을 정리합니다. 이 리포트는 개발자가 빠르게 코드베이스를 이해하고 안전하게 보완할 수 있도록 설계되었습니다.

작성자: 자동분석기(요약/정리)
작성일: 2025-10-10

---

## 요약(한줄)
- 전반적으로 MVVM 기반의 WPF 앱 구조를 따르고 있으며, `Services` 레이어에 설정 파싱, UDP 전송, CBOR 코덱, 고수준 `AgentClient`가 구현되어 있습니다. 주요 이슈는 비동기 패턴(특히 TCS 생명주기), 로깅/오류 전파, 멀티 인플라이트 미지원, 그리고 UI-스레드 안전성 관련입니다. 권장 조치는 서비스 로깅 통합, Async command 패턴 정립, TCS 안정화(타임아웃/취소/재시도 규칙 명문화), 그리고 단위/통합 테스트 추가입니다.

---

## 프로젝트 구조(주요 파일)
- `App.xaml`, `App.xaml.cs` — 앱 진입 및 종료 로직. `OnExit`에서 ViewModel `IAsyncDisposable` 호출.
- `ViewModels/MainViewModel.cs` — UI 상태, 커맨드, Config 로드, AgentClient 인스턴스화 및 핸드셰이크(CRUD/hello) 로직.
- `ViewModels/ObservableObject.cs` — INotifyPropertyChanged 기본 구현.
- `ViewModels/RelayCommand.cs` — 동기 ICommand 구현(기존).
- `ViewModels/AsyncRelayCommand.cs` — 비동기 ICommand 구현(추가).
- `Services/ConfigService.cs` — `config/generated` 및 `config/qos` XML 파싱 (타입명, QoS 프로필 수집).
- `Services/ConfigLocator.cs` — config 경로 결정(CLI, ENV, 기본경로).
- `Services/ITransport.cs` — 전송 인터페이스(ITransport).
- `Services/UdpTransport.cs` — `UdpClient` 기반 전송 구현(수신 루프, DatagramReceived 이벤트).
- `Services/IFrameCodec.cs` — 코덱 인터페이스.
- `Services/CborFrameCodec.cs` — CBOR 안에 JSON 바이트 포장/디코드.
- `Services/Frames.cs` — `Req`, `Rsp`, `Evt` DTO.
- `Services/AgentClient.cs` — 단일 인플라이트 `RequestAsync`, 이벤트 분리, transport/codec 조합.
- `Services/Timeouts.cs` — 타임아웃 상수(HelloSeconds).
- `Views/*` — XAML 뷰(주요 UI).

(파일 경로는 루트에서 `Agent.UI.Wpf` 기준입니다; 파일명을 백틱으로 표기했습니다.)

---

## 아키텍처 관점 요약
- MVVM: `MainWindow.xaml`이 `MainViewModel`을 DataContext로 사용. 뷰와 로직 분리 원칙 준수.
- 서비스 분리: IO/비즈니스 로직은 `Services/`에 위치. UI는 서비스에 의존(주입 형태는 간단한 `new` 사용).
- 통신 계층: transport(UDP) -> codec(CBOR/JSON) -> AgentClient(요청/응답/이벤트) -> ViewModel.
- 설정: `ConfigService`가 `config` 디렉터리를 읽어 UI 목록을 동적으로 구성.

---

## 상세 코드 리뷰 — 주요 컴포넌트
아래 섹션에서는 핵심 파일별로 기능/핵심 로직, 발견한 문제점, 개선 제안을 정리합니다.

### 1) `Services/ConfigService.cs`
- 기능: `LoadTypeNames`, `LoadQosProfiles` 구현. XML 네임스페이스 무관하게 `LocalName`으로 태그 탐색.
- 장점: 예외를 잡아 로그(기본 `Debug.WriteLine` 또는 `LogAction`)로 남김, 중복 제거(HashSet), 정렬 처리.
- 리스크/개선:
  - `LogAction` 훅을 제공해 UI로 로그 전송 가능하게 구현되어 있음(좋음).
  - XML 파싱에서 더 엄격한 유효성 검사(비어있는 name 속성, 잘못된 형식 처리)를 추가하면 안전.

추천: 추가적인 단위 테스트(네임스페이스가 있는 XML, 누락/잘못된 name 속성 케이스)를 작성.

### 2) `Services/UdpTransport.cs`
- 기능: `UdpClient` 래퍼, 비동기 수신 루프, `DatagramReceived` 이벤트
- 장점: ReceiveLoop에서 CancellationToken을 사용, Start/Stop 구조 제공.
- 문제/리스크:
  - ReceiveLoop에서 예외를 로깅만 하고 재시도/소켓 재초기화 전략 없음. 일부 예외(소켓 해제 등)는 루프 종료 후 복구 불가 상태가 될 수 있음.
  - `SendAsync`는 `payload.ToArray()`를 사용 — 성능상 복사 비용 존재하지만 단순 구현으로 용인 가능.
- 개선 제안:
  - 수신 예외의 심각도에 따른 처리(로그 수준, 재시도, 재초기화) 정책 추가.
  - `LogAction`(추가됨)에 더 많은 문맥(예: 원격/로컬 엔드포인트)을 포함해 기록.

### 3) `Services/CborFrameCodec.cs`
- 기능: CBOR 바이트스트링 안에 JSON 직렬화된 바이트를 포장
- 장점: 간단하고 상호운용성이 쉬운 전략(JSON을 CBOR 바이트스트링으로 감싸 전송)
- 문제/리스크:
  - JSON 직렬화 후 `CborWriter.WriteByteString(js.ToArray())`로 보냄. 수신 시에는 `CborReader.ReadByteString()`로 가져와 `JsonDocument.Parse(byte[])` 호출.
  - TryDecode의 오류를 로깅하지만, 복구 불가인 경우 상위에서 대체 루틴 필요.
- 개선 제안:
  - 코덱에 명확한 오류 코드/예외를 던져 상위에서 정책 적용하도록 함.
  - 필요시 버전/스키마 검증(add proto field already present).

### 4) `Services/AgentClient.cs`
- 기능: `_tx`, `_fx` 조합, `RequestAsync`(단일 인플라이트), `EventReceived` 전파
- 장점: 단순한 request/response 흐름, cancellation 등록
- 중요한 리스크 (핵심)
  - 단일 `_pending` TaskCompletionSource만 사용 — 동시 다중 요청(멀티 인플라이트)을 지원하지 않음. 서로 다른 요청의 응답이 도착했을 때 충돌(최근 요청이 이전 요청을 덮어씀) 가능.
  - `_pending` 생성/취소 순환에서 레이스 컨디션 가능: `RequestAsync` 호출 직후 응답이 오면 `_pending`이 아직 완성되지 않은 경우가 존재하지만 현재 구현에서는 `Receive`에서 `_pending?.TrySetResult`를 호출하기 때문에 일반적으로 작동하지만 경계 조건에서 `null` 체크로 누락 가능.
  - Decode 실패시 단순 무시 -> 상위에서 추적 어렵다.
- 개선 제안:
  - correlation-id(요청 id)를 페이로드에 포함하고, AgentClient에서 `Dictionary<id,TCS>` 형태로 매핑. 멀티 인플라이트 지원 가능.
  - `_pending`의 상태 전환을 atomic하게 처리.
  - 디코드 실패시 로깅과 관측(메트릭) 추가.

### 5) `ViewModels/MainViewModel.cs`
- 기능: Config 로드, ComboBox 바인딩, Commands(Connect/Disconnect/Publish 등), Traffic/Logs 컬렉션
- 장점: UI 바인딩이 ObservableCollection/INotifyPropertyChanged를 잘 사용함.
- 상세 관찰사항 및 이슈:
  - `SelectedType` setter가 Topic을 자동으로 덮어씀. 사용자가 Topic을 수동으로 변경해도 이후 타입 선택시 덮어쓰게 되는 UX임(사용자 편의/정책 고려 필요).
  - Connect/Handshake 로직이 async 람다로 RelayCommand에 전달되는 패턴이 있었음 -> `AsyncRelayCommand` 추가로 문제 완화했음.
  - `_agent.EventReceived` 콜백에서 UI 컬렉션에 추가할 때 `Dispatcher.Invoke`로 마샬링 처리(적절).
  - ViewModel이 `IAsyncDisposable`을 구현해 App.OnExit에서 DisposeAsync 호출하도록 했음(좋음).
- 개선 제안:
  - Connect/Handshake 명령을 `AsyncRelayCommand`로 완전히 전환하고, UI에서 실행 상태(버튼 활성화/비활성화)를 바인딩하도록 함.
  - Topic 자동 덮어쓰기 정책: 사용자가 수정했을 때 자동 업데이트를 중단하는 플래그를 도입 추천.

### 6) `ViewModels/AsyncRelayCommand.cs` & `RelayCommand.cs`
- 현재 `RelayCommand`는 동기 Action을 실행, `AsyncRelayCommand`는 `Func<Task>`를 안전히 감싸서 예외를 캡처하고 `CanExecute` 상태를 관리합니다.
- 권장: UI에서 비동기 커맨드를 표준화하기 위해 `AsyncRelayCommand`를 사용하고 `RelayCommand`는 동기 작업에만 사용.

---

## 동시성/비동기 안전성 분석(핵심)
1. TaskCompletionSource 관리
   - 현재 `AgentClient.RequestAsync`에서 `_pending`을 재사용하지 않고 새로 생성하지만 이전 TCS를 취소(`TrySetCanceled`)만 하고 제거하지 않음. 이 부분은 보통 괜찮으나 Race 조건이 발생할 수 있으므로 아래 조치 권장:
     - 새 TCS 할당 직후(또는 TrySetCanceled 직후) 이전 TCS의 상태를 보장하고, `_pending`에 대한 읽기/쓰기를 lock 또는 Interlocked로 보호.
     - 멀티 인플라이트로 확장시 correlation-id 도입.

2. Cancellation/타임아웃
   - `RequestAsync`는 외부 `CancellationToken`을 등록. 그러나 `_pending`이 취소되는 케이스에서 예외 전파/정리 로직이 명확히 보장되어야 함.

3. UI 스레드 마샬링
   - EventReceived에서 `Dispatcher.Invoke` 사용으로 안전. 다만 냉정히 말하면 `Dispatcher.BeginInvoke`가 블로킹을 최소화하므로 권장될 수 있음.

4. 예외 전파
   - 서비스 레이어(LogAction 훅 추가로 개선됨)에서 예외를 ViewModel 수준으로 전파해 상태/로그를 남기도록 구현하는 것이 진단에 유리.

---

## 보안/운영 고려사항
- UDP는 무상태/비신뢰성 프로토콜이므로 재시도/중복/순서 문제를 설계적으로 다뤄야 합니다. 현재 코드는 단순 hello/REQ/RSP 검증 정도로만 사용되므로 운영 환경에서는 신뢰성 계층(재전송, ack, correlation id 등)을 고려해야 합니다.
- Config 파일(Open XML) 파싱 시 외부 엔티티/넘치는 입력값에 대비하여 안전한 파싱(정규화, 크기 제한)을 추가 권장.

---

## 권장 우선 작업 목록 (가장 시급한 것부터)
1. 서비스 로깅의 일원화(완료된 LogAction 훅을 모든 서비스에 적용하고, UI 레벨/파일/원격 로거로 전송하도록 설정).
2. `AgentClient`의 TCS/멀티 인플라이트 문제 해결 — correlation-id 도입 또는 Request/Response 매핑 도입.
3. `AsyncRelayCommand`로 모든 async 명령 전환(Connect/Handshake 등). 이미 추가되었으므로 MainViewModel에서 사용을 완전히 전환하고 버튼 활성화 바인딩 적용.
4. `UdpTransport` 수신 예외 처리 정책(재연결 전략 또는 fatal 오류 표시).
5. 단위 테스트 추가: `CborFrameCodec` Encode/Decode 라운드트립, `AgentClient`의 RequestAsync(모킹 transport) 시나리오.
6. 사용자 UX: Topic 자동 덮어쓰기 정책(옵션: 사용자가 편집하면 자동 덮어쓰기 중단).

---

## Quick-fix 코드 스니펫(권장 적용)
1) AgentClient: 간단한 correlation-id 도입 골격
```csharp
// Req에 correlation id 추가 or wrapper 사용
public sealed class Req { public string Correlation { get; init; } = Guid.NewGuid().ToString(); /* ... */ }
// AgentClient: dictionary map
private readonly ConcurrentDictionary<string, TaskCompletionSource<Rsp>> _pendingMap = new();
// RequestAsync: create tcs, add to map with correlation
// OnDatagram: decode, read correlation, try get and set result
```

2) UdpTransport: ReceiveLoop 예외 처리 로그/재시작
```csharp
catch (SocketException se) {
  Log($"udp socket error {se.Message}");
  // optionally: await StopAsync(); await StartAsync(previousAddress, previousPort);
}
```

---

## How to validate locally (권장 절차)
1. `dotnet build` — 컴파일 확인(현재 컴파일 OK)
2. 실행(개발 환경에서 UDP echo 서버 준비)
   - 외부 테스트 툴(e.g., netcat, custom UDP server)로 hello 요청을 수신하고 RSP를 전송하도록 설정
3. UI에서 Address/Port를 지정하고 Connect 버튼 클릭 — Messages 탭에 OUT/IN 기록이 남는지 확인
4. 실패 시 `Logs` 탭의 서비스 로그(Transport/Cbor/AgentClient) 확인

> 주의: 로컬에서 실행시 포트 충돌/파일 잠김 주의. 앱 종료 시 반드시 종료 후 재시작.

---

## 권장 일람(다음 작업 제안)
- 단기(오늘~1주): 서비스 로깅 강화, Async command 정리, AgentClient TCS 안정성 패치
- 중기(1~4주): 단위/통합 테스트 추가, 멀티 인플라이트/상관 ID 설계
- 장기(1~분기): 프로덕션 환경의 신뢰성(재전송/중복/보안) 및 CI 파이프라인 연계

---

## 참고: 소스 파일 인용
- `ViewModels/MainViewModel.cs` — Connect/Handshake 로직, EventReceived 마샬링, Topic 자동화
- `Services/AgentClient.cs` — RequestAsync, OnDatagram, single pending pattern
- `Services/CborFrameCodec.cs` — EncodeReq, TryDecode 구현
- `Services/UdpTransport.cs` — Start/Stop, ReceiveLoop
- `Services/ConfigService.cs` — XML 파싱

파일을 열어보려면 해당 경로에서 코드를 확인하세요. (예: `ViewModels/MainViewModel.cs`)

---

## 결론
현재 구현은 WPF+MVVM의 기본 규칙을 잘 따르고 있으며 네트워크/코덱 계층도 합리적으로 구성되어 있습니다. 다만 멀티 인플라이트, TCS 생명주기, 서비스 로그/예외 누락은 운영 단계에서 문제를 야기할 가능성이 큽니다. 우선순위는 서비스 로깅 통합과 `AgentClient`의 안정성(단일/다중 요청 처리) 보완이며, 그 뒤에 자동화 테스트를 추가하여 회귀를 방지하는 것이 안전한 로드맵입니다.

필요하시면 이 리포트를 바탕으로 3.5 보완 가이드를 제가 바로 생성해 구체적 코드 패치를 적용하겠습니다(예: correlation-id 구현안 + AsyncRelayCommand로 전환 + 로깅 확장). 어느 것을 우선하시겠습니까?

---

(끝)