# Bằng chứng review FileHandler — 2026-09-15

Review trên working tree `D:\Projects\trans_sys`, gồm code Office/token chưa commit có sẵn. Không sửa production hoặc test suite hiện hữu. Múi giờ `+07:00`.

## Kết quả và phạm vi

- SDK `10.0.401`, `net10.0`; MSBuild báo `LangVersion=14.0`, không phải C# 13 như mô tả yêu cầu.
- Release build bật XML documentation: 0 warnings/errors.
- Test suite: 345 passed, 0 failed/skipped; TRX tại `test-results/review-baseline.trx`.
- `dotnet format --verify-no-changes --no-restore`: exit 0; `format-verification.log` rỗng.
- `observations.json`: 24 observations của probes chính và microbenchmarks; `extra-observations.json`: 4 probes bổ sung.
- `http-observations.json`: 4 HTTP requests trên Kestrel loopback; `server.log` chỉ chứa dữ liệu fixture tổng hợp và stack traces của probes.
- `documentation-audit.json`: audit cú pháp Roslyn trên 99 files, 816 khai báo thuộc phạm vi method/local function/constructor/property/field/indexer/enum/member/record. Positional record params được đối chiếu; primary constructor của class được kiểm tra riêng khi đọc code. 27 khai báo chỉ có inheritdoc thay explicit tags; 8 vị trí cần biên tập từ ngữ; không có khai báo mất hoàn toàn XML comment trong tập audit.
- `source-manifest.json`: SHA-256 source/config tại thời điểm review để xác định đúng phiên bản.

Fixtures Word/Excel/PPT dùng để xác nhận lỗi nội dung đều có schema nguồn hợp lệ. Probes PPT thêm slide master/notes size vào fixture hiện hữu để kiểm tra toàn package độc lập; các file production test factory chưa được sửa. Output sai nội dung của các probe chính vẫn có 0 schema errors.

## Chạy lại

Chạy từ thư mục chứa tài liệu này bằng PowerShell, với .NET SDK và packages đã restore:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project ReviewProbe.csproj -c Release --artifacts-path artifacts/probe -p:NuGetAudit=false
dotnet run --project ReviewProbe.csproj -c Release --artifacts-path artifacts/probe -p:NuGetAudit=false -- extra
dotnet artifacts/probe/bin/ReviewProbe/release/FileHandler.Tests.dll audit
```

`ReviewProbe` dùng assembly name `FileHandler.Tests` để dùng quyền friend assembly hiện hữu cho kiểm tra diagnostics, không ghi đè binary test thật. Project chỉ compile hai source riêng và link read-only fixture factory hiện hữu. Các output build tách vào `artifacts/` và bị gitignore.

Build/test baseline từ thư mục `filehandler`:

```powershell
dotnet restore FileHandler.sln --artifacts-path 'D:\Projects\trans_sys\.ai\notes\filehandler-review-evidence\artifacts' --ignore-failed-sources -p:NuGetAudit=false
dotnet test FileHandler.sln -c Release --no-restore --artifacts-path 'D:\Projects\trans_sys\.ai\notes\filehandler-review-evidence\artifacts' -p:GenerateDocumentationFile=true --logger 'trx;LogFileName=review-baseline.trx' --results-directory 'D:\Projects\trans_sys\.ai\notes\filehandler-review-evidence\test-results'
dotnet build FileHandler.sln -c Release --no-restore --artifacts-path 'D:\Projects\trans_sys\.ai\notes\filehandler-review-evidence\artifacts' -p:GenerateDocumentationFile=true
```

HTTP probes cần chạy host riêng từ thư mục evidence:

```powershell
dotnet artifacts/bin/FileHandler.Api/release/FileHandler.Api.dll --urls http://127.0.0.1:5129 --contentRoot 'D:\Projects\trans_sys\filehandler\src\FileHandler.Api' --FileHandling:MaxMultipartBytes=4096 --FileHandling:MaxOutputBytes=100 --DebugTrace:Directory='D:\Projects\trans_sys\.ai\notes\filehandler-review-evidence\artifacts\server-traces' --DebugTrace:CaptureContent=false --Logging:EventLog:LogLevel:Default=None
```

Trong terminal khác, chạy `./http-probes.ps1`. Script gửi body tổng 5.453 byte bằng Content-Length và chunked, sau đó hai Office export để kiểm tra quota và relationship lỗi. Host này phải được dừng sau probe. Không dùng instance đang phục vụ người dùng.

## Giới hạn diễn giải

- Restore audit NuGet online gặp NU1900 do mạng sandbox; chỉ tắt NuGetAudit cho lệnh restore/probe, không đổi cấu hình repository. Chưa có kết luận audit CVE dependency.
- Build đầu tiên vào `obj/Release` hiện hữu gặp Access Denied; chuyển output build riêng. Không thay ACL hoặc xóa output có sẵn.
- Host Windows lần đầu gặp EventLog provider không được ghi log; host cuối tắt riêng provider đó bằng CLI. Kết quả HTTP cuối có payload 500 JSON đầy đủ. Đây là điều kiện môi trường, không dùng làm bằng chứng lỗi nghiệp vụ.
- Probe ZIP chỉ dùng payload 256 KiB. Với .NET 10 đang chạy, giả declared size thành 1 byte khiến entry reader trả 1 byte và bị CRC check từ chối; **không xác nhận ZIP bomb bypass bằng kỹ thuật này**. Không chạy bomb lớn hoặc OOM.
- Microbenchmark cùng process, warm-up, median 3/5 lần; `DOTNET_TieredCompilation=0` để tránh chuyển tier giữa kích thước. Có đo allocate tổng managed và flush trace, không đo peak RSS/LOH live hoặc p95/p99 HTTP. Không ngoại suy số đo thành capacity production.
- Audit Roslyn hỗ trợ review cấu trúc XML, không chứng minh mọi câu mô tả đúng nghĩa. Không coi `<inheritdoc/>` là thiếu toàn bộ documentation; nó không thỏa explicit summary/param/returns của AGENTS hiện tại.
- Chưa mở/render bằng Microsoft Office desktop; schema valid không chứng minh layout fidelity hoặc không repair.
- Probes lưu hành vi lỗi hiện tại để review; không dùng assertions chấp nhận lỗi làm acceptance tests sau remediation.
