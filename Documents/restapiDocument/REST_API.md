# WebTerminal REST API Reference

이 문서는 WebTerminal 프로젝트에서 제공하는 모든 REST API의 목록과 명세(사양)를 정리한 문서입니다. 모든 API는 `/api` 프리픽스를 가지며, 인증이 필요한 API는 Cookie 기반 인가(Authentication)를 사용합니다.

---

## 1. Authentication (인증)

### 1.1. 로그인
- **Endpoint**: `POST /api/auth/login`
- **Auth Required**: No (Rate Limiting 적용)
- **Request Body**:
  ```json
  {
    "username": "terukiss",
    "password": "my_password"
  }
  ```
- **Response** (200 OK):
  ```json
  {
    "userId": "guid",
    "username": "terukiss",
    "isAdmin": true
  }
  ```

### 1.2. 로그아웃
- **Endpoint**: `POST /api/auth/logout`
- **Auth Required**: Yes
- **Response** (200 OK): `{"Success": true}`

### 1.3. 비밀번호 변경
- **Endpoint**: `POST /api/auth/change-password`
- **Auth Required**: Yes
- **Request Body**:
  ```json
  {
    "oldPassword": "current_password",
    "newPassword": "new_password"
  }
  ```
- **Response** (200 OK): `{"Success": true}`

---

## 2. User Preferences (유저 설정)

### 2.1. 개인 설정 조회
- **Endpoint**: `GET /api/users/preferences`
- **Auth Required**: Yes
- **Response** (200 OK):
  ```json
  {
    "fontSize": 14,
    "themeBackground": "#090d16",
    "themeForeground": "#cbd5e1"
  }
  ```

### 2.2. 개인 설정 업데이트
- **Endpoint**: `PUT /api/users/preferences`
- **Auth Required**: Yes
- **Request Body**: JSON 형태로 클라이언트가 자유롭게 저장할 설정값 전달
- **Response** (200 OK): `{"success": true}`

---

## 3. File Manager (파일 탐색기)
**※ 주의:** 파일 관리 API는 서버 측 지정된 홈 디렉터리 등에서만 동작합니다.

### 3.1. 파일/디렉터리 목록 조회
- **Endpoint**: `GET /api/files/list?path={url_encoded_path}`
- **Auth Required**: Yes
- **Response** (200 OK):
  ```json
  {
    "currentPath": "/",
    "items": [
      {
        "name": "folder1",
        "isDirectory": true,
        "size": 0,
        "lastModified": "2026-07-01T12:00:00"
      }
    ]
  }
  ```

### 3.2. 파일/디렉터리 삭제
- **Endpoint**: `DELETE /api/files/delete?path={url_encoded_path}`
- **Auth Required**: Yes
- **Response** (200 OK): `{"success": true}`

### 3.3. 파일 다운로드
- **Endpoint**: `GET /api/files/download?path={url_encoded_path}`
- **Auth Required**: Yes
- **Response** (200 OK): `application/octet-stream` 또는 파일 타입에 맞는 Content-Type 스트림 반환

### 3.4. 파일 업로드
- **Endpoint**: `POST /api/files/upload?path={url_encoded_path}`
- **Auth Required**: Yes
- **Content-Type**: `multipart/form-data`
- **Request Body**:
  - `file`: 업로드할 파일 데이터
- **Response** (200 OK): `{"success": true}`

---

## 4. Admin API (관리자 전용 대시보드)
**※ 주의:** 이 항목의 모든 API는 `[Authorize(Roles = "Admin")]` 속성이 부여되어 있어 `Admin` 권한이 있는 사용자만 호출할 수 있습니다.

### 4.1. 시스템 자원 모니터링
- **Endpoint**: `GET /api/admin/system`
- **Response** (200 OK):
  ```json
  {
    "cpuUsagePercent": 15.5,
    "totalRamMb": 16384.0,
    "availableRamMb": 8192.0
  }
  ```

### 4.2. 활성 터미널 세션 목록 조회
- **Endpoint**: `GET /api/admin/sessions`
- **Response** (200 OK):
  ```json
  [
    {
      "sessionId": "guid",
      "ownerUserId": "guid",
      "createdAt": "2026-07-03T15:00:00Z",
      "lastActivityAt": "2026-07-03T15:05:00Z",
      "workingDirectory": "C:\\Users\\admin",
      "hasConnections": true,
      "connectionCount": 1
    }
  ]
  ```

### 4.3. 세션 강제 종료
- **Endpoint**: `DELETE /api/admin/sessions/{sessionId}`
- **Response** (200 OK): `{"Success": true}`

### 4.4. 사용자 전체 목록 조회
- **Endpoint**: `GET /api/admin/users`
- **Response** (200 OK):
  ```json
  [
    {
      "id": "guid",
      "username": "terukiss",
      "isActive": true,
      "isAdmin": true,
      "createdAt": "2026-07-01T12:00:00Z",
      "updatedAt": "2026-07-01T12:00:00Z",
      "failedLoginCount": 0,
      "lockedUntil": null
    }
  ]
  ```

### 4.5. 신규 사용자 생성
- **Endpoint**: `POST /api/admin/users`
- **Request Body**:
  ```json
  {
    "username": "newuser",
    "password": "securepassword123",
    "isAdmin": false
  }
  ```
- **Response** (200 OK):
  ```json
  {
    "UserId": "new-guid"
  }
  ```

### 4.6. 사용자 계정 삭제
- **Endpoint**: `DELETE /api/admin/users/{userId}`
- **Response** (200 OK): `{"Success": true}`
