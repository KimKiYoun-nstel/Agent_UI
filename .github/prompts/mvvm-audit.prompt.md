---
mode: 'agent'
description: '선택 코드의 MVVM 위반/중복/일관성 점검'
---

아래 C#/XAML 코드에서 다음을 점검하고 **근거 + 수정안(전/후 비교)**을 제시해줘.

- MVVM 위반: 뷰 로직이 ViewModel로 섞였는지, code-behind 의존이 있는지
- 중복 구현/유틸로 승격해야 할 로직
- 프로젝트 규칙 불일치: 속성 패턴(SetField), RelayCommand/CanExecute, DI, ObservableCollection 사용
- XAML: 바인딩 방향/경로/UpdateSourceTrigger, 인라인 스타일 남용

대상 코드:
${input:code:'검토할 C# 또는 XAML 코드를 붙여넣어 주세요'}
