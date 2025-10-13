---
mode: 'agent'
description: 'XAML 바인딩/검증/리소스 사용 진단'
---

다음 XAML에서 바인딩과 스타일 사용을 진단해줘. 각 이슈는 **원인 → 영향 → 수정 패치** 순서로 답해.

체크 목록:
- 바인딩 경로/Mode/UpdateSourceTrigger가 의도와 일치하는가
- DataContext 가정이 타당한가(조상 DataContext 누락/충돌)
- Validation(IDataErrorInfo/ValidationRule) 연결 필요 여부
- 인라인 색/마진/스타일 → Theme/Styles.xaml 리소스 키로 추출 가능성
- ItemsControl/ListBox 바인딩 시 ObservableCollection 사용 여부

대상 XAML:
${input:xaml:'분석할 XAML을 붙여넣어 주세요'}
