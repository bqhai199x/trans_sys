# Triển khai remediation FileHandler — 2026-09-18 [CDX]

Hoàn tất phần sửa mã theo F01–F18 của [review](filehandler-architecture-security-review.md), theo yêu cầu `implement`. Giữ các thay đổi và deletion có sẵn; không mở rộng O7/O8.

## Thay đổi

| Findings | Triển khai |
|---|---|
| F01–F05 | Binding theo đường dẫn XML chính xác, áp dụng mọi slot, chỉ serialize part thay đổi. Word giữ protected fields; PPT xác định đúng paragraph/table; Excel giữ rich runs, SST copy-on-write và áp dụng drawing. |
| F06–F07 | Word kiểm bảng trong các story được chọn; SHA-256 kiểm part không đổi. XML invariant kiểm source hash và giá trị cuối tại đúng scalar, giữ các node/attribute còn lại. SST kiểm prefix cũ và payload append. Schema kiểm baseline, dependencies và giới hạn số lỗi. |
| F08 | Giữ thứ tự xen kẽ r/k; whitespace và raw control là anchor, binding span giữ nguyên ký tự không được dịch. |
| F09–F11 | Giới hạn byte đọc thực tế, cả multipart chunked; XML preflight theo content type; quota import/export và output có lỗi 413, package hỏng dự kiến có lỗi 422. |
| F12 | Loại hàng/cột/sheet ẩn, orphan header khỏi units; từ chối bound/locked SDT, unsupported MC, field xuyên paragraph và merged follower chứa text. |
| F13 | Logger exception toàn cục chỉ ghi loại lỗi/request ID; không chuyển exception gốc vào sink. Trace redaction tiếp tục che nội dung. |
| F14 | Index đường dẫn/cell một lần, tích lũy runs bằng StringBuilder, cache hash scalar/anchor; merge index và hidden-column sweep không mở rộng từng vùng; Markdown token canonicalization forward pass. |
| F15 | Quota trong lúc đọc/dựng plan/serialize, hash bằng stream, kiểm byte Markdown trước compose; trace có ngân sách file/capture, flush async và JSON summary khi vượt giới hạn. Danh sách log v1 không lọc đọc metadata. |
| F16–F17 | Soft break Markdown thành protected token; mọi đường mở SDK dùng explicit read settings, AutoSave=false, NoProcess và MaxCharactersInPart. |
| F18 | Sửa XML docs/spacing, xác nhận SDK dùng C#14; formatter và Roslyn audit sạch. |

Các validator cấu trúc riêng định dạng vẫn kiểm các thuộc tính tổng quát; việc bảo vệ node/attribute ngoài phạm vi sửa nằm trong package XML invariant bắt buộc trước publish.

## Kiểm chứng

- Release build thông qua test với `GenerateDocumentationFile=true`: không warning/error compile.
- **359/359 tests pass**, 0 skipped; baseline 345. Regression kiểm vị trí XML, rich formatting, field, token order, raw controls, orphan header, merge, partial story, quota, log redaction và XML edits trái phép.
- `dotnet format --verify-no-changes --no-restore`: exit 0.
- Roslyn audit: **108 files / 918 declarations / 0 findings**, không còn inheritdoc-only.
- Chạy lại 24 observations và 4 supplemental probes; source bytes được giữ, rich text/drawings cập nhật đúng, XML đổi suffix vẫn bị chặn quota.
- HTTP Kestrel: multipart 5.453 byte với limit 4.096 trả 413 cả Content-Length và chunked; output quota trả 413; duplicate relationship trả 422.
- Benchmark tổng hợp Word Analyze 2k/4k/8k paragraph: khoảng **20,6 / 44,8 / 102,6 ms**, so với baseline 132 / 541 / 1.916 ms. Không coi số đo máy local là SLA production.
- [Evidence và lệnh tái chạy](filehandler-implementation-evidence/README.md), [TRX](filehandler-implementation-evidence/test-results/implementation.trx).

## Migration và giới hạn

Client phải import lại tài liệu sau cập nhật: template bảo vệ soft break/whitespace/control và thứ tự r/k đã thay đổi. Không dùng lại array dịch từ template cũ.

Chưa thực hiện Office desktop/no-repair gate, load test production hoặc dependency CVE audit online. NuGet audit feed không truy cập được ở phiên này; chỉ tắt audit trên CLI chạy probe, không đổi project. Debug log chưa tự động retention; cần chính sách vận hành bên ngoài. Search nội dung và log legacy vẫn dùng đường đọc đầy đủ. Các điểm này không được suy diễn thành đã đạt production readiness.

Build dùng artifacts riêng vì obj hiện hữu bị quyền ghi chặn; format qua bản sao rồi apply diff, không sửa ACL. Không commit hoặc deploy.
