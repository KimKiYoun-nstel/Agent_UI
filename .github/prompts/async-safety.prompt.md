---
mode: 'agent'
description: '비동기/스레딩 안전성 점검(UI freeze·예외·취소)'
---

아래 ViewModel/Service 코드를 **UI 멈춤 없이 안전한 비동기 패턴**으로 점검해줘.
문제 지점은 **라인 단위**로 지적하고, **안전한 대안 코드(전/후)**를 제시해.

점검 기준:
- await 누락/동기 호출(블로킹)로 인한 UI freeze 위험
- CancellationToken 처리(전달/취소/정리)
- 예외 전파/로깅/사용자 피드백(Status) 경로
- UI 스레드 전환 필요 시 Dispatcher 사용 위치
- ConfigureAwait의 필요성(라이브러리 경계)

대상 코드:
${input:code:'분석할 C# 코드를 붙여넣어 주세요'}
