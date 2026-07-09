# Database Design

## Stops Tablosu

| Alan | Tip |
|------|-----|
| Id | int |
| StopId | int |
| Name | varchar |
| Latitude | double precision |
| Longitude | double precision |

### Primary Key# Database Design

## Stop

Otobüs duraklarını tutar.

| Alan | Tip |
|------|-----|
| Id | int |
| ExternalStopId | string |
| Name | string |
| Latitude | double |
| Longitude | double |
| CreatedAt | DateTime |
| UpdatedAt | DateTime |

---

## StopRoute

Bir durağın geçtiği hatları tutar.

| Alan | Tip |
|------|-----|
| Id | int |
| StopId | int |
| RouteNumber | string |
| CreatedAt | DateTime |

İlişki:

- Stop (1) → (N) StopRoute

---

## ImportRun

CSV import işlemlerini kayıt altına alır.

| Alan | Tip |
|------|-----|
| Id | int |
| SourceName | string |
| StartedAt | DateTime |
| FinishedAt | DateTime |
| ImportedRecordCount | int |
| UpdatedRecordCount | int |
| FailedRecordCount | int |
| Status | string |
| ErrorMessage | string |

---

## ExternalApiLog

Dış servislere yapılan istekleri kaydeder.

| Alan | Tip |
|------|-----|
| Id | int |
| EndpointName | string |
| RequestUrl | string |
| HttpStatusCode | int |
| ResponseDurationMs | int |
| IsSuccessful | bool |
| ErrorMessage | string |
| CreatedAt | DateTime |

- Id

### Açıklama

Bu tabloda ESHOT durak bilgileri tutulacaktır.

İleride ihtiyaç halinde yeni tablolar eklenebilir.