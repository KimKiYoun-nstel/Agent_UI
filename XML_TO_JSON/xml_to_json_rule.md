
# XML -> JSON 규칙 (프로젝트-중립)

이 문서는 XML(스키마 정의)의 구조를 해석하여 JSON 샘플을 생성하는 방법을 "프로젝트 종속성 없이" 기술합니다.
여기서 제시하는 규칙은 특정 클래스명이나 타입명을 문서의 본문 규칙으로 포함하지 않습니다. 단, 문서 말미에 예시(첨부된 XML 스니펫과 해당 JSON 샘플)를 제공합니다.

## 적용 대상

- 입력: XML 기반 타입 정의(typedef, enum, struct/member 등) — 보통 `<typedef>`, `<enum>`, `<struct>` 와 내부 `member` 노드를 포함
- 출력: 각 struct 타입에 대해 생성된 JSON 샘플(테스트/문서용)

## 핵심 규칙 요약

1. 수집 순서

- XML 파일들을 모두 읽어 `typedef`와 `enum` 정의를 먼저 수집합니다. 그 다음 `struct`들을 파싱하여 필드 목록을 만듭니다.

1. typedef 해석

- typedef 맵에서 이름(또는 short name: 모듈 접두사 제거)을 조회합니다.
- typedef가 다른 typedef를 가리킬 경우 최대 2레벨(현재 구현 기준)까지 해석합니다. 구현을 확장하면 루프로 바꿀 수 있습니다.

1. 시퀀스 판정

- 다음 중 하나면 시퀀스로 처리합니다:
  - 멤버에 `sequenceMaxLength` 또는 유사 속성이 존재
  - 타입 문자열이 `T[]` 또는 `sequence<T>` 패턴을 포함
- 시퀀스의 원소 타입은 위 표기에서 추출하거나 typedef의 `Underlying`을 사용해 결정합니다.

1. 기본형(primitive) 및 구조체 판정

- 타입명(또는 typedef의 underlying)을 문자열 패턴으로 매칭하여 기본형을 추정합니다. 대표 heuristic:
  - 문자열 계열: `shortstring`, `string`, `char` → string
  - 정수/수치: `int`, `int32`, `int64`, `frequency`, `hertz`, `power`, `volume` → int
  - 실수: `double`, `float` → double
  - 불리언: `bool` → bool
  - 구조체 판단: 타입명이 `c_` 같은 네이밍 규칙을 따르거나 이미 파싱된 struct 목록에 존재하면 struct

1. enum 처리 우선순위

- 필드 내부에 enum/ enumerator가 직접 선언돼 있으면 이를 `Field.EnumValues`로 사용합니다.
- 내부에 없으면 전역 수집된 enum 맵(`_enums`)에서 타입명을 찾아 enum 값을 얻습니다.

1. 방어 코드

- 개별 XML 파싱 오류는 해당 파일/노드 수준에서 처리(무시)하고 전체 파싱 흐름을 중단시키지 않습니다.
- 스키마가 없거나 판정이 불확실한 경우 `BasicKindFrom` 기반의 안전값을 사용합니다.

## JSON 샘플 생성 규칙 (세부)

- 목적: 각 struct 타입에 대해 인간이 읽을 수 있고 최소한의 테스트 유효성을 제공하는 JSON 예시를 생성합니다.

- 기본 동작
  - `BuildSample(typeName)`은 해당 타입의 스키마(TypeSchema)를 조회합니다.
  - 각 필드에 대해 `SampleForField(field)`를 호출해 값(또는 중첩 객체/배열)을 할당합니다.

- 필드별 샘플 규칙

- int 계열: 1
- double/float 계열: 1.0
- bool: false
- string: 필드명이 `A_` 같은 식별자 접두사를 가지면 접두사를 제거한 값을 사용(예: `A_crewRoleName` → `"crewRoleName"`), 아니면 빈 문자열 또는 필드명 기반 기본값
- enum: 가능한 경우 `Field.EnumValues`의 첫 번째 항목, 없으면 전역 enum 맵의 첫 값
- struct(중첩 객체): NestedType의 스키마로 재귀적으로 `BuildSample(NestedType)` 호출
- sequence(배열): 원소를 하나만 포함한 배열로 생성
  - 문자열 시퀀스: 단일 문자열 요소 (필드명 A_ 제거 규칙 적용)
  - 프리미티브 시퀀스: 원소의 기본값(위 규칙)에 따라 단일 요소 배열
  - 구조체 시퀀스: 원소 타입의 `BuildSample` 결과를 단일 요소로 배열

## 출력 형식

- JSON은 사람이 읽기 쉬운 형태로 생성하되, Publish용으로 직렬화할 때는 공백을 제거한 compact 표현으로 변환합니다(예: Data.text = JsonSerializer.Serialize(sample, new JsonSerializerOptions{WriteIndented=false})).

