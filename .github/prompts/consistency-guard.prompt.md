---
mode: 'agent'
description: '명명 규칙/주석 규칙/스타일 리소스 일관성 점검'
---

코드가 팀 규칙을 따르는지 점검하고, 어긋난 부분을 **체크리스트 + 자동 수정 패치**로 제시해줘.

규칙 기준:
- C#: PascalCase(공개), camelCase(로컬/필드), 비동기는 Async 접미사
- 주석: Doxygen ///, 가능하면 한글(불가 시 간결한 영문 토큰)
- XAML: Theme/Styles.xaml 리소스 키 사용(매직 넘버 최소화)
- 속성/컬렉션/명령 패턴: SetField, ObservableCollection, RelayCommand/CanExecute

대상 코드:
${input:code:'검사할 C# 또는 XAML을 붙여넣어 주세요'}
