# 🛡️ SystemCareSuite

[![Framework](https://img.shields.io/badge/.NET-8.0-512BD4?logo=.net&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![GitHub release (latest by date)](https://img.shields.io/github/v/release/mikuchan1004/SystemCareSuite?color=brightgreen&logo=github)](https://github.com/mikuchan1004/SystemCareSuite/releases/latest)
[![Collaborated with Gemini](https://img.shields.io/badge/Collaborated%20with-Gemini-4285F4?logo=google-gemini&logoColor=white)](#)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**SystemCareSuite**는 시스템 상태 확인, 메모리 최적화, 임시파일 정리, 레지스트리 정리 및 WinSxS 저장소 관리를 한 번에 해주는 **.NET 8.0 및 WPF 기반 올인원 시스템 케어 도구**입니다.

---

### 🖼️ 스크린샷 (Screenshot)

<img width="1232" height="770" alt="image" src="https://github.com/user-attachments/assets/951db7eb-50f4-4d14-866d-f62e7cd6ddf1" />


### ✨ 주요 기능 (Key Features)

1. **📊 대시보드 & 원클릭 RAM 메모리 부스트**
   * **실시간 리소스 모니터링**: CPU 부하, 물리 메모리(RAM) 점유율 및 여유 공간, 시스템 업타임을 1초 단위로 추적합니다.
   * **원클릭 RAM 부스트**: `EmptyWorkingSet` API를 활용해 프로세스들이 점유 중인 유휴 물리 메모리를 즉시 확보합니다.
   * **드라이브 & 프로세스 현황**: 로컬 스토리지 용량 게이지(클릭 시 탐색기 실행) 및 메모리 점유 상위 프로세스(TOP 6)를 실시간 표시합니다.

2. **🧹 통합 시스템 & 개발 폴더 클리너**
   * **사전 용량 스캔 & 선택 정리**: 정리 전 확보 가능한 예상 용량을 미리 스캔하거나 원하는 항목만 골라 정리할 수 있습니다.
   * **지원 대상**: 사용자 임시 파일(`%TEMP%`), Windows 시스템 임시 파일(`Windows\Temp`), 프리패치(`Prefetch`), Windows 업데이트 캐시(`wuauserv` 서비스 제어 포함), 배달 최적화 캐시, Discord 캐시, 개발 프로젝트 빌드 캐시(`bin`, `obj`, `.vs`).

3. **🗂️ 레지스트리 스캐너 & 시작프로그램 관리**
   * 삭제된 프로그램의 유령 시작프로그램 항목, 빈 소프트웨어 잔상 키, MUI 캐시, 깨진 파일 확장자 연결을 정밀 진단 및 정리합니다.
   * **정상 시작앱 조회** 기능을 통해 현재 윈도우에 등록된 활성 시작프로그램 목록도 한눈에 확인할 수 있습니다.

4. **📦 WinSxS 컴포넌트 저장소 관리 (DISM 통합)**
   * Windows 구성 요소 저장소(`WinSxS`)의 실제 크기, 공유 크기, 확보 가능 예상 용량을 분석합니다.
   * 일반 정리(`/StartComponentCleanup`) 및 심층 초기화 정리(`/ResetBase`) 진행률을 실시간 프로그레스 바와 컬러 콘솔 로그로 제공합니다.

---

### 🛠️ 기술 스택 (Tech Stack)

* **Framework**: .NET 8.0 (`net8.0-windows`)
* **UI**: WPF (Windows Presentation Foundation)
* **Language**: C# 12

---

### ⚙️ 설치 및 실행 방법

1. [Releases](https://github.com/mikuchan1004/SystemCareSuite/releases) 페이지에서 최신 `SystemCareSuite.exe` 파일을 다운로드합니다.
2. 별도의 런타임 설치나 압축 해제 없이 바로 실행 가능한 **Single-File 포터블** 방식입니다.

---

### 👤 개발자 (Author)

* **GitHub**: [@mikuchan1004](https://github.com/mikuchan1004)

---

### 📄 라이선스 (License)

본 프로젝트는 MIT 라이선스에 따라 자유롭게 이용 가능합니다.