## 예시(첨부된 XML 스니펫과 JSON 샘플)

아래 예시는 문서의 규칙을 적용해 생성한 실제 샘플입니다. XML 원본은 각 struct 요소의 멤버 목록만 발췌했습니다.

예시 1 — 타입: C_Actual_Alarm (XML 원본 발췌)

```xml
<struct name="C_Actual_Alarm">
  <member name="A_sourceID" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_IdentifierType" key="true"/>
  <member name="A_timeOfDataGeneration" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_DateTimeType"/>
  <member name="A_componentName" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_ShortString"/>
  <member name="A_nature" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_ShortString"/>
  <member name="A_subsystemName" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_ShortString"/>
  <member name="A_measure" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_ShortString"/>
  <member name="A_dateTimeRaised" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_DateTimeType"/>
  <member name="A_alarmState" type="nonBasic" nonBasicTypeName="P_Alarms_PSM::T_Actual_Alarm_StateType"/>
  <member name="A_raisingCondition_sourceID" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_IdentifierType"/>
  <member name="A_alarmCategory_sourceID" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_IdentifierType"/>
</struct>
```

JSON 샘플 (규칙 적용 결과):

```json
{
  "A_alarmCategory_sourceID": { "A_instanceId": 1, "A_resourceId": 1 },
  "A_alarmState": "L_Actual_Alarm_StateType_Unacknowledged",
  "A_componentName": "componentName",
  "A_dateTimeRaised": { "A_nanoseconds": 1, "A_second": 1 },
  "A_measure": "measure",
  "A_nature": "nature",
  "A_raisingCondition_sourceID": { "A_instanceId": 1, "A_resourceId": 1 },
  "A_sourceID": { "A_instanceId": 1, "A_resourceId": 1 },
  "A_subsystemName": "subsystemName",
  "A_timeOfDataGeneration": { "A_nanoseconds": 1, "A_second": 1 }
}
```

예시 2 — 타입: C_Crew_Role_In_Mission_State (XML 원본 발췌)

```xml
<struct name="C_Crew_Role_In_Mission_State">
  <member name="A_sourceID" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_IdentifierType" key="true"/>
  <member name="A_timeOfDataGeneration" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_DateTimeType"/>
  <member name="A_crewRoleName" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_ShortString"/>
  <member name="A_relevantAlarmType_sourceID" sequenceMaxLength="100" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_IdentifierType"/>
  <member name="A_missionState_sourceID" sequenceMaxLength="100" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_IdentifierType"/>
</struct>
```

JSON 샘플:

```json
{
  "A_crewRoleName": "crewRoleName",
  "A_missionState_sourceID": [ { "A_instanceId": 1, "A_resourceId": 1 } ],
  "A_relevantAlarmType_sourceID": [ { "A_instanceId": 1, "A_resourceId": 1 } ],
  "A_sourceID": { "A_instanceId": 1, "A_resourceId": 1 },
  "A_timeOfDataGeneration": { "A_nanoseconds": 1, "A_second": 1 }
}
```

예시 3 — 타입: C_Tone_Specification (XML 원본 발췌)

```xml
<struct name="C_Tone_Specification">
  <member name="A_sourceID" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_IdentifierType" key="true"/>
  <member name="A_timeOfDataGeneration" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_DateTimeType"/>
  <member name="A_toneFrequency" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_FrequencyInHertzType"/>
  <member name="A_toneModulationType" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_ShortString"/>
  <member name="A_toneRepetitionFrequency" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_FrequencyInHertzType"/>
  <member name="A_toneMaxVolume" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_PowerInWattsType"/>
  <member name="A_alarmCategorySpecification_sourceID" type="nonBasic" nonBasicTypeName="P_LDM_Common::T_IdentifierType"/>
</struct>
```

JSON 샘플:

```json
{
  "A_alarmCategorySpecification_sourceID": { "A_instanceId": 1, "A_resourceId": 1 },
  "A_sourceID": { "A_instanceId": 1, "A_resourceId": 1 },
  "A_timeOfDataGeneration": { "A_nanoseconds": 1, "A_second": 1 },
  "A_toneFrequency": 1,
  "A_toneMaxVolume": 1,
  "A_toneModulationType": "toneModulationType",
  "A_toneRepetitionFrequency": 1
}
```

---

추가 제안

- 원하면 이 규칙 문서에 대해 `markdownlint` 규칙을 적용해 린트 통과 상태로 만들고, 간단한 유닛 테스트(샘플 생성) 2~3개를 추가해 자동 검증을 마련하겠습니다.

끝.


  ---

  참고 파일:

  - Services/XmlTypeSchemaProvider.cs
  - Services/SampleJsonBuilder.cs

  문서는 코드 구현을 기반으로 생성되었습니다. 코드 변경 시 문서도 갱신하세요。
