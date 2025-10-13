# copilot-instructions.md

- 모든 진행상황과 답변은 한글로 한다.

## 1) 목적과 적용 범위

* 본 문서는 **.NET 8 WPF + MVVM** 기반 UI 코드에 대한 **전역 개발 지침**입니다.
* 특정 기능 구현 방법은 포함하지 않습니다. (별도 기능 설계 문서에서 다룸)

---

## 2) 전역 원칙

* **MVVM 준수**: View(XAML) ↔ ViewModel(C#) ↔ Service/Model 레이어 분리.
* **기존 코드 이해 후 수정**: 새 코드는 기존 구조/명명/패턴을 **재사용**하고, **중복 코드**를 만들지 않습니다.
* **코드 위치 정확성**: 추가/수정 코드는 **유효한 스코프 안**(중괄호·XAML 트리 일관성 유지)에만 배치합니다.
* **빌드 금지**: 제안/수정 후 **빌드를 실행하지 않습니다.**
* **주석 정책**: **Doxygen 스타일**로 주석을 작성합니다. 가능하면 **한글**, 불가 시 **영문 토큰** 사용.
* **응답 언어**: 모든 설명/주석/문서화는 **한글**을 우선합니다.

---

## 3) 프로젝트 구조(고정)

```
UI/Ui.Wpf/
├─ Views/            # XAML만 (레이아웃/바인딩)
├─ ViewModels/       # ObservableObject/RelayCommand 기반 VM
├─ Services/         # IO/IPC/설정 등 UI 비의존 서비스
├─ Theme/            # Colors.xaml, Styles.xaml 등 공용 스타일
└─ Properties/       # 기본 WPF 메타
```

---

## 4) MVVM 아키텍처 규칙

* **View(XAML)**

  * 로직 없음. Code-behind는 **최소화**(필수 초기화 외 금지).
  * 모든 상호작용은 **Binding**(`Text="{Binding ...}"`, `Command="{Binding ...}"`)으로 연결.
* **ViewModel(C#)**

  * `ObservableObject`(INotifyPropertyChanged) 상속.
  * 속성은 반드시 `SetField(ref field, value)`로 변경하여 UI와 동기화.
  * 사용자 액션은 `RelayCommand`로 노출. 필요 시 `CanExecute` 제공.
* **Service/Model**

  * 파일 IO, IPC, 파서, 시간, 설정 등 **UI 비의존** 로직만 위치.
  * `async/await` 기반 비동기 제공. UI 스레드 접근 금지(필요 시 ViewModel에서 Dispatcher 사용).

---

## 5) XAML 작성 지침

* **레이아웃**: `Grid` 중심. 열/행 정의로 **라벨-필드 정렬**.
* **스타일/간격**: 공용 `Theme/Styles.xaml`의 스타일 키 활용:

  * `LabelText`(TextBlock), `FieldControl`(TextBox/ComboBox 등), `SmallButton`(작은 버튼).
* **바인딩**:

  * 단방향 표시: `Mode=OneWay` (기본).
  * 사용자 입력: `UpdateSourceTrigger=PropertyChanged` 고려.
  * 컬렉션 표시: `ItemsControl`/`ListBox` + `ObservableCollection<T>`.
* **리소스**: 색/마진/서식은 **리소스 딕셔너리**로 재사용, XAML에 매직 넘버 최소화.
* **네임**: 자동 생성 `x:Name` 남용 금지. 바인딩으로 해결.

---

## 6) ViewModel 코딩 규칙

* **속성 패턴**

  ```csharp
  /// <summary>설명…</summary>
  private string _value = "";
  public string Value
  {
      get => _value;
      set => SetField(ref _value, value);
  }
  ```
* **명령 패턴**

  ```csharp
  /// <summary>사용자 액션 설명…</summary>
  public RelayCommand DoActionCommand { get; }
  DoActionCommand = new RelayCommand(DoAction, CanDoAction);

  private void DoAction() { /* 서비스 호출/상태 갱신 */ }
  private bool CanDoAction() => !string.IsNullOrWhiteSpace(Value);
  // 상태 변화 시 반드시:
  DoActionCommand.RaiseCanExecuteChanged();
  ```
* **컬렉션**: UI 바인딩 컬렉션은 `ObservableCollection<T>`만 사용.
  항목 변경은 `Add/Remove/Clear`로, 교체 시 새 인스턴스 생성 대신 **기존 컬렉션 조작** 선호.
* **의존성 주입**: 서비스는 **생성자 주입**. 전역 싱글톤/정적 상태 지양.

---

## 7) 비동기·스레딩

* **서비스**는 `Task` 반환의 **비동기 메서드** 제공.
* ViewModel에서 호출 시 `await` 사용, 예외는 `try/catch`로 처리 후 사용자 피드백 제공.
* UI 업데이트는 UI 스레드 보장 필요 시 `Application.Current.Dispatcher.Invoke/BeginInvoke`.

---

## 8) 예외·로깅·상태

* **예외 처리**: 서비스 경계에서 잡고 **의미 있는 메시지**로 변환. 삼키지 말 것.
* **사용자 피드백**: ViewModel에 `Status`(string) 유지. 장기 로그용으로 `ObservableCollection<string> Logs` 사용 가능.
* **메시지/트래픽**: 별도 컬렉션을 쓰더라도 **ViewModel 컬렉션 규칙**(위 6항)을 따름.

---

## 9) 명명/서식 규칙

* **C#**: PascalCase(공개 멤버), camelCase(로컬/필드), `Async` 접미사(비동기).
* **XAML**: 리소스 키 PascalCase, 바인딩 대상 속성명과 동일하게 유지.
* **주석**: Doxygen(`///`) 사용, 가능하면 **한글 설명**. 불가 시 간결한 영문 토큰.

---

## 10) 경로·설정 정책

* 실행 시 `config` 경로 검색 순서:
  `--config="..."`
  → 환경변수 `AGENT_CONFIG_DIR`
  → 실행폴더 기준 `./config`
* `csproj`의 `config/**` 복사 규칙은 **유지**. 상대경로 변경 시 해당 항목만 수정.

---

## 11) 금지/주의 목록

* Code-behind에 **업무 로직** 구현 금지(초기화/뷰 제스처 처리 수준만 허용).
* **하드코딩 경로/포트/상수** 금지(설정/상수 클래스/리소스로 분리).
* **불필요한 외부 패키지** 도입 금지(설득력 있는 근거·리뷰 필요).
* **빌드/실행**을 자동으로 트리거하는 스크립트/작업 추가 금지.
* **중복 타입/함수** 생성 금지: 기존 구현 확장 또는 공용 유틸로 승격.

---

## 12) PR/리뷰 체크리스트

* [ ] MVVM 분리 준수(뷰에 로직 없음).
* [ ] 기존 패턴/명명 재사용, 중복 없음.
* [ ] 스코프/레이아웃 유효(컴파일/런타임 오류 없는 위치에 삽입).
* [ ] Doxygen 주석 추가(가능하면 한글).
* [ ] 비동기/예외/상태 피드백 처리.
* [ ] 스타일/간격은 공용 리소스 사용(매직 숫자 최소화).
* [ ] 빌드/실행 절차를 **추가하지 않음**.

---

## 13) 예시 스니펫(템플릿)

**View(XAML) 바인딩 예**

```xml
<Grid Margin="12">
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="Auto"/>
    <ColumnDefinition Width="*"/>
  </Grid.ColumnDefinitions>

  <TextBlock Text="이름" Style="{StaticResource LabelText}"/>
  <TextBox Grid.Column="1" Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}"
           Style="{StaticResource FieldControl}"/>
</Grid>
```

**ViewModel 속성/명령**

```csharp
/// <summary>사용자 이름</summary>
private string _name = "";
public string Name { get => _name; set => SetField(ref _name, value); }

/// <summary>저장 수행</summary>
public RelayCommand SaveCommand { get; }
public MainViewModel(IMyService svc) {
    SaveCommand = new RelayCommand(Save, CanSave);
}
private void Save() { /* 서비스 호출 */ }
private bool CanSave() => !string.IsNullOrWhiteSpace(Name);
```

---

## 14) 문서/주석 예(한글 Doxygen)

```csharp
/// \brief 설정 파일을 로드한다.
/// \param path 설정 파일의 절대 경로
/// \return 로드 성공 여부
public bool LoadConfig(string path) { ... }
```

---

이 문서는 **Copilot이 항상 우선적으로 따라야 하는 기본 규범**입니다.
기능별 구현 지시가 필요할 때는 별도의 **기능 설계/가이드 문서**를 작성해 이 문서와 함께 참고하세요.
