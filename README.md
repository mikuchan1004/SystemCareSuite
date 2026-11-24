# 🛡️ SystemCareSuite

[![Framework](https://img.shields.io/badge/.NET-8.0-512BD4?logo=.net&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![GitHub release (latest by date)](https://img.shields.io/github/v/release/mikuchan1004/SystemCareSuite?color=brightgreen&logo=github)](https://github.com/mikuchan1004/SystemCareSuite/releases/latest)
[![VirusTotal](https://img.shields.io/badge/VirusTotal-Safe%20%26%20Clean-0066FF?logo=virustotal&logoColor=white)](https://www.virustotal.com/gui/file/c8ad18fac63f1d4a18501def8cd99e1aa3df01fac2e3cab157acaae50011002f)
[![Collaborated with Gemini](https://img.shields.io/badge/Collaborated%20with-Gemini-4285F4?logo=google-gemini&logoColor=white)](#)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**SystemCareSuite**는 시스템 상태 확인, 메모리 최적화, 임시파일 정리, 레지스트리 정리 및 WinSxS 저장소 관리를 한 번에 해주는 **.NET 8.0 및 WPF 기반 올인원 시스템 케어 도구**입니다.

---

### 🖼️ 스크린샷 (Screenshot)

<img width="1480" height="816" alt="image" src="https://github.com/user-attachments/assets/f1315153-a94e-4675-944a-c50916811a7a" />


### ✨ 주요 기능 (Key Features)

1. **📊 대시보드 & 원클릭 RAM 메모리 부스트**
   * **실시간 리소스 모니터링**: CPU 부하, 물리 메모리(RAM) 점유율 및 여유 공간, 시스템 업타임을 1초 단위로 추적합니다.
   * **원클릭 & 자동 RAM 부스트**: `EmptyWorkingSet` API를 활용해 유휴 물리 메모리를 즉시 확보하며, 부스트 제외 목록(화이트리스트)·Windows 핵심 시스템 프로세스 자동 보호 및 **RAM 88% 이상 시 자동 부스트**를 지원합니다.
   * **드라이브 & 프로세스 제어**: 로컬 스토리지 용량 게이지(클릭 시 탐색기 실행)와 메모리 상위 프로세스(TOP 6) 우클릭 메뉴(**제외 목록 추가 · 실행 파일 위치 열기 · 프로세스 종료**)를 제공합니다.

2. **🧹 통합 시스템 & 개발 폴더 클리너**
   * **파일 단위 정밀 스캔 & 실시간 진행률**: 삭제 전 파일 목록을 확인하고 원하는 항목만 선택 정리할 수 있으며, 실시간 진행률 바와 2,500개 상한 초과 안내를 제공합니다.
   * **최근 24시간 파일 보호 & 설정 자동 저장**: 최근 수정된 임시/캐시 파일을 건너뛰는 안전 보호 옵션과 개발 폴더 경로·선택 범주를 `settings.json`에 자동 저장합니다.
   * **지원 대상**: 사용자 임시 파일(`%TEMP%`), Windows 시스템 임시 파일(`Windows\Temp`), 프리패치(`Prefetch`), Windows 업데이트 캐시(`wuauserv` 제어 포함), 배달 최적화 캐시, 오류 보고서(WER)·썸네일 캐시, Chrome/Edge 브라우저 캐시, Discord 캐시, 개발 프로젝트 빌드 캐시(`bin`, `obj`, `.vs`), NuGet/npm 패키지 캐시, Windows 휴지통 비우기.

3. **🗂️ 레지스트리 스캐너 & 시작프로그램 관리**
   * 삭제된 프로그램의 유령 시작프로그램 항목, 빈 소프트웨어 잔상 키, MUI 캐시, 깨진 파일 확장자 연결을 정밀 진단 및 정리합니다.
   * **정리 전 `.reg` 자동 백업**: 항목 삭제 직전 `Backups` 폴더에 복구용 `.reg` 파일을 자동 저장해 언제든 더블클릭으로 되돌릴 수 있습니다.
   * **시작프로그램 사용/중지 전환**: 레지스트리(`Run`) 및 시작프로그램 폴더에 등록된 정상·중지된 시작앱을 함께 조회하고, 완전 삭제 없이 **사용 중 ↔ 사용 안 함** 상태로 안전하게 전환할 수 있습니다.

4. **📦 WinSxS 저장소 관리 & Windows 시스템 복구**
   * Windows 구성 요소 저장소(`WinSxS`)의 실제 크기, 공유 크기, 확보 가능 예상 용량을 분석하고 최근 분석 결과를 저장합니다(한국어/영문 Windows 동시 지원).
   * 일반 정리(`/StartComponentCleanup`), 심층 초기화 정리(`/ResetBase`)와 함께 **`SFC /scannow`**(시스템 파일 검사) 및 **`DISM /RestoreHealth`**(시스템 이미지 복구)를 실시간 진행률·컬러 콘솔 로그로 제공합니다.

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
