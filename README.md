# Tcping

.NET 10으로 작성된 크로스플랫폼 TCP Ping 유틸리티입니다. 지정한 호스트와 포트에 TCP 연결을 시도하여 연결 가능 여부와 응답 시간, 성공률을 출력합니다.

Eli Fulkerson의 `tcping.exe`와 유사한 사용법과 출력 형식을 제공하며 Windows x64와 Linux x64에서 동일한 명령행 옵션을 사용할 수 있습니다.

## 주요 기능

- DNS 이름, IPv4, IPv6 지원
- `Socket.ConnectAsync()` 기반 비동기 TCP 연결
- 연결 timeout 및 Probe 간격 설정
- 연속 실행과 Ctrl+C 종료 후 통계 출력
- IPv4/IPv6 강제 선택
- Source IP Address 바인딩
- DNS 주기적 갱신 및 reverse DNS/FQDN 출력
- 연속 실패 횟수에 따른 종료
- 상태 변경 시에만 출력
- rolling window 기반 jitter 계산
- 파일에 정의된 여러 대상 순차 검사
- 콘솔 출력을 로그 파일에 동시에 기록
- 외부 NuGet 패키지 없는 단일 실행 파일 배포

## 요구 사항

소스에서 빌드하려면 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)가 필요합니다.

게시된 self-contained 실행 파일은 대상 시스템에 .NET Runtime을 별도로 설치하지 않아도 실행할 수 있습니다.

## 기본 사용법

```text
tcping google.com 443
```

포트를 생략하면 기본 포트 `80`을 사용하며, 기본적으로 4회 Probe합니다.

```text
tcping google.com
```

출력 예시:

```text
Probing 142.250.199.78:443/tcp - Port is open - time=59.889ms
Probing 142.250.199.78:443/tcp - Port is open - time=59.321ms
Probing 142.250.199.78:443/tcp - Port is open - time=60.730ms
Probing 142.250.199.78:443/tcp - Port is open - time=57.636ms

Ping statistics for 142.250.199.78:443
     4 probes sent.
     4 successful, 0 failed.  (0.00% fail)
Approximate trip times in milli-seconds:
     Minimum = 57.636ms, Maximum = 60.730ms, Average = 59.394ms
```

## 명령행 옵션

```text
Usage:
  tcping [options] host [port]
  tcping [options] --file targets.txt

Options:
  -t                Ctrl+C를 누를 때까지 계속 Probe합니다.
  -n count          Probe 횟수를 지정합니다. 기본값은 4입니다.
  -i seconds        Probe 간격을 초 단위로 지정합니다. 소수점을 지원합니다.
  -w seconds        연결 timeout을 초 단위로 지정합니다. 소수점을 지원합니다.
  -d                각 결과 앞에 현재 날짜와 시간을 표시합니다.
  -r count          지정한 Probe 횟수마다 DNS를 다시 조회합니다.
  -s                처음 연결에 성공하면 종료합니다.
  -4                IPv4만 사용합니다.
  -6                IPv6만 사용합니다.
  -c                OPEN/CLOSED 상태가 변경될 때만 결과를 출력합니다.
  -g count          지정한 횟수만큼 연속 실패하면 종료합니다.
  -S address        TCP Socket에 사용할 Source IP Address를 지정합니다.
  -p port           대상 포트를 지정하거나 파일의 포트를 덮어씁니다.
  -j                최근 응답 시간을 이용한 jitter를 표시합니다.
  -js count         Jitter rolling window 크기를 지정합니다. 기본값은 5입니다.
  --fqdn            Reverse DNS가 가능하면 hostname/FQDN을 함께 표시합니다.
  --tee file        콘솔 출력을 파일에도 기록합니다. 기본적으로 덮어씁니다.
  --append          --tee 로그 파일에 이어서 기록합니다.
  --file file       파일에서 검사 대상을 읽습니다.
  -v, --version     버전을 표시하고 종료합니다.
  /?, -?, --help    도움말을 표시하고 종료합니다.
```

## 사용 예시

Probe 횟수, 간격 및 timeout 지정:

```text
tcping -n 10 -i 0.5 -w 1.5 google.com 443
```

Ctrl+C를 누를 때까지 계속 실행:

```text
tcping -t google.com 443
```

IPv4 또는 IPv6만 사용:

```text
tcping -4 google.com 443
tcping -6 google.com 443
```

연속 3회 실패하면 종료:

```text
tcping -t -g 3 192.0.2.1 443
```

처음 성공한 Probe에서 종료:

```text
tcping -s database.example.com 5432
```

상태가 변경될 때만 출력:

```text
tcping -t -c service.example.com 443
```

Jitter와 FQDN 표시:

```text
tcping -j -js 10 --fqdn google.com 443
```

Source IP Address 지정:

```text
tcping -S 192.168.0.10 server.example.com 443
```

로그 파일에 콘솔 출력을 함께 기록:

```text
tcping --tee tcping.log google.com 443
tcping --tee tcping.log --append google.com 443
```

## 파일 입력

`--file` 옵션을 사용하면 여러 대상을 순차적으로 검사할 수 있습니다.

```text
tcping --file servers.txt
```

`servers.txt` 예시:

```text
# Web
google.com 443
web01 443

# Database
oracle01 1521
mysql01 3306
```

빈 줄과 `#`으로 시작하는 주석은 무시합니다. 포트가 없는 줄은 기본 포트 `80`을 사용합니다.

`-p`를 함께 지정하면 파일에 기록된 포트보다 명령행의 포트를 우선합니다.

```text
tcping --file servers.txt -p 443
```

## 빌드

```bash
dotnet restore
dotnet build -c Release
```

## Single-file 게시

Windows x64:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

출력 파일:

```text
bin/Release/net10.0/win-x64/publish/tcping.exe
```

Linux x64:

```bash
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

출력 파일:

```text
bin/Release/net10.0/linux-x64/publish/tcping
```

Linux에 설치:

```bash
chmod +x tcping
sudo cp tcping /usr/local/bin/tcping
```

설치 후 어느 경로에서든 실행할 수 있습니다.

```bash
tcping google.com 443
```

## 종료 코드

- `0`: 모든 대상에서 한 번 이상 연결 성공
- `1`: 하나 이상의 대상에서 연결 성공 없음
- `2`: 잘못된 옵션, 포트 또는 입력 파일

## 지원 플랫폼

- Windows x64
- Linux x64
- Rocky Linux 8/9 x64

## 라이선스

이 저장소에 별도의 라이선스 파일이 추가되기 전까지는 저작권자의 명시적인 허가 없이 재배포 조건을 추정하지 마십시오.
